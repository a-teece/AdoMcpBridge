using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdoMcpBridge.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorizationSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RedirectUri = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    ClientCodeChallenge = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ClientCodeChallengeMethod = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ClientState = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    EntraCodeVerifier = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EntraState = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.SessionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_EntraState",
                table: "Sessions",
                column: "EntraState",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_ExpiresAt",
                table: "Sessions",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sessions");
        }
    }
}
