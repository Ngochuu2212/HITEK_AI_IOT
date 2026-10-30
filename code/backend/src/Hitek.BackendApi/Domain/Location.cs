namespace Hitek.BackendApi.Domain;

// Khu vực vật lý — nguồn gốc của mọi giá trị areaId mà AI Service và IoT dùng chung.
// Id là GUID vì có nhiều tenant: hai khách hàng đều có thể đặt mã "khu-vuc-kho",
// nên mã dễ đọc chuyển sang cột Code và chỉ cần duy nhất trong phạm vi (TenantId, Code).
public class Location : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
