using Hitek.BackendApi.Domain;
using Hitek.BackendApi.RiskEngine;

namespace Hitek.BackendApi.Tests;

// Kiểm thử riêng bảng luật, không đụng database: đưa vào tập nguồn đang active, mốc thời
// gian và bộ luật, nhận ra mức cảnh báo cùng các luật đã quyết định mức đó.
public class RiskEngineRuleTests
{
    private static RiskDecision Decide(EventSource[] sources, int utcHour = 10, List<RiskRule>? rules = null)
    {
        var now = new DateTime(2026, 9, 23, utcHour, 0, 0, DateTimeKind.Utc);
        return RiskRuleEvaluator.Decide(sources, now, rules ?? RiskRuleDefaults.Create(new RiskEngineOptions()));
    }

    private static AlertLevel Level(EventSource[] sources, int utcHour = 10) => Decide(sources, utcHour).Level;

    // R1 — đếm số nguồn độc lập đang cùng báo.
    [Fact]
    public void KhongCoNguon_TraVeLow() =>
        Assert.Equal(AlertLevel.Low, Level([]));

    [Fact]
    public void MotNguon_TraVeMedium() =>
        Assert.Equal(AlertLevel.Medium, Level([EventSource.Pir]));

    [Fact]
    public void HaiNguon_TraVeHigh() =>
        Assert.Equal(AlertLevel.High, Level([EventSource.Pir, EventSource.Beam]));

    [Fact]
    public void BaNguon_TraVeCritical() =>
        Assert.Equal(AlertLevel.Critical, Level([EventSource.Pir, EventSource.Beam, EventSource.Ai]));

    // R2 — cùng hai nguồn nhưng ngoài khung giờ cho phép thì nặng hơn hẳn.
    // Mặc định khung giờ là 01:00-11:00 UTC, nên 20:00 UTC là ngoài giờ.
    [Fact]
    public void HaiNguon_NgoaiKhungGio_EpLenCritical()
    {
        var decision = Decide([EventSource.Pir, EventSource.Beam], utcHour: 20);

        Assert.Equal(AlertLevel.Critical, decision.Level);
        Assert.Equal(["R2"], decision.TriggeredRules);
    }

    [Fact]
    public void MotNguon_NgoaiKhungGio_VanChiLaMedium() =>
        Assert.Equal(AlertLevel.Medium, Level([EventSource.Pir], utcHour: 20));

    // R3 — cặp camera thấy người cộng cảm biến cửa mở, ngay trong giờ làm việc.
    [Fact]
    public void AiVaDoor_TrongKhungGio_EpLenCritical()
    {
        var decision = Decide([EventSource.Ai, EventSource.Door]);

        Assert.Equal(AlertLevel.Critical, decision.Level);
        Assert.Equal(["R3"], decision.TriggeredRules);
    }

    // Cặp khác cũng hai nguồn nhưng không phải cặp đặc biệt thì vẫn chỉ High.
    // Phép thử này giữ cho R3 không bị viết nhầm thành "hai nguồn bất kỳ".
    [Fact]
    public void AiVaPir_TrongKhungGio_ChiLaHigh() =>
        Assert.Equal(AlertLevel.High, Level([EventSource.Ai, EventSource.Pir]));

    [Fact]
    public void DoorMotMinh_ChiLaMedium() =>
        Assert.Equal(AlertLevel.Medium, Level([EventSource.Door]));

    [Fact]
    public void NhieuLuatCungMucCaoNhat_ThiGhiDuCacLuat() =>
        Assert.Equal(["R1-3", "R2"], Decide([EventSource.Pir, EventSource.Beam, EventSource.Door], utcHour: 20).TriggeredRules);

    // Luật là dữ liệu: tắt R3 thì cặp AI + cửa chỉ còn là hai nguồn bình thường.
    [Fact]
    public void TatLuatR3_ThiAiVaDoorChiConHigh()
    {
        var rules = RiskRuleDefaults.Create(new RiskEngineOptions());
        rules.Single(r => r.Code == "R3").Enabled = false;

        Assert.Equal(AlertLevel.High, Decide([EventSource.Ai, EventSource.Door], rules: rules).Level);
    }

    [Fact]
    public void LuatTuThem_PirCongBeam_DuocApDung()
    {
        var rules = RiskRuleDefaults.Create(new RiskEngineOptions());
        rules.Add(new RiskRule { Code = "X1", Level = AlertLevel.Critical, RequiredSources = "PIR,BEAM", SortOrder = 60 });

        var decision = Decide([EventSource.Pir, EventSource.Beam], rules: rules);

        Assert.Equal(AlertLevel.Critical, decision.Level);
        Assert.Equal(["X1"], decision.TriggeredRules);
    }

    // Luật không điều kiện nào (chỉ có thể do sửa tay dưới database) không được khớp bừa.
    [Fact]
    public void LuatKhongCoDieuKien_ThiBoQua()
    {
        List<RiskRule> rules = [new RiskRule { Code = "BAD", Level = AlertLevel.Critical }];

        Assert.Equal(AlertLevel.Low, Decide([EventSource.Pir], rules: rules).Level);
    }
}
