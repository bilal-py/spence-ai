using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpenceAI.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class _20261113214414_InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Categories",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                Color = table.Column<string>(type: "TEXT", maxLength: 9, nullable: false),
                Icon = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                MonthlyBudget = table.Column<decimal>(type: "TEXT", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Categories", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ImportBatches",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                FileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                Source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                Error = table.Column<string>(type: "TEXT", nullable: true),
                StagedRowsJson = table.Column<string>(type: "TEXT", nullable: true),
                TransactionCount = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ImportBatches", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Settings",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                AiProvider = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                AiModel = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                OllamaUrl = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                ProtectedApiKey = table.Column<string>(type: "TEXT", nullable: true),
                Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Settings", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "MerchantRules",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Pattern = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                CategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MerchantRules", x => x.Id);
                table.ForeignKey(
                    name: "FK_MerchantRules_Categories_CategoryId",
                    column: x => x.CategoryId,
                    principalTable: "Categories",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Transactions",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                Type = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                Merchant = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                CategoryId = table.Column<int>(type: "INTEGER", nullable: true),
                Source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                ImportBatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                Fingerprint = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Transactions", x => x.Id);
                table.ForeignKey(
                    name: "FK_Transactions_Categories_CategoryId",
                    column: x => x.CategoryId,
                    principalTable: "Categories",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_Transactions_ImportBatches_ImportBatchId",
                    column: x => x.ImportBatchId,
                    principalTable: "ImportBatches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Categories_Name",
            table: "Categories",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ImportBatches_CreatedAt",
            table: "ImportBatches",
            column: "CreatedAt");

        migrationBuilder.CreateIndex(
            name: "IX_MerchantRules_CategoryId",
            table: "MerchantRules",
            column: "CategoryId");

        migrationBuilder.CreateIndex(
            name: "IX_MerchantRules_Pattern",
            table: "MerchantRules",
            column: "Pattern",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Transactions_CategoryId",
            table: "Transactions",
            column: "CategoryId");

        migrationBuilder.CreateIndex(
            name: "IX_Transactions_Date",
            table: "Transactions",
            column: "Date");

        migrationBuilder.CreateIndex(
            name: "IX_Transactions_Fingerprint",
            table: "Transactions",
            column: "Fingerprint");

        migrationBuilder.CreateIndex(
            name: "IX_Transactions_ImportBatchId",
            table: "Transactions",
            column: "ImportBatchId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "MerchantRules");

        migrationBuilder.DropTable(
            name: "Settings");

        migrationBuilder.DropTable(
            name: "Transactions");

        migrationBuilder.DropTable(
            name: "Categories");

        migrationBuilder.DropTable(
            name: "ImportBatches");
    }
}
