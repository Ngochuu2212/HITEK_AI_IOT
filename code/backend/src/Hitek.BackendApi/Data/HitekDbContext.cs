using Hitek.BackendApi.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Hitek.BackendApi.Data;

public class HitekDbContext(DbContextOptions<HitekDbContext> options, ITenantProvider tenantProvider)
    : DbContext(options)
{
    // Phải là property của context thì EF Core mới tham số hoá được giá trị này
    // trong global query filter, thay vì nướng cứng vào model đã cache.
    public Guid TenantId { get; } = tenantProvider.TenantId;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<TenantMember> TenantMembers => Set<TenantMember>();
    public DbSet<Camera> Cameras => Set<Camera>();
    public DbSet<AiEvent> AiEvents => Set<AiEvent>();
    public DbSet<Evidence> Evidence => Set<Evidence>();
    public DbSet<Sensor> Sensors => Set<Sensor>();
    public DbSet<SensorReading> SensorReadings => Set<SensorReading>();
    public DbSet<EventRecord> Events => Set<EventRecord>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<AlertEvent> AlertEvents => Set<AlertEvent>();
    public DbSet<RiskRule> RiskRules => Set<RiskRule>();
    public DbSet<SystemLog> SystemLogs => Set<SystemLog>();

    // Tự đóng dấu TenantId lúc insert. Nhờ vậy controller và Risk Engine không chỗ nào
    // phải tự nhớ gán tenant — quên một chỗ là dữ liệu lẫn sang khách hàng khác.
    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var entry in ChangeTracker.Entries<ITenantScoped>())
        {
            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                entry.Entity.TenantId = TenantId;
        }

        return base.SaveChangesAsync(ct);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ===================================================================
        // Bảng do AI Service sở hữu — Backend chỉ đọc, không tạo, không seed.
        // ExcludeFromMigrations để EF Core không sinh lệnh tạo/sửa các bảng này;
        // cấu trúc của chúng nằm ở code/database/postgres/init bên repo AI Service.
        // ===================================================================

        modelBuilder.Entity<Tenant>(e =>
        {
            e.ToTable("tenant", t => t.ExcludeFromMigrations());
            e.Property(x => x.Code).HasMaxLength(100);
            e.Property(x => x.Name).HasMaxLength(255);
        });

        // Hai bảng phục vụ đăng nhập. Không gắn query filter: lúc đăng nhập chưa có
        // tenant hiện tại, phải tra chéo mọi tenant mà người dùng đó là thành viên.
        modelBuilder.Entity<AppUser>(e => e.ToTable("app_user", t => t.ExcludeFromMigrations()));
        modelBuilder.Entity<TenantMember>(e => e.ToTable("tenant_member", t => t.ExcludeFromMigrations()));

        modelBuilder.Entity<Camera>(e =>
        {
            e.ToTable("camera", t => t.ExcludeFromMigrations());
            e.HasQueryFilter(x => x.TenantId == TenantId);
            // Cột status là kiểu enum riêng của PostgreSQL (camera_status). EF gửi chuỗi dưới
            // dạng text nên PostgreSQL từ chối; vì vậy EF không bao giờ ghi cột này: thêm mới
            // thì database tự đặt 'active', đổi trạng thái đi qua CameraStatusWriter.
            e.Property(x => x.Status).Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
            e.Property(x => x.Status).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        });

        // Khoá ngoại event.ai_event_id → ai_event đã gắn bằng SQL thuần trong migration đầu
        // tiên, nên ở đây cố ý không khai báo quan hệ, tránh EF sinh thêm một khoá trùng.
        modelBuilder.Entity<AiEvent>(e =>
        {
            e.ToTable("ai_event", t => t.ExcludeFromMigrations());
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        modelBuilder.Entity<Evidence>(e =>
        {
            e.ToTable("evidence", t => t.ExcludeFromMigrations());
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        modelBuilder.Entity<Location>(e =>
        {
            e.ToTable("location", t => t.ExcludeFromMigrations());
            e.Property(x => x.Code).HasMaxLength(100);
            e.Property(x => x.Name).HasMaxLength(255);

            // Khai báo cho đúng quan hệ có thật bên schema của AI Service. Không sinh
            // ra lệnh nào vì cả hai bảng đều bị loại khỏi migration, nhưng EF cần biết
            // quan hệ này để xếp đúng thứ tự khi có thao tác ghi.
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);

            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        // ===================================================================
        // Bảng do Backend sở hữu — tạo và đổi cấu trúc bằng EF Core migration.
        // ===================================================================

        modelBuilder.Entity<Sensor>(e =>
        {
            e.ToTable("sensor");
            e.Property(x => x.Name).HasMaxLength(255);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.AreaId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.AreaId });
            e.HasQueryFilter(x => x.TenantId == TenantId);

            // Khu vực "warehouse" do AI Service seed sẵn, nên migration này phải chạy
            // sau file SQL init bên đó, nếu không khoá ngoại sẽ không tìm thấy dòng.
            e.HasData(
                new Sensor { Id = SeedIds.SensorPir, TenantId = SeedIds.Tenant, Name = "PIR-Kho-01", Type = SensorType.Pir, AreaId = SeedIds.AreaWarehouse, Status = SensorStatus.Offline },
                new Sensor { Id = SeedIds.SensorDoor, TenantId = SeedIds.Tenant, Name = "Door-Kho-01", Type = SensorType.Door, AreaId = SeedIds.AreaWarehouse, Status = SensorStatus.Offline },
                new Sensor { Id = SeedIds.SensorBeam, TenantId = SeedIds.Tenant, Name = "Beam-Kho-01", Type = SensorType.Beam, AreaId = SeedIds.AreaWarehouse, Status = SensorStatus.Offline }
            );
        });

        modelBuilder.Entity<SensorReading>(e =>
        {
            e.ToTable("sensor_reading");
            e.HasOne(x => x.Sensor)
                .WithMany(s => s.Readings)
                .HasForeignKey(x => x.SensorId)
                .OnDelete(DeleteBehavior.Cascade);

            // Một cảm biến không thể có hai giá trị khác nhau tại cùng một mốc thời gian,
            // nên cặp này vừa là chỉ mục truy vấn vừa là khoá chống trùng cho MQTT QoS 1.
            e.HasIndex(x => new { x.SensorId, x.RecordedAt }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        modelBuilder.Entity<EventRecord>(e =>
        {
            e.ToTable("event");
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.EventType).HasMaxLength(100);
            e.Property(x => x.DedupKey).HasMaxLength(200).IsRequired();

            // Khoá ngoại tới location biến lỗi "gửi sai areaId" thành lỗi thấy ngay
            // lúc ghi, thay vì âm thầm không gộp được nguồn mà chẳng báo gì.
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.AreaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SensorReading).WithMany().HasForeignKey(x => x.SensorReadingId).OnDelete(DeleteBehavior.SetNull);

            // Cột thiết bị là bản sao để lọc, nhưng vẫn gắn khoá ngoại: cameraId gửi sai từ
            // AI Service phải bị từ chối ngay, không được lọt vào làm lệch kết quả tra cứu.
            e.HasOne<Sensor>().WithMany().HasForeignKey(x => x.SensorId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Camera>().WithMany().HasForeignKey(x => x.CameraId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.TenantId, x.SensorId, x.OccurredAt });
            e.HasIndex(x => new { x.TenantId, x.CameraId, x.OccurredAt });

            // Chốt chặn chống trùng nằm ở đây, không phải ở tầng controller: kiểm tra
            // trong code vẫn hở khi hai request trùng nhau chạy song song.
            e.HasIndex(x => new { x.TenantId, x.DedupKey }).IsUnique();

            e.HasIndex(x => new { x.TenantId, x.AreaId, x.OccurredAt });
            e.HasQueryFilter(x => x.TenantId == TenantId);

            // AiEventId trỏ sang bảng ai_event của AI Service. Khoá ngoại được gắn bằng
            // SQL thuần trong migration, vì ai_event không phải thực thể EF Core nên
            // EF không tự sinh ràng buộc này được.
        });

        modelBuilder.Entity<Alert>(e =>
        {
            // Không đặt tên "alert": script init của AI Service vẫn giữ bảng alert làm bảng ánh xạ
            // (ai_event.alert_id, evidence.alert_id trỏ vào đó), hai bảng không thể trùng tên.
            e.ToTable("risk_alert");
            e.Property(x => x.Level).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PeakLevel).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.TriggeredRules).HasMaxLength(200);
            e.Property(x => x.HandledStatus).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(2000);
            e.Property(x => x.HandledByName).HasMaxLength(255);
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.AreaId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.AreaId, x.EndedAt });
            e.HasIndex(x => new { x.TenantId, x.StartedAt });
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        modelBuilder.Entity<AlertEvent>(e =>
        {
            e.ToTable("alert_event");
            e.Property(x => x.LevelAtLink).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.Alert).WithMany(a => a.Links).HasForeignKey(x => x.AlertId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.AlertId, x.EventId }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        modelBuilder.Entity<RiskRule>(e =>
        {
            e.ToTable("risk_rule");
            e.Property(x => x.Code).HasMaxLength(50).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.Level).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RequiredSources).HasMaxLength(100);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        modelBuilder.Entity<SystemLog>(e =>
        {
            e.ToTable("system_log");
            e.Property(x => x.Level).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Message).HasMaxLength(1000).IsRequired();
            e.Property(x => x.Actor).HasMaxLength(255);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.OccurredAt });
            e.HasIndex(x => new { x.TenantId, x.Category, x.OccurredAt });
            e.HasQueryFilter(x => x.TenantId == TenantId);
        });

        ApplySnakeCaseColumnNames(modelBuilder);
    }

    // AI Service đặt tên bảng và cột theo chữ thường có gạch dưới, còn EF Core mặc
    // định giữ nguyên chữ hoa rồi bọc ngoặc kép. Hai kiểu này không nhìn thấy nhau:
    // trong PostgreSQL, alert và "Alert" là hai bảng khác nhau. Đổi hết tên cột sang
    // một quy ước duy nhất ở đây thay vì gõ tay từng cột, vừa gọn vừa khỏi sót.
    private static void ApplySnakeCaseColumnNames(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                var current = property.GetColumnName();
                property.SetColumnName(ToSnakeCase(current));
            }
        }
    }

    private static string ToSnakeCase(string name)
    {
        var buffer = new System.Text.StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && name[i - 1] != '_') buffer.Append('_');
                buffer.Append(char.ToLowerInvariant(c));
            }
            else
            {
                buffer.Append(c);
            }
        }

        return buffer.ToString();
    }
}
