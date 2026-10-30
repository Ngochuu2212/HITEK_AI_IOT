namespace Hitek.BackendApi.Data;

// Id cố định cho dữ liệu seed.
//
// Tenant và Location giờ do AI Service seed qua file SQL init (002_demo_seed.sql),
// Backend chỉ đọc lại. Giá trị dưới đây phải khớp đúng file đó, không được tự đặt.
//
// Sensor thì Backend seed, id cố định vì Hitek.SensorSimulator chạy như một tiến
// trình riêng, không đọc được database nên phải biết trước sensorId để publish.
public static class SeedIds
{
    public static readonly Guid Tenant = Guid.Parse("00000000-0000-4000-8000-000000000001");

    // "warehouse" trong 002_demo_seed.sql — khu vực gắn 3 cảm biến demo.
    public static readonly Guid AreaWarehouse = Guid.Parse("550e8400-e29b-41d4-a716-446655440101");

    public static readonly Guid SensorPir = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid SensorDoor = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid SensorBeam = Guid.Parse("33333333-3333-3333-3333-333333333333");
}
