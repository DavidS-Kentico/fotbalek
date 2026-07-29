using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fotbalek.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartAnnouncedAt",
                table: "Seasons",
                type: "datetimeoffset",
                nullable: true);

            // Backfill guard, hand-written because the scaffolder will not produce it: without this,
            // the feature's first act is to announce the start of every season in the team's history
            // (AI/notifications.md §3.6).
            //
            // The StartsAt predicate is NOT optional. Stamping every existing season would also mute
            // seasons that were already created but are scheduled to start AFTER this deployment —
            // permanently, since the guard is a one-way flag. Those are the exact rows the feature is
            // supposed to catch.
            //
            // LadderLeader needs no equivalent seed: the first evaluation per (team, scope, category)
            // writes its snapshot silently, which is the same guard by other means (§6.3).
            // NotificationPreferences needs none either — an empty table means everyone is on the
            // defaults, which is the intended starting state (§8.2).
            migrationBuilder.Sql("""
                UPDATE Seasons
                SET StartAnnouncedAt = CreatedAt
                WHERE StartAnnouncedAt IS NULL AND StartsAt <= SYSDATETIMEOFFSET();
                """);

            migrationBuilder.CreateTable(
                name: "LadderLeaders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    SeasonId = table.Column<int>(type: "int", nullable: true),
                    Category = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PlayerId = table.Column<int>(type: "int", nullable: false),
                    PartnerPlayerId = table.Column<int>(type: "int", nullable: true),
                    EvaluatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LadderLeaders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LadderLeaders_Players_PartnerPlayerId",
                        column: x => x.PartnerPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LadderLeaders_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LadderLeaders_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LadderLeaders_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    TeamId = table.Column<int>(type: "int", nullable: true),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Channels = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationPreferences_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NotificationPreferences_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReadAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ActorPlayerId = table.Column<int>(type: "int", nullable: true),
                    SubjectPlayerId = table.Column<int>(type: "int", nullable: true),
                    MatchId = table.Column<int>(type: "int", nullable: true),
                    SeasonId = table.Column<int>(type: "int", nullable: true),
                    ChatMessageId = table.Column<int>(type: "int", nullable: true),
                    Category = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Value = table.Column<int>(type: "int", nullable: true),
                    Emoji = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    DedupKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Notifications_ChatMessages_ChatMessageId",
                        column: x => x.ChatMessageId,
                        principalTable: "ChatMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Notifications_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Notifications_Players_ActorPlayerId",
                        column: x => x.ActorPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Notifications_Players_SubjectPlayerId",
                        column: x => x.SubjectPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Notifications_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Notifications_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_TeamId_StartAnnouncedAt_StartsAt",
                table: "Seasons",
                columns: new[] { "TeamId", "StartAnnouncedAt", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LadderLeaders_PartnerPlayerId",
                table: "LadderLeaders",
                column: "PartnerPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_LadderLeaders_PlayerId",
                table: "LadderLeaders",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_LadderLeaders_SeasonId",
                table: "LadderLeaders",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_LadderLeaders_TeamId_SeasonId_Category",
                table: "LadderLeaders",
                columns: new[] { "TeamId", "SeasonId", "Category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_TeamId",
                table: "NotificationPreferences",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_UserId",
                table: "NotificationPreferences",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_UserId_TeamId_Category",
                table: "NotificationPreferences",
                columns: new[] { "UserId", "TeamId", "Category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ActorPlayerId",
                table: "Notifications",
                column: "ActorPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ChatMessageId",
                table: "Notifications",
                column: "ChatMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_MatchId",
                table: "Notifications",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_SeasonId",
                table: "Notifications",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_SubjectPlayerId",
                table: "Notifications",
                column: "SubjectPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TeamId",
                table: "Notifications",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_DedupKey",
                table: "Notifications",
                columns: new[] { "UserId", "DedupKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_Id",
                table: "Notifications",
                columns: new[] { "UserId", "Id" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_Unseen",
                table: "Notifications",
                column: "UserId",
                filter: "[SeenAt] IS NULL")
                .Annotation("SqlServer:Include", new[] { "TeamId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LadderLeaders");

            migrationBuilder.DropTable(
                name: "NotificationPreferences");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Seasons_TeamId_StartAnnouncedAt_StartsAt",
                table: "Seasons");

            migrationBuilder.DropColumn(
                name: "StartAnnouncedAt",
                table: "Seasons");
        }
    }
}
