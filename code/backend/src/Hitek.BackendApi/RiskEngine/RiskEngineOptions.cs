namespace Hitek.BackendApi.RiskEngine;

// Tham số kỹ thuật của Risk Engine, dùng chung cho mọi tenant. Còn luật kết hợp nguồn và
// mức cảnh báo là dữ liệu riêng từng tenant, quản lý qua /api/v1/risk-rules.
public class RiskEngineOptions
{
    // Consecutive true SensorReadings needed before an IoT source counts as "active".
    public int DebounceCount { get; set; } = 3;

    // An AI detection counts as "active" if one landed within this many seconds ago.
    public int AiActiveWindowSeconds { get; set; } = 3;

    // Risk Engine's own confidence cutoff — AI Service sends every detection unfiltered.
    public double MinConfidence { get; set; } = 0.6;

    // How long an area must show zero active sources before an open Alert actually closes.
    public int CooldownSeconds { get; set; } = 60;

    // AI Service gửi lại cùng một vụ vi phạm theo từng khung hình. Ghi tối đa một sự kiện
    // mỗi ngần này giây cho mỗi ai_event: vẫn đủ dày để nguồn AI không rơi khỏi cửa sổ
    // AiActiveWindowSeconds, mà bảng event không phình ra hàng chục dòng mỗi giây.
    public int AiEventMinIntervalSeconds { get; set; } = 1;

    // "Khung giờ cho phép" mặc định (giờ UTC) gán cho luật R2 khi tạo bộ luật cho tenant mới.
    // 01:00-11:00 UTC là 08:00-18:00 giờ Việt Nam. Đổi sau đó thì sửa luật qua API.
    public int AllowedStartUtcHour { get; set; } = 1;
    public int AllowedEndUtcHour { get; set; } = 11;
}
