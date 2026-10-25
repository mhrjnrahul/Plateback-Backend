using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlateBack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LiveListingsCoveringIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_listings_latitude_longitude",
                table: "listings");

            migrationBuilder.CreateIndex(
                name: "ix_listings_live",
                table: "listings",
                column: "pickup_end",
                filter: "status IN ('Active', 'SoldOut')")
                .Annotation("Npgsql:IndexInclude", new[] { "status", "latitude", "longitude", "organization_id", "id", "price", "category", "title" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_listings_live",
                table: "listings");

            migrationBuilder.CreateIndex(
                name: "ix_listings_latitude_longitude",
                table: "listings",
                columns: new[] { "latitude", "longitude" },
                filter: "status = 'Active'");
        }
    }
}
