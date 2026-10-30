using Hitek.BackendApi.Common;
using Hitek.BackendApi.Data;
using Hitek.BackendApi.Domain;
using Hitek.BackendApi.RiskEngine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hitek.BackendApi.Monitoring;

// Hạ cảm biến xuống Offline khi quá lâu không nhận được bản tin nào.
//
// Trước đây trạng thái chỉ đi một chiều: IotController nâng lên Online mỗi lần có
// reading, nhưng không chỗ nào đưa về lại, nên một cảm biến chết vẫn hiện Online mãi.
// Đề án mục 2.4 yêu cầu theo dõi được online/offline, còn checklist nghiệm thu mục 11
// yêu cầu xử lý được tình huống cảm biến mất kết nối.
//
// Phải là tiến trình nền quét định kỳ chứ không thể xử lý trong controller: đúng lúc
// cảm biến chết thì không còn request nào gửi tới để mà kiểm tra.
public class SensorMonitorService(
    IServiceScopeFactory scopeFactory,
    IOptions<SensorMonitorOptions> options,
    TimeProvider clock,
    ILogger<SensorMonitorService> logger) : BackgroundService
{
    private readonly SensorMonitorOptions _opt = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Theo dõi cảm biến: quét mỗi {Interval}s, quá {Threshold}s không có tin thì hạ xuống Offline",
            _opt.ScanIntervalSeconds, _opt.OfflineAfterSeconds);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_opt.ScanIntervalSeconds), clock);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
                await MarkStaleSensorsOfflineAsync(stoppingToken);
                await ReevaluateOpenAlertsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Một lần quét lỗi không được phép giết luôn vòng lặp, nếu không thì
                // từ đó trở đi không cảm biến nào bị phát hiện mất kết nối nữa.
                logger.LogError(ex, "Lỗi khi quét trạng thái cảm biến, bỏ qua lượt này");
            }
        }
    }

    public async Task<int> MarkStaleSensorsOfflineAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HitekDbContext>();

        var cutoff = clock.GetUtcNow().UtcDateTime.AddSeconds(-_opt.OfflineAfterSeconds);

        // IgnoreQueryFilters là cố ý: đây là tác vụ hệ thống chạy nền, không có request
        // nào nên không có tenant hiện tại. Không bỏ bộ lọc thì nó chỉ quét đúng tenant
        // mặc định, cảm biến của các tenant còn lại chết mà vẫn báo Online.
        var stale = await db.Sensors
            .IgnoreQueryFilters()
            .Where(s => s.Status == SensorStatus.Online
                        && (s.LastSeenAt == null || s.LastSeenAt < cutoff))
            .ToListAsync(ct);

        if (stale.Count == 0) return 0;

        foreach (var sensor in stale)
        {
            sensor.Status = SensorStatus.Offline;
            SystemLogWriter.Add(db, clock.GetUtcNow().UtcDateTime, SystemLogLevel.Warning, SystemLogCategory.Sensor,
                sensor.LastSeenAt is null
                    ? $"Cảm biến {sensor.Name} mất kết nối: chưa từng nhận được dữ liệu."
                    : $"Cảm biến {sensor.Name} mất kết nối: quá {_opt.OfflineAfterSeconds} giây không có dữ liệu, tin cuối lúc {sensor.LastSeenAt:O}.",
                sensor.Id, "system", sensor.TenantId);
            logger.LogWarning(
                "Cảm biến {Name} ({SensorId}) chuyển sang Offline, tin cuối lúc {LastSeenAt}",
                sensor.Name, sensor.Id, sensor.LastSeenAt);
        }

        await db.SaveChangesAsync(ct);
        return stale.Count;
    }

    // Risk Engine chỉ chạy khi có dữ liệu mới gửi tới. Nếu mọi thiết bị trong khu vực cùng im
    // lặng (người đã đi, hoặc thiết bị mất kết nối) thì không còn request nào để kích hoạt
    // bước đóng cảnh báo sau thời gian chờ, và cảnh báo treo mở mãi. Quét định kỳ ở đây để
    // đánh giá lại các khu vực đang có cảnh báo mở.
    public async Task<int> ReevaluateOpenAlertsAsync(CancellationToken ct)
    {
        List<(Guid TenantId, Guid AreaId)> openAreas;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HitekDbContext>();
            openAreas = (await db.Alerts
                    .IgnoreQueryFilters()
                    .Where(a => a.EndedAt == null)
                    .Select(a => new { a.TenantId, a.AreaId })
                    .Distinct()
                    .ToListAsync(ct))
                .Select(a => (a.TenantId, a.AreaId))
                .ToList();
        }

        foreach (var (tenantId, areaId) in openAreas)
        {
            try
            {
                // Mỗi khu vực một scope riêng, đặt tenant trước khi DbContext được tạo ra.
                using var scope = scopeFactory.CreateScope();
                scope.ServiceProvider.GetRequiredService<TenantContext>().TenantId = tenantId;
                await scope.ServiceProvider.GetRequiredService<RiskEngineService>().EvaluateAreaAsync(areaId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Lỗi khi đánh giá lại khu vực {AreaId} của tenant {TenantId}", areaId, tenantId);
            }
        }

        return openAreas.Count;
    }
}
