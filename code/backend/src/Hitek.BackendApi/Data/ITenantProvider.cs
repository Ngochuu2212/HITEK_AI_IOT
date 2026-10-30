using Hitek.BackendApi.Auth;

namespace Hitek.BackendApi.Data;

public interface ITenantProvider
{
    Guid TenantId { get; }
}

// Tenant đặt tường minh cho tác vụ nền (không có request, không có token). Sống theo scope:
// tác vụ nền tạo một scope cho mỗi tenant, đặt giá trị này trước khi lấy DbContext ra dùng.
public class TenantContext
{
    public Guid? TenantId { get; set; }
}

// Tenant hiện tại lấy từ claim tenant_id trong token, không lấy từ bất kỳ thứ gì người
// gọi tự khai. Trước khi có JWT, giá trị này đọc tạm từ header X-Tenant-Id — giữ lại
// header đó thì ai cũng gõ được tenant của khách hàng khác và đọc dữ liệu của họ.
//
// Request chưa xác thực (đăng nhập, xin token) không có tenant nào: trả Guid.Empty để
// global query filter không khớp dòng nào, thay vì rơi về một tenant mặc định.
public class HttpTenantProvider(IHttpContextAccessor accessor, TenantContext context) : ITenantProvider
{
    public Guid TenantId
    {
        get
        {
            if (context.TenantId is { } explicitTenant) return explicitTenant;
            var raw = accessor.HttpContext?.User.FindFirst(HitekClaims.TenantId)?.Value;
            return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
        }
    }
}
