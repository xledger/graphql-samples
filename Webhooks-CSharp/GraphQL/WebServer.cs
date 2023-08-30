using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace Webhooks.GraphQL {
    static class WebServer {
        internal static readonly int PORT = 9920;
        internal static readonly int SECURE_PORT = PORT + 1;

        public record Addresses(Uri Http, Uri Https);

        /// <summary>
        /// Starts a Kestrel web server that listens until cancellation and 
        /// handles all project POSTs with the `handleProjectsMessage` function.
        /// </summary>
        public static Task Fly(
            Func<string, string, Task<IResult>> handleProjectsMessage,
            out Addresses addrs,
            CancellationToken tok
        ) {
            addrs = new Addresses(
                new Uri($"http://localhost:{PORT}/"),
                new Uri($"https://localhost:{SECURE_PORT}"));

            var builder = WebApplication.CreateBuilder();
            builder.Host.UseSerilog(Log.Logger);
            builder.WebHost.UseUrls(addrs.Http.AbsoluteUri, addrs.Https.AbsoluteUri);

            var app = builder.Build();
            app.MapGet("/ping", () => Results.Json(new {
                data = "pong",
                now = DateTime.UtcNow,
            }));
            app.MapPost("/projects",
                async (
                    HttpRequest request,
                    [FromHeader(Name = "X-XL-Webhook-Signature")] string sig
                ) => {
                    using var r = new StreamReader(request.Body);
                    var body = await r.ReadToEndAsync();
                    return await handleProjectsMessage(sig, body);
                });

            return Task.Run(() => app.Run(), tok);
        }
    }
}
