using System.ComponentModel.DataAnnotations;

namespace Hitek.BackendApi.Dtos;

// Lượt đọc đã được IoT Service chuẩn hoá: giá trị nhị phân, mốc thời gian UTC.
// ReceivedAt là lúc IoT Service nhận bản tin, tách khỏi RecordedAt là lúc thiết bị đo.
public record SensorReadingRequest(Guid SensorId, bool Value, DateTime RecordedAt, DateTime? ReceivedAt = null);

// Payload AI Service gửi khi phát hiện vi phạm (vùng cấm, lảng vảng, đông người...).
// AreaId bắt buộc là một giá trị lấy từ GET /api/v1/areas, sai là bị từ chối ngay.
public record AiEventRequest(
    Guid CameraId,
    [MaxLength(100)] string ObjectType,
    [Range(0.0, 1.0)] double Confidence,
    BoundingBoxDto? BoundingBox,
    Guid AreaId,
    DateTime OccurredAt,
    // Id bản ghi AI_EVENT bên AI Service, để lần ngược về dữ liệu gốc và bằng chứng. Có id
    // thì loại sự kiện lấy luôn từ ai_event.event_type, khỏi phải gửi kèm.
    Guid? AiEventId,
    // Chỉ dùng khi không có AiEventId, ví dụ lúc thử bằng Postman.
    [MaxLength(100)] string? EventType = null);

// X, Y là tâm khung theo pixel (không phải góc trên trái) — đã thống nhất với Hoàng Hà.
public record BoundingBoxDto(int X, int Y, int W, int H);

// Phản hồi của POST /api/v1/events. AI Service gắn alertId vào ai_event và evidence;
// cơ chế chống chụp bằng chứng trùng bên đó tra theo giá trị này.
// alertId có thể null khi sự kiện chưa đủ làm phát sinh cảnh báo, ví dụ độ tin cậy
// dưới ngưỡng và cũng không có nguồn nào khác đang hoạt động trong khu vực.
public record AiEventAcceptedResponse(Guid? AlertId);
