using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Hitek.IotService;

// Xin và giữ token service của Backend (client credentials). Token sống nhiều giờ nên chỉ
// xin lại khi sắp hết hạn hoặc khi Backend báo 401 (ví dụ Backend vừa đổi khoá ký).
public sealed class BackendTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<BackendApiOptions> options,
    TimeProvider clock,
    ILogger<BackendTokenProvider> logger)
{
    public const string AuthClientName = "BackendAuth";
    private static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTime _expiresAt;

    // Tenant ghi trong token của chính IoT Service — dùng để chặn bên gọi HTTP thuộc tenant khác.
    public Guid? TenantId { get; private set; }

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (IsUsable()) return _token!;

        await _lock.WaitAsync(ct);
        try
        {
            if (IsUsable()) return _token!;

            var client = httpClientFactory.CreateClient(AuthClientName);
            var opt = options.Value;
            using var response = await client.PostAsJsonAsync("/api/v1/auth/token",
                new { clientId = opt.ClientId, clientSecret = opt.ClientSecret }, ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Backend từ chối cấp token cho IoT Service ({(int)response.StatusCode}). Kiểm tra BackendApi:ClientId/ClientSecret.",
                    null, response.StatusCode);
            }

            var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            _token = body.GetProperty("accessToken").GetString();
            _expiresAt = body.GetProperty("expiresAt").GetDateTime().ToUniversalTime();
            TenantId = body.GetProperty("tenantId").GetGuid();
            logger.LogInformation("Đã nhận token service của Backend, hết hạn lúc {ExpiresAt:O}", _expiresAt);
            return _token!;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Invalidate(string token)
    {
        if (_token == token) _token = null;
    }

    private bool IsUsable() => _token is not null && clock.GetUtcNow().UtcDateTime < _expiresAt - RenewBefore;
}

// Gắn token vào mọi request gửi sang Backend; gặp 401 thì xin token mới và gửi lại đúng một lần.
public sealed class BackendAuthHandler(BackendTokenProvider tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = await tokens.GetTokenAsync(ct);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await base.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;

        response.Dispose();
        tokens.Invalidate(token);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(ct));
        return await base.SendAsync(request, ct);
    }
}
