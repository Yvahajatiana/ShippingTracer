using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParcelFlow.Delivery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProtectDeliveryProofs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The recipient name becomes ciphertext. Existing names are kept as UTF-8 bytes (no
            // encryption marker): the service still reads them and its maintenance job encrypts them.
            migrationBuilder.Sql("""
                ALTER TABLE delivery_attempts
                ALTER COLUMN "RecipientName" TYPE bytea USING convert_to("RecipientName", 'UTF8');
                """);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProofsPurgedAtUtc",
                table: "delivery_attempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_delivery_attempts_proof_retention",
                table: "delivery_attempts",
                column: "OccurredAtUtc",
                filter: "\"ProofsPurgedAtUtc\" IS NULL AND \"Outcome\" = 'Delivered'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_delivery_attempts_proof_retention",
                table: "delivery_attempts");

            migrationBuilder.DropColumn(
                name: "ProofsPurgedAtUtc",
                table: "delivery_attempts");

            // Only plaintext names can go back to text: refuse while encrypted names exist.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM delivery_attempts
                               WHERE substring("RecipientName" FROM 1 FOR 5) = '\x0050464501'::bytea) THEN
                        RAISE EXCEPTION 'delivery_attempts holds encrypted recipient names: this migration cannot be reverted';
                    END IF;
                END $$;
                ALTER TABLE delivery_attempts
                ALTER COLUMN "RecipientName" TYPE character varying(120) USING convert_from("RecipientName", 'UTF8');
                """);
        }
    }
}
