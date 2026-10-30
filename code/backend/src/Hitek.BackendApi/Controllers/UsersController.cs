using System.Net;
using Hitek.BackendApi.Auth;
using Hitek.BackendApi.Common;
using Hitek.BackendApi.Data;
using Hitek.BackendApi.Domain;
using Hitek.BackendApi.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hitek.BackendApi.Controllers;

// Quản lý người dùng của tenant hiện tại (đề án mục 3: Backend API quản lý người dùng).
//
// Bảng app_user và tenant_member do AI Service tạo; Backend chỉ ghi dữ liệu, không đổi cấu
// trúc. Cột role/status bên đó là ENUM của PostgreSQL: gửi chuỗi dưới dạng tham số thì
// PostgreSQL từ chối ("is of type member_role but expression is of type text"), nên các giá
// trị này được đưa vào câu lệnh dưới dạng hằng chuỗi — chỉ sau khi đã đối chiếu với danh
// sách cố định, không bao giờ lấy nguyên văn từ người gọi.
[ApiController]
[Route("api/v1/users")]
[Authorize(Policy = AuthPolicies.Admin)]
public class UsersController(HitekDbContext db, TimeProvider clock) : ControllerBase
{
    private const string Active = "active";
    private const string Disabled = "disabled";
    private const int PasswordWorkFactor = 11;

    private static readonly string[] AssignableRoles = [HitekRoles.Viewer, HitekRoles.Operator, HitekRoles.Admin, HitekRoles.Owner];
    private static readonly string[] MemberStatuses = [Active, Disabled];

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetUsers(CancellationToken ct)
    {
        var members = await db.TenantMembers.AsNoTracking().Where(m => m.TenantId == db.TenantId).ToListAsync(ct);
        var userIds = members.Select(m => m.UserId).ToList();
        var users = await db.AppUsers.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);

        return Ok(members
            .Where(m => users.ContainsKey(m.UserId))
            .Select(m => ToDto(users[m.UserId], m))
            .OrderBy(u => u.Email, StringComparer.Ordinal)
            .ToList());
    }

    // Email đã có tài khoản (ví dụ đang thuộc tenant khác) thì chỉ thêm vào tenant này, không
    // đụng tới mật khẩu và họ tên của tài khoản đó.
    [HttpPost]
    public async Task<ActionResult<UserDto>> CreateUser(CreateUserRequest request, CancellationToken ct)
    {
        var role = RequireRole(request.Role);
        var email = request.Email.Trim().ToLowerInvariant();

        var user = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Email.ToLower() == email, ct);
        if (user is not null && await db.TenantMembers.AnyAsync(m => m.TenantId == db.TenantId && m.UserId == user.Id, ct))
            throw ApiException.Conflict("USER_ALREADY_MEMBER", "Tài khoản với email này đã thuộc tenant.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (user is null)
        {
            user = new AppUser
            {
                Id = Guid.NewGuid(),
                Email = email,
                FullName = request.FullName.Trim(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, PasswordWorkFactor),
                Status = Active
            };
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO app_user (id, email, full_name, password_hash, status) VALUES ({0}, {1}, {2}, {3}, 'active')",
                    [user.Id, user.Email, user.FullName, user.PasswordHash!], ct);
            }
            catch (Exception ex) when (ex.IsRawUniqueViolation())
            {
                throw ApiException.Conflict("EMAIL_TAKEN", "Email đã được dùng cho một tài khoản khác.");
            }
        }

        var member = new TenantMember { Id = Guid.NewGuid(), TenantId = db.TenantId, UserId = user.Id, Role = role, Status = Active };
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO tenant_member (id, tenant_id, user_id, role, status) VALUES ({0}, {1}, {2}, " + Literal(role) + ", 'active')",
            [member.Id, member.TenantId, member.UserId], ct);

        Log($"{User.DisplayName()} thêm {email} vào tenant với vai trò {role}.", user.Id);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return CreatedAtAction(nameof(GetUsers), null, ToDto(user, member));
    }

    // Đổi vai trò hoặc khoá/mở quyền truy cập tenant. Khoá ở đây là khoá thành viên của tenant
    // này, tài khoản vẫn dùng được ở tenant khác nếu có.
    [HttpPatch("{userId:guid}")]
    public async Task<ActionResult<UserDto>> UpdateUser(Guid userId, UpdateUserRequest request, CancellationToken ct)
    {
        var (user, member) = await FindMemberAsync(userId, ct);

        // Tự hạ quyền hoặc tự khoá mình thì có thể không còn ai quản trị tenant nữa.
        if (User.UserId() == userId)
            throw ApiException.Conflict("CANNOT_MODIFY_SELF", "Không thể tự đổi vai trò hoặc tự khoá tài khoản của chính mình.");
        EnsureCanManage(member.Role);

        var changes = new List<string>();
        if (request.Role is not null)
        {
            var role = RequireRole(request.Role);
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE tenant_member SET role = " + Literal(role) + " WHERE id = {0}", [member.Id], ct);
            changes.Add($"vai trò {member.Role} → {role}");
            member.Role = role;
        }

        if (request.Status is not null)
        {
            var status = request.Status.Trim().ToLowerInvariant();
            if (!MemberStatuses.Contains(status))
                throw ApiException.BadRequest("VALIDATION_ERROR", "status: Chỉ nhận active hoặc disabled.");
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE tenant_member SET status = " + Literal(status) + " WHERE id = {0}", [member.Id], ct);
            changes.Add($"trạng thái {member.Status} → {status}");
            member.Status = status;
        }

        if (changes.Count > 0)
        {
            Log($"{User.DisplayName()} sửa thành viên {user.Email}: {string.Join(", ", changes)}.", user.Id);
            await db.SaveChangesAsync(ct);
        }

        return Ok(ToDto(user, member));
    }

    // app_user dùng chung giữa các tenant, nên quản trị viên một tenant chỉ được đặt lại mật
    // khẩu cho tài khoản chỉ thuộc riêng tenant mình.
    [HttpPost("{userId:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid userId, ResetPasswordRequest request, CancellationToken ct)
    {
        var (_, member) = await FindMemberAsync(userId, ct);
        EnsureCanManage(member.Role);

        if (await db.TenantMembers.AnyAsync(m => m.UserId == userId && m.TenantId != db.TenantId, ct))
            throw ApiException.Conflict("USER_IN_OTHER_TENANTS",
                "Tài khoản này còn thuộc tenant khác, chủ tài khoản phải tự đổi mật khẩu.");

        var user = await db.AppUsers.FirstAsync(u => u.Id == userId, ct);
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword, PasswordWorkFactor);
        Log($"{User.DisplayName()} đặt lại mật khẩu cho {user.Email}.", user.Id);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    private async Task<(AppUser User, TenantMember Member)> FindMemberAsync(Guid userId, CancellationToken ct)
    {
        var member = await db.TenantMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.TenantId == db.TenantId && m.UserId == userId, ct);
        var user = member is null ? null : await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (member is null || user is null)
            throw ApiException.NotFound("USER_NOT_FOUND", "Không tìm thấy người dùng này trong tenant.");
        return (user, member);
    }

    private string RequireRole(string? requested)
    {
        var role = requested?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!AssignableRoles.Contains(role))
            throw ApiException.BadRequest("VALIDATION_ERROR", "role: Chỉ nhận viewer, operator, admin hoặc owner.");
        if (role == HitekRoles.Owner && !User.IsInRole(HitekRoles.Owner))
            throw new ApiException(HttpStatusCode.Forbidden, "FORBIDDEN", "Chỉ chủ sở hữu tenant mới cấp được vai trò owner.");
        return role;
    }

    private void EnsureCanManage(string targetRole)
    {
        if (targetRole == HitekRoles.Owner && !User.IsInRole(HitekRoles.Owner))
            throw new ApiException(HttpStatusCode.Forbidden, "FORBIDDEN", "Quản trị viên không được sửa tài khoản chủ sở hữu tenant.");
    }

    // Chỉ gọi với giá trị đã đối chiếu ở AssignableRoles/MemberStatuses.
    private static string Literal(string whitelisted) => $"'{whitelisted}'";

    private void Log(string message, Guid userId) =>
        SystemLogWriter.Add(db, clock.GetUtcNow().UtcDateTime, SystemLogLevel.Info, SystemLogCategory.User,
            message, userId, User.Actor());

    private static UserDto ToDto(AppUser user, TenantMember member) => new(
        user.Id, user.Email, user.FullName, member.Role,
        user.Status == Active && member.Status == Active ? Active : Disabled);
}
