using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// The full-text catalog and the one full-text index, on Shipments.SearchText. EF has no model
    /// for full-text objects, so this is raw SQL and the model snapshot does not change.
    /// </summary>
    /// <remarks>
    /// SQL Server rejects full-text DDL inside a user transaction, hence suppressTransaction on every call.
    /// LANGUAGE 0 (Neutral): its word breaker splits on punctuation such as apostrophes, which matches the
    /// query side (ShipmentSearch splits on every non-letter, non-digit). STOPLIST = OFF so no word is
    /// silently unsearchable. CHANGE_TRACKING AUTO keeps the index current as rows change.
    /// </remarks>
    public partial class AddShipmentFullTextSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE FULLTEXT CATALOG [ShipmentSearch] AS DEFAULT;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                """
                CREATE FULLTEXT INDEX ON [dbo].[Shipments] ([SearchText] LANGUAGE 0)
                    KEY INDEX [PK_Shipments]
                    ON [ShipmentSearch]
                    WITH (STOPLIST = OFF, CHANGE_TRACKING = AUTO);
                """,
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP FULLTEXT INDEX ON [dbo].[Shipments];",
                suppressTransaction: true);

            migrationBuilder.Sql(
                "DROP FULLTEXT CATALOG [ShipmentSearch];",
                suppressTransaction: true);
        }
    }
}
