using Hitek.BackendApi.Auth;
using Hitek.BackendApi.Common;
using Hitek.BackendApi.Data;
using Hitek.BackendApi.Domain;
using Hitek.BackendApi.Dtos;
using Hitek.BackendApi.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hitek.BackendApi.Controllers;

[ApiController]
[Route("api/v1/alerts")]
[Authorize(Policy = AuthPolicies.Viewer)]
public class AlertsController(
    HitekDbContext db,
    IHubContext<AlertsHub> hub,
    IOptions<AiServiceOptions> aiService,
    TimeProvider clock) : ControllerBase
{
    // Bộ lọc phục vụ tra cứu lịch sử — đề án mục 4.5 yêu cầu lọc được theo thời gian,
    // thiết bị, loại sự kiện và mức độ.
    [HttpGet]
    public async Task<ActionResult<PagedResult<AlertDto>>> GetAlerts(
        // Lọc theo mức cao nhất cảnh báo từng đạt, không phải mức hiện tại: cảnh báo đã
        // đóng luôn về LOW, lọc theo mức hiện tại thì không tìm lại được vụ Critical cũ.
        [FromQuery] AlertLevel? level,
        [FromQuery] AlertHandledStatus? status,
        [FromQuery] Guid? areaId,
        [FromQuery] EventSource? source,
        [FromQuery] string? eventType,
        [FromQuery] Guid? deviceId,
        [FromQuery] bool? open,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var (p, ps) = Paging.Normalize(page, pageSize);

        var query = db.Alerts.AsQueryable();
        if (level is not null) query = query.Where(a => a.PeakLevel == level);
        if (status is not null) query = query.Where(a => a.HandledStatus == status);
        if (areaId is not null) query = query.Where(a => a.AreaId == areaId);
        if (open is true) query = query.Where(a => a.EndedAt == null);
        if (open is false) query = query.Where(a => a.EndedAt != null);

        // Loại sự kiện và thiết bị không nằm trên bảng alert mà trên các sự kiện đã góp
        // vào nó, nên lọc qua bảng nối: lấy cảnh báo có ít nhất một sự kiện khớp điều kiện.
        if (source is not null)
            query = query.Where(a => a.Links.Any(l => l.Event!.Source == source));
        if (!string.IsNullOrWhiteSpace(eventType))
        {
            var type = eventType.Trim().ToLowerInvariant();
            query = query.Where(a => a.Links.Any(l => l.Event!.EventType == type));
        }
        if (deviceId is not null)
            query = query.Where(a => a.Links.Any(l => l.Event!.SensorId == deviceId || l.Event!.CameraId == deviceId));

        // Lọc theo thời điểm cảnh báo bắt đầu. Khoảng lấy mở ở đầu, đóng ở cuối, nên
        // hai khoảng liền nhau không đếm trùng một cảnh báo nằm đúng ranh giới.
        if (from is not null) query = query.Where(a => a.StartedAt >= from);
        if (to is not null) query = query.Where(a => a.StartedAt < to);

        query = query.OrderByDescending(a => a.StartedAt);

        var total = await query.CountAsync(ct);
        var items = await query.Skip((p - 1) * ps).Take(ps).ToListAsync(ct);
        var origins = await AlertOriginLoader.LoadAsync(db, items.Select(a => a.Id).ToList(), ct);

        return Ok(new PagedResult<AlertDto>
        {
            Items = items.Select(a => a.ToDto(origins.GetValueOrDefault(a.Id, AlertOrigins.None))).ToList(),
            Page = p,
            PageSize = ps,
            Total = total
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AlertDto>> GetAlert(Guid id, CancellationToken ct)
    {
        var alert = await FindAlertAsync(id, ct);
        var origins = await AlertOriginLoader.LoadAsync(db, [id], ct);
        return Ok(alert.ToDto(origins.GetValueOrDefault(id, AlertOrigins.None)));
    }

    // Truy vết: những sự kiện nào đã đóng góp vào cảnh báo này, và ở mức nào.
    [HttpGet("{id:guid}/events")]
    public async Task<ActionResult<IReadOnlyList<AlertEventDto>>> GetAlertEvents(Guid id, CancellationToken ct)
    {
        await FindAlertAsync(id, ct);

        // Sắp xếp trước rồi mới chiếu sang DTO: EF Core không dịch được OrderBy
        // đặt trên thuộc tính của một record vừa khởi tạo trong câu truy vấn.
        var links = await db.AlertEvents
            .Where(l => l.AlertId == id)
            .OrderBy(l => l.LinkedAt)
            .Select(l => new AlertEventDto(
                l.Event!.Id,
                l.Event.Source,
                l.Event.EventType,
                l.Event.OccurredAt,
                l.Event.Confidence,
                l.Event.SensorId,
                l.Event.CameraId,
                l.LevelAtLink,
                l.LinkedAt))
            .ToListAsync(ct);

        return Ok(links);
    }

    // Ảnh và clip bằng chứng của cảnh báo (đề án mục 4.3, checklist nghiệm thu mục 10).
    //
    // Gom từ hai phía cho đủ: bằng chứng AI Service đã gắn thẳng alert_id, và bằng chứng
    // của những sự kiện AI đã góp vào cảnh báo qua bảng nối — phòng trường hợp bằng chứng
    // được chụp trước khi bên đó kịp nhận alertId từ Backend.
    [HttpGet("{id:guid}/evidence")]
    public async Task<ActionResult<IReadOnlyList<EvidenceDto>>> GetAlertEvidence(Guid id, CancellationToken ct)
    {
        await FindAlertAsync(id, ct);

        var aiEventIds = await db.AlertEvents
            .Where(l => l.AlertId == id && l.Event!.AiEventId != null)
            .Select(l => l.Event!.AiEventId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var evidence = await db.Evidence
            .Where(e => e.AlertId == id || aiEventIds.Contains(e.AiEventId))
            .OrderBy(e => e.CapturedAt)
            .ToListAsync(ct);

        var baseUrl = aiService.Value.PublicBaseUrl?.TrimEnd('/');
        return Ok(evidence.Select(e => new EvidenceDto(
            e.Id, e.EvidenceType, e.MimeType, e.FileSize, e.CapturedAt, e.DurationSeconds,
            e.CameraId, e.AiEventId, e.Status,
            string.IsNullOrEmpty(baseUrl) ? null : $"{baseUrl}/api/evidence/{e.Id}/media")).ToList());
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = AuthPolicies.Operator)]
    public async Task<ActionResult<AlertDto>> UpdateAlert(Guid id, UpdateAlertRequest request, CancellationToken ct)
    {
        var alert = await FindAlertAsync(id, ct);

        // Đóng xử lý là bước cuối của người vận hành. Cho sửa tiếp thì hai người cùng mở
        // một cảnh báo sẽ ghi đè kết luận của nhau mà không ai hay.
        if (alert.HandledStatus == AlertHandledStatus.Closed)
            throw ApiException.Conflict("ALERT_ALREADY_CLOSED",
                "Cảnh báo đã được đóng xử lý trước đó, không thể cập nhật thêm.");

        var now = clock.GetUtcNow().UtcDateTime;
        var previous = alert.HandledStatus;
        alert.HandledStatus = request.HandledStatus;
        alert.Note = request.Note;
        alert.HandledByUserId = User.UserId();
        alert.HandledByName = User.DisplayName();
        alert.HandledAt = now;

        SystemLogWriter.Add(db, now, SystemLogLevel.Info, SystemLogCategory.Alert,
            $"{User.DisplayName()} cập nhật xử lý cảnh báo: {Display(previous)} → {Display(request.HandledStatus)}" +
            (string.IsNullOrWhiteSpace(request.Note) ? "." : $". Ghi chú: {request.Note}"),
            alert.Id, User.Actor());
        await db.SaveChangesAsync(ct);

        await AlertBroadcast.SendAsync(db, hub, alert, ct);

        var origins = await AlertOriginLoader.LoadAsync(db, [id], ct);
        return Ok(alert.ToDto(origins.GetValueOrDefault(id, AlertOrigins.None)));
    }

    private async Task<Alert> FindAlertAsync(Guid id, CancellationToken ct) =>
        await db.Alerts.FirstOrDefaultAsync(a => a.Id == id, ct)
        ?? throw ApiException.NotFound("ALERT_NOT_FOUND", "Không tìm thấy cảnh báo với id đã cho.");

    private static string Display(AlertHandledStatus status) => status.ToString().ToUpperInvariant();
}
