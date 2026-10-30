using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hitek.BackendApi.Data;
using Hitek.BackendApi.Domain;

namespace Hitek.BackendApi.Tests;

// Dữ liệu đi vào tới cảnh báo, gọi đúng như IoT Service và AI Service sẽ gọi qua HTTP.
public sealed class IngestApiTests : IDisposable
{
    private readonly BackendApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    private async Task<HttpClient> AiServiceAsync() =>
        _api.ClientWithToken(await _api.ServiceTokenAsync("ai-service", "ai-service-dev-secret"));

    [Fact]
    public async Task BaLuotDocTrueLienTiep_TaoCanhBaoMedium_DashboardDocDuoc()
    {
        using var iot = await _api.ServiceClientAsync();
        await iot.PostReadingsAsync(SeedIds.SensorPir, BackendApiFactory.RecentUtc(), true, true, true);

        using var viewer = await _api.UserClientAsync(BackendApiFactory.ViewerEmail);
        var page = await (await viewer.GetAsync("/api/v1/alerts")).JsonAsync();

        Assert.Equal(1, page.GetProperty("total").GetInt32());
        var alert = page.GetProperty("items")[0];
        Assert.Equal("MEDIUM", alert.GetProperty("level").GetString());
        Assert.Equal("OPEN", alert.GetProperty("handledStatus").GetString());
        Assert.Equal(1, alert.GetProperty("sourceCount").GetInt32());
        Assert.Equal(new[] { "PIR" }, alert.GetProperty("sources").Strings());
        Assert.Equal(JsonValueKind.Null, alert.GetProperty("endedAt").ValueKind);
    }

    [Fact]
    public async Task MocThoiGianKemMuiGio_DuocQuyVeUtc_CaKhiGhiLanKhiLoc()
    {
        using var iot = await _api.ServiceClientAsync();

        // 17:00 giờ Việt Nam là 10:00 UTC. Trước khi sửa, chuỗi có offset khác "Z" làm
        // Npgsql từ chối ghi và trả lỗi 500.
        var response = await iot.PostAsJsonAsync("/api/v1/iot/readings",
            new { sensorId = SeedIds.SensorPir, value = true, recordedAt = "2026-10-24T17:00:00+07:00" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using var viewer = await _api.UserClientAsync(BackendApiFactory.ViewerEmail);
        var events = await (await viewer.GetAsync($"/api/v1/events?deviceId={SeedIds.SensorPir}")).JsonAsync();
        var ev = Assert.Single(events.GetProperty("items").EnumerateArray());
        Assert.Equal("2026-10-24T10:00:00.0000000Z", ev.GetProperty("occurredAt").GetString());
        Assert.Equal("PIR", ev.GetProperty("source").GetString());

        async Task<int> CountBetween(string from, string to) =>
            (await (await viewer.GetAsync($"/api/v1/events?from={from}&to={to}")).JsonAsync())
            .GetProperty("total").GetInt32();

        Assert.Equal(1, await CountBetween("2026-10-24T09:59:59Z", "2026-10-24T10:00:01Z"));
        Assert.Equal(1, await CountBetween("2026-10-24T16:59:59%2B07:00", "2026-10-24T17:00:01%2B07:00"));
        Assert.Equal(0, await CountBetween("2026-10-24T10:00:01Z", "2026-10-24T11:00:00Z"));
    }

    [Fact]
    public async Task BanTinGuiLai_TheoQos1_ChiGhiMotLan()
    {
        using var iot = await _api.ServiceClientAsync();
        var reading = new { sensorId = SeedIds.SensorDoor, value = true, recordedAt = "2026-10-24T10:00:00Z" };

        Assert.Equal(HttpStatusCode.Accepted, (await iot.PostAsJsonAsync("/api/v1/iot/readings", reading)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await iot.PostAsJsonAsync("/api/v1/iot/readings", reading)).StatusCode);

        using var viewer = await _api.UserClientAsync(BackendApiFactory.ViewerEmail);
        var events = await (await viewer.GetAsync($"/api/v1/events?deviceId={SeedIds.SensorDoor}")).JsonAsync();
        Assert.Equal(1, events.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task LuotDocFalse_KhongTaoSuKien_NhungVanTinhLaCamBienConSong()
    {
        using var iot = await _api.ServiceClientAsync();
        await iot.PostReadingsAsync(SeedIds.SensorBeam, BackendApiFactory.RecentUtc(), false);

        using var viewer = await _api.UserClientAsync(BackendApiFactory.ViewerEmail);
        var events = await (await viewer.GetAsync($"/api/v1/events?deviceId={SeedIds.SensorBeam}")).JsonAsync();
        Assert.Equal(0, events.GetProperty("total").GetInt32());

        var sensor = await (await viewer.GetAsync($"/api/v1/sensors/{SeedIds.SensorBeam}")).JsonAsync();
        Assert.Equal("ONLINE", sensor.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, sensor.GetProperty("lastSeenAt").ValueKind);
    }

    [Fact]
    public async Task CamBienKhongTonTai_Tra404()
    {
        using var iot = await _api.ServiceClientAsync();

        var response = await iot.PostAsJsonAsync("/api/v1/iot/readings",
            new { sensorId = Guid.NewGuid(), value = true, recordedAt = DateTime.UtcNow }, BackendApiFactory.Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("SENSOR_NOT_FOUND", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task DuLieuSaiDinhDang_TraLoiValidationTheoKhungLoiChung()
    {
        using var iot = await _api.ServiceClientAsync();

        var response = await iot.PostAsJsonAsync("/api/v1/iot/readings",
            new { sensorId = SeedIds.SensorPir, value = true, recordedAt = "hom-qua" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (await response.JsonAsync()).GetProperty("error");
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
        Assert.Contains("recordedAt", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task SuKienAi_TraVeAlertId_CanhBaoGhiNguonAiVaTruyVetDuoc()
    {
        using var ai = await AiServiceAsync();

        var response = await ai.PostAiEventAsync(await _api.NewAiEventAsync());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var alertId = (await response.JsonAsync()).GetProperty("alertId").GetGuid();

        using var viewer = await _api.UserClientAsync(BackendApiFactory.ViewerEmail);
        var alert = await (await viewer.GetAsync($"/api/v1/alerts/{alertId}")).JsonAsync();
        Assert.Equal("MEDIUM", alert.GetProperty("level").GetString());
        Assert.Equal(new[] { "AI" }, alert.GetProperty("sources").Strings());

        var link = Assert.Single((await (await viewer.GetAsync($"/api/v1/alerts/{alertId}/events")).JsonAsync()).EnumerateArray());
        Assert.Equal("AI", link.GetProperty("source").GetString());
        Assert.Equal(TestSchema.CameraWarehouse, link.GetProperty("cameraId").GetGuid());
        Assert.Equal("MEDIUM", link.GetProperty("levelAtLink").GetString());
        Assert.Equal(0.9, link.GetProperty("confidence").GetDouble());

        async Task<int> CountAlerts(string query) =>
            (await (await viewer.GetAsync($"/api/v1/alerts?{query}")).JsonAsync()).GetProperty("total").GetInt32();

        Assert.Equal(1, await CountAlerts("source=AI"));
        Assert.Equal(0, await CountAlerts("source=PIR"));
        Assert.Equal(1, await CountAlerts($"deviceId={TestSchema.CameraWarehouse}"));
        Assert.Equal(0, await CountAlerts($"deviceId={SeedIds.SensorPir}"));
        Assert.Equal(1, await CountAlerts("level=MEDIUM&status=OPEN"));
    }

    [Fact]
    public async Task SuKienAiGuiLai_TraCungAlertId_KhongGhiTrung()
    {
        using var ai = await AiServiceAsync();
        var aiEventId = await _api.NewAiEventAsync();
        var occurredAt = DateTime.UtcNow;

        var first = await (await ai.PostAiEventAsync(aiEventId, occurredAt: occurredAt)).JsonAsync();
        var retry = await ai.PostAiEventAsync(aiEventId, occurredAt: occurredAt);

        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        Assert.Equal(first.GetProperty("alertId").GetGuid(), (await retry.JsonAsync()).GetProperty("alertId").GetGuid());

        using var viewer = await _api.UserClientAsync(BackendApiFactory.ViewerEmail);
        var events = await (await viewer.GetAsync("/api/v1/events?source=AI")).JsonAsync();
        Assert.Equal(1, events.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task SuKienAiDuoiNguongTinCay_KhongTaoCanhBao()
    {
        using var ai = await AiServiceAsync();

        // AI gửi mọi phát hiện, Risk Engine mới là nơi lọc theo ngưỡng 0.6.
        var response = await ai.PostAiEventAsync(await _api.NewAiEventAsync(), confidence: 0.3);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await response.JsonAsync()).GetProperty("alertId").ValueKind);
    }

    [Fact]
    public async Task CameraCuaTenantKhac_BiTuChoi()
    {
        using var ai = await AiServiceAsync();

        var response = await ai.PostAiEventAsync(Guid.NewGuid(), cameraId: TestSchema.CameraOtherTenant);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("CAMERA_NOT_FOUND", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task KhuVucCuaTenantKhac_BiTuChoi()
    {
        using var ai = await AiServiceAsync();

        var response = await ai.PostAiEventAsync(Guid.NewGuid(), areaId: TestSchema.AreaOtherTenant);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("AREA_NOT_FOUND", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task CuaMoCungLucCameraThayNguoi_LenCritical_VaGhiLaiVetNangMuc()
    {
        using var iot = await _api.ServiceClientAsync();
        await iot.PostReadingsAsync(SeedIds.SensorDoor, BackendApiFactory.RecentUtc(), true, true, true);

        using var ai = await AiServiceAsync();
        var alertId = (await (await ai.PostAiEventAsync(await _api.NewAiEventAsync())).JsonAsync()).GetProperty("alertId").GetGuid();

        using var viewer = await _api.UserClientAsync(BackendApiFactory.ViewerEmail);
        var alert = await (await viewer.GetAsync($"/api/v1/alerts/{alertId}")).JsonAsync();
        Assert.Equal("CRITICAL", alert.GetProperty("level").GetString());
        Assert.Equal(2, alert.GetProperty("sourceCount").GetInt32());
        Assert.Equal(new[] { "AI", "DOOR" }, alert.GetProperty("sources").Strings());

        // Cửa mở trước nên cảnh báo mở ở mức Medium; sự kiện AI tới sau đẩy lên Critical.
        var trace = (await (await viewer.GetAsync($"/api/v1/alerts/{alertId}/events")).JsonAsync())
            .EnumerateArray()
            .Select(l => (l.GetProperty("source").GetString()!, l.GetProperty("levelAtLink").GetString()!))
            .ToList();
        Assert.Equal(new[] { ("DOOR", "MEDIUM"), ("AI", "CRITICAL") }, trace);
    }

    [Fact]
    public async Task BangChung_GomCaLoaiGanThangCanhBaoLanLoaiGanQuaSuKienAi()
    {
        using var ai = await AiServiceAsync();
        var aiEventId = await _api.NewAiEventAsync();
        var alertId = (await (await ai.PostAiEventAsync(aiEventId)).JsonAsync()).GetProperty("alertId").GetGuid();

        var snapshot = Guid.NewGuid();
        var clip = Guid.NewGuid();
        var unrelated = Guid.NewGuid();
        var capturedAt = DateTime.UtcNow;
        await _api.WithDbAsync(async db =>
        {
            db.Evidence.AddRange(
                // AI Service đã kịp nhận alertId và gắn thẳng vào bằng chứng.
                new Evidence
                {
                    Id = snapshot, TenantId = TestSchema.TenantA, AlertId = alertId, AiEventId = aiEventId,
                    CameraId = TestSchema.CameraWarehouse, EvidenceType = "snapshot", MimeType = "image/jpeg",
                    FileSize = 48_213, CapturedAt = capturedAt, Status = "stored"
                },
                // Clip ghi xong trước khi có alertId, chỉ nối được qua sự kiện AI.
                new Evidence
                {
                    Id = clip, TenantId = TestSchema.TenantA, AiEventId = aiEventId,
                    CameraId = TestSchema.CameraWarehouse, EvidenceType = "video_clip", MimeType = "video/mp4",
                    CapturedAt = capturedAt.AddSeconds(1), DurationSeconds = 10, Status = "stored"
                },
                new Evidence
                {
                    Id = unrelated, TenantId = TestSchema.TenantA, AiEventId = Guid.NewGuid(),
                    CameraId = TestSchema.CameraWarehouse, EvidenceType = "snapshot", MimeType = "image/jpeg",
                    CapturedAt = capturedAt
                });
            await db.SaveChangesAsync();
        });

        using var viewer = await _api.UserClientAsync(BackendApiFactory.ViewerEmail);
        var evidence = (await (await viewer.GetAsync($"/api/v1/alerts/{alertId}/evidence")).JsonAsync())
            .EnumerateArray().ToList();

        Assert.Equal(new[] { snapshot, clip }, evidence.Select(e => e.GetProperty("id").GetGuid()));
        Assert.Equal($"http://localhost:8010/api/evidence/{snapshot}/media", evidence[0].GetProperty("mediaUrl").GetString());
        Assert.Equal("video_clip", evidence[1].GetProperty("evidenceType").GetString());
        Assert.Equal(10, evidence[1].GetProperty("durationSeconds").GetDouble());
    }

    [Fact]
    public async Task NguoiVanHanh_XacNhanRoiDongXuLy_SauDoKhongSuaDuocNua()
    {
        using var iot = await _api.ServiceClientAsync();
        await iot.PostReadingsAsync(SeedIds.SensorPir, BackendApiFactory.RecentUtc(), true, true, true);

        using var operatorClient = await _api.UserClientAsync(BackendApiFactory.OperatorEmail);
        var alertId = (await (await operatorClient.GetAsync("/api/v1/alerts")).JsonAsync())
            .GetProperty("items")[0].GetProperty("id").GetGuid();
        var url = $"/api/v1/alerts/{alertId}";

        var acknowledged = await operatorClient.PatchAsJsonAsync(url, new { handledStatus = "ACKNOWLEDGED", note = "Đã cử bảo vệ xuống kho" });
        Assert.Equal(HttpStatusCode.OK, acknowledged.StatusCode);
        var body = await acknowledged.JsonAsync();
        Assert.Equal("ACKNOWLEDGED", body.GetProperty("handledStatus").GetString());
        Assert.Equal("Đã cử bảo vệ xuống kho", body.GetProperty("note").GetString());

        var closed = await operatorClient.PatchAsJsonAsync(url, new { handledStatus = "CLOSED", note = "Nhân viên kho làm thêm giờ" });
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);

        var reopened = await operatorClient.PatchAsJsonAsync(url, new { handledStatus = "OPEN" });
        Assert.Equal(HttpStatusCode.Conflict, reopened.StatusCode);
        Assert.Equal("ALERT_ALREADY_CLOSED", await reopened.ErrorCodeAsync());
    }

    [Fact]
    public async Task TrangThaiXuLyKhongHopLe_TraLoiValidation()
    {
        using var operatorClient = await _api.UserClientAsync(BackendApiFactory.OperatorEmail);

        var response = await operatorClient.PatchAsJsonAsync($"/api/v1/alerts/{Guid.NewGuid()}", new { handledStatus = "DONE" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR", await response.ErrorCodeAsync());
    }
}
