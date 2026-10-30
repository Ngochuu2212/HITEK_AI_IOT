namespace Hitek.BackendApi.Domain;

// Khoá định danh để chống ghi trùng. Cần thiết vì cả hai nguồn đều gửi lại được:
// AI Service tự retry khi Backend lỗi, còn MQTT QoS 1 bảo đảm "ít nhất một lần"
// nên bản tin lặp là chuyện bình thường theo thiết kế, không phải sự cố.
public static class EventDedupKey
{
    // Bên AI Service, một dòng ai_event ứng với cả một lần vi phạm và được cập nhật dần
    // theo từng khung hình (last_seen_at tăng dần). Nên id ai_event thôi là chưa đủ: gửi lại
    // đúng một lần cập nhật thì trùng cả id lẫn mốc thời gian, còn một lần cập nhật mới của
    // cùng vụ vi phạm thì lệch mốc thời gian và phải được ghi nhận như một sự kiện mới.
    public static string ForAi(Guid? aiEventId, Guid cameraId, DateTime occurredAt, string objectType) =>
        aiEventId is { } id
            ? $"ai:{id}:{occurredAt.Ticks}"
            // Chưa có aiEventId thì dựng khoá tự nhiên từ chính nội dung sự kiện. Một retry
            // gửi lại y nguyên payload nên trùng khoá; còn hai lần detect khác nhau luôn lệch
            // occurredAt, nên không bị gộp nhầm thành một.
            : $"ai:{cameraId}:{occurredAt.Ticks}:{objectType}";

    // Event của IoT sinh ra từ một reading đã chống trùng sẵn, nên mượn luôn id reading.
    public static string ForIot(Guid sensorReadingId) => $"iot:{sensorReadingId}";
}
