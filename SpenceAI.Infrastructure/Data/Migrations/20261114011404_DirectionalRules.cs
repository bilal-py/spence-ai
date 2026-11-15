using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpenceAI.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class _20261114011404_DirectionalRules : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_MerchantRules_Pattern",
            table: "MerchantRules");

        migrationBuilder.AddColumn<string>(
            name: "Direction",
            table: "MerchantRules",
            type: "TEXT",
            maxLength: 8,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "UpdatedAt",
            table: "MerchantRules",
            type: "TEXT",
            nullable: false,
            defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

        // Existing rules keep Direction = null (they match both ways) and were last changed when they were created.
        migrationBuilder.Sql("UPDATE MerchantRules SET UpdatedAt = CreatedAt;");

        migrationBuilder.CreateIndex(
            name: "IX_MerchantRules_Pattern_Direction",
            table: "MerchantRules",
            columns: new[] { "Pattern", "Direction" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_MerchantRules_Pattern_Direction",
            table: "MerchantRules");

        migrationBuilder.DropColumn(
            name: "Direction",
            table: "MerchantRules");

        migrationBuilder.DropColumn(
            name: "UpdatedAt",
            table: "MerchantRules");

        migrationBuilder.CreateIndex(
            name: "IX_MerchantRules_Pattern",
            table: "MerchantRules",
            column: "Pattern",
            unique: true);
    }
}
