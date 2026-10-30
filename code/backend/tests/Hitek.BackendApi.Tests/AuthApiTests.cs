using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Hitek.BackendApi.Auth;
using Hitek.BackendApi.Data;
using Hitek.BackendApi.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Hitek.BackendApi.Tests;

// Xác thực và phân quyền đi qua HTTP thật: đăng nhập, token service, chính sách theo vai trò.
public sealed class AuthApiTests : IDisposable
{
    private readonly BackendApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task KhongCoToken_ThiTra401KemKhungLoiChung()
    {
        using var client = _api.CreateClient();

        var response = await client.GetAsync("/api/v1/alerts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("UNAUTHORIZED", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task TokenKyBangKhoaKhac_ThiBiTuChoi()
    {
        // Token giả mạo đủ claim admin nhưng ký bằng khoá không phải của Backend.
        var forged = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "hitek-backend-api",
            Audience = "hitek-ai-iot",
            Subject = new ClaimsIdentity([
                new Claim(HitekClaims.TenantId, TestSchema.TenantA.ToString()),
                new Claim(HitekClaims.Role, HitekRoles.Admin)
            ]),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes("khoa-cua-ke-gia-mao-dai-hon-32-byte-cho-du")),
                SecurityAlgorithms.HmacSha256)
        });
        using var client = _api.ClientWithToken(forged);

        var response = await client.GetAsync("/api/v1/alerts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("UNAUTHORIZED", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task TokenHetHan_ThiTraMaTokenExpired()
    {
        // Phát token bằng đúng khoá và cấu hình của Backend, nhưng lùi đồng hồ về hai giờ
        // trước: token sống 60 phút nên đã hết hạn từ một giờ trước.
        var tokens = new TokenService(
            _api.Services.GetRequiredService<IOptions<JwtOptions>>(),
            _api.Services.GetRequiredService<IOptions<AuthOptions>>(),
            new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(-2)));
        var expired = tokens.ForUser(
            new AppUser { Id = Guid.NewGuid(), Email = BackendApiFactory.ViewerEmail, FullName = "Viewer" },
            new TenantMember { TenantId = TestSchema.TenantA, Role = HitekRoles.Viewer });
        using var client = _api.ClientWithToken(expired.AccessToken);

        var response = await client.GetAsync("/api/v1/alerts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("TOKEN_EXPIRED", await response.ErrorCodeAsync());
    }

    [Theory]
    [InlineData(BackendApiFactory.AdminEmail, "sai-mat-khau")]
    [InlineData("khong-ton-tai@demo.local", BackendApiFactory.Password)]
    [InlineData(BackendApiFactory.DisabledEmail, BackendApiFactory.Password)]
    public async Task DangNhapSai_HoacTaiKhoanBiKhoa_ThiCungMotThongBao(string email, string password)
    {
        using var client = _api.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("INVALID_CREDENTIALS", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task DangNhapDung_ThiNhanTokenVaMeTraDungTenantVaiTro()
    {
        using var anonymous = _api.CreateClient();

        // Email gõ hoa thường lẫn lộn, có khoảng trắng — vẫn phải khớp.
        var login = await anonymous.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "  Admin@Demo.Local ", password = BackendApiFactory.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var body = await login.JsonAsync();
        Assert.Equal("Bearer", body.GetProperty("tokenType").GetString());
        Assert.Equal("admin", body.GetProperty("role").GetString());
        Assert.Equal(TestSchema.TenantA, body.GetProperty("tenantId").GetGuid());
        Assert.Equal(BackendApiFactory.AdminEmail, body.GetProperty("user").GetProperty("email").GetString());
        Assert.EndsWith("Z", body.GetProperty("expiresAt").GetString());

        using var client = _api.ClientWithToken(body.GetProperty("accessToken").GetString()!);
        var me = await (await client.GetAsync("/api/v1/auth/me")).JsonAsync();

        Assert.Equal("user", me.GetProperty("tokenType").GetString());
        Assert.Equal("admin", me.GetProperty("role").GetString());
        Assert.Equal(TestSchema.TenantA, me.GetProperty("tenantId").GetGuid());
        Assert.Equal(BackendApiFactory.AdminEmail, me.GetProperty("email").GetString());
    }

    [Fact]
    public async Task NguoiThuocNhieuTenant_PhaiChonTenantKhiDangNhap()
    {
        using var client = _api.CreateClient();

        var withoutTenant = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = BackendApiFactory.MultiTenantEmail, password = BackendApiFactory.Password });
        Assert.Equal(HttpStatusCode.BadRequest, withoutTenant.StatusCode);
        Assert.Equal("TENANT_REQUIRED", await withoutTenant.ErrorCodeAsync());

        var notMember = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = BackendApiFactory.ViewerEmail, password = BackendApiFactory.Password, tenantId = TestSchema.TenantB });
        Assert.Equal(HttpStatusCode.Forbidden, notMember.StatusCode);
        Assert.Equal("TENANT_ACCESS_DENIED", await notMember.ErrorCodeAsync());

        var chosen = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = BackendApiFactory.MultiTenantEmail, password = BackendApiFactory.Password, tenantId = TestSchema.TenantB });
        Assert.Equal(HttpStatusCode.OK, chosen.StatusCode);
        Assert.Equal(TestSchema.TenantB, (await chosen.JsonAsync()).GetProperty("tenantId").GetGuid());
    }

    [Fact]
    public async Task ServiceSaiSecret_ThiBiTuChoi()
    {
        using var client = _api.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/token",
            new { clientId = "iot-service", clientSecret = "doan-mo" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("INVALID_CLIENT", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task ServiceDungSecret_ThiNhanTokenVaiTroService()
    {
        using var client = _api.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/token",
            new { clientId = "ai-service", clientSecret = "ai-service-dev-secret" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.JsonAsync();
        Assert.Equal("service", body.GetProperty("role").GetString());
        Assert.Equal(TestSchema.TenantA, body.GetProperty("tenantId").GetGuid());
    }

    [Fact]
    public async Task Viewer_KhongDuocCapNhatCanhBao_OperatorThiDuocQuaCuaPhanQuyen()
    {
        var missingAlert = Guid.NewGuid();
        var patch = new { handledStatus = "ACKNOWLEDGED" };

        using var viewer = await _api.UserClientAsync(BackendApiFactory.ViewerEmail);
        var denied = await viewer.PatchAsJsonAsync($"/api/v1/alerts/{missingAlert}", patch);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("FORBIDDEN", await denied.ErrorCodeAsync());

        // Operator qua được bước phân quyền, tới controller mới biết cảnh báo không tồn tại.
        using var operatorClient = await _api.UserClientAsync(BackendApiFactory.OperatorEmail);
        var notFound = await operatorClient.PatchAsJsonAsync($"/api/v1/alerts/{missingAlert}", patch);
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Equal("ALERT_NOT_FOUND", await notFound.ErrorCodeAsync());
    }

    [Fact]
    public async Task TaiKhoanNguoiDung_KhongDuocGhiDuLieuCamBien()
    {
        using var admin = await _api.UserClientAsync(BackendApiFactory.AdminEmail);

        var response = await admin.PostAsJsonAsync("/api/v1/iot/readings",
            new { sensorId = SeedIds.SensorPir, value = true, recordedAt = DateTime.UtcNow }, BackendApiFactory.Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TokenService_KhongDocDuocCanhBao_NhungDocDuocDanhSachKhuVuc()
    {
        using var service = await _api.ServiceClientAsync();

        var alerts = await service.GetAsync("/api/v1/alerts");
        Assert.Equal(HttpStatusCode.Forbidden, alerts.StatusCode);

        var areas = await service.GetAsync("/api/v1/areas");
        Assert.Equal(HttpStatusCode.OK, areas.StatusCode);
        var area = Assert.Single((await areas.JsonAsync()).EnumerateArray());
        Assert.Equal(TestSchema.AreaWarehouse, area.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task ChiAdminMoiThemDuocCamBien()
    {
        var request = new { name = "PIR-Kho-02", type = "PIR", areaId = TestSchema.AreaWarehouse };

        using var viewer = await _api.UserClientAsync(BackendApiFactory.ViewerEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/v1/sensors", request)).StatusCode);

        using var admin = await _api.UserClientAsync(BackendApiFactory.AdminEmail);
        var created = await admin.PostAsJsonAsync("/api/v1/sensors", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var body = await created.JsonAsync();
        Assert.Equal("PIR", body.GetProperty("type").GetString());
        Assert.Equal("OFFLINE", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Health_KhongCanToken()
    {
        using var client = _api.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", (await response.JsonAsync()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Cors_ChiMoChoOriginCuaDashboard()
    {
        using var client = _api.CreateClient();

        async Task<HttpResponseMessage> Preflight(string origin)
        {
            var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/alerts");
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", "GET");
            request.Headers.Add("Access-Control-Request-Headers", "authorization");
            return await client.SendAsync(request);
        }

        var allowed = await Preflight("http://localhost:5173");
        Assert.Equal("http://localhost:5173", allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());

        var foreign = await Preflight("http://evil.example");
        Assert.False(foreign.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Swagger_SinhDuocTaiLieuApi()
    {
        using var client = _api.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var paths = (await response.JsonAsync()).GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/alerts/{id}/evidence", out _));
        Assert.True(paths.TryGetProperty("/api/v1/auth/login", out _));
    }
}
