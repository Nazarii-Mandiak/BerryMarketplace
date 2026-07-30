using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BerryExchange.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddListingSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "Listings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Listings_DeletedAt",
                table: "Listings",
                column: "DeletedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Listings_DeletedAt",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Listings");
        }
    }
}
