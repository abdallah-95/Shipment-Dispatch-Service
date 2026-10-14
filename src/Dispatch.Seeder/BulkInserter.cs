using System.Data;
using Dispatch.Domain.Entities;
using Microsoft.Data.SqlClient;

namespace Dispatch.Seeder;

internal static class BulkInserter
{
    /// <summary>
    /// Writes the whole dataset in one transaction, so a failure cannot leave a partially seeded
    /// database that the "shipments exist" idempotency check would mistake for complete.
    /// </summary>
    public static async Task InsertAsync(SqlConnection connection, SeedDataset data, CancellationToken cancellationToken)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await CopyAsync(connection, transaction, "Customers", CustomersTable(data.Customers), cancellationToken);
        await CopyAsync(connection, transaction, "Drivers", DriversTable(data.Drivers), cancellationToken);
        await CopyAsync(connection, transaction, "Shipments", ShipmentsTable(data.Shipments), cancellationToken);
        await CopyAsync(connection, transaction, "ShipmentStatusHistory", HistoryTable(data.History), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task CopyAsync(
        SqlConnection connection, SqlTransaction transaction, string table, DataTable rows, CancellationToken cancellationToken)
    {
        // KeepIdentity: ids are assigned by the generator so history can reference shipments.
        // CheckConstraints: bulk copy skips FK checks by default, which would leave the foreign
        // keys marked "not trusted" and unusable by the optimizer.
        using var bulk = new SqlBulkCopy(
            connection,
            SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.TableLock,
            transaction)
        {
            DestinationTableName = $"dbo.{table}",
            BatchSize = 10_000,
            BulkCopyTimeout = 0,
        };

        foreach (DataColumn column in rows.Columns)
        {
            bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulk.WriteToServerAsync(rows, cancellationToken);
    }

    private static DataTable CustomersTable(IReadOnlyList<Customer> customers)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("Name", typeof(string));
        foreach (var c in customers)
        {
            table.Rows.Add(c.Id, c.Name);
        }

        return table;
    }

    private static DataTable DriversTable(IReadOnlyList<Driver> drivers)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("Name", typeof(string));
        foreach (var d in drivers)
        {
            table.Rows.Add(d.Id, d.Name);
        }

        return table;
    }

    private static DataTable ShipmentsTable(IReadOnlyList<Shipment> shipments)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("TrackingNumber", typeof(string));
        table.Columns.Add("CustomerId", typeof(int));
        table.Columns.Add("DriverId", typeof(int));
        table.Columns.Add("Origin", typeof(string));
        table.Columns.Add("Destination", typeof(string));
        table.Columns.Add("WeightKg", typeof(decimal));
        table.Columns.Add("Status", typeof(string));
        table.Columns.Add("CreatedAt", typeof(DateTime));
        table.Columns.Add("UpdatedAt", typeof(DateTime));
        table.Columns.Add("Version", typeof(int));
        table.Columns.Add("SearchText", typeof(string));
        foreach (var s in shipments)
        {
            table.Rows.Add(
                s.Id, s.TrackingNumber, s.CustomerId, (object?)s.DriverId ?? DBNull.Value, s.Origin, s.Destination,
                s.WeightKg, s.Status.ToString(), s.CreatedAt, s.UpdatedAt, s.Version, s.SearchText);
        }

        return table;
    }

    private static DataTable HistoryTable(IReadOnlyList<ShipmentStatusHistory> history)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("ShipmentId", typeof(int));
        table.Columns.Add("FromStatus", typeof(string));
        table.Columns.Add("ToStatus", typeof(string));
        table.Columns.Add("ChangedAt", typeof(DateTime));
        foreach (var h in history)
        {
            table.Rows.Add(h.Id, h.ShipmentId, (object?)h.FromStatus?.ToString() ?? DBNull.Value, h.ToStatus.ToString(), h.ChangedAt);
        }

        return table;
    }
}
