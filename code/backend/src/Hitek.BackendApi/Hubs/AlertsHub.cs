using Hitek.BackendApi.Auth;
using Hitek.BackendApi.Data;
using Hitek.BackendApi.Domain;
using Hitek.BackendApi.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Hitek.BackendApi.Hubs;

// Dashboard kết nối vào đây để nhận cảnh báo theo thời gian thực. Sự kiện duy nhất là
// "AlertUpdated", phát khi cảnh báo được tạo, đổi mức, đổi trạng thái xử lý hoặc đóng.
[Authorize(Policy = AuthPolicies.Viewer)]
public class AlertsHub : Hub
{
    public static string TenantGroup(Guid tenantId) => $"tenant:{tenantId}";

    // Mỗi kết nối vào đúng nhóm tenant ghi trong token. Phát cho Clients.All như trước
    // thì Dashboard của khách hàng này nhận luôn cảnh báo của khách hàng khác.
    public override async Task OnConnectedAsync()
    {
        if (Guid.TryParse(Context.User?.FindFirst(HitekClaims.TenantId)?.Value, out var tenantId))
            await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(tenantId));

        await base.OnConnectedAsync();
    }
}

public static class AlertBroadcast
{
    public static async Task SendAsync(HitekDbContext db, IHubContext<AlertsHub> hub, Alert alert, CancellationToken ct)
    {
        var origins = await AlertOriginLoader.LoadAsync(db, [alert.Id], ct);
        var dto = alert.ToDto(origins.GetValueOrDefault(alert.Id, AlertOrigins.None));
        await hub.Clients.Group(AlertsHub.TenantGroup(alert.TenantId)).SendAsync("AlertUpdated", dto, ct);
    }
}

// Nguồn phát sinh và loại sự kiện của cảnh báo (đề án mục 4.3), suy ra từ bảng nối
// AlertEvent: mọi sự kiện từng góp vào cảnh báo trong suốt vòng đời của nó.
public static class AlertOriginLoader
{
    public static async Task<Dictionary<Guid, AlertOrigins>> LoadAsync(
        HitekDbContext db, IReadOnlyCollection<Guid> alertIds, CancellationToken ct)
    {
        if (alertIds.Count == 0) return new();

        var rows = await db.AlertEvents
            .Where(l => alertIds.Contains(l.AlertId))
            .Select(l => new { l.AlertId, l.Event!.Source, l.Event.EventType })
            .Distinct()
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.AlertId)
            .ToDictionary(
                g => g.Key,
                g => new AlertOrigins(
                    g.Select(r => r.Source).Distinct().OrderBy(s => s).ToList(),
                    g.Select(r => r.EventType).OfType<string>().Distinct().OrderBy(t => t, StringComparer.Ordinal).ToList()));
    }
}
