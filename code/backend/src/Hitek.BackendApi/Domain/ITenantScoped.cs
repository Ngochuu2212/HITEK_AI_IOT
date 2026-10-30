namespace Hitek.BackendApi.Domain;

// Đánh dấu bảng thuộc phạm vi một tenant. HitekDbContext dựa vào interface này để
// vừa tự gán TenantId lúc insert, vừa áp global query filter lúc đọc — nhờ vậy
// tầng controller không bao giờ phải tự nhớ lọc tenant.
public interface ITenantScoped
{
    Guid TenantId { get; set; }
}
