using Hitek.BackendApi.Auth;
using Hitek.BackendApi.Common;
using Hitek.BackendApi.Data;
using Hitek.BackendApi.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hitek.BackendApi.Controllers;

// Danh sách thiết bị cho màn hình quản lý thiết bị (đề án mục 4.4): camera, cảm biến,
// vị trí, trạng thái online/offline và thời gian nhận dữ liệu cuối cùng.
//
// Camera do AI Service quản lý, cảm biến do Backend quản lý, nhưng người vận hành chỉ
// cần một bảng. Gộp ở đây để Dashboard khỏi phải gọi hai service rồi tự ghép.
[ApiController]
[Route("api/v1/devices")]
[Authorize(Policy = AuthPolicies.Viewer)]
public class DevicesController(HitekDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<DeviceDto>>> GetDevices(
        [FromQuery] DeviceKind? kind,
        [FromQuery] Guid? areaId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var (p, ps) = Paging.Normalize(page, pageSize);
        var devices = new List<DeviceDto>();

        if (kind is null or DeviceKind.Sensor)
        {
            var sensors = db.Sensors.AsNoTracking();
            if (areaId is not null) sensors = sensors.Where(s => s.AreaId == areaId);
            devices.AddRange((await sensors.ToListAsync(ct)).Select(s => new DeviceDto(
                s.Id, DeviceKind.Sensor, s.Name, s.Type.ToString().ToUpperInvariant(), s.AreaId,
                s.Status.ToString().ToUpperInvariant(), s.LastSeenAt)));
        }

        if (kind is null or DeviceKind.Camera)
        {
            var cameras = db.Cameras.AsNoTracking();
            if (areaId is not null) cameras = cameras.Where(c => c.LocationId == areaId);
            devices.AddRange((await cameras.ToListAsync(ct)).Select(c => new DeviceDto(
                c.Id, DeviceKind.Camera, c.Name, c.SourceType.ToUpperInvariant(), c.LocationId,
                c.Status.ToUpperInvariant(), c.LastSeenAt)));
        }

        // Ghép hai bảng nên phân trang trong bộ nhớ. Số thiết bị của một tenant chỉ ở mức
        // vài chục, khác hẳn bảng event lên tới hàng chục dòng mỗi giây.
        var ordered = devices.OrderBy(d => d.Kind).ThenBy(d => d.Name).ToList();
        return Ok(new PagedResult<DeviceDto>
        {
            Items = ordered.Skip((p - 1) * ps).Take(ps).ToList(),
            Page = p,
            PageSize = ps,
            Total = ordered.Count
        });
    }
}
