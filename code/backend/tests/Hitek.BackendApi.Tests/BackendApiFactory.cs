using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Hitek.BackendApi.Common;
using Hitek.BackendApi.Data;
using Hitek.BackendApi.Domain;
using Hitek.BackendApi.Monitoring;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Hitek.BackendApi.Tests;

// Dựng nguyên ứng dụng Backend thật — đủ middleware, xác thực JWT, phân quyền, SignalR —
// chỉ thay PostgreSQL bằng SQLite trong bộ nhớ. Kiểm thử qua đây là gọi HTTP thật như
// AI Service hay Dashboard sẽ gọi, không gọi tắt vào controller.
public sealed class BackendApiFactory : WebApplicationFactory<Program>
{
    public const string Password = "Test@1234";
    public const string AdminEmail = "admin@demo.local";
    public const string OperatorEmail = "operator@demo.local";
    public const string ViewerEmail = "viewer@demo.local";
    public const string ViewerOtherTenantEmail = "viewer@other.local";
    public const string MultiTenantEmail = "multi@demo.local";
    public const string DisabledEmail = "disabled@demo.local";

    public static readonly JsonSerializerOptions Json = CreateJsonOptions();

    // Mốc bắt đầu cho một chuỗi lượt đọc: vài chục giây trước, nhưng không lùi qua nửa đêm
    // UTC, để số liệu "trong ngày" của Dashboard không lệch khi chạy kiểm thử lúc 7 giờ sáng.
    public static DateTime RecentUtc()
    {
        var now = DateTime.UtcNow;
        var start = now.AddSeconds(-30);
        return start.Date == now.Date ? start : now.Date;
    }

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public BackendApiFactory() => _connection.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Kiểm thử chạy SQLite và tự dựng schema: tắt bước migrate và đặt mật khẩu demo của môi trường dev.
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("DevSeed:DefaultPassword", "");

        // Khung giờ cho phép phủ cả ngày, để luật "ngoài giờ" (R2) không phụ thuộc vào giờ
        // của máy đang chạy kiểm thử. Cửa sổ AI nới rộng để máy chậm không làm phép thử chập chờn.
        builder.UseSetting("RiskEngine:AllowedStartUtcHour", "0");
        builder.UseSetting("RiskEngine:AllowedEndUtcHour", "24");
        builder.UseSetting("RiskEngine:AiActiveWindowSeconds", "120");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<HitekDbContext>>();
            services.AddDbContext<HitekDbContext>(o => o.UseSqlite(_connection));

            // SQLite trong bộ nhớ sống trên đúng một kết nối, mà kết nối đó không an toàn khi
            // dùng từ nhiều luồng. Tiến trình quét nền chạy song song với request sẽ tranh kết
            // nối, nên tắt ở đây; phần quét đã có kiểm thử riêng trong SensorMonitorTests.
            services.Remove(services.Single(d =>
                d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(SensorMonitorService)));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        var scopes = host.Services.GetRequiredService<IServiceScopeFactory>();

        // Mỗi DbContext một scope riêng, vì TestSchema tự Dispose context mà nó nhận.
        TestSchema.Create(_connection, () => scopes.CreateScope().ServiceProvider.GetRequiredService<HitekDbContext>());
        using (var scope = scopes.CreateScope())
            SeedUsers(scope.ServiceProvider.GetRequiredService<HitekDbContext>());

        return host;
    }

    private static void SeedUsers(HitekDbContext db)
    {
        // Cost thấp chỉ để kiểm thử chạy nhanh; BCrypt kiểm tra được hash ở mọi mức cost.
        var hash = BCrypt.Net.BCrypt.HashPassword(Password, workFactor: 4);

        void Add(string email, string status, params (Guid Tenant, string Role)[] memberships)
        {
            var user = new AppUser { Id = Guid.NewGuid(), Email = email, FullName = email, PasswordHash = hash, Status = status };
            db.AppUsers.Add(user);
            foreach (var (tenant, role) in memberships)
                db.TenantMembers.Add(new TenantMember { Id = Guid.NewGuid(), TenantId = tenant, UserId = user.Id, Role = role, Status = "active" });
        }

        Add(AdminEmail, "active", (TestSchema.TenantA, "admin"));
        Add(OperatorEmail, "active", (TestSchema.TenantA, "operator"));
        Add(ViewerEmail, "active", (TestSchema.TenantA, "viewer"));
        Add(ViewerOtherTenantEmail, "active", (TestSchema.TenantB, "viewer"));
        Add(MultiTenantEmail, "active", (TestSchema.TenantA, "viewer"), (TestSchema.TenantB, "viewer"));
        Add(DisabledEmail, "disabled", (TestSchema.TenantA, "admin"));
        db.SaveChanges();
    }

    // Đọc/ghi thẳng database để chuẩn bị dữ liệu hoặc kiểm tra kết quả. Không có request nên
    // không có tenant hiện tại: truy vấn bảng có bộ lọc tenant phải gọi IgnoreQueryFilters.
    public async Task WithDbAsync(Func<HitekDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<HitekDbContext>());
    }

    // AI Service ghi ai_event trước rồi mới gọi POST /api/v1/events; phép thử làm đúng thứ tự đó.
    public async Task<Guid> NewAiEventAsync(Guid? cameraId = null, string eventType = "person_intrusion", Guid? tenantId = null)
    {
        var id = Guid.NewGuid();
        await WithDbAsync(async db =>
        {
            db.AiEvents.Add(new AiEvent
            {
                Id = id,
                TenantId = tenantId ?? TestSchema.TenantA,
                CameraId = cameraId ?? TestSchema.CameraWarehouse,
                EventType = eventType
            });
            await db.SaveChangesAsync();
        });
        return id;
    }

    public async Task<string> UserTokenAsync(string email, Guid? tenantId = null)
    {
        using var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password, tenantId }, Json);
        response.EnsureSuccessStatusCode();
        return (await response.JsonAsync()).GetProperty("accessToken").GetString()!;
    }

    public async Task<string> ServiceTokenAsync(string clientId = "iot-service", string secret = "iot-service-dev-secret")
    {
        using var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/token", new { clientId, clientSecret = secret }, Json);
        response.EnsureSuccessStatusCode();
        return (await response.JsonAsync()).GetProperty("accessToken").GetString()!;
    }

    public HttpClient ClientWithToken(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<HttpClient> UserClientAsync(string email) => ClientWithToken(await UserTokenAsync(email));

    public async Task<HttpClient> ServiceClientAsync() => ClientWithToken(await ServiceTokenAsync());

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        ApiJson.Configure(options);
        return options;
    }
}

public static class HttpTestExtensions
{
    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(BackendApiFactory.Json);

    public static async Task<string?> ErrorCodeAsync(this HttpResponseMessage response) =>
        (await response.JsonAsync()).GetProperty("error").GetProperty("code").GetString();

    // Gửi một chuỗi lượt đọc cách nhau một giây, như IoT Service chuyển tiếp từ MQTT.
    public static async Task PostReadingsAsync(this HttpClient service, Guid sensorId, DateTime start, params bool[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            var response = await service.PostAsJsonAsync("/api/v1/iot/readings",
                new { sensorId, value = values[i], recordedAt = start.AddSeconds(i) }, BackendApiFactory.Json);
            response.EnsureSuccessStatusCode();
        }
    }

    public static Task<HttpResponseMessage> PostAiEventAsync(
        this HttpClient service, Guid aiEventId, Guid? cameraId = null, Guid? areaId = null,
        double confidence = 0.9, DateTime? occurredAt = null) =>
        service.PostAsJsonAsync("/api/v1/events", new
        {
            cameraId = cameraId ?? TestSchema.CameraWarehouse,
            objectType = "person",
            confidence,
            boundingBox = new { x = 320, y = 240, w = 80, h = 200 },
            areaId = areaId ?? TestSchema.AreaWarehouse,
            occurredAt = occurredAt ?? DateTime.UtcNow,
            aiEventId
        }, BackendApiFactory.Json);

    public static IEnumerable<string?> Strings(this JsonElement array) =>
        array.EnumerateArray().Select(e => e.GetString());
}
