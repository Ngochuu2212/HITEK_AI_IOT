using Hitek.BackendApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace Hitek.BackendApi.Tests;

// Kiểm thử Risk Engine chạy thật trên database: chống nhiễu, vòng đời cảnh báo,
// bảng nối truy vết và cách ly theo tenant.
public class RiskEngineTests
{
    [Fact]
    public async Task ChuaDuLuotDocLienTiep_ThiChuaMoCanhBao()
    {
        using var harness = new TestHarness();
        using var db = harness.NewDbContext();
        var pir = harness.AddSensor(db, SensorType.Pir);

        // Ngưỡng chống nhiễu mặc định là 3, mới có 2 nên chưa tính là nguồn đang hoạt động.
        harness.PushReadings(db, pir, true, true);
        await harness.NewRiskEngine(db).EvaluateAreaAsync(TestHarness.AreaWarehouse, default);

        Assert.Empty(await db.Alerts.ToListAsync());
    }

    [Fact]
    public async Task DuBaLuotDocLienTiep_ThiMoCanhBaoMucMedium()
    {
        using var harness = new TestHarness();
        using var db = harness.NewDbContext();
        var pir = harness.AddSensor(db, SensorType.Pir);

        harness.PushReadings(db, pir, true, true, true);
        var alertId = await harness.NewRiskEngine(db).EvaluateAreaAsync(TestHarness.AreaWarehouse, default);

        var alert = Assert.Single(await db.Alerts.ToListAsync());
        Assert.Equal(alertId, alert.Id);
        Assert.Equal(AlertLevel.Medium, alert.Level);
        Assert.Equal(1, alert.SourceCount);
        Assert.Null(alert.EndedAt);
    }

    [Fact]
    public async Task MotLuotDocFalseXenGiua_ThiDutChuoiVaKhongMoCanhBao()
    {
        using var harness = new TestHarness();
        using var db = harness.NewDbContext();
        var pir = harness.AddSensor(db, SensorType.Pir);

        // Đúng tình huống từng gặp khi chạy thử tay: bộ giả lập chen một lượt false
        // vào giữa làm đứt chuỗi, nên không đủ ba lượt liên tiếp.
        harness.PushReadings(db, pir, true, true, false, true);
        await harness.NewRiskEngine(db).EvaluateAreaAsync(TestHarness.AreaWarehouse, default);

        Assert.Empty(await db.Alerts.ToListAsync());
    }

    [Fact]
    public async Task GopNguonAiVoiIot_ThiNangLenHigh()
    {
        using var harness = new TestHarness();
        using var db = harness.NewDbContext();
        var pir = harness.AddSensor(db, SensorType.Pir);

        harness.PushReadings(db, pir, true, true, true);
        var engine = harness.NewRiskEngine(db);
        await engine.EvaluateAreaAsync(TestHarness.AreaWarehouse, default);

        harness.PushAiEvent(db);
        await engine.EvaluateAreaAsync(TestHarness.AreaWarehouse, default);

        var alert = Assert.Single(await db.Alerts.ToListAsync());
        Assert.Equal(AlertLevel.High, alert.Level);
        Assert.Equal(2, alert.SourceCount);
    }

    [Fact]
    public async Task AiCongCamBienCua_ThiLenThangCritical()
    {
        using var harness = new TestHarness();
        using var db = harness.NewDbContext();
        var door = harness.AddSensor(db, SensorType.Door);

        harness.PushReadings(db, door, true, true, true);
        var engine = harness.NewRiskEngine(db);
        await engine.EvaluateAreaAsync(TestHarness.AreaWarehouse, default);

        harness.PushAiEvent(db);
        await engine.EvaluateAreaAsync(TestHarness.AreaWarehouse, default);

        // Vẫn chỉ hai nguồn và đang trong khung giờ cho phép, nhưng đúng cặp mà R3 xét.
        var alert = Assert.Single(await db.Alerts.ToListAsync());
        Assert.Equal(AlertLevel.Critical, alert.Level);
        Assert.Equal(2, alert.SourceCount);
    }

    [Fact]
    public async Task DoTinCayDuoiNguong_ThiKhongTinhLaNguonDangHoatDong()
    {
        using var harness = new TestHarness();
        using var db = harness.NewDbContext();

        // Ngưỡng mặc định là 0.6, gửi 0.4 nên Risk Engine phải bỏ qua.
        harness.PushAiEvent(db, confidence: 0.4);
        await harness.NewRiskEngine(db).EvaluateAreaAsync(TestHarness.AreaWarehouse, default);

        Assert.Empty(await db.Alerts.ToListAsync());
    }

    [Fact]
    public async Task HetNguonNhungChuaQuaThoiGianCho_ThiCanhBaoVanMo()
    {
        using var harness = new TestHarness();
        using var db = harness.NewDbContext();
        var pir = harness.AddSensor(db, SensorType.Pir);
        var engine = harness.NewRiskEngine(db);

        harness.PushReadings(db, pir, true, true, true);
        await engine.EvaluateAreaAsync(TestHarness.AreaWarehouse, default);

        harness.PushReadings(db, pir, false, false, false);
        await engine.EvaluateAreaAsync(TestHarness.AreaWarehouse, default);

        var alert = Assert.Single(await db.Alerts.ToListAsync());
        Assert.Null(alert.EndedAt);
    }

    [Fact]
    public async Task QuaThoiGianChoMaKhongCoTinHieu_ThiDongCanhBao()
    {
        using var harness = new TestHarness();
        using var db = harness.NewDbContext();
        var pir = harness.AddSensor(db, SensorType.Pir);
        var engine = harness.NewRiskEngine(db);

        harness.PushReadings(db, pir, true, true, true);
        await engine.EvaluateAreaAsync(TestHarness.AreaWarehouse, default);

        harness.PushReadings(db, pir, false, false, false);

        // Đẩy đồng hồ vượt quá thời gian chờ để mọi tín hiệu dương đều rơi ra ngoài cửa sổ.
        harness.Clock.Advance(TimeSpan.FromSeconds(harness.RiskOptions.CooldownSeconds + 10));
        var alertId = await engine.EvaluateAreaAsync(TestHarness.AreaWarehouse, default);

        var alert = Assert.Single(await db.Alerts.ToListAsync());
        Assert.NotNull(alert.EndedAt);
        Assert.Equal(AlertLevel.Low, alert.Level);
        Assert.Equal(0, alert.SourceCount);

        // Mức hiện tại về Low, nhưng lịch sử vẫn phải nhớ vụ này từng ở mức Medium.
        Assert.Equal(AlertLevel.Medium, alert.PeakLevel);

        // Khu vực không còn cảnh báo nào đang mở nên không có id để trả về.
        Assert.Null(alertId);
    }

    [Fact]
    public async Task MotKhuVucChiCoDungMotCanhBaoDangMo()
    {
        using var harness = new TestHarness();
        using var db = harness.NewDbContext();
        var pir = harness.AddSensor(db, SensorType.Pir);
        var engine = harness.NewRiskEngine(db);

        harness.PushReadings(db, pir, true, true, true);
        await engine.EvaluateAreaAsync(TestHarness.AreaWarehouse, default);
        await engine.EvaluateAreaAsync(TestHarness.AreaWarehouse, default);
        await engine.EvaluateAreaAsync(TestHarness.AreaWarehouse, default);

        Assert.Single(await db.Alerts.Where(a => a.EndedAt == null).ToListAsync());
    }

    [Fact]
    public async Task BangNoiGhiLaiCaLucTaoLanLucNangMuc()
    {
        using var harness = new TestHarness();
        using var db = harness.NewDbContext();
        var pir = harness.AddSensor(db, SensorType.Pir);
        var engine = harness.NewRiskEngine(db);

        harness.PushReadings(db, pir, true, true, true);
        await engine.EvaluateAreaAsync(TestHarness.AreaWarehouse, default);

        harness.PushAiEvent(db);
        await engine.EvaluateAreaAsync(TestHarness.AreaWarehouse, default);

        var links = await db.AlertEvents.OrderBy(l => l.LinkedAt).ToListAsync();

        // Hai dòng: một của cảm biến lúc mở cảnh báo, một của AI lúc nâng mức. Thiếu
        // dòng thứ hai thì mất hẳn dấu vết vì sao cảnh báo lên mức cao hơn.
        Assert.Equal(2, links.Count);
        Assert.Contains(links, l => l.LevelAtLink == AlertLevel.Medium);
        Assert.Contains(links, l => l.LevelAtLink == AlertLevel.High);
    }

    [Fact]
    public async Task MoiCapCanhBaoVaSuKienChiNoiMotLan()
    {
        using var harness = new TestHarness();
        using var db = harness.NewDbContext();
        var pir = harness.AddSensor(db, SensorType.Pir);
        var engine = harness.NewRiskEngine(db);

        harness.PushReadings(db, pir, true, true, true);
        await engine.EvaluateAreaAsync(TestHarness.AreaWarehouse, default);
        await engine.EvaluateAreaAsync(TestHarness.AreaWarehouse, default);

        var links = await db.AlertEvents.ToListAsync();
        Assert.Single(links);
    }

    [Fact]
    public async Task CanhBaoCuaTenantKhac_ThiKhongNhinThay()
    {
        using var harness = new TestHarness();

        // Dựng cảnh báo bên tenant B trước.
        harness.Tenant.TenantId = TestHarness.TenantB;
        using (var dbB = harness.NewDbContext())
        {
            var sensorB = harness.AddSensor(dbB, SensorType.Pir, TestHarness.AreaOtherTenant, TestHarness.TenantB);
            harness.PushReadings(dbB, sensorB, true, true, true);
            await harness.NewRiskEngine(dbB).EvaluateAreaAsync(TestHarness.AreaOtherTenant, default);
            Assert.Single(await dbB.Alerts.ToListAsync());
        }

        // Cùng database, nhưng đọc dưới danh nghĩa tenant A thì không thấy gì của B.
        harness.Tenant.TenantId = TestHarness.TenantA;
        using var dbA = harness.NewDbContext();

        // Tenant A chưa có cảnh báo nào của riêng nó.
        Assert.Empty(await dbA.Alerts.ToListAsync());

        // Cảm biến thì tenant A có sẵn ba cái từ dữ liệu seed, nên điều cần khẳng định
        // là không lẫn cảm biến của tenant B vào, chứ không phải danh sách rỗng.
        var sensorsSeenByA = await dbA.Sensors.ToListAsync();
        Assert.All(sensorsSeenByA, s => Assert.Equal(TestHarness.TenantA, s.TenantId));
        Assert.DoesNotContain(sensorsSeenByA, s => s.AreaId == TestHarness.AreaOtherTenant);
    }
}
