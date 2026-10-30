namespace Hitek.BackendApi.Domain;

public enum EventSource
{
    Ai,
    Pir,
    Door,
    Beam
}

// Bảng chuẩn hoá dùng chung cho cả AI lẫn IoT: chỉ giữ tham chiếu tới bản ghi gốc,
// dữ liệu thô nằm ở SensorReading (IoT) và AI_EVENT (AI Service).
// Đặt tên EventRecord (ánh xạ sang bảng "Event") vì `event` là từ khoá của C#.
public class EventRecord : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public EventSource Source { get; set; }

    // Loại sự kiện (đề án mục 4.3 và 4.5). Nguồn AI giữ nguyên event_type bên AI Service
    // (person_intrusion, loitering, crowd_limit...), nguồn cảm biến suy ra từ loại cảm biến.
    // Khác Source: Source cho biết ai báo, EventType cho biết chuyện gì xảy ra.
    public string? EventType { get; set; }

    public Guid AreaId { get; set; }
    public DateTime OccurredAt { get; set; }

    // Tách làm hai cột thay vì một cột RefId đa nghĩa: mỗi bản ghi chỉ điền đúng một cột,
    // nhờ vậy đặt được khoá ngoại thật và nhìn cột là biết nguồn, khỏi phải suy từ Source.
    public Guid? SensorReadingId { get; set; }
    public Guid? AiEventId { get; set; }

    // Thiết bị phát sinh sự kiện: cảm biến với nguồn IoT, camera với nguồn AI. Bản sao có
    // chủ ý — đề án mục 4.5 yêu cầu tra cứu lịch sử lọc được theo thiết bị, giữ sẵn ở đây
    // thì lọc chỉ cần một điều kiện, khỏi nối qua sensor_reading hay ai_event.
    public Guid? SensorId { get; set; }
    public Guid? CameraId { get; set; }

    // Khoá chống ghi trùng, duy nhất trong phạm vi một tenant — xem EventDedupKey.
    public string DedupKey { get; set; } = string.Empty;

    // Bản sao có chủ ý từ AI_EVENT (thang 0-1, chỉ có khi Source = Ai). Giữ tại đây để
    // Risk Engine khỏi phải join ở đường nóng — nó chạy lại sau mỗi sự kiện gửi lên.
    public double? Confidence { get; set; }

    public SensorReading? SensorReading { get; set; }
}

// Loại sự kiện của nguồn cảm biến. Viết thường có gạch dưới cho cùng kiểu với giá trị
// event_type bên AI Service, để Dashboard lọc chung một danh sách.
public static class SensorEventTypes
{
    public const string MotionDetected = "motion_detected";
    public const string DoorOpened = "door_opened";
    public const string BeamBroken = "beam_broken";

    public static string For(SensorType type) => type switch
    {
        SensorType.Pir => MotionDetected,
        SensorType.Door => DoorOpened,
        SensorType.Beam => BeamBroken,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Loại cảm biến chưa có loại sự kiện tương ứng.")
    };
}
