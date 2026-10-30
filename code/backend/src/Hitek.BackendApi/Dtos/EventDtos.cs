using Hitek.BackendApi.Domain;

namespace Hitek.BackendApi.Dtos;

public record EventDto(
    Guid Id,
    EventSource Source,
    string? EventType,
    Guid AreaId,
    DateTime OccurredAt,
    double? Confidence,
    Guid? SensorId,
    Guid? CameraId,
    Guid? AiEventId,
    Guid? SensorReadingId);

public enum DeviceKind
{
    Camera,
    Sensor
}

// Một dòng trong danh sách thiết bị (đề án mục 4.4): camera và cảm biến chung một hình
// dạng, để Dashboard hiển thị một bảng duy nhất với trạng thái và lần nhận dữ liệu cuối.
public record DeviceDto(
    Guid Id,
    DeviceKind Kind,
    string Name,
    string Type,
    Guid AreaId,
    string Status,
    DateTime? LastSeenAt);
