namespace Hitek.BackendApi.Domain;

public enum SystemLogLevel
{
    Info,
    Warning,
    Error
}

public enum SystemLogCategory
{
    Sensor,
    Alert,
    RiskRule,
    User
}

// Nhật ký hệ thống (đối tượng SystemLog, đề án mục 5.1): cảm biến mất/có lại kết nối, cảnh
// báo phát sinh và được xử lý, thay đổi luật và tài khoản. Khác log văn bản của tiến trình:
// đây là dữ liệu theo tenant, người vận hành xem được ngay trên Dashboard.
public class SystemLog : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime OccurredAt { get; set; }
    public SystemLogLevel Level { get; set; }
    public SystemLogCategory Category { get; set; }
    public string Message { get; set; } = string.Empty;

    // Đối tượng liên quan (cảm biến, cảnh báo, luật, người dùng) để Dashboard dẫn link sang.
    public Guid? EntityId { get; set; }

    // Ai gây ra: email người dùng, mã service, hoặc "system" với tác vụ tự động.
    public string? Actor { get; set; }
}
