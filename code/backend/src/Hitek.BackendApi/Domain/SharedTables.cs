namespace Hitek.BackendApi.Domain;

// Các bảng dưới đây do AI Service tạo bằng file SQL init. Backend không tạo, không sửa cấu
// trúc — xem HitekDbContext (ExcludeFromMigrations). Phần lớn chỉ đọc; riêng app_user và
// tenant_member thì Backend ghi dữ liệu qua API quản lý người dùng (đề án mục 3 giao việc
// quản lý người dùng cho Backend API), cấu trúc bảng vẫn do bên AI Service giữ.
//
// Các cột trạng thái và vai trò bên đó là kiểu ENUM của PostgreSQL. Ở đây đọc thành
// chuỗi và chỉ so sánh trong bộ nhớ, vì đem chuỗi tham số hoá so với cột ENUM trong
// câu WHERE thì PostgreSQL báo lỗi "operator does not exist".

// app_user — tài khoản đăng nhập. Không gắn tenant: một người có thể thuộc nhiều tenant.
public class AppUser
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public string Status { get; set; } = string.Empty;
}

// tenant_member — người dùng thuộc tenant nào, với vai trò gì (owner/admin/operator/viewer).
// Không có global query filter: lúc đăng nhập chưa biết tenant hiện tại là gì.
public class TenantMember
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

// camera — Backend đọc để gộp vào danh sách thiết bị cho Dashboard (đề án mục 4.4).
public class Camera : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LocationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? LastSeenAt { get; set; }
}

// ai_event — một vụ vi phạm AI phát hiện. Backend đọc lúc nhận POST /api/v1/events để kiểm
// tra sự kiện thuộc đúng tenant, đúng camera, và lấy loại sự kiện (event_type).
public class AiEvent : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CameraId { get; set; }
    public string EventType { get; set; } = string.Empty;
}

// evidence — AI Service ghi tệp ảnh/clip thẳng lên kho lưu trữ đối tượng rồi ghi dòng
// mô tả vào đây. Backend đọc để trả về bằng chứng theo từng cảnh báo (đề án mục 4.3).
public class Evidence : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? AlertId { get; set; }
    public Guid AiEventId { get; set; }
    public Guid CameraId { get; set; }
    public string EvidenceType { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long? FileSize { get; set; }
    public DateTime CapturedAt { get; set; }
    public double? DurationSeconds { get; set; }
    public string? Status { get; set; }
}
