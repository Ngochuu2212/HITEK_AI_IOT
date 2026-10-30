using Hitek.BackendApi.Auth;
using Hitek.BackendApi.Common;
using Hitek.BackendApi.Data;
using Hitek.BackendApi.Domain;
using Hitek.BackendApi.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hitek.BackendApi.Controllers;

[ApiController]
[Route("api/v1/iot")]
[Authorize(Policy = AuthPolicies.Ingest)]
public class IotController(
    HitekDbContext db,
    RiskEngine.RiskEngineService riskEngine,
    TimeProvider clock,
    ILogger<IotController> logger) : ControllerBase
{
    // Hitek.IotService gọi vào đây sau khi chuẩn hoá một bản tin MQTT (hoặc gọi thẳng qua HTTP).
    [HttpPost("readings")]
    public async Task<IActionResult> PostReading(SensorReadingRequest request, CancellationToken ct)
    {
        var sensor = await db.Sensors.FindAsync([request.SensorId], ct)
            ?? throw ApiException.NotFound("SENSOR_NOT_FOUND", "Không tìm thấy cảm biến với id đã cho.");

        // MQTT QoS 1 bảo đảm "ít nhất một lần", nên broker gửi lại bản tin là chuyện bình
        // thường. Không chặn ở đây thì mỗi lần gửi lại lại đẻ thêm một reading và một event,
        // làm sai luôn cửa sổ trượt chống nhiễu của Risk Engine.
        if (await db.SensorReadings.AnyAsync(r => r.SensorId == sensor.Id && r.RecordedAt == request.RecordedAt, ct))
            return Accepted();

        var now = clock.GetUtcNow().UtcDateTime;
        var reading = new SensorReading
        {
            Id = Guid.NewGuid(),
            SensorId = sensor.Id,
            Value = request.Value,
            RecordedAt = request.RecordedAt,
            // IoT Service gắn mốc nhận lúc bản tin tới nó. Gọi thẳng endpoint này (công cụ
            // kiểm thử) thì không có mốc đó, lấy luôn lúc Backend nhận.
            ReceivedAt = request.ReceivedAt ?? now
        };
        db.SensorReadings.Add(reading);

        // Nhịp sống: có bản tin gửi về là cảm biến còn kết nối, bất kể giá trị true hay false.
        if (sensor.Status == SensorStatus.Offline)
        {
            SystemLogWriter.Add(db, now, SystemLogLevel.Info, SystemLogCategory.Sensor,
                sensor.LastSeenAt is null
                    ? $"Cảm biến {sensor.Name} bắt đầu gửi dữ liệu."
                    : $"Cảm biến {sensor.Name} kết nối lại sau khi mất tín hiệu từ {sensor.LastSeenAt:O}.",
                sensor.Id, User.Actor());
        }
        sensor.LastSeenAt = now;
        sensor.Status = SensorStatus.Online;

        if (request.Value)
        {
            db.Events.Add(new EventRecord
            {
                Id = Guid.NewGuid(),
                Source = RiskEngine.RiskEngineService.ToEventSource(sensor.Type),
                EventType = SensorEventTypes.For(sensor.Type),
                AreaId = sensor.AreaId,
                OccurredAt = request.RecordedAt,
                SensorId = sensor.Id,
                DedupKey = EventDedupKey.ForIot(reading.Id),
                // Gán qua navigation để EF tự xếp thứ tự insert: SensorReading trước, Event sau.
                SensorReading = reading
            });
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            logger.LogInformation("Bỏ qua reading trùng của cảm biến {SensorId} tại {RecordedAt}", sensor.Id, request.RecordedAt);
            return Accepted();
        }

        await riskEngine.EvaluateAreaAsync(sensor.AreaId, ct);

        return Accepted();
    }
}
