using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectReplyLearning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReplyLessons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplyLessons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReplyLessonEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    LessonId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplyLessonEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReplyLessonEvidence_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReplyLessonEvidence_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReplyLessonEvidence_ReplyLessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "ReplyLessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReplyLessonEvidence_ConversationId",
                table: "ReplyLessonEvidence",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplyLessonEvidence_LessonId",
                table: "ReplyLessonEvidence",
                column: "LessonId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplyLessonEvidence_MessageId",
                table: "ReplyLessonEvidence",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplyLessonEvidence_ProjectId_LessonId_ConversationId",
                table: "ReplyLessonEvidence",
                columns: new[] { "ProjectId", "LessonId", "ConversationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReplyLessons_ProjectId_Channel_Code",
                table: "ReplyLessons",
                columns: new[] { "ProjectId", "Channel", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReplyLessonEvidence");

            migrationBuilder.DropTable(
                name: "ReplyLessons");
        }
    }
}
