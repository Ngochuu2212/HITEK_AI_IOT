using Hitek.BackendApi.Auth;
using Hitek.BackendApi.Common;
using Hitek.BackendApi.Data;
using Hitek.BackendApi.Domain;
using Hitek.BackendApi.Dtos;
using Hitek.BackendApi.RiskEngine;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hitek.BackendApi.Controllers;

// Bảng luật kết hợp nguồn của Risk Engine. Sửa ở đây có hiệu lực ngay từ lần đánh giá kế
// tiếp, không cần khởi động lại. Ai cũng xem được để hiểu vì sao cảnh báo ở mức đó, chỉ
// quản trị viên tenant được sửa.
[ApiController]
[Route("api/v1/risk-rules")]
[Authorize(Policy = AuthPolicies.Viewer)]
public class RiskRulesController(
    HitekDbContext db,
    IOptions<RiskEngineOptions> riskOptions,
    TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RiskRuleDto>>> GetRules(CancellationToken ct)
    {
        var rules = await RiskRuleStore.LoadAsync(db, riskOptions.Value, ct);
        return Ok(rules.OrderBy(r => r.SortOrder).ThenBy(r => r.Code, StringComparer.Ordinal).Select(r => r.ToDto()).ToList());
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.Admin)]
    public async Task<ActionResult<RiskRuleDto>> CreateRule(RiskRuleRequest request, CancellationToken ct)
    {
        var code = request.Code?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code))
            throw ApiException.BadRequest("VALIDATION_ERROR", "code: Bắt buộc khi tạo luật mới.");
        Validate(request);

        // Bảo đảm tenant đã có bộ luật mặc định trước khi thêm luật riêng, nếu không lần đọc
        // sau thấy đã có luật sẽ không tạo bộ mặc định nữa và tenant mất luôn R1-R3.
        await RiskRuleStore.LoadAsync(db, riskOptions.Value, ct);
        if (await db.RiskRules.AnyAsync(r => r.Code == code, ct))
            throw ApiException.Conflict("RISK_RULE_CODE_EXISTS", $"Đã có luật mang mã {code}.");

        var rule = new RiskRule { Id = Guid.NewGuid(), Code = code };
        Apply(rule, request);
        db.RiskRules.Add(rule);
        Log($"{User.DisplayName()} thêm luật {rule.Code} \"{rule.Name}\" (mức {Display(rule.Level)}).", rule.Id);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            throw ApiException.Conflict("RISK_RULE_CODE_EXISTS", $"Đã có luật mang mã {code}.");
        }

        return CreatedAtAction(nameof(GetRules), null, rule.ToDto());
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthPolicies.Admin)]
    public async Task<ActionResult<RiskRuleDto>> UpdateRule(Guid id, RiskRuleRequest request, CancellationToken ct)
    {
        var rule = await FindRuleAsync(id, ct);
        Validate(request);

        Apply(rule, request);
        Log($"{User.DisplayName()} sửa luật {rule.Code}: {(rule.Enabled ? "bật" : "tắt")}, mức {Display(rule.Level)}.", rule.Id);
        await db.SaveChangesAsync(ct);

        return Ok(rule.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.Admin)]
    public async Task<IActionResult> DeleteRule(Guid id, CancellationToken ct)
    {
        var rule = await FindRuleAsync(id, ct);

        // Xoá luật cuối cùng thì lần đánh giá sau sẽ tự tạo lại bộ mặc định — người dùng muốn
        // "không có luật nào" thường thực ra là muốn tắt; chặn lại cho khỏi bất ngờ.
        if (await db.RiskRules.CountAsync(ct) == 1)
            throw ApiException.Conflict("LAST_RISK_RULE", "Không xoá được luật cuối cùng. Tắt luật (enabled = false) thay vì xoá.");

        db.RiskRules.Remove(rule);
        Log($"{User.DisplayName()} xoá luật {rule.Code} \"{rule.Name}\".", rule.Id);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    private static void Validate(RiskRuleRequest request)
    {
        var required = request.RequiredSources ?? [];
        if (request.MinSources is null && required.Count == 0)
            throw ApiException.BadRequest("VALIDATION_ERROR",
                "Luật phải có ít nhất một điều kiện: minSources hoặc requiredSources.");

        if (request.OutsideAllowedHoursOnly)
        {
            if (request.AllowedStartUtcHour is not { } start || request.AllowedEndUtcHour is not { } end)
                throw ApiException.BadRequest("VALIDATION_ERROR",
                    "Luật ngoài khung giờ phải có allowedStartUtcHour và allowedEndUtcHour.");
            if (start >= end)
                throw ApiException.BadRequest("VALIDATION_ERROR",
                    "allowedStartUtcHour phải nhỏ hơn allowedEndUtcHour (giờ UTC, khung [bắt đầu, kết thúc)).");
        }
    }

    private static void Apply(RiskRule rule, RiskRuleRequest request)
    {
        rule.Name = request.Name.Trim();
        rule.Description = request.Description?.Trim();
        rule.Enabled = request.Enabled;
        rule.Level = request.Level;
        rule.MinSources = request.MinSources;
        rule.RequiredSources = RiskRuleEvaluator.FormatSources(request.RequiredSources);
        rule.OutsideAllowedHoursOnly = request.OutsideAllowedHoursOnly;
        rule.AllowedStartUtcHour = request.OutsideAllowedHoursOnly ? request.AllowedStartUtcHour : null;
        rule.AllowedEndUtcHour = request.OutsideAllowedHoursOnly ? request.AllowedEndUtcHour : null;
        rule.SortOrder = request.SortOrder;
    }

    private async Task<RiskRule> FindRuleAsync(Guid id, CancellationToken ct) =>
        await db.RiskRules.FirstOrDefaultAsync(r => r.Id == id, ct)
        ?? throw ApiException.NotFound("RISK_RULE_NOT_FOUND", "Không tìm thấy luật với id đã cho.");

    private void Log(string message, Guid ruleId) =>
        SystemLogWriter.Add(db, clock.GetUtcNow().UtcDateTime, SystemLogLevel.Info, SystemLogCategory.RiskRule,
            message, ruleId, User.Actor());

    private static string Display(AlertLevel level) => level.ToString().ToUpperInvariant();
}
