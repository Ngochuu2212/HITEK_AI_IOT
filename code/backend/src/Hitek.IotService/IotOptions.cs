namespace Hitek.IotService;

public class MqttOptions
{
    // Tắt được để chạy riêng đường HTTP (kiểm thử, hoặc nơi không có broker).
    public bool Enabled { get; set; } = true;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public string ClientId { get; set; } = "hitek-iot-service";

    // hitek/<sensorId>/reading — mã cảm biến nằm trong topic, không nằm trong nội dung tin.
    public string ReadingTopicFilter { get; set; } = "hitek/+/reading";
}

public class BackendApiOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5250";

    // Thông tin client credentials để xin token service tại POST /api/v1/auth/token.
    public string ClientId { get; set; } = "iot-service";
    public string ClientSecret { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 10;
}

public class IngestOptions
{
    // Hàng đợi trong bộ nhớ giữa phía nhận tin và phía gửi sang Backend. Đầy thì bỏ bản
    // ghi cũ nhất: với dữ liệu cảm biến, trạng thái mới quan trọng hơn trạng thái đã qua.
    public int QueueCapacity { get; set; } = 1000;

    // Múi giờ áp cho mốc thời gian thiết bị gửi mà không kèm múi giờ, ví dụ
    // "2026-10-24T17:00:00". Theo quy ước chung mặc định coi là UTC.
    public string DeviceTimeZone { get; set; } = "UTC";

    // Thiết bị lệch đồng hồ quá mức này về phía tương lai thì từ chối, vì một mốc thời gian
    // sai như vậy làm hỏng thứ tự lượt đọc mà cơ chế chống nhiễu của Risk Engine dựa vào.
    public int MaxFutureSkewSeconds { get; set; } = 300;

    // Gửi sang Backend lỗi tạm thời thì thử lại với thời gian chờ tăng dần tới mức trần.
    public int RetryInitialDelayMilliseconds { get; set; } = 1000;
    public int RetryMaxDelaySeconds { get; set; } = 30;
}

// Dùng để kiểm tra token của bên gọi đường HTTP dự phòng: cùng khoá ký và cùng issuer với
// Backend, vì token do Backend phát.
public class JwtOptions
{
    public string Issuer { get; set; } = "hitek-backend-api";
    public string Audience { get; set; } = "hitek-ai-iot";
    public string SigningKey { get; set; } = string.Empty;
}
