using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpenceAI.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class _20261113234925_RefundTypeAndTransferRules : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<int>(
            name: "CategoryId",
            table: "MerchantRules",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AddColumn<string>(
            name: "Type",
            table: "MerchantRules",
            type: "TEXT",
            maxLength: 16,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Type",
            table: "MerchantRules");

        migrationBuilder.AlterColumn<int>(
            name: "CategoryId",
            table: "MerchantRules",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);
    }
}
