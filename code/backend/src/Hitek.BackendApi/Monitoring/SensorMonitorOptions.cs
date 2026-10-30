namespace Hitek.BackendApi.Monitoring;

public class SensorMonitorOptions
{
    // Quá bằng này giây không nhận được bản tin nào thì coi như cảm biến mất kết nối.
    // Bộ giả lập gửi cách nhau 2-5 giây, nên 30 giây là đã lỡ nhiều lượt liên tiếp,
    // đủ chắc để không báo nhầm chỉ vì một lần trễ mạng.
    public int OfflineAfterSeconds { get; set; } = 30;

    // Chu kỳ quét. Nhỏ hơn ngưỡng trên để phát hiện không bị trễ thêm một chu kỳ nữa.
    public int ScanIntervalSeconds { get; set; } = 10;
}
