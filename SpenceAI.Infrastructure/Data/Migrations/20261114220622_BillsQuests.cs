using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpenceAI.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class _20261114220622_BillsQuests : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DismissedBillSuggestions",
            table: "Settings",
            type: "TEXT",
            maxLength: 4000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PetAccessory",
            table: "Settings",
            type: "TEXT",
            maxLength: 20,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "Bills",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                AmountVaries = table.Column<bool>(type: "INTEGER", nullable: false),
                Type = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                Cadence = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                Interval = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                FirstDueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                EndsOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                MatchPattern = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                CategoryId = table.Column<int>(type: "INTEGER", nullable: true),
                AccountId = table.Column<int>(type: "INTEGER", nullable: true),
                Autopay = table.Column<bool>(type: "INTEGER", nullable: false),
                IsSubscription = table.Column<bool>(type: "INTEGER", nullable: false),
                RemindDaysBefore = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 3),
                IsPaused = table.Column<bool>(type: "INTEGER", nullable: false),
                CancelledOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                LastReviewedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Bills", x => x.Id);
                table.ForeignKey(
                    name: "FK_Bills_Accounts_AccountId",
                    column: x => x.AccountId,
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_Bills_Categories_CategoryId",
                    column: x => x.CategoryId,
                    principalTable: "Categories",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "QuestCompletions",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                WeekStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                Key = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Xp = table.Column<int>(type: "INTEGER", nullable: false),
                CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_QuestCompletions", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "BillMarks",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                BillId = table.Column<int>(type: "INTEGER", nullable: false),
                DueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                TransactionId = table.Column<int>(type: "INTEGER", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BillMarks", x => x.Id);
                table.ForeignKey(
                    name: "FK_BillMarks_Bills_BillId",
                    column: x => x.BillId,
                    principalTable: "Bills",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_BillMarks_Transactions_TransactionId",
                    column: x => x.TransactionId,
                    principalTable: "Transactions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BillMarks_BillId_DueDate",
            table: "BillMarks",
            columns: new[] { "BillId", "DueDate" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_BillMarks_TransactionId",
            table: "BillMarks",
            column: "TransactionId");

        migrationBuilder.CreateIndex(
            name: "IX_Bills_AccountId",
            table: "Bills",
            column: "AccountId");

        migrationBuilder.CreateIndex(
            name: "IX_Bills_CategoryId",
            table: "Bills",
            column: "CategoryId");

        migrationBuilder.CreateIndex(
            name: "IX_QuestCompletions_WeekStart_Key",
            table: "QuestCompletions",
            columns: new[] { "WeekStart", "Key" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "BillMarks");

        migrationBuilder.DropTable(
            name: "QuestCompletions");

        migrationBuilder.DropTable(
            name: "Bills");

        migrationBuilder.DropColumn(
            name: "DismissedBillSuggestions",
            table: "Settings");

        migrationBuilder.DropColumn(
            name: "PetAccessory",
            table: "Settings");
    }
}
