using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParcelFlow.Shipment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentTimeoutToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PaymentTimeoutTokenId",
                table: "shipment_bookings",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentTimeoutTokenId",
                table: "shipment_bookings");
        }
    }
}
