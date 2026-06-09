using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdoMcpBridge.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuthorizationCodes",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RedirectUri = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    PkceChallenge = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PkceMethod = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    EntraRefreshTokenEncrypted = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserObjectId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserPrincipalName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorizationCodes", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    ClientId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ClientName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RedirectUrisJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.ClientId);
                });

            migrationBuilder.CreateTable(
                name: "Tokens",
                columns: table => new
                {
                    AccessTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RefreshTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EntraRefreshTokenEncrypted = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserObjectId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserPrincipalName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    AccessTokenExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RefreshTokenExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tokens", x => x.AccessTokenHash);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizationCodes_ExpiresAt",
                table: "AuthorizationCodes",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_Tokens_RefreshTokenExpiresAt",
                table: "Tokens",
                column: "RefreshTokenExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_Tokens_RefreshTokenHash",
                table: "Tokens",
                column: "RefreshTokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthorizationCodes");

            migrationBuilder.DropTable(
                name: "Clients");

            migrationBuilder.DropTable(
                name: "Tokens");
        }
    }
}
