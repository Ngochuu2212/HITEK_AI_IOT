using System.ComponentModel.DataAnnotations;
using Hitek.BackendApi.Domain;

namespace Hitek.BackendApi.Dtos;

public record SensorDto(Guid Id, string Name, SensorType Type, Guid AreaId, SensorStatus Status, DateTime? LastSeenAt);

public record CreateSensorRequest([Required, MaxLength(255)] string Name, SensorType Type, Guid AreaId);

// Loại cảm biến là đặc tính vật lý của thiết bị nên không đổi được; đổi loại thì xoá rồi
// thêm cảm biến mới, để các lượt đọc cũ không bị hiểu sai nghĩa.
public record UpdateSensorRequest([Required, MaxLength(255)] string Name, Guid AreaId);

// Một lượt đọc thô (đề án mục 2.4: mã thiết bị, loại cảm biến, giá trị, vị trí, thời gian).
// RecordedAt là lúc thiết bị đo, ReceivedAt là lúc hệ thống nhận — chênh lệch là độ trễ.
public record SensorReadingDto(
    Guid Id,
    Guid SensorId,
    SensorType SensorType,
    Guid AreaId,
    bool Value,
    DateTime RecordedAt,
    DateTime? ReceivedAt);

public static class SensorMapping
{
    public static SensorDto ToDto(this Sensor s) => new(s.Id, s.Name, s.Type, s.AreaId, s.Status, s.LastSeenAt);
}
