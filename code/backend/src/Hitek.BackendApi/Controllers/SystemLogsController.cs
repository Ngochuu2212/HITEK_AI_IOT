using Hitek.BackendApi.Auth;
using Hitek.BackendApi.Common;
using Hitek.BackendApi.Data;
using Hitek.BackendApi.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hitek.BackendApi.Controllers;

public record SystemLogDto(
    Guid Id,
    DateTime OccurredAt,
    SystemLogLevel Level,
    SystemLogCategory Category,
    string Message,
    Guid? EntityId,
    string? Actor);

// Nhật ký hệ thống cho người vận hành: cảm biến mất/có lại kết nối, cảnh báo phát sinh, ai
// đã xử lý cảnh báo, ai đã đổi luật hay tài khoản.
[ApiController]
[Route("api/v1/system-logs")]
[Authorize(Policy = AuthPolicies.Operator)]
public class SystemLogsController(HitekDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<SystemLogDto>>> GetLogs(
        [FromQuery] SystemLogCategory? category,
        [FromQuery] SystemLogLevel? level,
        [FromQuery] Guid? entityId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var (p, ps) = Paging.Normalize(page, pageSize);

        var query = db.SystemLogs.AsNoTracking();
        if (category is not null) query = query.Where(l => l.Category == category);
        if (level is not null) query = query.Where(l => l.Level == level);
        if (entityId is not null) query = query.Where(l => l.EntityId == entityId);
        if (from is not null) query = query.Where(l => l.OccurredAt >= from);
        if (to is not null) query = query.Where(l => l.OccurredAt < to);
        query = query.OrderByDescending(l => l.OccurredAt);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((p - 1) * ps)
            .Take(ps)
            .Select(l => new SystemLogDto(l.Id, l.OccurredAt, l.Level, l.Category, l.Message, l.EntityId, l.Actor))
            .ToListAsync(ct);

        return Ok(new PagedResult<SystemLogDto> { Items = items, Page = p, PageSize = ps, Total = total });
    }
}
