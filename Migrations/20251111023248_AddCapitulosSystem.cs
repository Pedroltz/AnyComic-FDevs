using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnyComic.Migrations
{
    /// <inheritdoc />
    public partial class AddCapitulosSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaginasMangas_Mangas_MangaId",
                table: "PaginasMangas");

            // Step 1: Create Capitulos table
            migrationBuilder.CreateTable(
                name: "Capitulos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MangaId = table.Column<int>(type: "int", nullable: false),
                    NumeroCapitulo = table.Column<int>(type: "int", nullable: false),
                    NomeCapitulo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DataCriacao = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Capitulos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Capitulos_Mangas_MangaId",
                        column: x => x.MangaId,
                        principalTable: "Mangas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Capitulos_MangaId",
                table: "Capitulos",
                column: "MangaId");

            // Step 2: Create a default chapter (Chapter 1) for each existing manga
            migrationBuilder.Sql(@"
                INSERT INTO Capitulos (MangaId, NumeroCapitulo, DataCriacao)
                SELECT DISTINCT Id, 1, GETDATE()
                FROM Mangas
            ");

            // Step 3: Add CapituloId column (nullable first to allow data migration)
            migrationBuilder.AddColumn<int>(
                name: "CapituloId",
                table: "PaginasMangas",
                type: "int",
                nullable: true);

            // Step 4: Update existing pages to point to the default chapter
            migrationBuilder.Sql(@"
                UPDATE p
                SET p.CapituloId = c.Id
                FROM PaginasMangas p
                INNER JOIN Capitulos c ON p.MangaId = c.MangaId
                WHERE c.NumeroCapitulo = 1
            ");

            // Step 5: Make CapituloId non-nullable now that all pages have a chapter
            migrationBuilder.AlterColumn<int>(
                name: "CapituloId",
                table: "PaginasMangas",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            // Step 6: Create index and foreign keys
            migrationBuilder.CreateIndex(
                name: "IX_PaginasMangas_CapituloId",
                table: "PaginasMangas",
                column: "CapituloId");

            migrationBuilder.AddForeignKey(
                name: "FK_PaginasMangas_Capitulos_CapituloId",
                table: "PaginasMangas",
                column: "CapituloId",
                principalTable: "Capitulos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PaginasMangas_Mangas_MangaId",
                table: "PaginasMangas",
                column: "MangaId",
                principalTable: "Mangas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaginasMangas_Capitulos_CapituloId",
                table: "PaginasMangas");

            migrationBuilder.DropForeignKey(
                name: "FK_PaginasMangas_Mangas_MangaId",
                table: "PaginasMangas");

            migrationBuilder.DropTable(
                name: "Capitulos");

            migrationBuilder.DropIndex(
                name: "IX_PaginasMangas_CapituloId",
                table: "PaginasMangas");

            migrationBuilder.DropColumn(
                name: "CapituloId",
                table: "PaginasMangas");

            migrationBuilder.AddForeignKey(
                name: "FK_PaginasMangas_Mangas_MangaId",
                table: "PaginasMangas",
                column: "MangaId",
                principalTable: "Mangas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
