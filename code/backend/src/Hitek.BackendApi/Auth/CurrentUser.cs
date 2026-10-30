using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Hitek.BackendApi.Auth;

public static class CurrentUser
{
    // Tên ghi vào nhật ký: email với người dùng, mã client với service.
    public static string Actor(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(HitekClaims.Email)
        ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? "unknown";

    // Chỉ token người dùng mới có id tài khoản; token service thì sub là mã client.
    public static Guid? UserId(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(HitekClaims.TokenType) == "user" &&
        Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id)
            ? id
            : null;

    public static string DisplayName(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(HitekClaims.Name) ?? principal.Actor();
}
