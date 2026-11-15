using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpenceAI.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class _20261114151435_Accounts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "AccountId",
            table: "Transactions",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "Accounts",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                Last4 = table.Column<string>(type: "TEXT", maxLength: 4, nullable: true),
                Color = table.Column<string>(type: "TEXT", maxLength: 9, nullable: false),
                IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Accounts", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Transactions_AccountId",
            table: "Transactions",
            column: "AccountId");

        migrationBuilder.CreateIndex(
            name: "IX_Accounts_Name",
            table: "Accounts",
            column: "Name",
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_Transactions_Accounts_AccountId",
            table: "Transactions",
            column: "AccountId",
            principalTable: "Accounts",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Transactions_Accounts_AccountId",
            table: "Transactions");

        migrationBuilder.DropTable(
            name: "Accounts");

        migrationBuilder.DropIndex(
            name: "IX_Transactions_AccountId",
            table: "Transactions");

        migrationBuilder.DropColumn(
            name: "AccountId",
            table: "Transactions");
    }
}
