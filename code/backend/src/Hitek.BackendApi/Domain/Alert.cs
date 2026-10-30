namespace Hitek.BackendApi.Domain;

public enum AlertLevel
{
    Low,
    Medium,
    High,
    Critical
}

public enum AlertHandledStatus
{
    Open,
    Acknowledged,
    Closed
}

public class Alert : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AreaId { get; set; }

    // Mức hiện tại: lên xuống theo số nguồn đang hoạt động, về Low khi cảnh báo đóng.
    public AlertLevel Level { get; set; }

    // Mức cao nhất từng đạt trong cả vòng đời. Tra cứu lịch sử theo mức độ (đề án mục 4.5)
    // dựa vào cột này: một vụ từng lên Critical thì vẫn là vụ Critical sau khi đã đóng.
    public AlertLevel PeakLevel { get; set; }

    // Giữ sẵn số nguồn đang active thay vì đếm lại qua AlertEvent mỗi lần đọc:
    // Dashboard hiển thị cột này liên tục, còn AlertEvent phục vụ truy vết nguyên nhân.
    public int SourceCount { get; set; }

    // Mã các luật đã quyết định mức hiện tại, ví dụ "R3" hay "R1-3,R2". Trả lời câu hỏi
    // "vì sao cảnh báo đang ở mức này" mà không phải đọc lại bảng luật.
    public string? TriggeredRules { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public AlertHandledStatus HandledStatus { get; set; } = AlertHandledStatus.Open;
    public string? Note { get; set; }

    // Người vận hành cập nhật xử lý gần nhất (đề án mục 4.3). Lưu kèm tên hiển thị để đọc
    // lại được ngay, kể cả khi tài khoản đó sau này bị đổi tên hoặc khoá.
    public Guid? HandledByUserId { get; set; }
    public string? HandledByName { get; set; }
    public DateTime? HandledAt { get; set; }

    public ICollection<AlertEvent> Links { get; set; } = new List<AlertEvent>();
}
