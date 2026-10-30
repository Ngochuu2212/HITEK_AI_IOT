using System.Net;
using Hitek.BackendApi.Data;

namespace Hitek.BackendApi.Tests;

// Các API đọc phục vụ Web Dashboard: thiết bị, tổng quan, lịch sử, và cách ly theo tenant.
public sealed class QueryApiTests : IDisposable
{
    private readonly BackendApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task DanhSachThietBi_GopCameraVaCamBien_ChiCuaTenantMinh()
    {
        using var viewerA = await _api.UserClientAsync(BackendApiFactory.ViewerEmail);
        var page = await (await viewerA.GetAsync("/api/v1/devices")).JsonAsync();

        Assert.Equal(4, page.GetProperty("total").GetInt32());
        var items = page.GetProperty("items").EnumerateArray().ToList();

        var camera = items[0];
        Assert.Equal("CAMERA", camera.GetProperty("kind").GetString());
        Assert.Equal(TestSchema.CameraWarehouse, camera.GetProperty("id").GetGuid());
        Assert.Equal("RTSP", camera.GetProperty("type").GetString());
        Assert.Equal("ACTIVE", camera.GetProperty("status").GetString());

        Assert.All(items.Skip(1), s => Assert.Equal("SENSOR", s.GetProperty("kind").GetString()));
        Assert.Equal(new[] { "BEAM", "DOOR", "PIR" }, items.Skip(1).Select(s => s.GetProperty("type").GetString()));

        var cameras = await (await viewerA.GetAsync("/api/v1/devices?kind=CAMERA")).JsonAsync();
        Assert.Equal(1, cameras.GetProperty("total").GetInt32());

        using var viewerB = await _api.UserClientAsync(BackendApiFactory.ViewerOtherTenantEmail);
        var other = await (await viewerB.GetAsync("/api/v1/devices")).JsonAsync();
        var onlyCamera = Assert.Single(other.GetProperty("items").EnumerateArray());
        Assert.Equal(TestSchema.CameraOtherTenant, onlyCamera.GetProperty("id").GetGuid());
        Assert.Equal("OFFLINE", onlyCamera.GetProperty("status").GetString());
    }

    [Fact]
    public async Task TongQuanDashboard_DemDungTheoTrangThaiVaMucDo()
    {
        using var iot = await _api.ServiceClientAsync();
        await iot.PostReadingsAsync(SeedIds.SensorPir, BackendApiFactory.RecentUtc(), true, true, true);

        using var viewer = await _api.UserClientAsync(BackendApiFactory.ViewerEmail);
        var summary = await (await viewer.GetAsync("/api/v1/dashboard/summary")).JsonAsync();

        var cameras = summary.GetProperty("cameras");
        Assert.Equal(1, cameras.GetProperty("total").GetInt32());
        Assert.Equal(1, cameras.GetProperty("active").GetInt32());

        var sensors = summary.GetProperty("sensors");
        Assert.Equal(3, sensors.GetProperty("total").GetInt32());
        Assert.Equal(1, sensors.GetProperty("online").GetInt32());
        Assert.Equal(2, sensors.GetProperty("offline").GetInt32());

        var events = summary.GetProperty("eventsToday");
        Assert.Equal(3, events.GetProperty("total").GetInt32());
        Assert.Equal(3, events.GetProperty("pir").GetInt32());

        var alerts = summary.GetProperty("alertsToday");
        Assert.Equal(1, alerts.GetProperty("total").GetInt32());
        Assert.Equal(1, alerts.GetProperty("medium").GetInt32());

        Assert.Equal(1, summary.GetProperty("openAlerts").GetInt32());
        var recent = Assert.Single(summary.GetProperty("recentAlerts").EnumerateArray());
        Assert.Equal(new[] { "PIR" }, recent.GetProperty("sources").Strings());
    }

    [Fact]
    public async Task TenantKhac_KhongThayDuLieu_KeCaKhiTuKhaiHeaderTenant()
    {
        using var iot = await _api.ServiceClientAsync();
        await iot.PostReadingsAsync(SeedIds.SensorPir, BackendApiFactory.RecentUtc(), true, true, true);

        using var viewerA = await _api.UserClientAsync(BackendApiFactory.ViewerEmail);
        var alertId = (await (await viewerA.GetAsync("/api/v1/alerts")).JsonAsync())
            .GetProperty("items")[0].GetProperty("id").GetGuid();

        // Header X-Tenant-Id từng là cách chọn tenant trước khi có JWT. Giờ tenant chỉ lấy
        // từ token, nên tự khai tenant của người khác cũng không đọc được gì.
        using var viewerB = await _api.UserClientAsync(BackendApiFactory.ViewerOtherTenantEmail);
        viewerB.DefaultRequestHeaders.Add("X-Tenant-Id", TestSchema.TenantA.ToString());

        Assert.Equal(0, (await (await viewerB.GetAsync("/api/v1/alerts")).JsonAsync()).GetProperty("total").GetInt32());
        Assert.Equal(0, (await (await viewerB.GetAsync("/api/v1/events")).JsonAsync()).GetProperty("total").GetInt32());

        var direct = await viewerB.GetAsync($"/api/v1/alerts/{alertId}");
        Assert.Equal(HttpStatusCode.NotFound, direct.StatusCode);
        Assert.Equal("ALERT_NOT_FOUND", await direct.ErrorCodeAsync());

        var summary = await (await viewerB.GetAsync("/api/v1/dashboard/summary")).JsonAsync();
        Assert.Equal(0, summary.GetProperty("openAlerts").GetInt32());
        Assert.Equal(0, summary.GetProperty("sensors").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task LichSuSuKien_LocTheoNguon_TheoThietBi_VaPhanTrang()
    {
        var start = BackendApiFactory.RecentUtc();
        using var iot = await _api.ServiceClientAsync();
        await iot.PostReadingsAsync(SeedIds.SensorPir, start, true, true, true);
        await iot.PostReadingsAsync(SeedIds.SensorDoor, start, true, true, true);

        using var ai = _api.ClientWithToken(await _api.ServiceTokenAsync("ai-service", "ai-service-dev-secret"));
        (await ai.PostAiEventAsync(await _api.NewAiEventAsync())).EnsureSuccessStatusCode();

        using var viewer = await _api.UserClientAsync(BackendApiFactory.ViewerEmail);

        async Task<int> Count(string query) =>
            (await (await viewer.GetAsync($"/api/v1/events?{query}")).JsonAsync()).GetProperty("total").GetInt32();

        Assert.Equal(7, await Count(""));
        Assert.Equal(3, await Count("source=DOOR"));
        Assert.Equal(1, await Count("source=AI"));
        Assert.Equal(3, await Count($"deviceId={SeedIds.SensorPir}"));
        Assert.Equal(1, await Count($"deviceId={TestSchema.CameraWarehouse}"));
        Assert.Equal(7, await Count($"areaId={TestSchema.AreaWarehouse}"));

        var secondPage = await (await viewer.GetAsync("/api/v1/events?page=2&pageSize=2")).JsonAsync();
        Assert.Equal(2, secondPage.GetProperty("page").GetInt32());
        Assert.Equal(2, secondPage.GetProperty("items").GetArrayLength());
        Assert.Equal(7, secondPage.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("/api/v1/alerts?level=EXTREME")]
    [InlineData("/api/v1/events?from=hom-qua")]
    [InlineData("/api/v1/devices?kind=DRONE")]
    public async Task ThamSoTruyVanSai_TraLoiValidationTheoKhungLoiChung(string url)
    {
        using var viewer = await _api.UserClientAsync(BackendApiFactory.ViewerEmail);

        var response = await viewer.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR", await response.ErrorCodeAsync());
    }
}
