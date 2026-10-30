using Hitek.BackendApi.Data;
using Hitek.BackendApi.Domain;
using Hitek.BackendApi.Hubs;
using Hitek.BackendApi.Monitoring;
using Hitek.BackendApi.RiskEngine;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hitek.BackendApi.Tests;

// Dựng một hệ thống thu nhỏ chạy trên SQLite trong bộ nhớ.
//
// Dùng SQLite chứ không dùng provider InMemory vì cần hành vi quan hệ thật: khoá
// ngoại, ràng buộc duy nhất và kiểu dữ liệu. Chính những thứ đó mới là cái đang
// được kiểm thử ở đây, InMemory bỏ qua hết nên sẽ cho kết quả xanh giả.
public sealed class TestHarness : IDisposable
{
    public static readonly Guid TenantA = TestSchema.TenantA;
    public static readonly Guid TenantB = TestSchema.TenantB;
    public static readonly Guid AreaWarehouse = TestSchema.AreaWarehouse;
    public static readonly Guid AreaOtherTenant = TestSchema.AreaOtherTenant;

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public FakeTimeProvider Clock { get; }
    public MutableTenantProvider Tenant { get; }
    public RiskEngineOptions RiskOptions { get; }
    public SensorMonitorOptions MonitorOptions { get; }

    public TestHarness()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        // 10:00 UTC nằm trong khung giờ cho phép mặc định (01:00-11:00 UTC), nên luật
        // "ngoài khung giờ" không tự kích hoạt và làm nhiễu những phép thử khác.
        Clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 23, 10, 0, 0, TimeSpan.Zero));
        Tenant = new MutableTenantProvider { TenantId = TenantA };
        RiskOptions = new RiskEngineOptions();
        MonitorOptions = new SensorMonitorOptions();

        var services = new ServiceCollection();
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantProvider>(sp => new ScopedTenantProvider(sp.GetRequiredService<TenantContext>(), Tenant));
        services.AddDbContext<HitekDbContext>(o => o.UseSqlite(_connection));
        services.AddSingleton<IHubContext<AlertsHub>, NoOpHubContext>();
        services.AddSingleton(Options.Create(RiskOptions));
        services.AddSingleton<TimeProvider>(Clock);
        services.AddScoped<RiskEngineService>();
        _provider = services.BuildServiceProvider();

        TestSchema.Create(_connection, NewDbContext);
    }

    public HitekDbContext NewDbContext() => _provider.GetRequiredService<IServiceScopeFactory>()
        .CreateScope().ServiceProvider.GetRequiredService<HitekDbContext>();

    public RiskEngineService NewRiskEngine(HitekDbContext db) =>
        new(db, new NoOpHubContext(), Options.Create(RiskOptions), Clock);

    public SensorMonitorService NewSensorMonitor() =>
        new(_provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(MonitorOptions),
            Clock,
            NullLogger<SensorMonitorService>.Instance);

    public Sensor AddSensor(HitekDbContext db, SensorType type, Guid? areaId = null, Guid? tenantId = null)
    {
        var sensor = new Sensor
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId ?? TenantA,
            Name = $"{type}-test",
            Type = type,
            AreaId = areaId ?? AreaWarehouse,
            // Đang kết nối, như sau khi đã gửi lượt đọc đầu tiên qua IotController.
            Status = SensorStatus.Online
        };
        db.Sensors.Add(sensor);
        db.SaveChanges();
        return sensor;
    }

    private DateTime _lastReadingAt = DateTime.MinValue;

    // Ghi đúng số lượt đọc mà cơ chế chống nhiễu đòi hỏi, kèm Event tương ứng cho
    // mỗi lượt true — mô phỏng lại những gì IotController làm.
    public void PushReadings(HitekDbContext db, Sensor sensor, params bool[] values)
    {
        // Mốc thời gian phải luôn tiến lên, kể cả giữa hai lần gọi liên tiếp, vì cặp
        // (cảm biến, thời điểm) có ràng buộc duy nhất — đúng cơ chế chống trùng đang
        // bảo vệ đường MQTT.
        var at = Clock.GetUtcNow().UtcDateTime;
        if (at <= _lastReadingAt) at = _lastReadingAt;
        foreach (var value in values)
        {
            at = at.AddSeconds(1);
            var reading = new SensorReading
            {
                Id = Guid.NewGuid(),
                TenantId = sensor.TenantId,
                SensorId = sensor.Id,
                Value = value,
                RecordedAt = at
            };
            db.SensorReadings.Add(reading);

            if (value)
            {
                db.Events.Add(new EventRecord
                {
                    Id = Guid.NewGuid(),
                    TenantId = sensor.TenantId,
                    Source = RiskEngineService.ToEventSource(sensor.Type),
                    AreaId = sensor.AreaId,
                    OccurredAt = at,
                    DedupKey = EventDedupKey.ForIot(reading.Id),
                    SensorReading = reading
                });
            }
        }

        _lastReadingAt = at;
        db.SaveChanges();
    }

    public void PushAiEvent(HitekDbContext db, double confidence = 0.9, Guid? areaId = null)
    {
        var aiEventId = Guid.NewGuid();
        db.Events.Add(new EventRecord
        {
            Id = Guid.NewGuid(),
            TenantId = TenantA,
            Source = EventSource.Ai,
            AreaId = areaId ?? AreaWarehouse,
            OccurredAt = Clock.GetUtcNow().UtcDateTime,
            DedupKey = EventDedupKey.ForAi(aiEventId, Guid.NewGuid(), Clock.GetUtcNow().UtcDateTime, "person"),
            AiEventId = aiEventId,
            Confidence = confidence
        });
        db.SaveChanges();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }
}

public sealed class MutableTenantProvider : ITenantProvider
{
    public Guid TenantId { get; set; }
}

// Giống HttpTenantProvider: tenant đặt tường minh cho tác vụ nền được ưu tiên.
internal sealed class ScopedTenantProvider(TenantContext context, MutableTenantProvider fallback) : ITenantProvider
{
    public Guid TenantId => context.TenantId ?? fallback.TenantId;
}

// Risk Engine đẩy cập nhật qua SignalR. Phép thử không quan tâm phần đó nên nuốt
// luôn, thay vì kéo cả hạ tầng SignalR vào chỉ để chạy được hàm.
internal sealed class NoOpHubContext : IHubContext<AlertsHub>
{
    public IHubClients Clients { get; } = new NoOpHubClients();
    public IGroupManager Groups { get; } = new NoOpGroupManager();
}

internal sealed class NoOpHubClients : IHubClients
{
    private static readonly IClientProxy Proxy = new NoOpClientProxy();
    public IClientProxy All => Proxy;
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
    public IClientProxy Client(string connectionId) => Proxy;
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
    public IClientProxy Group(string groupName) => Proxy;
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
    public IClientProxy User(string userId) => Proxy;
    public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
}

internal sealed class NoOpClientProxy : IClientProxy
{
    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

internal sealed class NoOpGroupManager : IGroupManager
{
    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
