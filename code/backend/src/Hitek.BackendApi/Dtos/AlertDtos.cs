using System.ComponentModel.DataAnnotations;
using Hitek.BackendApi.Domain;

namespace Hitek.BackendApi.Dtos;

public record AlertDto(
    Guid Id,
    Guid AreaId,
    // Mức hiện tại, về LOW khi cảnh báo đã đóng.
    AlertLevel Level,
    // Mức cao nhất từng đạt — dùng cái này khi tra cứu lịch sử theo mức độ.
    AlertLevel PeakLevel,
    int SourceCount,
    // Mọi nguồn từng góp sự kiện vào cảnh báo này — "nguồn phát sinh" ở đề án mục 4.3.
    // Khác SourceCount: đó là số nguồn đang cùng hoạt động ngay lúc này.
    IReadOnlyList<EventSource> Sources,
    // Các loại sự kiện đã góp vào cảnh báo, ví dụ person_intrusion, door_opened.
    IReadOnlyList<string> EventTypes,
    // Mã luật quyết định mức hiện tại, ví dụ ["R3"]. Rỗng khi cảnh báo đã đóng.
    IReadOnlyList<string> TriggeredRules,
    DateTime StartedAt,
    DateTime? EndedAt,
    AlertHandledStatus HandledStatus,
    string? Note,
    string? HandledBy,
    DateTime? HandledAt);

public record UpdateAlertRequest(AlertHandledStatus HandledStatus, [MaxLength(2000)] string? Note);

// Một dòng trong lịch sử truy vết: sự kiện nào đã đẩy cảnh báo lên mức nào, lúc nào.
public record AlertEventDto(
    Guid EventId,
    EventSource Source,
    string? EventType,
    DateTime OccurredAt,
    double? Confidence,
    Guid? SensorId,
    Guid? CameraId,
    AlertLevel LevelAtLink,
    DateTime LinkedAt);

// Mô tả một ảnh/clip bằng chứng. Tệp nằm ở kho lưu trữ của AI Service; MediaUrl trỏ thẳng
// sang endpoint tải tệp bên đó, Backend không trung chuyển dữ liệu nhị phân.
public record EvidenceDto(
    Guid Id,
    string EvidenceType,
    string MimeType,
    long? FileSize,
    DateTime CapturedAt,
    double? DurationSeconds,
    Guid CameraId,
    Guid AiEventId,
    string? Status,
    string? MediaUrl);

// Nguồn và loại sự kiện đã góp vào một cảnh báo, suy ra từ bảng nối AlertEvent.
public sealed record AlertOrigins(IReadOnlyList<EventSource> Sources, IReadOnlyList<string> EventTypes)
{
    public static readonly AlertOrigins None = new([], []);
}

public static class AlertMapping
{
    public static AlertDto ToDto(this Alert a, AlertOrigins origins) =>
        new(a.Id, a.AreaId, a.Level, a.PeakLevel, a.SourceCount, origins.Sources, origins.EventTypes,
            string.IsNullOrEmpty(a.TriggeredRules) ? [] : a.TriggeredRules.Split(','),
            a.StartedAt, a.EndedAt, a.HandledStatus, a.Note, a.HandledByName, a.HandledAt);
}
