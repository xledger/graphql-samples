using Microsoft.Data.Sqlite;
using Webhooks.Utils;

namespace Webhooks.DB.Models {
    class SyncStatus {
        internal enum SyncType {
            None = 0,
            QuerySyncing = 1,
            WebhookListening = 2
        }

        internal string TableName { get; }
        internal SyncType Type { get; set; }
        internal string? SyncValue { get; set; }
        internal DateTime StartTime { get; set; }
        internal DateTime AsOfTime { get; set; }
        internal int? WebhookXledgerDbId { get; set; }

        internal SyncStatus(
            string tableName,
            string syncType,
            string syncValue,
            double startTime,
            double asOfTime,
            int? webhookXledgerDbId
        ) {
            TableName = tableName;
            Type = Enum.Parse<SyncType>(syncType);

            SyncValue = syncValue;
            StartTime = Dates.JulianToDateTime(startTime);
            AsOfTime = Dates.JulianToDateTime(asOfTime);
            WebhookXledgerDbId = webhookXledgerDbId;
        }

        internal SyncStatus(
            string tableName,
            SyncType syncType,
            string? syncValue,
            DateTime startTime,
            DateTime asOfTime,
            int? webhookXledgerDbId
        ) {
            TableName = tableName;
            Type = syncType;

            SyncValue = syncValue;
            StartTime = startTime;
            AsOfTime = asOfTime;
            WebhookXledgerDbId = webhookXledgerDbId;
        }

        async static internal Task<SyncStatus?> FetchAsync(Database db, string tableName) {
            using var conn = await db.GetOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"select tableName, syncType, syncValue, startTime, asOfTime, webhookXledgerDbId
from SyncStatus
where tableName = @tableName";
            cmd.Parameters.AddWithValue("tableName", tableName);

            using var rdr = await cmd.ExecuteReaderAsync();
            if (!await rdr.ReadAsync()) {
                return null;
            }

            int? webhookXledgerDbId = null;
            if (rdr.GetValue(5) is object _5 and not DBNull) {
                webhookXledgerDbId = Convert.ToInt32(_5);
            }
            return new SyncStatus(
                Convert.ToString(rdr.GetValue(0))!,
                Convert.ToString(rdr.GetValue(1))!,
                Convert.ToString(rdr.GetValue(2))!,
                Convert.ToDouble(rdr.GetValue(3)),
                Convert.ToDouble(rdr.GetValue(4)),
                webhookXledgerDbId
            );
        }

        async internal Task SaveAsync(SqliteConnection conn, CancellationToken tok = default) {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO SyncStatus(tableName, syncType, syncValue, startTime, asOfTime, webhookXledgerDbId)
VALUES(@tableName, @syncType, @syncValue, @startTime, @asOfTime, @webhookXledgerDbId)
ON CONFLICT(tableName) DO
UPDATE SET
  syncType = excluded.syncType
 ,syncValue = excluded.syncValue
 ,startTime = excluded.startTime
 ,asOfTime = excluded.asOfTime
 ,webhookXledgerDbId = excluded.webhookXledgerDbId;";
            cmd.Parameters.AddWithValue2("tableName", TableName);
            cmd.Parameters.AddWithValue2("syncType", Type.ToString());
            cmd.Parameters.AddWithValue2("syncValue", SyncValue);
            cmd.Parameters.AddWithValue2("startTime", Dates.DateTimeToJulian(StartTime));
            cmd.Parameters.AddWithValue2("asOfTime", Dates.DateTimeToJulian(AsOfTime));
            cmd.Parameters.AddWithValue2("webhookXledgerDbId", WebhookXledgerDbId);
            await cmd.ExecuteNonQueryAsync(tok);
        }

        async internal Task SaveAsync(Database db, CancellationToken tok = default) {
            using var conn = await db.GetOpenConnection(tok);
            
            await SaveAsync(conn, tok);
        }
    }
}
