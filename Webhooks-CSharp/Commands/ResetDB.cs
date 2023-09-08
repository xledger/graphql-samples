using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Serilog;
using Spectre.Console.Cli;
using Webhooks.DB;


namespace Webhooks.Commands {
    class ResetDB : AsyncCommand<ResetDB.Settings> {
        public class Settings : CommandSettings {
            [CommandArgument(0, "<config-file-path>")]
            public required string ConfigFilePath { get; set; }
        }

        public async override Task<int> ExecuteAsync(CommandContext context, Settings settings) {
            var config = await Config.FromJsonFile(settings.ConfigFilePath);
            var dbDir = Directory.GetParent(config.DbPath)!;

            var files = dbDir.GetFiles(
                Path.GetFileName(config.DbPath) + "*",
                SearchOption.TopDirectoryOnly);

            var success = true;
            foreach (var f in files) {
                var path = f.FullName;

                try {
                    f.Delete();
                    Log.Information("Deleted file: {f}", f);
                } catch (Exception ex) {
                    Log.Error("Failed to delete file: {f}. {ex}", f, ex.Message);
                    success = false;
                }
                
            }

            if (success) {
                Log.Information("Successfully deleted old DB.");
            }

            await MigrateDB.RunMigrationsAsync(config);

            return success ? 0 : 1;
        }
    }
}
