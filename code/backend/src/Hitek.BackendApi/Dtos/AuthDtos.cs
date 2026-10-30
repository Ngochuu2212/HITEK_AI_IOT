namespace Hitek.BackendApi.Dtos;

// TenantId chỉ cần khi một người thuộc nhiều tenant; thuộc đúng một tenant thì bỏ trống.
public record LoginRequest(string Email, string Password, Guid? TenantId);

// Luồng client credentials cho service nội bộ (IoT Service, AI Service).
public record ServiceTokenRequest(string ClientId, string ClientSecret);

public record AuthUserDto(Guid Id, string Email, string FullName);

public record TokenResponse(
    string AccessToken,
    string TokenType,
    DateTime ExpiresAt,
    Guid TenantId,
    string Role,
    AuthUserDto? User);

public record MeResponse(string Subject, string TokenType, Guid TenantId, string Role, string? Name, string? Email);
