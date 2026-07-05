using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fotbalek.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddSeasons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SeasonEloAfter",
                table: "MatchPlayers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeasonEloBefore",
                table: "MatchPlayers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeasonEloChange",
                table: "MatchPlayers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeasonId",
                table: "Matches",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Seasons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Seasons_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeasonAwards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    PartnerPlayerId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonAwards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeasonAwards_Players_PartnerPlayerId",
                        column: x => x.PartnerPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeasonAwards_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeasonAwards_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeasonPairs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    Player1Id = table.Column<int>(type: "int", nullable: false),
                    Player2Id = table.Column<int>(type: "int", nullable: false),
                    MatchesTogether = table.Column<int>(type: "int", nullable: false),
                    WinsTogether = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonPairs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeasonPairs_Players_Player1Id",
                        column: x => x.Player1Id,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeasonPairs_Players_Player2Id",
                        column: x => x.Player2Id,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeasonPairs_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeasonPlayers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<int>(type: "int", nullable: false),
                    Elo = table.Column<int>(type: "int", nullable: false, defaultValue: 1000)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonPlayers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeasonPlayers_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeasonPlayers_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeasonPlayerResults",
                columns: table => new
                {
                    SeasonPlayerId = table.Column<int>(type: "int", nullable: false),
                    FinalRank = table.Column<int>(type: "int", nullable: true),
                    Wins = table.Column<int>(type: "int", nullable: false),
                    Losses = table.Column<int>(type: "int", nullable: false),
                    MatchesPlayed = table.Column<int>(type: "int", nullable: false),
                    LongestWinStreak = table.Column<int>(type: "int", nullable: false),
                    LongestLossStreak = table.Column<int>(type: "int", nullable: false),
                    GoalkeeperMatches = table.Column<int>(type: "int", nullable: false),
                    GoalsConcededAsGoalkeeper = table.Column<int>(type: "int", nullable: false),
                    AttackerMatches = table.Column<int>(type: "int", nullable: false),
                    GoalsScoredAsAttacker = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonPlayerResults", x => x.SeasonPlayerId);
                    table.ForeignKey(
                        name: "FK_SeasonPlayerResults_SeasonPlayers_SeasonPlayerId",
                        column: x => x.SeasonPlayerId,
                        principalTable: "SeasonPlayers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_SeasonId",
                table: "Matches",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonAwards_PartnerPlayerId",
                table: "SeasonAwards",
                column: "PartnerPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonAwards_PlayerId",
                table: "SeasonAwards",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonAwards_SeasonId",
                table: "SeasonAwards",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonAwards_SeasonId_Category_Rank_PlayerId",
                table: "SeasonAwards",
                columns: new[] { "SeasonId", "Category", "Rank", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeasonPairs_Player1Id",
                table: "SeasonPairs",
                column: "Player1Id");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonPairs_Player2Id",
                table: "SeasonPairs",
                column: "Player2Id");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonPairs_SeasonId",
                table: "SeasonPairs",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonPairs_SeasonId_Player1Id_Player2Id",
                table: "SeasonPairs",
                columns: new[] { "SeasonId", "Player1Id", "Player2Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeasonPlayers_PlayerId",
                table: "SeasonPlayers",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonPlayers_SeasonId",
                table: "SeasonPlayers",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonPlayers_SeasonId_PlayerId",
                table: "SeasonPlayers",
                columns: new[] { "SeasonId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_TeamId",
                table: "Seasons",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_TeamId_ClosedAt_EndsAt",
                table: "Seasons",
                columns: new[] { "TeamId", "ClosedAt", "EndsAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Matches_Seasons_SeasonId",
                table: "Matches",
                column: "SeasonId",
                principalTable: "Seasons",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Matches_Seasons_SeasonId",
                table: "Matches");

            migrationBuilder.DropTable(
                name: "SeasonAwards");

            migrationBuilder.DropTable(
                name: "SeasonPairs");

            migrationBuilder.DropTable(
                name: "SeasonPlayerResults");

            migrationBuilder.DropTable(
                name: "SeasonPlayers");

            migrationBuilder.DropTable(
                name: "Seasons");

            migrationBuilder.DropIndex(
                name: "IX_Matches_SeasonId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "SeasonEloAfter",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "SeasonEloBefore",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "SeasonEloChange",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "SeasonId",
                table: "Matches");
        }
    }
}
