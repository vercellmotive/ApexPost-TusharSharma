using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexPost.Tracking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CreateTrackingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tracking");

            migrationBuilder.CreateTable(
                name: "Shipments",
                schema: "tracking",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TrackingNumber = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    OriginBranchCode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    RecipientName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RecipientPhone = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    RecipientAddress = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    WeightKg = table.Column<decimal>(type: "decimal(6,3)", precision: 6, scale: 3, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shipments", x => x.Id);
                    table.CheckConstraint("CK_Shipments_Status", "[Status] IN ('Created', 'PickedUp', 'InTransit', 'OutForDelivery', 'Delivered', 'Failed')");
                    table.CheckConstraint("CK_Shipments_TrackingNumber", "[TrackingNumber] LIKE 'APX-[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'");
                    table.CheckConstraint("CK_Shipments_Weight", "[WeightKg] BETWEEN 0.1 AND 70");
                });

            migrationBuilder.CreateTable(
                name: "Scans",
                schema: "tracking",
                columns: table => new
                {
                    ScanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShipmentId = table.Column<int>(type: "int", nullable: false),
                    ScanType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    CourierId = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ScannedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RecipientSignatureName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scans", x => x.ScanId);
                    table.CheckConstraint("CK_Scans_DeliveredSignature", "[ScanType] <> 'Delivered' OR [RecipientSignatureName] IS NOT NULL");
                    table.CheckConstraint("CK_Scans_ScanType", "[ScanType] IN ('Pickup', 'InTransit', 'Delivered', 'Failed')");
                    table.ForeignKey(
                        name: "FK_Scans_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalSchema: "tracking",
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Scans_Shipment_ScannedAt",
                schema: "tracking",
                table: "Scans",
                columns: new[] { "ShipmentId", "ScannedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_City_Created",
                schema: "tracking",
                table: "Shipments",
                columns: new[] { "City", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_Created",
                schema: "tracking",
                table: "Shipments",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_Status_Created",
                schema: "tracking",
                table: "Shipments",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_TrackingNumber",
                schema: "tracking",
                table: "Shipments",
                column: "TrackingNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Scans",
                schema: "tracking");

            migrationBuilder.DropTable(
                name: "Shipments",
                schema: "tracking");
        }
    }
}
