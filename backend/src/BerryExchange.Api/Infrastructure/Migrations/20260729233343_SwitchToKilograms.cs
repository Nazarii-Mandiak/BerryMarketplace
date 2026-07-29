using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BerryExchange.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SwitchToKilograms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-written: the EF-scaffolded version of this migration diffed the rename
            // and the type change as unrelated drops/adds (and even cross-matched
            // PricePerPint onto QuantityAvailableKg), which would have discarded every
            // existing price and quantity. RenameColumn + AlterColumn preserves the actual
            // values - they are simply reinterpreted as kilograms from here on, per
            // ADR-0018 (no unit conversion is applied).
            migrationBuilder.RenameColumn(
                name: "PricePerPint",
                table: "Listings",
                newName: "PricePerKg");

            migrationBuilder.RenameColumn(
                name: "QuantityAvailable",
                table: "Listings",
                newName: "QuantityAvailableKg");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityAvailableKg",
                table: "Listings",
                type: "numeric(10,2)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.RenameColumn(
                name: "Quantity",
                table: "Reservations",
                newName: "QuantityKg");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityKg",
                table: "Reservations",
                type: "numeric(10,2)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Retyping numeric(10,2) back to integer truncates any fractional quantity -
            // inherent to reversing a widening type change, not avoidable here.
            migrationBuilder.AlterColumn<int>(
                name: "QuantityKg",
                table: "Reservations",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)");

            migrationBuilder.RenameColumn(
                name: "QuantityKg",
                table: "Reservations",
                newName: "Quantity");

            migrationBuilder.AlterColumn<int>(
                name: "QuantityAvailableKg",
                table: "Listings",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)");

            migrationBuilder.RenameColumn(
                name: "QuantityAvailableKg",
                table: "Listings",
                newName: "QuantityAvailable");

            migrationBuilder.RenameColumn(
                name: "PricePerKg",
                table: "Listings",
                newName: "PricePerPint");
        }
    }
}
