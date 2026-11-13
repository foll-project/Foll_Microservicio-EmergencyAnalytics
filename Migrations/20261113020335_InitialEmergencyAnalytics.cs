using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace foll_emergency_analytics.Migrations
{
    /// <inheritdoc />
    public partial class InitialEmergencyAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "emergency");

            migrationBuilder.CreateTable(
                name: "fall_types",
                schema: "emergency",
                columns: table => new
                {
                    fall_type_id = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    severity_level = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fall_types", x => x.fall_type_id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "emergency",
                columns: table => new
                {
                    emergency_outbox_message_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_on = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    processed_on = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_emergency_outbox_messages", x => x.emergency_outbox_message_id);
                });

            migrationBuilder.CreateTable(
                name: "emergency_incidents",
                schema: "emergency",
                columns: table => new
                {
                    emergency_incident_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    incident_key = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<long>(type: "bigint", nullable: false),
                    patient_id = table.Column<long>(type: "bigint", nullable: false),
                    fall_type_id = table.Column<short>(type: "smallint", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    opened_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    last_signal_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    cancelled_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    closed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    closed_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    ai_confidence_score = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    final_observation = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    last_source_payload = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_emergency_incidents", x => x.emergency_incident_id);
                    table.ForeignKey(
                        name: "fk_emergency_incidents_fall_types_fall_type_id",
                        column: x => x.fall_type_id,
                        principalSchema: "emergency",
                        principalTable: "fall_types",
                        principalColumn: "fall_type_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "emergency",
                table: "fall_types",
                columns: new[] { "fall_type_id", "description", "name", "severity_level" },
                values: new object[,]
                {
                    { (short)1, "Caída hacia adelante detectada por patrón vectorial frontal del dataset SISFALL.", "FRONTAL", (short)1 },
                    { (short)2, "Caída lateral detectada por desplazamiento dominante en eje lateral del dataset SISFALL.", "LATERAL", (short)2 },
                    { (short)3, "Tipo de caída no clasificado o no enviado por el dispositivo/IA.", "UNKNOWN", (short)2 },
                    { (short)4, "Caída hacia atrás detectada por patrón vectorial posterior del dataset SISFALL.", "BACKWARD", (short)1 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_emergency_incidents_device_id",
                schema: "emergency",
                table: "emergency_incidents",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "ix_emergency_incidents_device_id1",
                schema: "emergency",
                table: "emergency_incidents",
                column: "device_id",
                unique: true,
                filter: "\"status\" = 'Open'");

            migrationBuilder.CreateIndex(
                name: "ix_emergency_incidents_fall_type_id",
                schema: "emergency",
                table: "emergency_incidents",
                column: "fall_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_emergency_incidents_incident_key",
                schema: "emergency",
                table: "emergency_incidents",
                column: "incident_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_emergency_incidents_patient_id",
                schema: "emergency",
                table: "emergency_incidents",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_emergency_incidents_status",
                schema: "emergency",
                table: "emergency_incidents",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_fall_types_name",
                schema: "emergency",
                table: "fall_types",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_emergency_outbox_messages_processed_on",
                schema: "emergency",
                table: "outbox_messages",
                column: "processed_on");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "emergency_incidents",
                schema: "emergency");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "emergency");

            migrationBuilder.DropTable(
                name: "fall_types",
                schema: "emergency");
        }
    }
}
