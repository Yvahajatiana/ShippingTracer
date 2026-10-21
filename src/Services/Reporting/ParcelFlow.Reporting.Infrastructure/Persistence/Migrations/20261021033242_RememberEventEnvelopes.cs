using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParcelFlow.Reporting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RememberEventEnvelopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "event_envelopes",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_envelopes", x => x.EventId);
                    table.ForeignKey(
                        name: "FK_event_envelopes_status_facts_SourceEventId",
                        column: x => x.SourceEventId,
                        principalTable: "status_facts",
                        principalColumn: "SourceEventId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_event_envelopes_SourceEventId",
                table: "event_envelopes",
                column: "SourceEventId");

            migrationBuilder.Sql("""
                INSERT INTO event_envelopes ("EventId", "SourceEventId")
                SELECT "EventId", "SourceEventId" FROM status_facts;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_envelopes");
        }
    }
}
