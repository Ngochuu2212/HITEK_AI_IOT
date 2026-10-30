using Hitek.BackendApi.Data;
using Hitek.BackendApi.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hitek.BackendApi.Tests;

// Dựng schema SQLite dùng chung cho kiểm thử đơn vị (TestHarness) lẫn kiểm thử tích hợp
// qua HTTP (BackendApiFactory), để hai bên luôn chạy trên cùng một cấu trúc bảng.
public static class TestSchema
{
    public static readonly Guid TenantA = Guid.Parse("00000000-0000-4000-8000-000000000001");
    public static readonly Guid TenantB = Guid.Parse("00000000-0000-4000-8000-0000000000b2");
    public static readonly Guid AreaWarehouse = Guid.Parse("550e8400-e29b-41d4-a716-446655440101");
    public static readonly Guid AreaOtherTenant = Guid.Parse("550e8400-e29b-41d4-a716-4466554409b2");
    public static readonly Guid CameraWarehouse = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    public static readonly Guid CameraOtherTenant = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000b2");

    // Thứ tự dưới đây quan trọng, và từng bước đều có lý do.
    //
    // Dữ liệu seed cảm biến có khoá ngoại trỏ sang location, bảng event trỏ sang camera,
    // nên các bảng dùng chung phải tồn tại và có dữ liệu trước. Nhưng dựng chúng trước rồi
    // gọi EnsureCreated thì EnsureCreated thấy database đã có bảng và bỏ qua sạch, nên phải
    // sinh script schema rồi chạy tay.
    //
    // Dữ liệu của các bảng đó cũng phải chèn qua EF chứ không phải SQL thuần: EF ghi Guid
    // xuống SQLite theo định dạng riêng của nó, chèn tay là hai bên lệch nhau và khoá ngoại
    // không khớp.
    public static void Create(SqliteConnection connection, Func<HitekDbContext> newDbContext)
    {
        CreateSharedTables(connection);

        using var db = newDbContext();
        SeedSharedTables(db);
        db.Database.ExecuteSqlRaw(db.Database.GenerateCreateScript());
    }

    // Các bảng này do AI Service sở hữu nên bị loại khỏi migration của Backend, và script
    // schema cũng bỏ qua chúng. Dựng tay ở đây cho đúng vai trò: chúng tồn tại sẵn từ phía
    // kia, Backend chỉ đọc.
    private static void CreateSharedTables(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE tenant (
                id TEXT NOT NULL PRIMARY KEY, code TEXT, name TEXT, status TEXT);
            CREATE TABLE location (
                id TEXT NOT NULL PRIMARY KEY, tenant_id TEXT NOT NULL, code TEXT, name TEXT,
                FOREIGN KEY (tenant_id) REFERENCES tenant(id));
            CREATE TABLE app_user (
                id TEXT NOT NULL PRIMARY KEY, email TEXT, full_name TEXT, password_hash TEXT, status TEXT);
            CREATE TABLE tenant_member (
                id TEXT NOT NULL PRIMARY KEY, tenant_id TEXT NOT NULL, user_id TEXT NOT NULL,
                role TEXT, status TEXT);
            CREATE TABLE camera (
                id TEXT NOT NULL PRIMARY KEY, tenant_id TEXT NOT NULL, location_id TEXT NOT NULL,
                name TEXT, source_type TEXT, status TEXT, last_seen_at TEXT);
            CREATE TABLE ai_event (
                id TEXT NOT NULL PRIMARY KEY, tenant_id TEXT NOT NULL, camera_id TEXT NOT NULL, event_type TEXT NOT NULL);
            CREATE TABLE evidence (
                id TEXT NOT NULL PRIMARY KEY, tenant_id TEXT NOT NULL, alert_id TEXT, ai_event_id TEXT NOT NULL,
                camera_id TEXT NOT NULL, evidence_type TEXT, mime_type TEXT, file_size INTEGER,
                captured_at TEXT NOT NULL, duration_seconds REAL, status TEXT);
            """;
        command.ExecuteNonQuery();
    }

    private static void SeedSharedTables(HitekDbContext db)
    {
        db.Tenants.AddRange(
            new Tenant { Id = TenantA, Code = "demo-tenant", Name = "Demo Tenant", Status = "active" },
            new Tenant { Id = TenantB, Code = "other-tenant", Name = "Other Tenant", Status = "active" });
        db.Locations.AddRange(
            new Location { Id = AreaWarehouse, TenantId = TenantA, Code = "warehouse", Name = "Warehouse" },
            new Location { Id = AreaOtherTenant, TenantId = TenantB, Code = "warehouse", Name = "Warehouse" });
        db.Cameras.AddRange(
            new Camera { Id = CameraWarehouse, TenantId = TenantA, LocationId = AreaWarehouse, Name = "Cam-Kho", SourceType = "rtsp", Status = "active" },
            new Camera { Id = CameraOtherTenant, TenantId = TenantB, LocationId = AreaOtherTenant, Name = "Cam-B", SourceType = "rtsp", Status = "offline" });
        db.SaveChanges();
    }
}
