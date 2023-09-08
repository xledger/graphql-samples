using Spectre.Console.Cli;
using Webhooks.DB;
using Webhooks.GraphQL;

namespace Webhooks.Commands {
    class SyncProjects : AsyncCommand<SyncProjects.Settings> {
        public class Settings : CommandSettings {
            [CommandArgument(0, "<config-file-path>")]
            public required string ConfigFilePath { get; set; }
        }

        public async override Task<int> ExecuteAsync(CommandContext context, Settings settings) {
            var config = await Config.FromJsonFile(settings.ConfigFilePath);
            config.Validate();

            var db = new Database(config);

            var cts = new CancellationTokenSource();
            var graphQLClient = new GraphQLClient(config.GraphQLToken, new Uri(config.GraphQLEndpoint));
            var projectSyncer = new ProjectSyncer(db, graphQLClient, config, cts.Token);

            await projectSyncer.Run();

            return 0;
        }
    }
}
