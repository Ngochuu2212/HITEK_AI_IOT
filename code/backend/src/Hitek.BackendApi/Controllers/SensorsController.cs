using Hitek.BackendApi.Auth;
using Hitek.BackendApi.Common;
using Hitek.BackendApi.Data;
using Hitek.BackendApi.Domain;
using Hitek.BackendApi.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hitek.BackendApi.Controllers;

// Quản lý cảm biến (đề án mục 3, trách nhiệm "quản lý cảm biến" của IoT Service; IoT Service
// không chạm database nên phần dữ liệu nằm ở Backend).
[ApiController]
[Route("api/v1/sensors")]
[Authorize(Policy = AuthPolicies.Viewer)]
public class SensorsController(HitekDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<SensorDto>>> GetSensors(
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
    {
        var (p, ps) = Paging.Normalize(page, pageSize);

        var query = db.Sensors.OrderBy(s => s.Name);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((p - 1) * ps).Take(ps).ToListAsync(ct);

        return Ok(new PagedResult<SensorDto>
        {
            Items = items.Select(s => s.ToDto()).ToList(),
            Page = p,
            PageSize = ps,
            Total = total
        });
    }

    // Thêm thiết bị là thay đổi cấu hình hệ thống, chỉ quản trị viên tenant được làm.
    [HttpPost]
    [Authorize(Policy = AuthPolicies.Admin)]
    public async Task<ActionResult<SensorDto>> CreateSensor(CreateSensorRequest request, CancellationToken ct)
    {
        await EnsureAreaExistsAsync(request.AreaId, ct);

        var sensor = new Sensor
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Type = request.Type,
            AreaId = request.AreaId,
            Status = SensorStatus.Offline
        };

        db.Sensors.Add(sensor);
        SystemLogWriter.Add(db, clock.GetUtcNow().UtcDateTime, SystemLogLevel.Info, SystemLogCategory.Sensor,
            $"{User.DisplayName()} thêm cảm biến {sensor.Name} ({sensor.Type.ToString().ToUpperInvariant()}).",
            sensor.Id, User.Actor());
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetSensor), new { id = sensor.Id }, sensor.ToDto());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SensorDto>> GetSensor(Guid id, CancellationToken ct) =>
        Ok((await FindSensorAsync(id, ct)).ToDto());

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthPolicies.Admin)]
    public async Task<ActionResult<SensorDto>> UpdateSensor(Guid id, UpdateSensorRequest request, CancellationToken ct)
    {
        var sensor = await FindSensorAsync(id, ct);
        await EnsureAreaExistsAsync(request.AreaId, ct);

        var changes = new List<string>();
        if (sensor.Name != request.Name.Trim()) changes.Add($"tên \"{sensor.Name}\" → \"{request.Name.Trim()}\"");
        if (sensor.AreaId != request.AreaId) changes.Add("khu vực");

        sensor.Name = request.Name.Trim();
        sensor.AreaId = request.AreaId;
        if (changes.Count > 0)
        {
            SystemLogWriter.Add(db, clock.GetUtcNow().UtcDateTime, SystemLogLevel.Info, SystemLogCategory.Sensor,
                $"{User.DisplayName()} sửa cảm biến {sensor.Name}: {string.Join(", ", changes)}.", sensor.Id, User.Actor());
        }
        await db.SaveChangesAsync(ct);

        return Ok(sensor.ToDto());
    }

    // Gỡ cảm biến thì lịch sử đọc của nó bị xoá theo (khoá ngoại xoá lan truyền), còn sự kiện
    // và cảnh báo đã sinh ra vẫn giữ để không làm gãy lịch sử cảnh báo.
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.Admin)]
    public async Task<IActionResult> DeleteSensor(Guid id, CancellationToken ct)
    {
        var sensor = await FindSensorAsync(id, ct);

        db.Sensors.Remove(sensor);
        SystemLogWriter.Add(db, clock.GetUtcNow().UtcDateTime, SystemLogLevel.Warning, SystemLogCategory.Sensor,
            $"{User.DisplayName()} gỡ cảm biến {sensor.Name} khỏi hệ thống.", sensor.Id, User.Actor());
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    // Lịch sử đọc thô của một cảm biến, mới nhất trước.
    [HttpGet("{id:guid}/readings")]
    public async Task<ActionResult<PagedResult<SensorReadingDto>>> GetReadings(
        Guid id,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var sensor = await FindSensorAsync(id, ct);
        var (p, ps) = Paging.Normalize(page, pageSize);

        var query = db.SensorReadings.AsNoTracking().Where(r => r.SensorId == id);
        if (from is not null) query = query.Where(r => r.RecordedAt >= from);
        if (to is not null) query = query.Where(r => r.RecordedAt < to);
        query = query.OrderByDescending(r => r.RecordedAt);

        var total = await query.CountAsync(ct);
        var items = await query.Skip((p - 1) * ps).Take(ps).ToListAsync(ct);

        return Ok(new PagedResult<SensorReadingDto>
        {
            Items = items.Select(r => new SensorReadingDto(
                r.Id, r.SensorId, sensor.Type, sensor.AreaId, r.Value, r.RecordedAt, r.ReceivedAt)).ToList(),
            Page = p,
            PageSize = ps,
            Total = total
        });
    }

    private async Task<Sensor> FindSensorAsync(Guid id, CancellationToken ct) =>
        await db.Sensors.FirstOrDefaultAsync(s => s.Id == id, ct)
        ?? throw ApiException.NotFound("SENSOR_NOT_FOUND", "Không tìm thấy cảm biến với id đã cho.");

    private async Task EnsureAreaExistsAsync(Guid areaId, CancellationToken ct)
    {
        if (!await db.Locations.AnyAsync(l => l.Id == areaId, ct))
            throw ApiException.BadRequest("AREA_NOT_FOUND", "areaId không hợp lệ. Lấy danh sách hợp lệ tại GET /api/v1/areas.");
    }
}
