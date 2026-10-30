using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Hitek.BackendApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "risk_alert",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    peak_level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_count = table.Column<int>(type: "integer", nullable: false),
                    triggered_rules = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    handled_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    handled_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    handled_by_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    handled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_risk_alert", x => x.id);
                    table.ForeignKey(
                        name: "FK_risk_alert_location_area_id",
                        column: x => x.area_id,
                        principalTable: "location",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "risk_rule",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    min_sources = table.Column<int>(type: "integer", nullable: true),
                    required_sources = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    outside_allowed_hours_only = table.Column<bool>(type: "boolean", nullable: false),
                    allowed_start_utc_hour = table.Column<int>(type: "integer", nullable: true),
                    allowed_end_utc_hour = table.Column<int>(type: "integer", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_risk_rule", x => x.id);
                    table.ForeignKey(
                        name: "FK_risk_rule_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sensor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sensor", x => x.id);
                    table.ForeignKey(
                        name: "FK_sensor_location_area_id",
                        column: x => x.area_id,
                        principalTable: "location",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sensor_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_log", x => x.id);
                    table.ForeignKey(
                        name: "FK_system_log_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sensor_reading",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sensor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<bool>(type: "boolean", nullable: false),
                    recorded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sensor_reading", x => x.id);
                    table.ForeignKey(
                        name: "FK_sensor_reading_sensor_sensor_id",
                        column: x => x.sensor_id,
                        principalTable: "sensor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    sensor_reading_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ai_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sensor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    camera_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dedup_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event", x => x.id);
                    table.ForeignKey(
                        name: "FK_event_camera_camera_id",
                        column: x => x.camera_id,
                        principalTable: "camera",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_event_location_area_id",
                        column: x => x.area_id,
                        principalTable: "location",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_event_sensor_reading_sensor_reading_id",
                        column: x => x.sensor_reading_id,
                        principalTable: "sensor_reading",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_event_sensor_sensor_id",
                        column: x => x.sensor_id,
                        principalTable: "sensor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "alert_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    level_at_link = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    linked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_event", x => x.id);
                    table.ForeignKey(
                        name: "FK_alert_event_event_event_id",
                        column: x => x.event_id,
                        principalTable: "event",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_alert_event_risk_alert_alert_id",
                        column: x => x.alert_id,
                        principalTable: "risk_alert",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "sensor",
                columns: new[] { "id", "area_id", "last_seen_at", "name", "status", "tenant_id", "type" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111111"), new Guid("550e8400-e29b-41d4-a716-446655440101"), null, "PIR-Kho-01", "Offline", new Guid("00000000-0000-4000-8000-000000000001"), "Pir" },
                    { new Guid("22222222-2222-2222-2222-222222222222"), new Guid("550e8400-e29b-41d4-a716-446655440101"), null, "Door-Kho-01", "Offline", new Guid("00000000-0000-4000-8000-000000000001"), "Door" },
                    { new Guid("33333333-3333-3333-3333-333333333333"), new Guid("550e8400-e29b-41d4-a716-446655440101"), null, "Beam-Kho-01", "Offline", new Guid("00000000-0000-4000-8000-000000000001"), "Beam" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_alert_event_alert_id_event_id",
                table: "alert_event",
                columns: new[] { "alert_id", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_alert_event_event_id",
                table: "alert_event",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "IX_event_area_id",
                table: "event",
                column: "area_id");

            migrationBuilder.CreateIndex(
                name: "IX_event_camera_id",
                table: "event",
                column: "camera_id");

            migrationBuilder.CreateIndex(
                name: "IX_event_sensor_id",
                table: "event",
                column: "sensor_id");

            migrationBuilder.CreateIndex(
                name: "IX_event_sensor_reading_id",
                table: "event",
                column: "sensor_reading_id");

            migrationBuilder.CreateIndex(
                name: "IX_event_tenant_id_area_id_occurred_at",
                table: "event",
                columns: new[] { "tenant_id", "area_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_event_tenant_id_camera_id_occurred_at",
                table: "event",
                columns: new[] { "tenant_id", "camera_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_event_tenant_id_dedup_key",
                table: "event",
                columns: new[] { "tenant_id", "dedup_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_event_tenant_id_sensor_id_occurred_at",
                table: "event",
                columns: new[] { "tenant_id", "sensor_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_risk_alert_area_id",
                table: "risk_alert",
                column: "area_id");

            migrationBuilder.CreateIndex(
                name: "IX_risk_alert_tenant_id_area_id_ended_at",
                table: "risk_alert",
                columns: new[] { "tenant_id", "area_id", "ended_at" });

            migrationBuilder.CreateIndex(
                name: "IX_risk_alert_tenant_id_started_at",
                table: "risk_alert",
                columns: new[] { "tenant_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_risk_rule_tenant_id_code",
                table: "risk_rule",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sensor_area_id",
                table: "sensor",
                column: "area_id");

            migrationBuilder.CreateIndex(
                name: "IX_sensor_tenant_id_area_id",
                table: "sensor",
                columns: new[] { "tenant_id", "area_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sensor_reading_sensor_id_recorded_at",
                table: "sensor_reading",
                columns: new[] { "sensor_id", "recorded_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_log_tenant_id_category_occurred_at",
                table: "system_log",
                columns: new[] { "tenant_id", "category", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_system_log_tenant_id_occurred_at",
                table: "system_log",
                columns: new[] { "tenant_id", "occurred_at" });

            // ai_event là bảng của AI Service, không phải thực thể EF Core, nên EF không tự sinh
            // khoá ngoại này được. Gắn bằng SQL để aiEventId sai bị chặn ngay lúc ghi.
            migrationBuilder.Sql("""
                ALTER TABLE "event"
                ADD CONSTRAINT fk_event_ai_event_ai_event_id
                FOREIGN KEY (ai_event_id) REFERENCES ai_event(id) ON DELETE SET NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "event" DROP CONSTRAINT IF EXISTS fk_event_ai_event_ai_event_id;
                """);

            migrationBuilder.DropTable(
                name: "alert_event");

            migrationBuilder.DropTable(
                name: "risk_rule");

            migrationBuilder.DropTable(
                name: "system_log");

            migrationBuilder.DropTable(
                name: "event");

            migrationBuilder.DropTable(
                name: "risk_alert");

            migrationBuilder.DropTable(
                name: "sensor_reading");

            migrationBuilder.DropTable(
                name: "sensor");
        }
    }
}
