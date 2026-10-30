namespace Hitek.BackendApi.Domain;

// Một luật kết hợp nguồn của Risk Engine (đối tượng RiskRule, đề án mục 5.1). Lưu thành dữ
// liệu thay vì viết cứng trong code để quản trị viên tenant bật/tắt, đổi mức, thêm luật mới
// qua API mà không phải sửa code hay khởi động lại.
//
// Một luật khớp khi mọi điều kiện đã khai báo đều đúng; điều kiện bỏ trống thì không xét.
// Mức cảnh báo là mức cao nhất trong các luật đang khớp.
public class RiskRule : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    // Mã ngắn để tham chiếu, ví dụ "R3". Duy nhất trong một tenant.
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
    public AlertLevel Level { get; set; }

    // Điều kiện 1: có ít nhất ngần này nguồn độc lập cùng hoạt động.
    public int? MinSources { get; set; }

    // Điều kiện 2: các nguồn này phải cùng đang hoạt động, lưu dạng "AI,DOOR".
    public string? RequiredSources { get; set; }

    // Điều kiện 3: chỉ áp dụng ngoài khung giờ cho phép [bắt đầu, kết thúc), tính theo giờ UTC.
    public bool OutsideAllowedHoursOnly { get; set; }
    public int? AllowedStartUtcHour { get; set; }
    public int? AllowedEndUtcHour { get; set; }

    public int SortOrder { get; set; }
}
