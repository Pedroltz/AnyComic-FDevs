using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnyComic.Migrations
{
    /// <inheritdoc />
    public partial class AddBannerTipo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MangaId",
                table: "Banners",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Tipo",
                table: "Banners",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Banners_MangaId",
                table: "Banners",
                column: "MangaId");

            migrationBuilder.AddForeignKey(
                name: "FK_Banners_Mangas_MangaId",
                table: "Banners",
                column: "MangaId",
                principalTable: "Mangas",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Banners_Mangas_MangaId",
                table: "Banners");

            migrationBuilder.DropIndex(
                name: "IX_Banners_MangaId",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "MangaId",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "Tipo",
                table: "Banners");
        }
    }
}
