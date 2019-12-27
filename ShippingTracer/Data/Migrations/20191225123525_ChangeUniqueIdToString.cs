using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ShippingTracer.Data.Migrations
{
    public partial class ChangeUniqueIdToString : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "UniqueId",
                table: "Shippings",
                nullable: true,
                oldClrType: typeof(Guid));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "UniqueId",
                table: "Shippings",
                nullable: false,
                oldClrType: typeof(string),
                oldNullable: true);
        }
    }
}
