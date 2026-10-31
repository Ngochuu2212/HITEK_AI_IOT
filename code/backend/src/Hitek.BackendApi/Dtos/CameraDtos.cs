using System.ComponentModel.DataAnnotations;
using Hitek.BackendApi.Domain;

namespace Hitek.BackendApi.Dtos;

// RTSP: camera IP (rtsp://, rtsps://) hoặc luồng HTTP. FILE: video mô phỏng trong thư mục
// video của hệ thống (đề án: "kết nối camera IP qua RTSP hoặc sử dụng video file để mô phỏng").
public enum CameraSourceType
{
    Rtsp,
    File
}

// SourceUrl có thể chứa tài khoản camera (rtsp://user:pass@...), nên chỉ trả cho quản trị viên.
public record CameraDto(
    Guid Id,
    string Name,
    Guid AreaId,
    string SourceType,
    string SourceUrl,
    string Status,
    int AiImageSize,
    DateTime? LastSeenAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record SaveCameraRequest(
    [Required, MaxLength(255)] string Name,
    Guid AreaId,
    CameraSourceType SourceType,
    [Required, MaxLength(1000)] string SourceUrl,
    int? AiImageSize = null);

// Ngưng camera thay vì xoá: xoá dòng camera sẽ xoá lan truyền toàn bộ sự kiện AI và bằng
// chứng của nó (khoá ngoại ON DELETE CASCADE trong schema AI), làm gãy lịch sử cảnh báo.
public record CameraStatusRequest(bool Enabled);

// Video mô phỏng có thể chọn làm nguồn camera; UsedByCamera là camera đang dùng file đó.
public record VideoFileDto(string Name, long SizeBytes, string? UsedByCamera);

public static class CameraMapping
{
    public static CameraDto ToDto(this Camera c) => new(
        c.Id, c.Name, c.LocationId, c.SourceType.ToUpperInvariant(), c.SourceUrl, c.Status.ToUpperInvariant(),
        c.AiImageSize, c.LastSeenAt, c.CreatedAt, c.UpdatedAt);
}
