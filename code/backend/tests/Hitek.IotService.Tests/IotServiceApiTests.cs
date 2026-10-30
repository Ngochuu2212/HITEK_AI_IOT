using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Hitek.IotService.Tests;

// Chạy IoT Service thật (đường HTTP, hàng đợi, bộ chuyển tiếp, token) với một Backend giả.
public sealed class IotServiceApiTests : IDisposable
{
    private const string SigningKey = "hitek-dev-only-signing-key-DO-NOT-USE-IN-PRODUCTION-2026";
    private static readonly Guid Tenant = Guid.Parse("00000000-0000-4000-8000-000000000001");
    private static readonly Guid Sensor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly FakeBackend _backend = new();
    private readonly WebApplicationFactory<Program> _factory;

    public IotServiceApiTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Mqtt:Enabled", "false");
            builder.UseSetting("Ingest:RetryInitialDelayMilliseconds", "20");
            builder.UseSetting("Ingest:RetryMaxDelaySeconds", "1");
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient(BackendTokenProvider.AuthClientName).ConfigurePrimaryHttpMessageHandler(() => _backend);
                services.AddHttpClient(BackendForwarder.ClientName).ConfigurePrimaryHttpMessageHandler(() => _backend);
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        _backend.Dispose();
    }

    private static string Token(string role, Guid? tenant = null) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = "hitek-backend-api",
        Audience = "hitek-ai-iot",
        Subject = new ClaimsIdentity([new Claim("role", role), new Claim("tenant_id", (tenant ?? Tenant).ToString())]),
        Expires = DateTime.UtcNow.AddHours(1),
        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256)
    });

    private HttpClient Client(string? token)
    {
        var client = _factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Body(string json) => new(json, Encoding.UTF8, "application/json");

    [Fact]
    public async Task DuongHttp_ChuanHoaRoiChuyenSangBackendKemTokenService()
    {
        using var client = Client(Token("service"));

        var response = await client.PostAsync($"/api/v1/readings/{Sensor}", Body("""{"value": "on", "recordedAt": "2026-10-24T17:00:00+07:00"}"""));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var forwarded = await _backend.NextReadingAsync();
        Assert.Equal(Sensor, forwarded.Body.GetProperty("sensorId").GetGuid());
        Assert.True(forwarded.Body.GetProperty("value").GetBoolean());
        Assert.Equal("2026-10-24T10:00:00Z", forwarded.Body.GetProperty("recordedAt").GetString());
        Assert.Equal(JsonValueKind.String, forwarded.Body.GetProperty("receivedAt").ValueKind);
        Assert.Equal("Bearer token-1", forwarded.Authorization);
    }

    [Fact]
    public async Task DuLieuSai_Tra400_VaKhongGuiSangBackend()
    {
        using var client = Client(Token("service"));

        var response = await client.PostAsync($"/api/v1/readings/{Sensor}", Body("""{"value": "maybe"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error");
        Assert.Equal("INVALID_READING", error.GetProperty("code").GetString());
        await Task.Delay(200);
        Assert.Equal(0, _backend.ReadingCalls);
    }

    [Fact]
    public async Task KhongCoToken_Tra401_TokenNguoiDung_Tra403()
    {
        using var anonymous = Client(null);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsync($"/api/v1/readings/{Sensor}", Body("""{"value": true}"""))).StatusCode);

        using var viewer = Client(Token("viewer"));
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.PostAsync($"/api/v1/readings/{Sensor}", Body("""{"value": true}"""))).StatusCode);
    }

    [Fact]
    public async Task TokenCuaTenantKhac_BiChan()
    {
        using var client = Client(Token("service"));
        await client.PostAsync($"/api/v1/readings/{Sensor}", Body("""{"value": true}"""));
        await _backend.NextReadingAsync();

        using var other = Client(Token("service", Guid.NewGuid()));
        var response = await other.PostAsync($"/api/v1/readings/{Sensor}", Body("""{"value": true}"""));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BackendTamNgung_ThiGiuLuotDocVaThuLai_KhongMatDuLieu()
    {
        _backend.ReadingResponses.Enqueue(HttpStatusCode.ServiceUnavailable);
        _backend.ReadingResponses.Enqueue(HttpStatusCode.ServiceUnavailable);
        using var client = Client(Token("service"));

        await client.PostAsync($"/api/v1/readings/{Sensor}", Body("""{"value": true, "recordedAt": "2026-10-24T10:00:00Z"}"""));

        // Hai lần 503 rồi mới tới lần thành công — cùng một lượt đọc được gửi ba lần.
        for (var i = 0; i < 3; i++) await _backend.NextReadingAsync();
        var status = await WaitForStatusAsync(client, s => s.GetProperty("forwarded").GetInt64() == 1);
        Assert.Equal(2, status.GetProperty("retries").GetInt64());
        Assert.Equal(0, status.GetProperty("queueLength").GetInt32());
    }

    [Fact]
    public async Task BackendBaoCamBienKhongTonTai_ThiBoQua_KhongThuLai()
    {
        _backend.ReadingResponses.Enqueue(HttpStatusCode.NotFound);
        using var client = Client(Token("service"));

        await client.PostAsync($"/api/v1/readings/{Guid.NewGuid()}", Body("""{"value": true}"""));

        await _backend.NextReadingAsync();
        var status = await WaitForStatusAsync(client, s => s.GetProperty("refusedByBackend").GetInt64() == 1);
        Assert.Equal(0, status.GetProperty("retries").GetInt64());
        Assert.Equal(1, _backend.ReadingCalls);
    }

    [Fact]
    public async Task Backend401_ThiXinTokenMoiRoiGuiLai()
    {
        _backend.ReadingResponses.Enqueue(HttpStatusCode.Unauthorized);
        using var client = Client(Token("service"));

        await client.PostAsync($"/api/v1/readings/{Sensor}", Body("""{"value": false}"""));

        Assert.Equal("Bearer token-1", (await _backend.NextReadingAsync()).Authorization);
        Assert.Equal("Bearer token-2", (await _backend.NextReadingAsync()).Authorization);
        await WaitForStatusAsync(client, s => s.GetProperty("forwarded").GetInt64() == 1);
    }

    private static async Task<JsonElement> WaitForStatusAsync(HttpClient client, Func<JsonElement, bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var status = await client.GetFromJsonAsync<JsonElement>("/api/v1/status");
            if (condition(status) || DateTime.UtcNow > deadline) return status;
            await Task.Delay(50);
        }
    }

    // Backend giả: cấp token đánh số tăng dần, trả mã trạng thái theo kịch bản cho mỗi lượt đọc.
    private sealed class FakeBackend : HttpMessageHandler
    {
        private int _tokenCount;
        private int _readingCalls;
        private readonly System.Threading.Channels.Channel<(JsonElement Body, string? Authorization)> _readings =
            System.Threading.Channels.Channel.CreateUnbounded<(JsonElement, string?)>();

        public ConcurrentQueue<HttpStatusCode> ReadingResponses { get; } = new();
        public int ReadingCalls => _readingCalls;

        public async Task<(JsonElement Body, string? Authorization)> NextReadingAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            return await _readings.Reader.ReadAsync(timeout.Token);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/token")
            {
                var n = Interlocked.Increment(ref _tokenCount);
                return Json(HttpStatusCode.OK, new { accessToken = $"token-{n}", expiresAt = DateTime.UtcNow.AddHours(12), tenantId = Tenant });
            }

            if (path == "/api/v1/iot/readings")
            {
                Interlocked.Increment(ref _readingCalls);
                var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone();
                var status = ReadingResponses.TryDequeue(out var scripted) ? scripted : HttpStatusCode.Accepted;
                await _readings.Writer.WriteAsync((body, request.Headers.Authorization?.ToString()), ct);
                return new HttpResponseMessage(status) { Content = new StringContent("{}") };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, object body) =>
            new(status) { Content = JsonContent.Create(body) };
    }
}
