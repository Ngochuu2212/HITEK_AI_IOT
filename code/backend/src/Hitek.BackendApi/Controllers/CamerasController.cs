using Hitek.BackendApi.Auth;
using Hitek.BackendApi.Common;
using Hitek.BackendApi.Data;
using Hitek.BackendApi.Domain;
using Hitek.BackendApi.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hitek.BackendApi.Controllers;

// Quản lý camera (đề án mục 4.4). Ghi thẳng vào bảng camera dùng chung: Zone Service và Web
// Dashboard đọc cùng bảng nên thấy camera mới ngay, Edge Gateway hỏi lại danh sách vài giây
// một lần rồi tự mở luồng video. Thêm, sửa, ngưng camera là thay đổi cấu hình hệ thống nên
// chỉ quản trị viên tenant được làm; người vận hành xem camera qua GET /api/v1/devices.
[ApiController]
[Route("api/v1/cameras")]
[Authorize(Policy = AuthPolicies.Admin)]
public class CamerasController(
    HitekDbContext db,
    TimeProvider clock,
    IOptions<CameraOptions> options,
    IWebHostEnvironment env) : ControllerBase
{
    private static readonly string[] StreamSchemes = ["rtsp", "rtsps", "http", "https"];

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CameraDto>>> GetCameras(CancellationToken ct)
    {
        var cameras = await db.Cameras.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
        return Ok(cameras.Select(c => c.ToDto()).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CameraDto>> GetCamera(Guid id, CancellationToken ct) =>
        Ok((await FindCameraAsync(id, ct)).ToDto());

    // Video mô phỏng có sẵn để chọn khi thêm camera loại FILE, kèm camera đang dùng nó (mỗi
    // nguồn chỉ gắn với một camera trong tenant).
    [HttpGet("video-files")]
    public async Task<ActionResult<IReadOnlyList<VideoFileDto>>> GetVideoFiles(CancellationToken ct)
    {
        var directory = VideoDirectory();
        if (directory is null) return Ok(Array.Empty<VideoFileDto>());

        var usedBy = await db.Cameras.AsNoTracking()
            .Where(c => c.SourceType == "FILE")
            .ToDictionaryAsync(c => c.SourceUrl, c => c.Name, ct);
        var files = new DirectoryInfo(directory).EnumerateFiles()
            .Where(f => CameraOptions.VideoExtensions.Contains(f.Extension.ToLowerInvariant()))
            .OrderBy(f => f.Name)
            .Select(f => new VideoFileDto(f.Name, f.Length, usedBy.GetValueOrDefault(f.Name)))
            .ToList();
        return Ok(files);
    }

    // Tải video lên thư mục video mô phỏng để chọn làm nguồn camera. Không ghi đè file đã có:
    // file đó có thể đang là nguồn của một camera khác.
    [HttpPost("video-files")]
    [RequestSizeLimit(CameraOptions.MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = CameraOptions.MaxUploadBytes)]
    public async Task<ActionResult<VideoFileDto>> UploadVideoFile(IFormFile? file, CancellationToken ct)
    {
        var directory = VideoDirectory()
            ?? throw ApiException.BadRequest("VIDEO_DIRECTORY_MISSING",
                "Máy chủ chưa có thư mục video mô phỏng (Cameras:VideoDirectory).");
        if (file is null || file.Length == 0)
            throw ApiException.BadRequest("VIDEO_FILE_REQUIRED", "Chưa chọn file video (trường \"file\" của form).");

        var name = VideoFileStore.SafeName(file.FileName);
        if (!CameraOptions.VideoExtensions.Contains(Path.GetExtension(name)))
            throw ApiException.BadRequest("VIDEO_FILE_TYPE",
                $"Chỉ nhận video {string.Join(", ", CameraOptions.VideoExtensions)}. Nên dùng .mp4 (H.264).");
        var target = Path.Combine(directory, name);
        if (System.IO.File.Exists(target))
            throw ApiException.Conflict("VIDEO_FILE_EXISTS", $"Đã có video tên {name}. Đổi tên file rồi tải lại.");

        // Ghi ra file tạm cạnh đích rồi mới đổi tên, để Edge Gateway và danh sách video không bao
        // giờ thấy một file đang ghi dở.
        var temp = Path.Combine(directory, $".upload-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var output = System.IO.File.Create(temp))
                await file.CopyToAsync(output, ct);

            var head = new byte[189];
            int read;
            await using (var input = System.IO.File.OpenRead(temp))
                read = await input.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
            if (!VideoFileStore.LooksLikeVideo(head.AsSpan(0, read)))
                throw ApiException.BadRequest("VIDEO_FILE_INVALID", "Nội dung file không phải video hợp lệ.");

            System.IO.File.Move(temp, target);
        }
        finally
        {
            if (System.IO.File.Exists(temp)) System.IO.File.Delete(temp);
        }

        SystemLogWriter.Add(db, clock.GetUtcNow().UtcDateTime, SystemLogLevel.Info, SystemLogCategory.Camera,
            $"{User.DisplayName()} tải lên video {name} ({file.Length / 1048576.0:0.0} MB).", null, User.Actor());
        await db.SaveChangesAsync(ct);

        return Ok(new VideoFileDto(name, file.Length, null));
    }

    [HttpPost]
    public async Task<ActionResult<CameraDto>> CreateCamera(SaveCameraRequest request, CancellationToken ct)
    {
        var (name, sourceType, sourceUrl) = await ValidateAsync(request, null, ct);
        var now = clock.GetUtcNow().UtcDateTime;

        var camera = new Camera
        {
            Id = Guid.NewGuid(),
            LocationId = request.AreaId,
            Name = name,
            SourceType = sourceType,
            SourceUrl = sourceUrl,
            AiImageSize = request.AiImageSize ?? 640,
            Status = "active", // database tự đặt, xem HitekDbContext
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Cameras.Add(camera);
        SystemLogWriter.Add(db, now, SystemLogLevel.Info, SystemLogCategory.Camera,
            $"{User.DisplayName()} thêm camera {camera.Name} ({DescribeSource(sourceType, sourceUrl)}).",
            camera.Id, User.Actor());
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetCamera), new { id = camera.Id }, camera.ToDto());
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CameraDto>> UpdateCamera(Guid id, SaveCameraRequest request, CancellationToken ct)
    {
        var camera = await FindCameraAsync(id, ct);
        var (name, sourceType, sourceUrl) = await ValidateAsync(request, id, ct);

        var changes = new List<string>();
        if (camera.Name != name) changes.Add($"tên \"{camera.Name}\" → \"{name}\"");
        if (camera.LocationId != request.AreaId) changes.Add("khu vực");
        if (camera.SourceType != sourceType || camera.SourceUrl != sourceUrl)
            changes.Add($"nguồn → {DescribeSource(sourceType, sourceUrl)}");
        var aiImageSize = request.AiImageSize ?? camera.AiImageSize;
        if (camera.AiImageSize != aiImageSize) changes.Add($"độ chi tiết AI {camera.AiImageSize} → {aiImageSize}");
        if (changes.Count == 0) return Ok(camera.ToDto());

        var now = clock.GetUtcNow().UtcDateTime;
        camera.Name = name;
        camera.LocationId = request.AreaId;
        camera.SourceType = sourceType;
        camera.SourceUrl = sourceUrl;
        camera.AiImageSize = aiImageSize;
        camera.UpdatedAt = now;
        SystemLogWriter.Add(db, now, SystemLogLevel.Info, SystemLogCategory.Camera,
            $"{User.DisplayName()} sửa camera {camera.Name}: {string.Join(", ", changes)}.", camera.Id, User.Actor());
        await db.SaveChangesAsync(ct);

        return Ok(camera.ToDto());
    }

    // Ngưng: Edge Gateway đóng luồng video, AI không phân tích camera này nữa, nhưng sự kiện,
    // bằng chứng và cảnh báo cũ vẫn giữ nguyên. Bật lại thì chạy tiếp.
    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<CameraDto>> SetStatus(Guid id, CameraStatusRequest request, CancellationToken ct)
    {
        var camera = await FindCameraAsync(id, ct);
        var target = request.Enabled ? "active" : "inactive";
        var running = camera.Status is "active" or "offline";
        if (running == request.Enabled) return Ok(camera.ToDto());

        var now = clock.GetUtcNow().UtcDateTime;
        // Giá trị status lấy từ hai hằng chuỗi ở trên, không từ dữ liệu người dùng. Viết thẳng
        // vào câu lệnh để PostgreSQL tự ép sang kiểu enum camera_status.
        await db.Database.ExecuteSqlRawAsync(
            $"UPDATE camera SET status = '{target}', updated_at = {{0}} WHERE id = {{1}} AND tenant_id = {{2}}",
            [now, camera.Id, camera.TenantId], ct);

        camera.Status = target;
        camera.UpdatedAt = now;
        SystemLogWriter.Add(db, now, request.Enabled ? SystemLogLevel.Info : SystemLogLevel.Warning, SystemLogCategory.Camera,
            request.Enabled
                ? $"{User.DisplayName()} bật lại camera {camera.Name}."
                : $"{User.DisplayName()} ngưng camera {camera.Name}: dừng đọc video và phân tích AI.",
            camera.Id, User.Actor());
        await db.SaveChangesAsync(ct);

        return Ok(camera.ToDto());
    }

    private async Task<(string Name, string SourceType, string SourceUrl)> ValidateAsync(
        SaveCameraRequest request, Guid? cameraId, CancellationToken ct)
    {
        var name = request.Name.Trim();
        if (name.Length == 0)
            throw ApiException.BadRequest("CAMERA_NAME_REQUIRED", "Tên camera không được để trống.");
        if (!await db.Locations.AnyAsync(l => l.Id == request.AreaId, ct))
            throw ApiException.BadRequest("AREA_NOT_FOUND", "areaId không hợp lệ. Lấy danh sách hợp lệ tại GET /api/v1/areas.");

        if (request.AiImageSize is { } size && !CameraOptions.AiImageSizes.Contains(size))
            throw ApiException.BadRequest("AI_IMAGE_SIZE_INVALID",
                $"Độ chi tiết AI chỉ nhận {string.Join(", ", CameraOptions.AiImageSizes)}.");

        var sourceType = request.SourceType == CameraSourceType.File ? "FILE" : "RTSP";
        var sourceUrl = request.SourceType == CameraSourceType.File
            ? ValidateVideoFile(request.SourceUrl.Trim())
            : ValidateStreamUrl(request.SourceUrl.Trim());

        if (await db.Cameras.AnyAsync(c => c.LocationId == request.AreaId && c.Name == name && c.Id != cameraId, ct))
            throw ApiException.Conflict("CAMERA_NAME_TAKEN", $"Khu vực này đã có camera tên \"{name}\".");
        var owner = await db.Cameras.AsNoTracking()
            .Where(c => c.SourceUrl == sourceUrl && c.Id != cameraId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(ct);
        if (owner is not null)
            throw ApiException.Conflict("CAMERA_SOURCE_TAKEN", $"Nguồn này đang được camera \"{owner}\" dùng. Mỗi nguồn chỉ gắn với một camera.");

        return (name, sourceType, sourceUrl);
    }

    private static string ValidateStreamUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !StreamSchemes.Contains(uri.Scheme) || string.IsNullOrEmpty(uri.Host))
            throw ApiException.BadRequest("CAMERA_SOURCE_INVALID",
                "Địa chỉ camera phải dạng rtsp://máy-chủ:cổng/đường-dẫn (hoặc rtsps, http, https).");
        return url;
    }

    // Chỉ nhận tên file nằm trong thư mục video, không nhận đường dẫn: người dùng không chọn
    // được file tuỳ ý trên máy chủ.
    private string ValidateVideoFile(string fileName)
    {
        var directory = VideoDirectory()
            ?? throw ApiException.BadRequest("VIDEO_DIRECTORY_MISSING",
                "Máy chủ chưa có thư mục video mô phỏng (Cameras:VideoDirectory).");
        if (fileName.Length == 0 || fileName != Path.GetFileName(fileName)
            || !CameraOptions.VideoExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant())
            || !System.IO.File.Exists(Path.Combine(directory, fileName)))
            throw ApiException.BadRequest("VIDEO_FILE_NOT_FOUND",
                "Không có video này. Chọn một file trong GET /api/v1/cameras/video-files.");
        return fileName;
    }

    private string? VideoDirectory()
    {
        var configured = options.Value.VideoDirectory;
        if (string.IsNullOrWhiteSpace(configured)) return null;
        var full = Path.GetFullPath(Path.Combine(env.ContentRootPath, configured));
        return Directory.Exists(full) ? full : null;
    }

    private static string DescribeSource(string sourceType, string sourceUrl) =>
        sourceType == "FILE" ? $"video {sourceUrl}" : $"luồng {MaskCredentials(sourceUrl)}";

    // Không ghi mật khẩu camera vào nhật ký.
    private static string MaskCredentials(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.UserInfo)
            ? url.Replace(uri.UserInfo + "@", "***@")
            : url;

    private async Task<Camera> FindCameraAsync(Guid id, CancellationToken ct) =>
        await db.Cameras.FirstOrDefaultAsync(c => c.Id == id, ct)
        ?? throw ApiException.NotFound("CAMERA_NOT_FOUND", "Không tìm thấy camera với id đã cho.");
}
