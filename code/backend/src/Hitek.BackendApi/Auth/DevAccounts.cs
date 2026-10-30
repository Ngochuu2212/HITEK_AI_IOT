using Hitek.BackendApi.Data;
using Microsoft.EntityFrameworkCore;

namespace Hitek.BackendApi.Auth;

public static class DevAccounts
{
    public static async Task SetMissingPasswordsAsync(HitekDbContext db, string password, ILogger logger)
    {
        var users = await db.AppUsers.Where(u => u.PasswordHash == null).ToListAsync();
        if (users.Count == 0) return;

        var hash = BCrypt.Net.BCrypt.HashPassword(password, 10);
        foreach (var user in users) user.PasswordHash = hash;
        await db.SaveChangesAsync();

        logger.LogWarning("Môi trường dev: đặt mật khẩu mặc định cho {Count} tài khoản chưa có mật khẩu: {Emails}",
            users.Count, string.Join(", ", users.Select(u => u.Email)));
    }
}
