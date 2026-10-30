using Hitek.BackendApi.Auth;
using Hitek.BackendApi.Common;
using Hitek.BackendApi.Data;
using Hitek.BackendApi.Domain;
using Hitek.BackendApi.Dtos;
using Hitek.BackendApi.RiskEngine;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hitek.BackendApi.Controllers;

[ApiController]
[Route("api/v1/events")]
public class EventsController(
    HitekDbContext db,
    RiskEngineService riskEngine,
    IOptions<RiskEngineOptions> riskOptions,
    ILogger<EventsController> logger) : ControllerBase
{
    // AI Service gọi vào đây khi phát hiện vi phạm (REST — đã thống nhất với Hoàng Hà).
    // Cố ý không lọc ngưỡng tin cậy ở đây: Risk Engine mới là nơi quyết định ngưỡng, AI gửi hết.
    [HttpPost]
    [Authorize(Policy = AuthPolicies.Ingest)]
    public async Task<IActionResult> PostAiEvent(AiEventRequest request, CancellationToken ct)
    {
        // Chặn ngay tại cửa nếu areaId không thuộc tenant hiện tại. Không có bước này thì
        // AI gửi nhầm areaId sẽ không báo lỗi gì, chỉ lặng lẽ không gộp được với IoT.
        var areaExists = await db.Locations.AnyAsync(l => l.Id == request.AreaId, ct);
        if (!areaExists)
            throw ApiException.BadRequest("AREA_NOT_FOUND", "areaId không hợp lệ. Lấy danh sách hợp lệ tại GET /api/v1/areas.");

        // Khoá ngoại chỉ kiểm tra camera có tồn tại, không kiểm tra thuộc tenant nào. Bước
        // này đi qua bộ lọc tenant nên chặn được cả camera của khách hàng khác.
        var cameraExists = await db.Cameras.AnyAsync(c => c.Id == request.CameraId, ct);
        if (!cameraExists)
            throw ApiException.BadRequest("CAMERA_NOT_FOUND", "cameraId không tồn tại trong tenant hiện tại.");

        // Cùng lý do với camera: khoá ngoại sang ai_event không biết tenant. Đọc ai_event qua
        // bộ lọc tenant để chặn id của khách hàng khác, rồi lấy luôn loại sự kiện từ đó.
        var eventType = request.EventType?.Trim().ToLowerInvariant();
        if (request.AiEventId is { } aiEventId)
        {
            var aiEvent = await db.AiEvents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == aiEventId, ct)
                ?? throw ApiException.BadRequest("AI_EVENT_NOT_FOUND",
                    "aiEventId không tồn tại trong tenant hiện tại. Ghi ai_event trước rồi mới gọi endpoint này.");
            if (aiEvent.CameraId != request.CameraId)
                throw ApiException.BadRequest("AI_EVENT_CAMERA_MISMATCH", "cameraId không khớp với camera của ai_event.");
            eventType = aiEvent.EventType.Trim().ToLowerInvariant();
        }

        var dedupKey = EventDedupKey.ForAi(request.AiEventId, request.CameraId, request.OccurredAt, request.ObjectType);

        // Đường nhanh: đã ghi rồi thì thôi. Trả 202 chứ không trả lỗi — với một lần retry,
        // kết quả mong muốn coi như đã đạt; trả lỗi chỉ khiến AI Service retry thêm nữa.
        // Vẫn phải trả alertId, nếu không thì mỗi lần retry AI Service lại mất liên kết
        // giữa bằng chứng và cảnh báo, dù bản ghi đã nằm sẵn trong hệ thống.
        if (await AlreadyRecordedAsync(request, dedupKey, ct))
            return Accepted(new AiEventAcceptedResponse(await riskEngine.GetOpenAlertIdAsync(request.AreaId, ct)));

        db.Events.Add(new EventRecord
        {
            Id = Guid.NewGuid(),
            Source = EventSource.Ai,
            EventType = eventType,
            AreaId = request.AreaId,
            OccurredAt = request.OccurredAt,
            DedupKey = dedupKey,
            AiEventId = request.AiEventId,
            CameraId = request.CameraId,
            Confidence = request.Confidence
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.ViolatesConstraintOn("camera"))
        {
            // Camera bị xoá đúng lúc giữa bước kiểm tra ở trên và lúc ghi.
            throw ApiException.BadRequest("CAMERA_NOT_FOUND", "cameraId không tồn tại trong tenant hiện tại.");
        }
        catch (DbUpdateException ex) when (ex.IsForeignKeyViolation())
        {
            // ai_event bị xoá đúng lúc giữa bước kiểm tra ở trên và lúc ghi.
            throw ApiException.BadRequest("AI_EVENT_NOT_FOUND",
                "aiEventId không tồn tại trong bảng ai_event. Ghi ai_event trước rồi mới gọi endpoint này.");
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // Hai bản sao của cùng một request chạy song song, cả hai cùng vượt qua bước
            // kiểm tra ở trên. Ràng buộc duy nhất chặn lại — với bên gọi vẫn là thành công.
            logger.LogInformation("Bỏ qua event AI trùng, dedupKey={DedupKey}", dedupKey);
            return Accepted(new AiEventAcceptedResponse(await riskEngine.GetOpenAlertIdAsync(request.AreaId, ct)));
        }

        var alertId = await riskEngine.EvaluateAreaAsync(request.AreaId, ct);

        return Accepted(new AiEventAcceptedResponse(alertId));
    }

    // Trùng khi gửi lại đúng bản cũ, hoặc khi cùng một vụ vi phạm vừa được ghi cách đây
    // chưa tới AiEventMinIntervalSeconds — AI cập nhật theo từng khung hình, giữ mỗi giây
    // một dòng là đủ để nguồn AI không rơi khỏi cửa sổ đang hoạt động của Risk Engine.
    private Task<bool> AlreadyRecordedAsync(AiEventRequest request, string dedupKey, CancellationToken ct)
    {
        if (request.AiEventId is not { } aiEventId)
            return db.Events.AnyAsync(e => e.DedupKey == dedupKey, ct);

        var since = request.OccurredAt.AddSeconds(-riskOptions.Value.AiEventMinIntervalSeconds);
        return db.Events.AnyAsync(e =>
            e.DedupKey == dedupKey ||
            (e.AiEventId == aiEventId && e.OccurredAt > since && e.OccurredAt <= request.OccurredAt), ct);
    }

    // Tra cứu lịch sử sự kiện đã chuẩn hoá của cả hai nguồn (đề án mục 4.5): lọc theo
    // thời gian, thiết bị, loại sự kiện và khu vực.
    [HttpGet]
    [Authorize(Policy = AuthPolicies.Viewer)]
    public async Task<ActionResult<PagedResult<EventDto>>> GetEvents(
        [FromQuery] EventSource? source,
        [FromQuery] string? eventType,
        [FromQuery] Guid? areaId,
        [FromQuery] Guid? deviceId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var (p, ps) = Paging.Normalize(page, pageSize);

        var query = db.Events.AsNoTracking();
        if (source is not null) query = query.Where(e => e.Source == source);
        if (!string.IsNullOrWhiteSpace(eventType))
        {
            var type = eventType.Trim().ToLowerInvariant();
            query = query.Where(e => e.EventType == type);
        }
        if (areaId is not null) query = query.Where(e => e.AreaId == areaId);
        if (deviceId is not null) query = query.Where(e => e.SensorId == deviceId || e.CameraId == deviceId);
        if (from is not null) query = query.Where(e => e.OccurredAt >= from);
        if (to is not null) query = query.Where(e => e.OccurredAt < to);

        query = query.OrderByDescending(e => e.OccurredAt);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((p - 1) * ps)
            .Take(ps)
            .Select(e => new EventDto(
                e.Id, e.Source, e.EventType, e.AreaId, e.OccurredAt, e.Confidence,
                e.SensorId, e.CameraId, e.AiEventId, e.SensorReadingId))
            .ToListAsync(ct);

        return Ok(new PagedResult<EventDto> { Items = items, Page = p, PageSize = ps, Total = total });
    }
}
