using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace BerryExchange.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddListingEnrichment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.AddColumn<string>(
                name: "AiTastingNotes",
                table: "Listings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<Vector>(
                name: "Embedding",
                table: "Listings",
                type: "vector(384)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Listings_Embedding",
                table: "Listings",
                column: "Embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Listings_Embedding",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "AiTastingNotes",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "Embedding",
                table: "Listings");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }
    }
}
