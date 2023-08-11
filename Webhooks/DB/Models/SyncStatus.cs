using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Webhooks.Utils;

namespace Webhooks.DB.Models {
    class SyncStatus {
        internal enum SyncType {
            None = 0,
            FullCursorSyncing = 1,
            LatestChangesSyncing = 2,
            WebhookListening = 3
        }

        internal string TableName { get; }
        internal SyncType Type { get; set; }
        internal string? SyncValue { get; set; }
        internal DateTime StartTime { get; set; }
        internal DateTime AsOfTime { get; set; }

        internal SyncStatus(string tableName, string syncType, string syncValue, double startTime, double asOfTime) {
            TableName = tableName;
            Type = Enum.Parse<SyncType>(syncType);

            SyncValue = syncValue;
            StartTime = Dates.JulianToDateTime(startTime);
            AsOfTime = Dates.JulianToDateTime(asOfTime);
        }

        internal SyncStatus(string tableName, SyncType syncType, string? syncValue, DateTime startTime, DateTime asOfTime) {
            TableName = tableName;
            Type = syncType;

            SyncValue = syncValue;
            StartTime = startTime;
            AsOfTime = asOfTime;
        }

        async static internal Task<SyncStatus?> Fetch(Database db, string tableName) {
            using var conn = await db.GetOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"select tableName, syncType, syncValue, startTime, asOfTime
from SyncStatus
where tableName = @tableName";
            cmd.Parameters.AddWithValue("tableName", tableName);

            using var rdr = await cmd.ExecuteReaderAsync();
            if (!await rdr.ReadAsync()) {
                return null;
            }

            return new SyncStatus(
                Convert.ToString(rdr.GetValue(0))!,
                Convert.ToString(rdr.GetValue(1))!,
                Convert.ToString(rdr.GetValue(2))!,
                Convert.ToDouble(rdr.GetValue(3)),
                Convert.ToDouble(rdr.GetValue(4))
            );
        }

        async internal Task SaveAsync(SqliteConnection conn, CancellationToken tok = default) {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO SyncStatus(tableName, syncType, syncValue, startTime, asOfTime)
VALUES(@tableName, @syncType, @syncValue, @startTime, @asOfTime)
ON CONFLICT(tableName) DO
UPDATE SET
  syncType = excluded.phonenumber
 ,syncValue = excluded.validDate
 ,startTime = excluded.startTime
 ,asOfTime = excluded.asOfTime;";
            cmd.Parameters.AddWithValue("tableName", TableName);
            cmd.Parameters.AddWithValue("syncType", Type.ToString());
            cmd.Parameters.AddWithValue("syncValue", SyncValue);
            cmd.Parameters.AddWithValue("startTime", Dates.DateTimeToJulian(StartTime));
            cmd.Parameters.AddWithValue("asOfTime", Dates.DateTimeToJulian(AsOfTime));
            await cmd.ExecuteNonQueryAsync(tok);
        }

        async internal Task SaveAsync(Database db, CancellationToken tok = default) {
            using var conn = await db.GetOpenConnection();
            
            await SaveAsync(conn, tok);
        }
    }
}
