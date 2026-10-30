using System.ComponentModel.DataAnnotations;

namespace Hitek.BackendApi.Dtos;

// Vai trò và trạng thái giữ chữ thường đúng như claim "role" trong token và enum bên database.
public record UserDto(Guid Id, string Email, string FullName, string Role, string Status);

public record CreateUserRequest(
    [Required, EmailAddress, MaxLength(255)] string Email,
    [Required, MaxLength(255)] string FullName,
    [Required, MinLength(8), MaxLength(128)] string Password,
    [Required] string Role);

public record UpdateUserRequest(string? Role, string? Status);

public record ResetPasswordRequest([Required, MinLength(8), MaxLength(128)] string NewPassword);

public record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required, MinLength(8), MaxLength(128)] string NewPassword);
