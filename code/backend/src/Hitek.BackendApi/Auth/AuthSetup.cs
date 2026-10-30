using System.Text;
using Hitek.BackendApi.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace Hitek.BackendApi.Auth;

public static class AuthPolicies
{
    public const string Viewer = nameof(Viewer);
    public const string Operator = nameof(Operator);
    public const string Admin = nameof(Admin);

    // Ghi dữ liệu cảm biến và sự kiện AI — chỉ token của service nội bộ mới có.
    public const string Ingest = nameof(Ingest);

    // Danh sách khu vực: Dashboard cần để hiển thị, AI Service cần để đối chiếu areaId.
    public const string AreaReader = nameof(AreaReader);
}

public static class AuthSetup
{
    private const int MinSigningKeyBytes = 32;

    public static IServiceCollection AddHitekAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
        var keyBytes = Encoding.UTF8.GetBytes(jwt.SigningKey);
        if (keyBytes.Length < MinSigningKeyBytes)
            throw new InvalidOperationException(
                $"Jwt:SigningKey phải dài tối thiểu {MinSigningKeyBytes} byte. Đặt qua biến môi trường Jwt__SigningKey.");

        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
        services.Configure<AuthOptions>(configuration.GetSection("Auth"));
        services.AddSingleton<TokenService>();
        services.AddSingleton<PasswordVerifier>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Giữ nguyên tên claim như lúc phát token ("role", "tenant_id"), không để
                // thư viện tự đổi sang các URI dài của WS-Federation.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = HitekClaims.Name,
                    RoleClaimType = HitekClaims.Role
                };

                options.Events = new JwtBearerEvents
                {
                    // WebSocket trong trình duyệt không gắn được header Authorization,
                    // nên client SignalR gửi token qua query string ?access_token=.
                    OnMessageReceived = context =>
                    {
                        var token = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                            context.Token = token;
                        return Task.CompletedTask;
                    },

                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        var expired = context.AuthenticateFailure is SecurityTokenExpiredException;
                        await ApiErrorWriter.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized,
                            expired ? "TOKEN_EXPIRED" : "UNAUTHORIZED",
                            expired
                                ? "Token đã hết hạn. Đăng nhập lại hoặc xin token mới."
                                : "Thiếu hoặc sai token. Gửi kèm header Authorization: Bearer <token>.");
                    },

                    OnForbidden = context => ApiErrorWriter.WriteAsync(context.HttpContext,
                        StatusCodes.Status403Forbidden, "FORBIDDEN",
                        "Tài khoản không đủ quyền thực hiện thao tác này.")
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthPolicies.Viewer, p =>
                p.RequireRole(HitekRoles.Viewer, HitekRoles.Operator, HitekRoles.Admin, HitekRoles.Owner));
            options.AddPolicy(AuthPolicies.Operator, p =>
                p.RequireRole(HitekRoles.Operator, HitekRoles.Admin, HitekRoles.Owner));
            options.AddPolicy(AuthPolicies.Admin, p =>
                p.RequireRole(HitekRoles.Admin, HitekRoles.Owner));
            options.AddPolicy(AuthPolicies.Ingest, p =>
                p.RequireRole(HitekRoles.Service));
            options.AddPolicy(AuthPolicies.AreaReader, p =>
                p.RequireRole(HitekRoles.Viewer, HitekRoles.Operator, HitekRoles.Admin, HitekRoles.Owner, HitekRoles.Service));

            // Endpoint nào quên gắn [Authorize] vẫn bị chặn với người lạ. Thà sau này phải
            // mở thêm [AllowAnonymous] một cách có chủ ý còn hơn lỡ để lộ dữ liệu.
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        });

        return services;
    }
}
