using Hitek.BackendApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace Hitek.BackendApi.Tests;

// Cảm biến mất kết nối phải tự chuyển sang Offline — đề án mục 2.4 và checklist
// nghiệm thu mục 11. Trước đây trạng thái chỉ đi một chiều lên Online.
public class SensorMonitorTests
{
    private static void MarkOnline(TestHarness harness, Data.HitekDbContext db, Sensor sensor, int secondsAgo)
    {
        sensor.Status = SensorStatus.Online;
        sensor.LastSeenAt = harness.Clock.GetUtcNow().UtcDateTime.AddSeconds(-secondsAgo);
        db.SaveChanges();
    }

    [Fact]
    public async Task QuaNguongKhongCoTin_ThiHaXuongOffline()
    {
        using var harness = new TestHarness();
        using var db = harness.NewDbContext();
        var pir = harness.AddSensor(db, SensorType.Pir);
        MarkOnline(harness, db, pir, secondsAgo: harness.MonitorOptions.OfflineAfterSeconds + 5);

        var changed = await harness.NewSensorMonitor().MarkStaleSensorsOfflineAsync(default);

        Assert.Equal(1, changed);
        using var check = harness.NewDbContext();
        Assert.Equal(SensorStatus.Offline, (await check.Sensors.FirstAsync(s => s.Id == pir.Id)).Status);
    }

    [Fact]
    public async Task VuaCoTinGanDay_ThiGiuNguyenOnline()
    {
        using var harness = new TestHarness();
        using var db = harness.NewDbContext();
        var pir = harness.AddSensor(db, SensorType.Pir);
        MarkOnline(harness, db, pir, secondsAgo: 5);

        var changed = await harness.NewSensorMonitor().MarkStaleSensorsOfflineAsync(default);

        Assert.Equal(0, changed);
        using var check = harness.NewDbContext();
        Assert.Equal(SensorStatus.Online, (await check.Sensors.FirstAsync(s => s.Id == pir.Id)).Status);
    }

    [Fact]
    public async Task ChuaTungNhanTinNaoMaDangOnline_ThiCungHaXuongOffline()
    {
        using var harness = new TestHarness();
        using var db = harness.NewDbContext();
        var pir = harness.AddSensor(db, SensorType.Pir);
        pir.Status = SensorStatus.Online;
        pir.LastSeenAt = null;
        db.SaveChanges();

        var changed = await harness.NewSensorMonitor().MarkStaleSensorsOfflineAsync(default);

        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task QuetCaCamBienCuaMoiTenant()
    {
        using var harness = new TestHarness();
        using var db = harness.NewDbContext();

        var sensorA = harness.AddSensor(db, SensorType.Pir);
        var sensorB = harness.AddSensor(db, SensorType.Beam, TestHarness.AreaOtherTenant, TestHarness.TenantB);
        MarkOnline(harness, db, sensorA, secondsAgo: 120);
        MarkOnline(harness, db, sensorB, secondsAgo: 120);

        // Tiến trình nền chạy ngoài mọi request nên không có tenant hiện tại. Nếu nó
        // không bỏ qua bộ lọc tenant thì chỉ thấy được cảm biến của tenant mặc định,
        // cảm biến của các tenant còn lại chết mà vẫn báo Online.
        var changed = await harness.NewSensorMonitor().MarkStaleSensorsOfflineAsync(default);

        Assert.Equal(2, changed);

        using var check = harness.NewDbContext();
        var all = await check.Sensors.IgnoreQueryFilters().ToListAsync();
        Assert.All(all, s => Assert.Equal(SensorStatus.Offline, s.Status));
    }

    // Mọi thiết bị cùng im lặng thì không có request nào kích hoạt Risk Engine; tiến trình nền
    // phải tự đánh giá lại để cảnh báo đóng sau thời gian chờ thay vì treo mở mãi.
    [Fact]
    public async Task KhongConDuLieuGuiToi_TienTrinhNenVanDongCanhBaoSauThoiGianCho()
    {
        using var harness = new TestHarness();
        using var db = harness.NewDbContext();
        var pir = harness.AddSensor(db, SensorType.Pir);
        harness.PushReadings(db, pir, true, true, true);
        await harness.NewRiskEngine(db).EvaluateAreaAsync(TestHarness.AreaWarehouse, default);

        harness.Clock.Advance(TimeSpan.FromSeconds(harness.RiskOptions.CooldownSeconds + 30));
        var monitor = harness.NewSensorMonitor();
        await monitor.MarkStaleSensorsOfflineAsync(default);
        await monitor.ReevaluateOpenAlertsAsync(default);

        using var check = harness.NewDbContext();
        var alert = Assert.Single(await check.Alerts.ToListAsync());
        Assert.NotNull(alert.EndedAt);
        Assert.Equal(AlertLevel.Medium, alert.PeakLevel);
    }

    [Fact]
    public async Task CamBienDaOffline_KhongTinhLaNguonDangHoatDong()
    {
        using var harness = new TestHarness();
        using var db = harness.NewDbContext();
        var pir = harness.AddSensor(db, SensorType.Pir);
        harness.PushReadings(db, pir, true, true, true);
        pir.Status = SensorStatus.Offline;
        db.SaveChanges();

        await harness.NewRiskEngine(db).EvaluateAreaAsync(TestHarness.AreaWarehouse, default);

        Assert.Empty(await db.Alerts.ToListAsync());
    }
}
