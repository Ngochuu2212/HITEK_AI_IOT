using Hitek.BackendApi.Common;
using Hitek.BackendApi.Data;
using Hitek.BackendApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace Hitek.BackendApi.RiskEngine;

public sealed record RiskDecision(AlertLevel Level, IReadOnlyList<string> TriggeredRules);

// Bảng luật kết hợp đa nguồn, chạy trên dữ liệu RiskRule của tenant:
//
//  R1  Đếm số nguồn độc lập đang active rồi quy ra mức (ba luật R1-1, R1-2, R1-3).
//  R2  Từ hai nguồn trở lên mà ngoài khung giờ cho phép thì lên thẳng Critical.
//  R3  Camera thấy người cùng lúc cảm biến cửa mở thì lên thẳng Critical.
//
// Mức cảnh báo là mức cao nhất trong các luật đang khớp. Tách khỏi phần ghi database để
// kiểm thử được từng luật mà không cần dựng cảnh báo thật.
public static class RiskRuleEvaluator
{
    public static RiskDecision Decide(
        IReadOnlyCollection<EventSource> activeSources, DateTime nowUtc, IReadOnlyCollection<RiskRule> rules)
    {
        var matched = rules
            .Where(r => r.Enabled && Matches(r, activeSources, nowUtc))
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.Code, StringComparer.Ordinal)
            .ToList();

        if (matched.Count == 0) return new RiskDecision(AlertLevel.Low, []);

        var level = matched.Max(r => r.Level);
        return new RiskDecision(level, matched.Where(r => r.Level == level).Select(r => r.Code).ToList());
    }

    public static bool Matches(RiskRule rule, IReadOnlyCollection<EventSource> activeSources, DateTime nowUtc)
    {
        var required = ParseSources(rule.RequiredSources);

        // Luật không có điều kiện nào thì khớp với mọi thứ, kể cả khi không có nguồn nào.
        // API đã chặn từ lúc tạo, đây là chốt chặn cuối phòng dữ liệu sửa tay dưới database.
        if (rule.MinSources is null && required.Count == 0) return false;
        if (activeSources.Count == 0) return false;

        if (rule.MinSources is { } min && activeSources.Count < min) return false;
        if (required.Any(s => !activeSources.Contains(s))) return false;

        if (rule.OutsideAllowedHoursOnly)
        {
            var start = rule.AllowedStartUtcHour ?? 0;
            var end = rule.AllowedEndUtcHour ?? 24;
            var insideAllowedHours = nowUtc.Hour >= start && nowUtc.Hour < end;
            if (insideAllowedHours) return false;
        }

        return true;
    }

    public static IReadOnlyList<EventSource> ParseSources(string? stored) =>
        string.IsNullOrWhiteSpace(stored)
            ? []
            : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => Enum.Parse<EventSource>(s, ignoreCase: true))
                .Distinct()
                .ToList();

    public static string? FormatSources(IEnumerable<EventSource>? sources)
    {
        var list = sources?.Distinct().OrderBy(s => s).ToList();
        return list is null || list.Count == 0
            ? null
            : string.Join(",", list.Select(s => s.ToString().ToUpperInvariant()));
    }
}

// Bộ luật mặc định, cho kết quả đúng như bảng luật đã chốt ở báo cáo Tuần 1.
public static class RiskRuleDefaults
{
    public static List<RiskRule> Create(RiskEngineOptions options) =>
    [
        new()
        {
            Id = Guid.NewGuid(), Code = "R1-1", Level = AlertLevel.Medium, MinSources = 1, SortOrder = 10,
            Name = "Một nguồn đang hoạt động",
            Description = "Một nguồn bất kỳ (camera hoặc một loại cảm biến) đã qua bước chống nhiễu."
        },
        new()
        {
            Id = Guid.NewGuid(), Code = "R1-2", Level = AlertLevel.High, MinSources = 2, SortOrder = 20,
            Name = "Hai nguồn độc lập cùng hoạt động",
            Description = "Hai nguồn khác loại cùng báo trong một khu vực, khó là nhiễu ngẫu nhiên."
        },
        new()
        {
            Id = Guid.NewGuid(), Code = "R1-3", Level = AlertLevel.Critical, MinSources = 3, SortOrder = 30,
            Name = "Từ ba nguồn trở lên cùng hoạt động",
            Description = "Ba lớp phát hiện độc lập cùng xác nhận một tình huống."
        },
        new()
        {
            Id = Guid.NewGuid(), Code = "R2", Level = AlertLevel.Critical, MinSources = 2, SortOrder = 40,
            OutsideAllowedHoursOnly = true,
            AllowedStartUtcHour = options.AllowedStartUtcHour,
            AllowedEndUtcHour = options.AllowedEndUtcHour,
            Name = "Nhiều nguồn ngoài khung giờ cho phép",
            Description = "Từ hai nguồn trở lên cùng báo ngoài giờ làm việc thì nghiêm trọng hơn hẳn."
        },
        new()
        {
            Id = Guid.NewGuid(), Code = "R3", Level = AlertLevel.Critical, RequiredSources = "AI,DOOR", SortOrder = 50,
            Name = "Camera thấy người khi cửa đang mở",
            Description = "Cửa mở cộng với người trong khung hình là dấu hiệu đột nhập rõ nhất trong các cặp nguồn."
        }
    ];
}

public static class RiskRuleStore
{
    // Tenant chưa có luật nào thì tạo bộ mặc định ngay lần đầu cần tới, để tenant mới thêm
    // vào vẫn có cảnh báo mà không ai phải nhớ chạy thêm script.
    public static async Task<List<RiskRule>> LoadAsync(HitekDbContext db, RiskEngineOptions options, CancellationToken ct)
    {
        var rules = await db.RiskRules.AsNoTracking().OrderBy(r => r.SortOrder).ToListAsync(ct);
        if (rules.Count > 0) return rules;

        var defaults = RiskRuleDefaults.Create(options);
        db.RiskRules.AddRange(defaults);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // Một request khác của cùng tenant vừa tạo xong trước. Bỏ bản của mình, đọc lại.
            foreach (var rule in defaults) db.Entry(rule).State = EntityState.Detached;
            return await db.RiskRules.AsNoTracking().OrderBy(r => r.SortOrder).ToListAsync(ct);
        }

        foreach (var rule in defaults) db.Entry(rule).State = EntityState.Detached;
        return defaults;
    }
}
