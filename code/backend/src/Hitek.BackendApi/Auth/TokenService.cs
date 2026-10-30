using System.Security.Claims;
using System.Text;
using Hitek.BackendApi.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Hitek.BackendApi.Auth;

public sealed record IssuedToken(string AccessToken, DateTime ExpiresAt);

public sealed class TokenService(IOptions<JwtOptions> jwtOptions, IOptions<AuthOptions> authOptions, TimeProvider clock)
{
    private readonly JwtOptions _jwt = jwtOptions.Value;
    private readonly AuthOptions _auth = authOptions.Value;
    private readonly JsonWebTokenHandler _handler = new();

    public IssuedToken ForUser(AppUser user, TenantMember membership) =>
        Issue(
            subject: user.Id.ToString(),
            tenantId: membership.TenantId,
            role: membership.Role,
            tokenType: "user",
            lifetime: TimeSpan.FromMinutes(_auth.UserTokenMinutes),
            extra:
            [
                new Claim(HitekClaims.Name, user.FullName),
                new Claim(HitekClaims.Email, user.Email)
            ]);

    public IssuedToken ForService(ServiceClientOptions client) =>
        Issue(
            subject: client.ClientId,
            tenantId: client.TenantId,
            role: HitekRoles.Service,
            tokenType: "service",
            lifetime: TimeSpan.FromMinutes(_auth.ServiceTokenMinutes),
            extra: [new Claim(HitekClaims.Name, client.ClientId)]);

    private IssuedToken Issue(string subject, Guid tenantId, string role, string tokenType, TimeSpan lifetime, Claim[] extra)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expiresAt = now.Add(lifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(HitekClaims.TenantId, tenantId.ToString()),
            new(HitekClaims.Role, role),
            new(HitekClaims.TokenType, tokenType)
        };
        claims.AddRange(extra);

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now,
            NotBefore = now,
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey)),
                SecurityAlgorithms.HmacSha256)
        });

        return new IssuedToken(token, expiresAt);
    }
}

public sealed class PasswordVerifier
{
    // Hash của một chuỗi ngẫu nhiên, chỉ để chạy BCrypt cho đủ thời gian khi email không
    // tồn tại. Không làm vậy thì phản hồi "sai email" nhanh hơn hẳn "sai mật khẩu", và kẻ
    // dò tài khoản đo độ trễ là biết email nào có thật trong hệ thống.
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString());

    // Dùng bcrypt vì hash dạng $2a$/$2b$ kiểm tra được ở cả .NET lẫn Python. Bảng app_user
    // do AI Service sở hữu, nếu sau này bên đó cũng cần đăng nhập thì đọc chung được.
    public bool Verify(string password, string? passwordHash)
    {
        if (string.IsNullOrEmpty(passwordHash))
        {
            BCrypt.Net.BCrypt.Verify(password, DummyHash);
            return false;
        }

        // Hash hỏng (sai định dạng, bị cắt cụt) làm BCrypt ném đủ kiểu ngoại lệ chứ không chỉ
        // SaltParseException. Với người đăng nhập, đó vẫn chỉ là "sai mật khẩu", không phải lỗi 500.
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch (Exception ex) when (ex is BCrypt.Net.SaltParseException or ArgumentException or IndexOutOfRangeException)
        {
            return false;
        }
    }
}
