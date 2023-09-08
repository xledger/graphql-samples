using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace Webhooks.GraphQL {
    static class WebServer {
        public record WebhookRequest(DateTimeOffset Date, string Signature, string Body);

        /// <summary>
        /// Starts a Kestrel web server that listens until cancellation and 
        /// handles all project POSTs with the `handleProjectsMessage` function.
        /// </summary>
        public static async Task<WebApplication> Fly(
            Func<WebhookRequest, Task<IResult>> handleProjectsMessage,
            string[] urls,
            CancellationToken tok
        ) {
            var builder = WebApplication.CreateBuilder();
            builder.Host.UseSerilog(Log.Logger);
            builder.WebHost.UseUrls(urls);

            var app = builder.Build();
            app.MapGet("/ping", () => Results.Json(new {
                data = "pong",
                now = DateTime.UtcNow,
            }));
            app.MapPost("/projects",
                async (
                    HttpRequest request,
                    [FromHeader(Name = "X-XL-Webhook-Signature")] string sig,
                    [FromHeader(Name = "Date")] DateTimeOffset date
                ) => {
                    Log.Information("Headers: {H}", request.Headers);
                    using var r = new StreamReader(request.Body);
                    var body = await r.ReadToEndAsync();
                    var req = new WebhookRequest(date, sig, body);
                    return await handleProjectsMessage(req);
                });

            await app.StartAsync(tok);
            return app;
        }
    }
}
