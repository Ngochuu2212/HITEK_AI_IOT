using System.Net;
using System.Text.Json;
using Hitek.BackendApi.Data;

namespace Hitek.BackendApi.Tests;

// Loại sự kiện (đề án mục 4.3, 4.5) và cách Backend nhận các lần cập nhật của cùng một vụ
// vi phạm từ AI Service.
public sealed class EventTypeApiTests : IDisposable
{
    private readonly BackendApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    private async Task<HttpClient> AiServiceAsync() =>
        _api.ClientWithToken(await _api.ServiceTokenAsync("ai-service", "ai-service-dev-secret"));

    private async Task<JsonElement> GetAsync(HttpClient client, string url) => await (await client.GetAsync(url)).JsonAsync();

    [Fact]
    public async Task LoaiSuKien_AiLayTuAiEvent_CamBienSuyRaTuLoaiCamBien_VaLocDuoc()
    {
        using var ai = await AiServiceAsync();
        (await ai.PostAiEventAsync(await _api.NewAiEventAsync(eventType: "loitering"))).EnsureSuccessStatusCode();
        using var iot = await _api.ServiceClientAsync();
        await iot.PostReadingsAsync(SeedIds.SensorDoor, BackendApiFactory.RecentUtc(), true, true, true);

        using var viewer = await _api.UserClientAsync(BackendApiFactory.ViewerEmail);
        Assert.Equal(1, (await GetAsync(viewer, "/api/v1/events?eventType=loitering")).GetProperty("total").GetInt32());
        Assert.Equal(3, (await GetAsync(viewer, "/api/v1/events?eventType=DOOR_OPENED")).GetProperty("total").GetInt32());

        var alert = (await GetAsync(viewer, "/api/v1/alerts")).GetProperty("items")[0];
        Assert.Equal(new[] { "door_opened", "loitering" }, alert.GetProperty("eventTypes").Strings());
        Assert.Equal(1, (await GetAsync(viewer, "/api/v1/alerts?eventType=loitering")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await GetAsync(viewer, "/api/v1/alerts?eventType=crowd_limit")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task CapNhatMoiCuaCungVuViPham_DuocGhiThemSuKien_TrongMotGiayThiLayMau()
    {
        using var ai = await AiServiceAsync();
        var aiEventId = await _api.NewAiEventAsync();
        var start = DateTime.UtcNow.AddSeconds(-5);

        // Cùng ai_event, last_seen_at tăng dần như AI Service cập nhật theo khung hình.
        foreach (var offsetMs in new[] { 0, 300, 600, 1500, 3000 })
            (await ai.PostAiEventAsync(aiEventId, occurredAt: start.AddMilliseconds(offsetMs))).EnsureSuccessStatusCode();

        using var viewer = await _api.UserClientAsync(BackendApiFactory.ViewerEmail);
        var events = await GetAsync(viewer, "/api/v1/events?source=AI");

        // 0 ms, 1500 ms và 3000 ms được ghi; 300 ms và 600 ms nằm trong một giây sau lần ghi trước.
        Assert.Equal(3, events.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task AiEventCuaTenantKhac_BiTuChoi()
    {
        using var ai = await AiServiceAsync();
        var foreign = await _api.NewAiEventAsync(cameraId: TestSchema.CameraOtherTenant, tenantId: TestSchema.TenantB);

        var response = await ai.PostAiEventAsync(foreign);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("AI_EVENT_NOT_FOUND", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task CameraKhongKhopAiEvent_BiTuChoi()
    {
        var otherCamera = Guid.NewGuid();
        await _api.WithDbAsync(async db =>
        {
            db.Cameras.Add(new Domain.Camera
            {
                Id = otherCamera, TenantId = TestSchema.TenantA, LocationId = TestSchema.AreaWarehouse,
                Name = "Cam-Cong", SourceType = "rtsp", Status = "active"
            });
            await db.SaveChangesAsync();
        });
        using var ai = await AiServiceAsync();

        var response = await ai.PostAiEventAsync(await _api.NewAiEventAsync(), cameraId: otherCamera);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("AI_EVENT_CAMERA_MISMATCH", await response.ErrorCodeAsync());
    }
}
