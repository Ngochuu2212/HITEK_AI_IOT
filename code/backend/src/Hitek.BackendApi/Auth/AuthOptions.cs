namespace Hitek.BackendApi.Auth;

public class JwtOptions
{
    public string Issuer { get; set; } = "hitek-backend-api";
    public string Audience { get; set; } = "hitek-ai-iot";

    // HMAC-SHA256 cần khoá tối thiểu 256 bit. Không đặt giá trị mặc định ở đây: thiếu
    // khoá thì ứng dụng dừng ngay lúc khởi động, thay vì chạy với một khoá ai cũng biết.
    public string SigningKey { get; set; } = string.Empty;
}

// Hệ thống có hai loại chủ thể gọi API với đặc tính khác hẳn nhau:
// - Người dùng thao tác qua Dashboard: đăng nhập bằng tài khoản, token sống ngắn.
// - Service nội bộ (IoT Service, AI Service): chạy tự động, không có người đứng sau,
//   nên token sống dài hơn nhưng quyền hẹp hơn — chỉ đủ để ghi dữ liệu vào.
public class AuthOptions
{
    public int UserTokenMinutes { get; set; } = 60;
    public int ServiceTokenMinutes { get; set; } = 720;
    public List<ServiceClientOptions> Clients { get; set; } = [];
}

public class ServiceClientOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    // Mỗi bộ thông tin đăng nhập của service gắn với đúng một tenant, nên dữ liệu nó
    // ghi vào tự động thuộc về tenant đó qua cùng cơ chế lọc như với người dùng.
    public Guid TenantId { get; set; }
}

public static class HitekClaims
{
    public const string TenantId = "tenant_id";
    public const string Role = "role";
    public const string Name = "name";
    public const string Email = "email";
    public const string TokenType = "token_type";
}

public static class HitekRoles
{
    // Trùng tên với enum member_role trong bảng tenant_member của AI Service.
    public const string Owner = "owner";
    public const string Admin = "admin";
    public const string Operator = "operator";
    public const string Viewer = "viewer";

    // Không có trong member_role — chỉ dành cho token của service nội bộ.
    public const string Service = "service";
}
