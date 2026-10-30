namespace Hitek.BackendApi.Domain;

// Bảng nối trả lời câu hỏi "vì sự kiện nào mà cảnh báo này lên mức đó".
// Mỗi cặp (Alert, Event) chỉ nối một lần; LevelAtLink ghi lại mức của cảnh báo
// ngay thời điểm sự kiện được nối, nên xếp các dòng theo LinkedAt là dựng lại
// được toàn bộ diễn biến nâng mức.
public class AlertEvent : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AlertId { get; set; }
    public Guid EventId { get; set; }
    public AlertLevel LevelAtLink { get; set; }
    public DateTime LinkedAt { get; set; }

    public Alert? Alert { get; set; }
    public EventRecord? Event { get; set; }
}
