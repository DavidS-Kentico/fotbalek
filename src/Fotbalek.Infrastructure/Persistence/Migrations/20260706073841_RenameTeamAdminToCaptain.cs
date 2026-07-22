using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fotbalek.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameTeamAdminToCaptain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Teams_AspNetUsers_AdminUserId",
                table: "Teams");

            migrationBuilder.RenameColumn(
                name: "AdminUserId",
                table: "Teams",
                newName: "CaptainUserId");

            migrationBuilder.RenameIndex(
                name: "IX_Teams_AdminUserId",
                table: "Teams",
                newName: "IX_Teams_CaptainUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Teams_AspNetUsers_CaptainUserId",
                table: "Teams",
                column: "CaptainUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Teams_AspNetUsers_CaptainUserId",
                table: "Teams");

            migrationBuilder.RenameColumn(
                name: "CaptainUserId",
                table: "Teams",
                newName: "AdminUserId");

            migrationBuilder.RenameIndex(
                name: "IX_Teams_CaptainUserId",
                table: "Teams",
                newName: "IX_Teams_AdminUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Teams_AspNetUsers_AdminUserId",
                table: "Teams",
                column: "AdminUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
