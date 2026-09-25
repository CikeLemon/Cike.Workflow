using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cike.Workflow.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowInstanceIsDebug : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDebug",
                table: "WorkflowInstances",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDebug",
                table: "WorkflowInstances");
        }
    }
}
