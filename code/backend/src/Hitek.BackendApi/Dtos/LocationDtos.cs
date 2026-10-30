using Hitek.BackendApi.Domain;

namespace Hitek.BackendApi.Dtos;

// AI Service lấy `id` (GUID) để gắn vào sự kiện; `code` chỉ để người đọc nhận ra khu vực.
public record LocationDto(Guid Id, string Code, string Name);

public static class LocationMapping
{
    public static LocationDto ToDto(this Location l) => new(l.Id, l.Code, l.Name);
}
