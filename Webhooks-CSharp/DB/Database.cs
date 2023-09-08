using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Webhooks.DB {
    class Database {
        const int SQLITE_CACHE_SIZE_KB = 60000; // The Sqlite default is just 2000.
        string ConnectionString { get; init; }

        static Database() {
            SQLitePCL.Batteries.Init();
        }

        internal Database(Config config) {
            if (string.IsNullOrWhiteSpace(config.DbPath)
                || !config.DbPath.EndsWith(".db")) {
                throw new ArgumentException("Expected a DbPath that ends with \".db\"");
            }

            var sb = new StringBuilder();
            SqliteConnectionStringBuilder.AppendKeyValuePair(sb, "Data Source", config.DbPath);
            ConnectionString = sb.ToString();
        }

        internal async Task<SqliteConnection> GetOpenConnection(CancellationToken tok = default) {
            var conn = new SqliteConnection(ConnectionString);
            await conn.OpenAsync(tok);

            // https://www.sqlite.org/pragma.html#pragma_journal_mode
            await ExecPragmaAsync(conn, "PRAGMA journal_mode=WAL;", tok);
            
            // https://www.sqlite.org/pragma.html#pragma_cache_size
            //                                     See link ^ for why this is negative
            await ExecPragmaAsync(conn, $"PRAGMA cache_size = -{SQLITE_CACHE_SIZE_KB};", tok);

            return conn;
        }

        internal async Task ExecPragmaAsync(SqliteConnection conn, string pragmaCall, CancellationToken tok = default) {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = pragmaCall;
            using (await cmd.ExecuteReaderAsync(tok)) { }
        }

        internal async Task ExecAsync(string sql, Dictionary<string, object>? parameters = null) {
            using var conn = await GetOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            AddParameters(cmd, parameters);

            await cmd.ExecuteNonQueryAsync();
        }

        internal async Task<int?> QueryScalarInt(string sql, Dictionary<string, object>? parameters = null) {
            using var conn = await GetOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            AddParameters(cmd, parameters);

            var ret = await cmd.ExecuteScalarAsync();
            return ret is not null
                ? Convert.ToInt32(ret)
                : null;
        }

        void AddParameters(SqliteCommand cmd, Dictionary<string, object>? parameters) {
            if (parameters is not null) {
                foreach ((var k, var v) in parameters) {
                    cmd.Parameters.AddWithValue(k, v);
                }
            }
        }
    }
}
