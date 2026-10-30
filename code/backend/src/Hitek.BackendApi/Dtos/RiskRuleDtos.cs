using System.ComponentModel.DataAnnotations;
using Hitek.BackendApi.Domain;
using Hitek.BackendApi.RiskEngine;

namespace Hitek.BackendApi.Dtos;

public record RiskRuleDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool Enabled,
    AlertLevel Level,
    int? MinSources,
    IReadOnlyList<EventSource> RequiredSources,
    bool OutsideAllowedHoursOnly,
    int? AllowedStartUtcHour,
    int? AllowedEndUtcHour,
    int SortOrder);

// Dùng chung cho tạo mới và cập nhật. Code chỉ dùng lúc tạo, cập nhật thì bỏ qua.
public record RiskRuleRequest(
    [MaxLength(50)] string? Code,
    [Required, MaxLength(200)] string Name,
    [MaxLength(1000)] string? Description,
    bool Enabled,
    AlertLevel Level,
    [Range(1, 4)] int? MinSources,
    IReadOnlyList<EventSource>? RequiredSources,
    bool OutsideAllowedHoursOnly,
    [Range(0, 23)] int? AllowedStartUtcHour,
    [Range(1, 24)] int? AllowedEndUtcHour,
    int SortOrder = 100);

public static class RiskRuleMapping
{
    public static RiskRuleDto ToDto(this RiskRule r) => new(
        r.Id, r.Code, r.Name, r.Description, r.Enabled, r.Level, r.MinSources,
        RiskRuleEvaluator.ParseSources(r.RequiredSources), r.OutsideAllowedHoursOnly,
        r.AllowedStartUtcHour, r.AllowedEndUtcHour, r.SortOrder);
}
