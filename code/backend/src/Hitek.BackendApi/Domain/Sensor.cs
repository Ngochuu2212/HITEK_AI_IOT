namespace Hitek.BackendApi.Domain;

public enum SensorType
{
    Pir,
    Door,
    Beam
}

public enum SensorStatus
{
    Online,
    Offline
}

public class Sensor : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public SensorType Type { get; set; }
    public Guid AreaId { get; set; }
    public SensorStatus Status { get; set; } = SensorStatus.Offline;
    public DateTime? LastSeenAt { get; set; }

    public ICollection<SensorReading> Readings { get; set; } = new List<SensorReading>();
}
