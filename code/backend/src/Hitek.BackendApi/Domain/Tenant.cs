namespace Hitek.BackendApi.Domain;

// Đơn vị sở hữu dữ liệu (khách hàng / tổ chức). Mọi bảng nghiệp vụ đều gắn TenantId
// và được lọc tự động qua global query filter trong HitekDbContext.
public class Tenant
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    // ENUM tenant_status bên AI Service ('active' / 'disabled'), đọc thành chuỗi.
    public string Status { get; set; } = string.Empty;
}
