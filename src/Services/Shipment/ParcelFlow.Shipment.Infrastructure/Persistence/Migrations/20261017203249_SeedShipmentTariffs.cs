using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ParcelFlow.Shipment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedShipmentTariffs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO tariffs ("Zone", "BasePrice", "PricePerKg")
                VALUES ('Domestic', 4.90, 1.20), ('Europe', 9.90, 2.50), ('International', 19.90, 5.00)
                ON CONFLICT ("Zone") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reference rows may predate this migration or contain administrator updates.
            // Keep them on rollback; rolling back InitialShipment separately drops the table.
        }
    }
}
