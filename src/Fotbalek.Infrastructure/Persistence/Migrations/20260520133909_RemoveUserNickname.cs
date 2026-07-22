using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fotbalek.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUserNickname : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Nickname",
                table: "AspNetUsers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Nickname",
                table: "AspNetUsers",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);
        }
    }
}
