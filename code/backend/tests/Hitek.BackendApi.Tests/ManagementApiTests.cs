using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hitek.BackendApi.Data;

namespace Hitek.BackendApi.Tests;

// Các chức năng quản lý: bảng luật cảnh báo, nhật ký hệ thống, người dùng, cảm biến.
public sealed class ManagementApiTests : IDisposable
{
    private readonly BackendApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    private static async Task<JsonElement> GetAsync(HttpClient client, string url) =>
        await (await client.GetAsync(url)).JsonAsync();

    // ---------------------------------------------------------------- Luật cảnh báo

    [Fact]
    public async Task BangLuat_MacDinhCoDuR1DenR3()
    {
        using var viewer = await _api.UserClientAsync(BackendApiFactory.ViewerEmail);

        var rules = (await GetAsync(viewer, "/api/v1/risk-rules")).EnumerateArray().ToList();

        Assert.Equal(new[] { "R1-1", "R1-2", "R1-3", "R2", "R3" }, rules.Select(r => r.GetProperty("code").GetString()));
        var r3 = rules.Single(r => r.GetProperty("code").GetString() == "R3");
        Assert.Equal(new[] { "AI", "DOOR" }, r3.GetProperty("requiredSources").Strings());
        Assert.Equal("CRITICAL", r3.GetProperty("level").GetString());
    }

    [Fact]
    public async Task AdminTatLuatR3_CanhBaoAiCongCuaChiConHigh_ViewerKhongSuaDuoc()
    {
        using var admin = await _api.UserClientAsync(BackendApiFactory.AdminEmail);
        var r3 = (await GetAsync(admin, "/api/v1/risk-rules")).EnumerateArray()
            .Single(r => r.GetProperty("code").GetString() == "R3");
        var update = new
        {
            name = r3.GetProperty("name").GetString(),
            enabled = false,
            level = "CRITICAL",
            requiredSources = new[] { "AI", "DOOR" },
            outsideAllowedHoursOnly = false,
            sortOrder = 50
        };

        using var viewer = await _api.UserClientAsync(BackendApiFactory.ViewerEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync($"/api/v1/risk-rules/{r3.GetProperty("id")}", update)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/v1/risk-rules/{r3.GetProperty("id")}", update)).StatusCode);

        using var iot = await _api.ServiceClientAsync();
        await iot.PostReadingsAsync(SeedIds.SensorDoor, BackendApiFactory.RecentUtc(), true, true, true);
        using var ai = _api.ClientWithToken(await _api.ServiceTokenAsync("ai-service", "ai-service-dev-secret"));
        var alertId = (await (await ai.PostAiEventAsync(await _api.NewAiEventAsync())).JsonAsync()).GetProperty("alertId").GetGuid();

        var alert = await GetAsync(viewer, $"/api/v1/alerts/{alertId}");
        Assert.Equal("HIGH", alert.GetProperty("level").GetString());
        Assert.Equal(new[] { "R1-2" }, alert.GetProperty("triggeredRules").Strings());
    }

    [Fact]
    public async Task ThemLuat_KhongDieuKienThiBiTuChoi_TrungMaThiXungDot()
    {
        using var admin = await _api.UserClientAsync(BackendApiFactory.AdminEmail);

        var noCondition = await admin.PostAsJsonAsync("/api/v1/risk-rules",
            new { code = "X1", name = "Luật rỗng", enabled = true, level = "HIGH" });
        Assert.Equal(HttpStatusCode.BadRequest, noCondition.StatusCode);
        Assert.Equal("VALIDATION_ERROR", await noCondition.ErrorCodeAsync());

        var rule = new { code = "x1", name = "PIR cùng Beam", enabled = true, level = "CRITICAL", requiredSources = new[] { "PIR", "BEAM" } };
        var created = await admin.PostAsJsonAsync("/api/v1/risk-rules", rule);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("X1", (await created.JsonAsync()).GetProperty("code").GetString());

        var duplicate = await admin.PostAsJsonAsync("/api/v1/risk-rules", rule);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("RISK_RULE_CODE_EXISTS", await duplicate.ErrorCodeAsync());

        Assert.Equal(6, (await GetAsync(admin, "/api/v1/risk-rules")).GetArrayLength());
    }

    // ---------------------------------------------------------------- Nhật ký và xử lý cảnh báo

    [Fact]
    public async Task NhatKy_GhiCamBienOnline_CanhBaoPhatSinh_VaNguoiXuLy()
    {
        using var iot = await _api.ServiceClientAsync();
        await iot.PostReadingsAsync(SeedIds.SensorPir, BackendApiFactory.RecentUtc(), true, true, true);

        using var operatorClient = await _api.UserClientAsync(BackendApiFactory.OperatorEmail);
        var alertId = (await GetAsync(operatorClient, "/api/v1/alerts")).GetProperty("items")[0].GetProperty("id").GetGuid();
        var patched = await (await operatorClient.PatchAsJsonAsync($"/api/v1/alerts/{alertId}",
            new { handledStatus = "ACKNOWLEDGED", note = "Đã kiểm tra" })).JsonAsync();
        Assert.Equal(BackendApiFactory.OperatorEmail, patched.GetProperty("handledBy").GetString());
        Assert.Equal(JsonValueKind.String, patched.GetProperty("handledAt").ValueKind);

        var sensorLogs = await GetAsync(operatorClient, "/api/v1/system-logs?category=SENSOR");
        Assert.Contains(sensorLogs.GetProperty("items").EnumerateArray(),
            l => l.GetProperty("entityId").GetGuid() == SeedIds.SensorPir);

        var alertLogs = (await GetAsync(operatorClient, $"/api/v1/system-logs?category=ALERT&entityId={alertId}"))
            .GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, alertLogs.Count);
        Assert.Equal(BackendApiFactory.OperatorEmail, alertLogs[0].GetProperty("actor").GetString());
        Assert.Equal("system", alertLogs[1].GetProperty("actor").GetString());

        using var viewer = await _api.UserClientAsync(BackendApiFactory.ViewerEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/v1/system-logs")).StatusCode);
    }

    // ---------------------------------------------------------------- Người dùng

    [Fact]
    public async Task AdminThemNguoiDung_NguoiDoDangNhapDuoc_VaChiThayNguoiCuaTenantMinh()
    {
        using var admin = await _api.UserClientAsync(BackendApiFactory.AdminEmail);

        var created = await admin.PostAsJsonAsync("/api/v1/users",
            new { email = "Bao.Ve@Demo.Local", fullName = "Bảo vệ ca đêm", password = "CaDem@2026", role = "operator" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("bao.ve@demo.local", (await created.JsonAsync()).GetProperty("email").GetString());

        var users = (await GetAsync(admin, "/api/v1/users")).EnumerateArray().Select(u => u.GetProperty("email").GetString()).ToList();
        Assert.Contains("bao.ve@demo.local", users);
        Assert.DoesNotContain(BackendApiFactory.ViewerOtherTenantEmail, users);

        using var anonymous = _api.CreateClient();
        var login = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { email = "bao.ve@demo.local", password = "CaDem@2026" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal("operator", (await login.JsonAsync()).GetProperty("role").GetString());

        var again = await admin.PostAsJsonAsync("/api/v1/users",
            new { email = "bao.ve@demo.local", fullName = "x", password = "CaDem@2026", role = "viewer" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("USER_ALREADY_MEMBER", await again.ErrorCodeAsync());
    }

    [Fact]
    public async Task DoiVaiTro_KhoaThanhVien_KhongTuSuaChinhMinh()
    {
        using var admin = await _api.UserClientAsync(BackendApiFactory.AdminEmail);
        var users = (await GetAsync(admin, "/api/v1/users")).EnumerateArray().ToList();
        Guid IdOf(string email) => users.Single(u => u.GetProperty("email").GetString() == email).GetProperty("id").GetGuid();

        var promoted = await admin.PatchAsJsonAsync($"/api/v1/users/{IdOf(BackendApiFactory.ViewerEmail)}", new { role = "operator" });
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);
        Assert.Equal("operator", (await promoted.JsonAsync()).GetProperty("role").GetString());

        var disabled = await admin.PatchAsJsonAsync($"/api/v1/users/{IdOf(BackendApiFactory.OperatorEmail)}", new { status = "disabled" });
        Assert.Equal("disabled", (await disabled.JsonAsync()).GetProperty("status").GetString());

        using var anonymous = _api.CreateClient();
        var blocked = await anonymous.PostAsJsonAsync("/api/v1/auth/login",
            new { email = BackendApiFactory.OperatorEmail, password = BackendApiFactory.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);

        var self = await admin.PatchAsJsonAsync($"/api/v1/users/{IdOf(BackendApiFactory.AdminEmail)}", new { role = "viewer" });
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        Assert.Equal("CANNOT_MODIFY_SELF", await self.ErrorCodeAsync());

        var badRole = await admin.PatchAsJsonAsync($"/api/v1/users/{IdOf(BackendApiFactory.ViewerEmail)}", new { role = "superuser" });
        Assert.Equal(HttpStatusCode.BadRequest, badRole.StatusCode);

        using var viewer = _api.ClientWithToken(await _api.UserTokenAsync(BackendApiFactory.MultiTenantEmail, TestSchema.TenantA));
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/v1/users")).StatusCode);
    }

    [Fact]
    public async Task DatLaiMatKhau_ChiChoTaiKhoanRiengTenant_VaNguoiDungTuDoiDuoc()
    {
        using var admin = await _api.UserClientAsync(BackendApiFactory.AdminEmail);
        var users = (await GetAsync(admin, "/api/v1/users")).EnumerateArray().ToList();
        Guid IdOf(string email) => users.Single(u => u.GetProperty("email").GetString() == email).GetProperty("id").GetGuid();

        var shared = await admin.PostAsJsonAsync($"/api/v1/users/{IdOf(BackendApiFactory.MultiTenantEmail)}/reset-password",
            new { newPassword = "MatKhauMoi@1" });
        Assert.Equal(HttpStatusCode.Conflict, shared.StatusCode);
        Assert.Equal("USER_IN_OTHER_TENANTS", await shared.ErrorCodeAsync());

        var reset = await admin.PostAsJsonAsync($"/api/v1/users/{IdOf(BackendApiFactory.ViewerEmail)}/reset-password",
            new { newPassword = "MatKhauMoi@1" });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        using var anonymous = _api.CreateClient();
        var login = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { email = BackendApiFactory.ViewerEmail, password = "MatKhauMoi@1" });
        var token = (await login.JsonAsync()).GetProperty("accessToken").GetString()!;

        using var viewer = _api.ClientWithToken(token);
        var wrong = await viewer.PutAsJsonAsync("/api/v1/auth/password", new { currentPassword = "sai", newPassword = "MatKhauKhac@2" });
        Assert.Equal("WRONG_PASSWORD", await wrong.ErrorCodeAsync());
        var changed = await viewer.PutAsJsonAsync("/api/v1/auth/password", new { currentPassword = "MatKhauMoi@1", newPassword = "MatKhauKhac@2" });
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
    }

    // ---------------------------------------------------------------- Cảm biến

    [Fact]
    public async Task CamBien_SuaTen_XemLichSuDoc_RoiGo()
    {
        using var iot = await _api.ServiceClientAsync();
        await iot.PostReadingsAsync(SeedIds.SensorBeam, new DateTime(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc), true, false, true);

        using var admin = await _api.UserClientAsync(BackendApiFactory.AdminEmail);
        var renamed = await admin.PutAsJsonAsync($"/api/v1/sensors/{SeedIds.SensorBeam}",
            new { name = "Beam-Cua-Sau", areaId = TestSchema.AreaWarehouse });
        Assert.Equal("Beam-Cua-Sau", (await renamed.JsonAsync()).GetProperty("name").GetString());

        var readings = await GetAsync(admin, $"/api/v1/sensors/{SeedIds.SensorBeam}/readings?pageSize=2");
        Assert.Equal(3, readings.GetProperty("total").GetInt32());
        var latest = readings.GetProperty("items")[0];
        Assert.Equal("2026-10-24T10:00:02.0000000Z", latest.GetProperty("recordedAt").GetString());
        Assert.Equal("BEAM", latest.GetProperty("sensorType").GetString());
        Assert.Equal(JsonValueKind.String, latest.GetProperty("receivedAt").ValueKind);

        var wrongArea = await admin.PutAsJsonAsync($"/api/v1/sensors/{SeedIds.SensorBeam}",
            new { name = "Beam", areaId = TestSchema.AreaOtherTenant });
        Assert.Equal("AREA_NOT_FOUND", await wrongArea.ErrorCodeAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/sensors/{SeedIds.SensorBeam}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/sensors/{SeedIds.SensorBeam}")).StatusCode);

        // Sự kiện đã sinh ra vẫn còn để lịch sử cảnh báo không bị gãy.
        Assert.Equal(2, (await GetAsync(admin, "/api/v1/events?source=BEAM")).GetProperty("total").GetInt32());
    }
}
