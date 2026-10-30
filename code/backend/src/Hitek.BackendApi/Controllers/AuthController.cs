using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Hitek.BackendApi.Auth;
using Hitek.BackendApi.Common;
using Hitek.BackendApi.Data;
using Hitek.BackendApi.Domain;
using Hitek.BackendApi.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Hitek.BackendApi.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController(
    HitekDbContext db,
    TokenService tokens,
    PasswordVerifier passwords,
    IOptions<AuthOptions> authOptions) : ControllerBase
{
    private const string Active = "active";

    // Người dùng Dashboard đăng nhập bằng tài khoản trong bảng app_user của AI Service.
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        var user = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Email.ToLower() == email, ct);

        // Luôn chạy bước kiểm tra mật khẩu, kể cả khi không có email đó, để "sai email" và
        // "sai mật khẩu" tốn thời gian như nhau và trả về cùng một thông báo.
        var passwordMatches = passwords.Verify(request.Password ?? string.Empty, user?.PasswordHash);
        if (user is null || !passwordMatches || user.Status != Active)
            throw ApiException.Unauthorized("INVALID_CREDENTIALS", "Email hoặc mật khẩu không đúng.");

        var memberships = await ActiveMembershipsAsync(user.Id, ct);
        if (memberships.Count == 0)
            throw ApiException.Unauthorized("NO_ACTIVE_TENANT", "Tài khoản chưa thuộc tenant nào đang hoạt động.");

        TenantMember membership;
        if (request.TenantId is { } requested)
        {
            membership = memberships.FirstOrDefault(m => m.TenantId == requested)
                ?? throw new ApiException(HttpStatusCode.Forbidden, "TENANT_ACCESS_DENIED",
                    "Tài khoản không phải thành viên của tenant đã chọn.");
        }
        else if (memberships.Count == 1)
        {
            membership = memberships[0];
        }
        else
        {
            throw ApiException.BadRequest("TENANT_REQUIRED",
                "Tài khoản thuộc nhiều tenant, gửi kèm tenantId để chọn tenant cần đăng nhập.");
        }

        var token = tokens.ForUser(user, membership);
        return Ok(new TokenResponse(
            token.AccessToken, "Bearer", token.ExpiresAt, membership.TenantId, membership.Role,
            new AuthUserDto(user.Id, user.Email, user.FullName)));
    }

    // Service nội bộ đổi clientId/clientSecret lấy token, không có người dùng đứng sau.
    [HttpPost("token")]
    [AllowAnonymous]
    public ActionResult<TokenResponse> IssueServiceToken(ServiceTokenRequest request)
    {
        var client = authOptions.Value.Clients.FirstOrDefault(c => c.ClientId == request.ClientId);
        if (client is null || !SecretsMatch(client.ClientSecret, request.ClientSecret))
            throw ApiException.Unauthorized("INVALID_CLIENT", "clientId hoặc clientSecret không đúng.");

        var token = tokens.ForService(client);
        return Ok(new TokenResponse(token.AccessToken, "Bearer", token.ExpiresAt, client.TenantId, HitekRoles.Service, null));
    }

    [HttpGet("me")]
    [Authorize]
    public ActionResult<MeResponse> Me() => Ok(new MeResponse(
        User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? string.Empty,
        User.FindFirstValue(HitekClaims.TokenType) ?? string.Empty,
        Guid.TryParse(User.FindFirstValue(HitekClaims.TenantId), out var tenantId) ? tenantId : Guid.Empty,
        User.FindFirstValue(HitekClaims.Role) ?? string.Empty,
        User.FindFirstValue(HitekClaims.Name),
        User.FindFirstValue(HitekClaims.Email)));

    // Người dùng tự đổi mật khẩu của mình. Token service không có tài khoản nên không dùng được.
    [HttpPut("password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var userId = User.UserId()
            ?? throw new ApiException(HttpStatusCode.Forbidden, "FORBIDDEN", "Chỉ tài khoản người dùng mới đổi được mật khẩu.");

        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw ApiException.Unauthorized("UNAUTHORIZED", "Tài khoản không còn tồn tại.");
        if (!passwords.Verify(request.CurrentPassword, user.PasswordHash))
            throw ApiException.BadRequest("WRONG_PASSWORD", "Mật khẩu hiện tại không đúng.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword, 11);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Trạng thái lưu bằng ENUM của PostgreSQL nên lọc trong bộ nhớ (xem SharedTables.cs).
    private async Task<List<TenantMember>> ActiveMembershipsAsync(Guid userId, CancellationToken ct)
    {
        var memberships = (await db.TenantMembers.AsNoTracking().Where(m => m.UserId == userId).ToListAsync(ct))
            .Where(m => m.Status == Active)
            .ToList();
        if (memberships.Count == 0) return memberships;

        var tenantIds = memberships.Select(m => m.TenantId).ToList();
        var activeTenants = (await db.Tenants.AsNoTracking().Where(t => tenantIds.Contains(t.Id)).ToListAsync(ct))
            .Where(t => t.Status == Active)
            .Select(t => t.Id)
            .ToHashSet();

        return memberships.Where(m => activeTenants.Contains(m.TenantId)).ToList();
    }

    // So sánh thời gian hằng, để không đoán được secret qua việc đo độ trễ từng ký tự.
    private static bool SecretsMatch(string expected, string? actual) =>
        !string.IsNullOrEmpty(expected) &&
        actual is not null &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
}
