using Hitek.BackendApi.Auth;
using Hitek.BackendApi.Data;
using Hitek.BackendApi.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hitek.BackendApi.Controllers;

// Danh sách khu vực hợp lệ của tenant hiện tại. AI Service đối chiếu areaId ở đây,
// Dashboard dùng để hiển thị tên khu vực thay cho GUID.
[ApiController]
[Route("api/v1/areas")]
[Authorize(Policy = AuthPolicies.AreaReader)]
public class AreasController(HitekDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LocationDto>>> GetAreas(CancellationToken ct)
    {
        var areas = await db.Locations.OrderBy(a => a.Name).ToListAsync(ct);
        return Ok(areas.Select(a => a.ToDto()).ToList());
    }
}
