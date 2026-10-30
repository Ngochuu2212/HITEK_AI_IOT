using Hitek.BackendApi.Common;
using Hitek.BackendApi.Data;
using Hitek.BackendApi.Domain;
using Hitek.BackendApi.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hitek.BackendApi.RiskEngine;

// Gộp nguồn theo luật: tìm các nguồn độc lập (AI/PIR/Door/Beam) đang cùng active trong một
// khu vực, đối chiếu với bảng luật RiskRule của tenant rồi quy ra mức cảnh báo. Chạy ngay
// sau khi reading/event được ghi xuống, chưa cần worker hay hàng đợi riêng ở quy mô này.
public class RiskEngineService(
    HitekDbContext db,
    IHubContext<AlertsHub> hub,
    IOptions<RiskEngineOptions> options,
    TimeProvider clock)
{
    private const string SystemActor = "system";

    private readonly RiskEngineOptions _opt = options.Value;

    // Trả về id của cảnh báo đang mở cho khu vực sau khi đánh giá, hoặc null nếu
    // không có cảnh báo nào. AI Service cần giá trị này để gắn ai_event và evidence
    // vào đúng cảnh báo — cơ chế chống chụp bằng chứng trùng bên đó dựa vào nó.
    public async Task<Guid?> EvaluateAreaAsync(Guid areaId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        var activeSources = await ComputeActiveSourcesAsync(areaId, now, ct);
        var sourceCount = activeSources.Count;
        var rules = await RiskRuleStore.LoadAsync(db, _opt, ct);
        var decision = RiskRuleEvaluator.Decide(activeSources.Keys, now, rules);
        var level = decision.Level;
        var triggeredRules = decision.TriggeredRules.Count > 0 ? string.Join(",", decision.TriggeredRules) : null;

        var openAlert = await db.Alerts
            .Where(a => a.AreaId == areaId && a.EndedAt == null)
            .FirstOrDefaultAsync(ct);

        if (sourceCount > 0)
        {
            if (openAlert is null)
            {
                openAlert = new Alert
                {
                    Id = Guid.NewGuid(),
                    AreaId = areaId,
                    Level = level,
                    PeakLevel = level,
                    SourceCount = sourceCount,
                    TriggeredRules = triggeredRules,
                    StartedAt = now,
                    HandledStatus = AlertHandledStatus.Open
                };
                db.Alerts.Add(openAlert);
                SystemLogWriter.Add(db, now, LogLevelFor(level), SystemLogCategory.Alert,
                    $"Phát sinh cảnh báo mức {Display(level)} tại {await AreaNameAsync(areaId, ct)}, " +
                    $"{sourceCount} nguồn: {string.Join(", ", activeSources.Keys.Select(Display))}" +
                    (triggeredRules is null ? "." : $" (luật {triggeredRules})."),
                    openAlert.Id, SystemActor);
                await db.SaveChangesAsync(ct);

                await LinkContributingEventsAsync(openAlert, activeSources, now, ct);
                await PushUpdateAsync(openAlert, ct);
            }
            else if (openAlert.Level != level || openAlert.SourceCount != sourceCount || openAlert.TriggeredRules != triggeredRules)
            {
                if (level > openAlert.PeakLevel)
                {
                    SystemLogWriter.Add(db, now, LogLevelFor(level), SystemLogCategory.Alert,
                        $"Cảnh báo tại {await AreaNameAsync(areaId, ct)} nâng lên mức {Display(level)}" +
                        (triggeredRules is null ? "." : $" (luật {triggeredRules})."),
                        openAlert.Id, SystemActor);
                }

                openAlert.Level = level;
                openAlert.PeakLevel = level > openAlert.PeakLevel ? level : openAlert.PeakLevel;
                openAlert.SourceCount = sourceCount;
                openAlert.TriggeredRules = triggeredRules;
                await db.SaveChangesAsync(ct);

                // Nối cả ở lần đổi mức, không chỉ lúc tạo — có vết này mới trả lời được
                // câu "vì sự kiện nào mà cảnh báo nhảy lên Critical".
                await LinkContributingEventsAsync(openAlert, activeSources, now, ct);
                await PushUpdateAsync(openAlert, ct);
            }

            return openAlert.Id;
        }

        // sourceCount == 0 — chỉ đóng sau một khoảng lặng, để một nhiễu lẻ không làm cảnh báo chớp tắt.
        if (openAlert is null) return null;

        var hasRecentSignal = await AnyPositiveSignalSinceAsync(areaId, now.AddSeconds(-_opt.CooldownSeconds), ct);

        // Vẫn trong thời gian chờ nên cảnh báo còn mở, trả id để bên gọi gắn vào.
        if (hasRecentSignal) return openAlert.Id;

        openAlert.EndedAt = now;
        openAlert.Level = AlertLevel.Low;
        openAlert.SourceCount = 0;
        openAlert.TriggeredRules = null;
        SystemLogWriter.Add(db, now, SystemLogLevel.Info, SystemLogCategory.Alert,
            $"Cảnh báo tại {await AreaNameAsync(areaId, ct)} tự đóng sau {_opt.CooldownSeconds} giây không còn tín hiệu " +
            $"(mức cao nhất đã đạt: {Display(openAlert.PeakLevel)}).",
            openAlert.Id, SystemActor);
        await db.SaveChangesAsync(ct);
        await PushUpdateAsync(openAlert, ct);

        // Vừa đóng xong, khu vực không còn cảnh báo nào đang mở.
        return null;
    }

    // Dùng cho đường xử lý sự kiện trùng: sự kiện đã ghi từ lần gửi trước nên không
    // đánh giá lại, nhưng bên gọi vẫn cần biết cảnh báo nào đang mở để gắn vào.
    public Task<Guid?> GetOpenAlertIdAsync(Guid areaId, CancellationToken ct) =>
        db.Alerts
            .Where(a => a.AreaId == areaId && a.EndedAt == null)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);

    // Trả về nguồn đang active kèm Event đại diện của nguồn đó, để còn ghi được AlertEvent.
    private async Task<Dictionary<EventSource, Guid?>> ComputeActiveSourcesAsync(
        Guid areaId, DateTime now, CancellationToken ct)
    {
        var active = new Dictionary<EventSource, Guid?>();

        // Cảm biến đã mất kết nối không còn là bằng chứng về chuyện đang xảy ra: một cảm biến
        // chết đúng lúc đang báo true mà vẫn được tính thì khu vực bị báo động mãi.
        var sensors = await db.Sensors.Where(s => s.AreaId == areaId && s.Status == SensorStatus.Online).ToListAsync(ct);
        foreach (var sensor in sensors)
        {
            var lastReadings = await db.SensorReadings
                .Where(r => r.SensorId == sensor.Id)
                .OrderByDescending(r => r.RecordedAt)
                .Take(_opt.DebounceCount)
                .ToListAsync(ct);

            var debounced = lastReadings.Count == _opt.DebounceCount && lastReadings.All(r => r.Value);
            if (!debounced) continue;

            var source = ToEventSource(sensor.Type);
            var windowStart = lastReadings.Min(r => r.RecordedAt);

            var eventId = await db.Events
                .Where(e => e.AreaId == areaId && e.Source == source && e.OccurredAt >= windowStart)
                .OrderByDescending(e => e.OccurredAt)
                .Select(e => (Guid?)e.Id)
                .FirstOrDefaultAsync(ct);

            // Nhiều cảm biến cùng loại trong một khu vực vẫn chỉ tính là một nguồn;
            // giữ lại Event đầu tiên tìm được, không để cái sau ghi đè thành rỗng.
            if (!active.TryGetValue(source, out var existing) || existing is null)
                active[source] = eventId;
        }

        var aiWindowStart = now.AddSeconds(-_opt.AiActiveWindowSeconds);
        var aiEventId = await db.Events
            .Where(e => e.AreaId == areaId &&
                        e.Source == EventSource.Ai &&
                        e.OccurredAt >= aiWindowStart &&
                        (e.Confidence == null || e.Confidence >= _opt.MinConfidence))
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(ct);
        if (aiEventId is not null) active[EventSource.Ai] = aiEventId;

        return active;
    }

    // Mỗi cặp (Alert, Event) chỉ nối một lần; LevelAtLink giữ mức tại đúng thời điểm nối.
    private async Task LinkContributingEventsAsync(
        Alert alert, Dictionary<EventSource, Guid?> activeSources, DateTime now, CancellationToken ct)
    {
        var eventIds = activeSources.Values.OfType<Guid>().Distinct().ToList();
        if (eventIds.Count == 0) return;

        var alreadyLinked = await db.AlertEvents
            .Where(l => l.AlertId == alert.Id && eventIds.Contains(l.EventId))
            .Select(l => l.EventId)
            .ToListAsync(ct);

        var missing = eventIds.Except(alreadyLinked).ToList();
        if (missing.Count == 0) return;

        foreach (var eventId in missing)
        {
            db.AlertEvents.Add(new AlertEvent
            {
                Id = Guid.NewGuid(),
                AlertId = alert.Id,
                EventId = eventId,
                LevelAtLink = alert.Level,
                LinkedAt = now
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<bool> AnyPositiveSignalSinceAsync(Guid areaId, DateTime since, CancellationToken ct)
    {
        var sensorIds = await db.Sensors.Where(s => s.AreaId == areaId).Select(s => s.Id).ToListAsync(ct);

        var hasReading = await db.SensorReadings
            .AnyAsync(r => sensorIds.Contains(r.SensorId) && r.Value && r.RecordedAt >= since, ct);
        if (hasReading) return true;

        return await db.Events.AnyAsync(e => e.AreaId == areaId && e.OccurredAt >= since, ct);
    }

    public static EventSource ToEventSource(SensorType type) => type switch
    {
        SensorType.Pir => EventSource.Pir,
        SensorType.Door => EventSource.Door,
        SensorType.Beam => EventSource.Beam,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Loại cảm biến chưa được ánh xạ sang EventSource.")
    };

    private async Task<string> AreaNameAsync(Guid areaId, CancellationToken ct) =>
        await db.Locations.Where(l => l.Id == areaId).Select(l => l.Name).FirstOrDefaultAsync(ct)
        ?? areaId.ToString();

    private static SystemLogLevel LogLevelFor(AlertLevel level) =>
        level >= AlertLevel.High ? SystemLogLevel.Warning : SystemLogLevel.Info;

    private static string Display(AlertLevel level) => level.ToString().ToUpperInvariant();

    private static string Display(EventSource source) => source.ToString().ToUpperInvariant();

    private Task PushUpdateAsync(Alert alert, CancellationToken ct) =>
        AlertBroadcast.SendAsync(db, hub, alert, ct);
}
