using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Hitek.BackendApi.Tests;

// Quản lý camera: thêm, sửa, ngưng; camera mới hiện ngay trong danh sách thiết bị.
public sealed class CameraApiTests : IDisposable
{
    private readonly BackendApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    private static object Camera(string name, string sourceType, string sourceUrl, Guid? areaId = null) => new
    {
        name,
        areaId = areaId ?? TestSchema.AreaWarehouse,
        sourceType,
        sourceUrl
    };

    [Fact]
    public async Task AdminThemCameraVideo_HienTrongDanhSachThietBi_VaGhiNhatKy()
    {
        using var admin = await _api.UserClientAsync(BackendApiFactory.AdminEmail);

        var response = await admin.PostAsJsonAsync("/api/v1/cameras", Camera("  Cam-Bai-Xe ", "FILE", "kho.mp4"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.JsonAsync();
        Assert.Equal("Cam-Bai-Xe", created.GetProperty("name").GetString());
        Assert.Equal("FILE", created.GetProperty("sourceType").GetString());
        Assert.Equal("ACTIVE", created.GetProperty("status").GetString());

        using var viewer = await _api.UserClientAsync(BackendApiFactory.ViewerEmail);
        var devices = (await (await viewer.GetAsync("/api/v1/devices?kind=CAMERA")).JsonAsync()).GetProperty("items");
        var device = devices.EnumerateArray().Single(d => d.GetProperty("name").GetString() == "Cam-Bai-Xe");
        Assert.Equal("ACTIVE", device.GetProperty("status").GetString());

        var logs = (await (await admin.GetAsync("/api/v1/system-logs?category=CAMERA")).JsonAsync()).GetProperty("items");
        Assert.Contains("thêm camera Cam-Bai-Xe (video kho.mp4)", logs[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task ChiAdminDuocThemCamera()
    {
        using var op = await _api.UserClientAsync(BackendApiFactory.OperatorEmail);

        var response = await op.PostAsJsonAsync("/api/v1/cameras", Camera("Cam-X", "FILE", "kho.mp4"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await op.GetAsync("/api/v1/cameras/video-files")).StatusCode);
    }

    [Fact]
    public async Task DanhSachVideo_ChiFileVideo_KemCameraDangDung()
    {
        using var admin = await _api.UserClientAsync(BackendApiFactory.AdminEmail);
        await admin.PostAsJsonAsync("/api/v1/cameras", Camera("Cam-Cong", "FILE", "cong.avi"));

        var files = (await (await admin.GetAsync("/api/v1/cameras/video-files")).JsonAsync()).EnumerateArray().ToList();

        Assert.Equal(new[] { "cong.avi", "kho.mp4" }, files.Select(f => f.GetProperty("name").GetString()));
        Assert.Equal("Cam-Cong", files[0].GetProperty("usedByCamera").GetString());
        Assert.Equal(JsonValueKind.Null, files[1].GetProperty("usedByCamera").ValueKind);
    }

    [Theory]
    [InlineData("FILE", "../appsettings.json", "VIDEO_FILE_NOT_FOUND")]
    [InlineData("FILE", "ghi-chu.txt", "VIDEO_FILE_NOT_FOUND")]
    [InlineData("FILE", "khong-co.mp4", "VIDEO_FILE_NOT_FOUND")]
    [InlineData("RTSP", "camera-cong", "CAMERA_SOURCE_INVALID")]
    [InlineData("RTSP", "ftp://10.0.0.5/live", "CAMERA_SOURCE_INVALID")]
    public async Task NguonKhongHopLe_BiTuChoi(string sourceType, string sourceUrl, string code)
    {
        using var admin = await _api.UserClientAsync(BackendApiFactory.AdminEmail);

        var response = await admin.PostAsJsonAsync("/api/v1/cameras", Camera("Cam-Loi", sourceType, sourceUrl));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task TrungTenTrongKhuVuc_HoacTrungNguon_Bao409()
    {
        using var admin = await _api.UserClientAsync(BackendApiFactory.AdminEmail);

        var sameName = await admin.PostAsJsonAsync("/api/v1/cameras", Camera("Cam-Kho", "RTSP", "rtsp://10.0.0.9:554/live"));
        var sameSource = await admin.PostAsJsonAsync("/api/v1/cameras", Camera("Cam-Moi", "RTSP", "rtsp://mediamtx:8554/camera1"));

        Assert.Equal(HttpStatusCode.Conflict, sameName.StatusCode);
        Assert.Equal("CAMERA_NAME_TAKEN", await sameName.ErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Conflict, sameSource.StatusCode);
        Assert.Contains("Cam-Kho", (await sameSource.JsonAsync()).GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task SuaCamera_DoiNguonSangRtsp_KhongLoMatKhauTrongNhatKy()
    {
        using var admin = await _api.UserClientAsync(BackendApiFactory.AdminEmail);
        var id = (await (await admin.PostAsJsonAsync("/api/v1/cameras", Camera("Cam-A", "FILE", "kho.mp4"))).JsonAsync()).GetProperty("id").GetGuid();

        var response = await admin.PutAsJsonAsync($"/api/v1/cameras/{id}", Camera("Cam-A2", "RTSP", "rtsp://admin:bimat@10.0.0.7:554/stream1"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var camera = await (await admin.GetAsync($"/api/v1/cameras/{id}")).JsonAsync();
        Assert.Equal(("Cam-A2", "RTSP"), (camera.GetProperty("name").GetString(), camera.GetProperty("sourceType").GetString()));
        var log = (await (await admin.GetAsync("/api/v1/system-logs?category=CAMERA")).JsonAsync()).GetProperty("items")[0].GetProperty("message").GetString();
        Assert.Contains("rtsp://***@10.0.0.7:554/stream1", log);
        Assert.DoesNotContain("bimat", log);
    }

    [Fact]
    public async Task NgungRoiBatLaiCamera_DoiTrangThaiTrongDatabase()
    {
        using var admin = await _api.UserClientAsync(BackendApiFactory.AdminEmail);
        var id = TestSchema.CameraWarehouse;

        var stopped = await (await admin.PatchAsJsonAsync($"/api/v1/cameras/{id}/status", new { enabled = false })).JsonAsync();
        string? stored = null;
        await _api.WithDbAsync(async db => stored = (await db.Cameras.IgnoreQueryFilters().SingleAsync(c => c.Id == id)).Status);

        Assert.Equal("INACTIVE", stopped.GetProperty("status").GetString());
        Assert.Equal("inactive", stored);

        var started = await (await admin.PatchAsJsonAsync($"/api/v1/cameras/{id}/status", new { enabled = true })).JsonAsync();
        Assert.Equal("ACTIVE", started.GetProperty("status").GetString());
    }

    // ---------------------------------------------------------------- Tải video lên

    private static readonly byte[] Mp4Header = [0, 0, 0, 0x20, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 2, 0];

    private static MultipartFormDataContent VideoForm(string fileName, byte[] content)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(content), "file", fileName);
        return form;
    }

    [Fact]
    public async Task TaiVideoLen_DoiTenAnToan_HienTrongDanhSach_VaDungLamNguonCamera()
    {
        using var admin = await _api.UserClientAsync(BackendApiFactory.AdminEmail);

        var response = await admin.PostAsync("/api/v1/cameras/video-files", VideoForm("Bãi đỗ xe (sáng).mp4", Mp4Header));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var name = (await response.JsonAsync()).GetProperty("name").GetString();
        Assert.Equal("Bai_do_xe__sang.mp4", name);
        Assert.True(File.Exists(Path.Combine(_api.VideoDirectory, name!)));
        Assert.Empty(Directory.GetFiles(_api.VideoDirectory, "*.tmp"));

        var files = (await (await admin.GetAsync("/api/v1/cameras/video-files")).JsonAsync()).EnumerateArray();
        Assert.Contains(name, files.Select(f => f.GetProperty("name").GetString()));
        var camera = await admin.PostAsJsonAsync("/api/v1/cameras", Camera("Cam-Bai-Do", "FILE", name!));
        Assert.Equal(HttpStatusCode.Created, camera.StatusCode);
    }

    [Theory]
    [InlineData("virus.exe", "VIDEO_FILE_TYPE")]
    [InlineData("gia-mao.mp4", "VIDEO_FILE_INVALID")]
    public async Task TaiVideo_SaiDuoiHoacNoiDungKhongPhaiVideo_BiTuChoi(string fileName, string code)
    {
        using var admin = await _api.UserClientAsync(BackendApiFactory.AdminEmail);

        var response = await admin.PostAsync("/api/v1/cameras/video-files", VideoForm(fileName, "khong phai video"u8.ToArray()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await response.ErrorCodeAsync());
        Assert.False(File.Exists(Path.Combine(_api.VideoDirectory, fileName)));
        Assert.Empty(Directory.GetFiles(_api.VideoDirectory, "*.tmp"));
    }

    [Fact]
    public async Task TaiVideo_TrungTen_KhongGhiDe_VaChiAdminDuocTai()
    {
        using var admin = await _api.UserClientAsync(BackendApiFactory.AdminEmail);
        using var op = await _api.UserClientAsync(BackendApiFactory.OperatorEmail);

        var duplicate = await admin.PostAsync("/api/v1/cameras/video-files", VideoForm("kho.mp4", Mp4Header));
        var byOperator = await op.PostAsync("/api/v1/cameras/video-files", VideoForm("moi.mp4", Mp4Header));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(new byte[] { 0 }, File.ReadAllBytes(Path.Combine(_api.VideoDirectory, "kho.mp4")));
        Assert.Equal(HttpStatusCode.Forbidden, byOperator.StatusCode);
    }

    // ---------------------------------------------------------------- Độ chi tiết AI

    [Fact]
    public async Task DoChiTietAi_MacDinh640_DoiDuocSang1280_GiaTriLaBiTuChoi()
    {
        using var admin = await _api.UserClientAsync(BackendApiFactory.AdminEmail);
        var created = await (await admin.PostAsJsonAsync("/api/v1/cameras", Camera("Cam-Dong-Nguoi", "FILE", "cong.avi"))).JsonAsync();
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(640, created.GetProperty("aiImageSize").GetInt32());

        var update = new { name = "Cam-Dong-Nguoi", areaId = TestSchema.AreaWarehouse, sourceType = "FILE", sourceUrl = "cong.avi", aiImageSize = 1280 };
        var updated = await (await admin.PutAsJsonAsync($"/api/v1/cameras/{id}", update)).JsonAsync();

        Assert.Equal(1280, updated.GetProperty("aiImageSize").GetInt32());
        var bad = await admin.PutAsJsonAsync($"/api/v1/cameras/{id}", new { name = "Cam-Dong-Nguoi", areaId = TestSchema.AreaWarehouse, sourceType = "FILE", sourceUrl = "cong.avi", aiImageSize = 4000 });
        Assert.Equal("AI_IMAGE_SIZE_INVALID", await bad.ErrorCodeAsync());
    }

    [Fact]
    public async Task KhongThayCameraCuaTenantKhac()
    {
        using var admin = await _api.UserClientAsync(BackendApiFactory.AdminEmail);

        var response = await admin.PatchAsJsonAsync($"/api/v1/cameras/{TestSchema.CameraOtherTenant}/status", new { enabled = false });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
