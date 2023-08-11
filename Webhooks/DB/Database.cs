using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Webhooks.DB {
    class Database {
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
            return conn;
        }

        internal async Task Exec(string sql, Dictionary<string, object>? parameters = null) {
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

        //internal enum SqlitePragma {
        //    SCHEMA_USER_VERSION = 1
        //}



        //internal async Task<object?> QueryPragma(SqlitePragma pragma) {
        //    using var conn = await GetOpenConnection();
        //    using var cmd = conn.CreateCommand();
        //    cmd.CommandText = $"PRAGMA {pragma};";

        //    using var rdr = await cmd.ExecuteReaderAsync();
        //    return rdr.Read()
        //        ? rdr.GetValue(0)
        //        : null;
        //}

        void AddParameters(SqliteCommand cmd, Dictionary<string, object>? parameters) {
            if (parameters is not null) {
                foreach ((var k, var v) in parameters) {
                    cmd.Parameters.AddWithValue(k, v);
                }
            }
        }
    }
}
