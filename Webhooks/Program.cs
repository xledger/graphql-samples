using Serilog;
using Spectre.Console.Cli;
using Webhooks.Commands;

var logCfg = new LoggerConfiguration();
logCfg.MinimumLevel.Is(Serilog.Events.LogEventLevel.Verbose);
logCfg.WriteTo.Console();
await using var logger = logCfg.CreateLogger();
Log.Logger = logger;

try {
    var app = new CommandApp();
    app.Configure(config => {
        config.AddCommand<MigrateDB>("migrate-db");
        config.PropagateExceptions();
    });
    return await app.RunAsync(args);
} catch (CommandRuntimeException ex) {
    if (ex.Pretty is not null) {
        Spectre.Console.AnsiConsole.Write(ex.Pretty);
    } else {
        Log.Error("{m}", ex.Message);
    }
    return 0;
} catch (Exception ex) {
    Log.Fatal("Fatal Error: {t} {ex}", ex.GetType().Name, ex);
    return 1;
}
