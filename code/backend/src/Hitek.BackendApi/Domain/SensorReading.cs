namespace Hitek.BackendApi.Domain;

// Dữ liệu thô từ cảm biến. Risk Engine đọc thẳng bảng này (chứ không đọc Event)
// vì cửa sổ trượt chống nhiễu cần cả tín hiệu âm, mà Event chỉ lưu tín hiệu dương.
public class SensorReading : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SensorId { get; set; }
    public bool Value { get; set; }

    // Hai mốc khác nhau có chủ ý: RecordedAt do thiết bị ghi lúc đo, ReceivedAt là lúc
    // IoT Service nhận được bản tin. Hiệu hai mốc chính là độ trễ đường truyền.
    public DateTime RecordedAt { get; set; }
    public DateTime? ReceivedAt { get; set; }

    public Sensor? Sensor { get; set; }
}
