using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BerryExchange.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddListingPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhotoContentType",
                table: "Listings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ListingPhotos",
                columns: table => new
                {
                    ListingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Bytes = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListingPhotos", x => x.ListingId);
                    table.ForeignKey(
                        name: "FK_ListingPhotos_Listings_ListingId",
                        column: x => x.ListingId,
                        principalTable: "Listings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ListingPhotos");

            migrationBuilder.DropColumn(
                name: "PhotoContentType",
                table: "Listings");
        }
    }
}
