using Hitek.BackendApi.Auth;
using Hitek.BackendApi.Data;
using Hitek.BackendApi.Domain;
using Hitek.BackendApi.Dtos;
using Hitek.BackendApi.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hitek.BackendApi.Controllers;

// Số liệu tổng quan cho Web Dashboard — đề án mục 4.1 yêu cầu số camera/cảm biến kèm
// trạng thái kết nối, số sự kiện trong ngày theo mức độ, và danh sách cảnh báo gần nhất.
[ApiController]
[Route("api/v1/dashboard")]
[Authorize(Policy = AuthPolicies.Viewer)]
public class DashboardController(HitekDbContext db, TimeProvider clock) : ControllerBase
{
    private const int RecentAlertCount = 5;

    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary(CancellationToken ct)
    {
        // "Trong ngày" tính theo UTC cho khớp với mọi mốc thời gian khác trong hệ thống.
        // Dashboard tự đổi sang giờ địa phương khi hiển thị.
        var startOfDay = clock.GetUtcNow().UtcDateTime.Date;

        // Gom nhóm ngay dưới database thay vì tải hết bản ghi về rồi đếm trong bộ nhớ:
        // bảng event có thể lên tới hàng chục dòng mỗi giây khi AI đang chạy.
        var sensorCounts = await db.Sensors
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Cột status của camera là ENUM bên AI Service, gom nhóm dưới database rồi so
        // chuỗi trong bộ nhớ (xem SharedTables.cs).
        var cameraCounts = await db.Cameras
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var eventCounts = await db.Events
            .Where(e => e.OccurredAt >= startOfDay)
            .GroupBy(e => e.Source)
            .Select(g => new { Source = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Đếm theo mức cao nhất từng đạt: vụ đã lên Critical rồi đóng vẫn là một vụ Critical.
        var alertCounts = await db.Alerts
            .Where(a => a.StartedAt >= startOfDay)
            .GroupBy(a => a.PeakLevel)
            .Select(g => new { Level = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var openAlerts = await db.Alerts.CountAsync(a => a.EndedAt == null, ct);

        var recentAlerts = await db.Alerts
            .OrderByDescending(a => a.StartedAt)
            .Take(RecentAlertCount)
            .ToListAsync(ct);

        var recentOrigins = await AlertOriginLoader.LoadAsync(db, recentAlerts.Select(a => a.Id).ToList(), ct);

        int Cameras(string status) => cameraCounts
            .Where(x => string.Equals(x.Status, status, StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.Count);
        int Sensors(SensorStatus status) => sensorCounts.FirstOrDefault(x => x.Status == status)?.Count ?? 0;
        int Events(EventSource source) => eventCounts.FirstOrDefault(x => x.Source == source)?.Count ?? 0;
        int Alerts(AlertLevel level) => alertCounts.FirstOrDefault(x => x.Level == level)?.Count ?? 0;

        return Ok(new DashboardSummaryDto(
            Cameras: new CameraSummaryDto(
                Total: cameraCounts.Sum(x => x.Count),
                Active: Cameras("active"),
                Inactive: Cameras("inactive"),
                Offline: Cameras("offline"),
                Disabled: Cameras("disabled")),
            Sensors: new SensorSummaryDto(
                Total: sensorCounts.Sum(x => x.Count),
                Online: Sensors(SensorStatus.Online),
                Offline: Sensors(SensorStatus.Offline)),
            EventsToday: new EventSummaryDto(
                Total: eventCounts.Sum(x => x.Count),
                Ai: Events(EventSource.Ai),
                Pir: Events(EventSource.Pir),
                Door: Events(EventSource.Door),
                Beam: Events(EventSource.Beam)),
            AlertsToday: new AlertSummaryDto(
                Total: alertCounts.Sum(x => x.Count),
                Low: Alerts(AlertLevel.Low),
                Medium: Alerts(AlertLevel.Medium),
                High: Alerts(AlertLevel.High),
                Critical: Alerts(AlertLevel.Critical)),
            OpenAlerts: openAlerts,
            RecentAlerts: recentAlerts.Select(a => a.ToDto(recentOrigins.GetValueOrDefault(a.Id, AlertOrigins.None))).ToList()));
    }
}
