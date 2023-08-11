using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Serilog;
using Spectre.Console.Cli;
using Webhooks.DB;

namespace Webhooks.Commands {
    public class MigrateDB : AsyncCommand<MigrateDB.Settings> {

        public class Settings : CommandSettings {
            [CommandArgument(0, "<config-file-path>")]
            public required string ConfigFilePath { get; set; }
        }

        public async override Task<int> ExecuteAsync(CommandContext context, Settings settings) {
            var config = await Config.FromJsonFile(settings.ConfigFilePath);

            var db = new Database(config);

            var userVersion = 0;

            {
                using var conn = await db.GetOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA user_version";
                using var rdr = await cmd.ExecuteReaderAsync();
                if (await rdr.ReadAsync()) {
                    userVersion = Convert.ToInt32(rdr.GetValue(0));
                }
            }

            Log.Information("Current user_version: {n}", userVersion);

            var migrationFiles = MigrationFile.ReadAll();
            var upToDate = true;
            
            foreach (var f in migrationFiles) {
                if (f.VersionNumber <= userVersion) {
                    continue;
                }
                upToDate = false;
                var sql = f.Sql;

                Log.Information("Running migration:\n{s}", sql);

                using var conn = await db.GetOpenConnection();
                var tx = await conn.BeginTransactionAsync();
                try {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    await cmd.ExecuteNonQueryAsync();
                    cmd.CommandText = $"PRAGMA user_version = {f.VersionNumber};";
                    cmd.ExecuteNonQuery();
                    await tx.CommitAsync();
                    Log.Information("Migrated successfully: {f} ", f);
                } catch (Exception ex) {
                    Log.Error("Error: {ex}", ex);
                    await tx.RollbackAsync();
                }
            }

            Log.Information("Up-to-date with all migrations.");

            return 0;
        }
    }
}
