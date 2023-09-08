using Localtunnel.Endpoints.Http;
using Localtunnel.Endpoints;
using Localtunnel.Handlers.Kestrel;
using Localtunnel.Processors;
using Localtunnel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Polly.Retry;
using Polly;
using Serilog;
using System.Security.Cryptography;
using System.Text;
using Webhooks.DB.Models;
using Webhooks.DB;
using Webhooks.Utils;

namespace Webhooks.GraphQL {
    using WebApplication = Microsoft.AspNetCore.Builder.WebApplication;
    using WebhookRequest = WebServer.WebhookRequest;

    /// <summary>
    /// Sync projects by following this strategy:
    ///
    /// 1. Sync all projects by enumerating all pages of the project connection. No filter. Keep track of when the syncing began.
    /// 2. Start the webhook.
    /// 3. Repeat step one, except filter by dateModified > SYNC_START - DATE.
    /// </summary>
    class ProjectSyncer {
        static readonly Lazy<string> UpsertProjectQuery = new(() => {
            var asm = typeof(ProjectSyncer).Assembly;
            var stream = asm.GetManifestResourceStream("Webhooks.DB.Queries.UpsertProject.sql");
            using var sr = new StreamReader(stream!);
            return sr.ReadToEnd();
        });

        static readonly IReadOnlyDictionary<string, FieldMapping> FieldMappingByGraphQLFieldName;
        static readonly IReadOnlyList<string> ObjectValueReferenceFields = new[] {
            "xgl", "glObject1", "glObject2", "glObject3", "glObject4", "glObject5"
        };

        static readonly IReadOnlyList<string> ProjectReferenceFields = new[] {
            "mainProject"
        };

        static ProjectSyncer () {
            var m = new Dictionary<string, FieldMapping>(50);

            var stringFields = new[] {
                "code", "description", "text",
                "email", "yourReference", "extIdentifier",
                "extOrder", "contract", "overview",
                "invoiceHeader", "invoiceFooter", "shortInfo",
                "shortInternalInfo"
            };

            foreach (var f in stringFields) {
                m[f] = FieldMapping.String;
            }

            var dateFields = new[] { "fromDate", "toDate" };
            foreach (var f in dateFields) {
                m[f] = FieldMapping.Date;
            }

            var moneyFields = new[] { "totalRevenue", "yearlyRevenue", "contractedRevenue", "totalCost", "yearlyCost" };
            foreach (var f in moneyFields) {
                m[f] = FieldMapping.MoneyString;
            }

            var floatFields = new[] { "pctCompleted", "totalEstimateHours", "yearlyEstimateHours", "budgetCoveragePercent" };
            foreach (var f in floatFields) {
                m[f] = FieldMapping.Float;
            }

            var boolFields = new[] {
                "external", "billable", "fixedClient", "allowPosting", "timesheetEntry",
                "accessControl", "assignment", "activity", "expenseLedger", "fundProject"
            };
            foreach (var f in boolFields) {
                m[f] = FieldMapping.Boolean;
            }

            var dateTimeFields = new[] { "createdAt", "modifiedAt", "progressDate" };
            foreach (var f in dateTimeFields) {
                m[f] = FieldMapping.CET_DateTime;
            }

            FieldMappingByGraphQLFieldName = m;
        }

        internal enum ProjectSyncerState {
            NotStarted,
            Initializing,
            CursorSyncing,
            IncrementallySyncing,
            WebhookListening
        }

        Database Db { get; }
        CancellationTokenSource InternalCts { get; }
        CancellationToken LinkedCancelTok { get; }
        GraphQLClient GraphQlClient { get; }
        AsyncRetryPolicy GraphQLRetryPolicy { get; }
        string[] Urls { get; }
        bool UseTunnel { get; }

        internal ProjectSyncerState State { get; private set; }

        WebApplication? Server { get; set; }

        internal ProjectSyncer(Database db, GraphQLClient graphQlClient, Config config, CancellationToken tok) {
            Db = db;
            GraphQlClient = graphQlClient;
            InternalCts = new();
            Urls = config.Urls;
            UseTunnel = config.UseTunnel;
            GraphQLRetryPolicy =
                Policy.Handle<Exception>(ex => !InternalCts.IsCancellationRequested)
                .WaitAndRetryAsync(
                    retryCount: 18, // More than enough to survive a large Xledger maintenance window
                    sleepDurationProvider: (i, ex, _ctx) => {
                        if (ex is XledgerGraphQLException xlEx && xlEx.ErrorKind != XledgerGraphQLErrorKind.Other) {
                            return xlEx.ErrorKind switch {
                                XledgerGraphQLErrorKind.ShortRateLimitReached => TimeSpan.FromSeconds(5),
                                XledgerGraphQLErrorKind.InsufficientCredits => TimeSpan.FromMinutes(20), // @TODO: Use extensions { resetAt } to get a precise delay
                                _ => throw new ArgumentOutOfRangeException("xlEx.ErrorKind"), // Not possible
                            };
                        } else if (i <= 3) {
                            return TimeSpan.FromSeconds(5 * i);
                        } else if (i <= 6) {
                            return TimeSpan.FromMinutes(i);
                        } else {
                            return TimeSpan.FromHours(2);
                        }
                    },
                    onRetryAsync: (ex, _i, delay, _ctx) => {
                        Log.Warning("ProjectSyncer GraphQL API exception. \"{m}\" Waiting {delay} to retry",
                            ex.Message,
                            delay);
                        return Task.FromResult(0);
                    });

            State = ProjectSyncerState.NotStarted;
            LinkedCancelTok = CancellationTokenSource.CreateLinkedTokenSource(InternalCts.Token, tok).Token;
            Console.CancelKeyPress += delegate {
                InternalCts.Cancel();
            };
        }

        internal async Task Run() {
            try {
                State = ProjectSyncerState.Initializing;
                Log.Information("ProjectSyncer: Initializing");

                if (UseTunnel) {
                    Log.Warning("Using a tunnel to make localhost available publicly transmits your data through a third-party service that Xledger does not control and is not responsible for. Don't send anything sensitive through this tunnel.");
                    while (true) {
                        Log.Warning("Are you certain you want to continue? [y/N]");
                        var line = Console.ReadLine();
                        if (string.IsNullOrEmpty(line) || line is "n" or "N") {
                            InternalCts.Cancel();
                            throw new DisallowTunnelException();
                        } else if (line is "y" or "Y") {
                            break;
                        }
                    }
                }

                var syncStatus = await SyncStatus.FetchAsync(Db, "Project");
                if (syncStatus is not null && syncStatus.Type == SyncStatus.SyncType.WebhookListening) {
                    await IncrementalSync(syncStatus);
                } else {
                    await FullCursorSync(syncStatus);
                }
            } catch (OperationCanceledException) {
                Log.Information("ProjectSyncer: Cancelled. Shutting down.");
            } catch (DisallowTunnelException) {
                Log.Information("ProjectSyncer: Rejected use of the tunnel. Shutting down.");
            } catch (Exception ex) {
                Log.Fatal("ProjectSyncer: Unexpected exception. Shutting down.\n{ex}", ex);
                throw;
            } finally {
                if (Server is not null) {
                    await Server.StopAsync(CancellationToken.None);
                }
                InternalCts.Cancel();
            }
        }

        /// <summary>
        /// See Step 1 of the class documentation.
        /// </summary>
        async Task FullCursorSync(SyncStatus? initialSyncStatus) {
            State = ProjectSyncerState.CursorSyncing;
            var syncStatus = initialSyncStatus;
            var nextCursor = initialSyncStatus?.SyncValue;

            if (syncStatus is null) {
                var now = DateTime.UtcNow;
                syncStatus = new SyncStatus("Project", SyncStatus.SyncType.QuerySyncing, nextCursor, now, now, null);
                await syncStatus.SaveAsync(Db);
            }

            var shouldContinue = true;
            while (shouldContinue) {
                Log.Verbose("Continuing after cursor {c}", nextCursor);
                var result = await GraphQLRetryPolicy.ExecuteAsync((tok) =>
                    GraphQlClient.QueryAsync(
                        Queries.ProjectsFullSyncQuery,
                        nextCursor is null
                            ? null
                            : new Dictionary<string, object?> { ["after"] = nextCursor },
                        tok),
                    LinkedCancelTok);

                var processResult = await ProcessQueryResults(result, syncStatus);
                shouldContinue = processResult.ShouldContinue;
                nextCursor = processResult.NextCursor;
                if (processResult.ShouldContinue) {
                    await Task.Delay(100, LinkedCancelTok);
                }
            }
            Log.Information("Full cursor sync completed.");

            await IncrementalSync(syncStatus);
        }

        public async Task IncrementalSync(SyncStatus syncStatus) {
            // 1. Start webhook.
            State = ProjectSyncerState.IncrementallySyncing;
            await StartWebhookAsync(syncStatus);

            var syncModifiedAtSince =
                syncStatus.Type == SyncStatus.SyncType.WebhookListening
                ? syncStatus.AsOfTime
                : syncStatus.StartTime;

            // To compensate for the possibility that this computer's clock is out of sync
            syncModifiedAtSince = syncModifiedAtSince.Subtract(TimeSpan.FromMinutes(15));
            Log.Debug("Fetching updates since {Since}.", syncModifiedAtSince);

            syncModifiedAtSince = Dates.UtcToCET(syncModifiedAtSince); // The Xledger modifiedAt datetimes are stored in CET

            // 2. Do one last cursor sync.
            string? nextCursor = null;
            var shouldContinue = true;
            while (shouldContinue) {
                Log.Verbose("Continuing after cursor {c}", nextCursor);
                var variables = new Dictionary<string, object?> {
                    ["since"] = syncModifiedAtSince.ToString(Dates.ISO_8601_DateTimeFormat)
                };
                if (nextCursor is not null) {
                    variables["after"] = nextCursor;
                }
                var result = await GraphQLRetryPolicy.ExecuteAsync((tok) =>
                    GraphQlClient.QueryAsync(
                        Queries.ProjectsLatestChangesSyncQuery,
                        variables,
                        tok),
                    LinkedCancelTok);

                var processResult = await ProcessQueryResults(result, syncStatus);
                shouldContinue = processResult.ShouldContinue;
                nextCursor = processResult.NextCursor;
                if (processResult.ShouldContinue) {
                    await Task.Delay(100, LinkedCancelTok);
                }
            }

            Log.Information("Fetching latest changes");
            syncStatus.Type = SyncStatus.SyncType.WebhookListening;
            var now = DateTime.UtcNow;
            syncStatus.StartTime = now;
            syncStatus.AsOfTime = now;
            State = ProjectSyncerState.IncrementallySyncing;
            await syncStatus.SaveAsync(Db, LinkedCancelTok);

            // 3. Keep running until we are cancelled.
            await Task.Delay(Timeout.InfiniteTimeSpan, LinkedCancelTok);
        }

        async Task<Uri> StartTunnel(Uri localAddress) {
            if (UseTunnel) {
                var loggerFactory = new LoggerFactory().AddSerilog(Log.Logger);

                using var client = new LocaltunnelClient(loggerFactory);
                var pipeline = new HttpRequestProcessingPipelineBuilder().Build();
                ITunnelEndpointFactory endpointFactory;
                if (localAddress.Scheme == "http") {
                    endpointFactory = new HttpTunnelEndpointFactory(localAddress.Host, localAddress.Port);
                } else if (localAddress.Scheme == "https") {
                    endpointFactory = new HttpsTunnelEndpointFactory(localAddress.Host, localAddress.Port);
                } else {
                    throw new Exception($"Unexpected local scheme. Expected http, https. Got {localAddress.Scheme}.");
                }
                var handler = new KestrelTunnelConnectionHandler(pipeline, endpointFactory);

                var tunnel = await client.OpenAsync(handler, cancellationToken: LinkedCancelTok);
                await tunnel.StartAsync(1, LinkedCancelTok);

                var t = tunnel.Information;
                Log.Information("Opened tunnel: {Id} {MaxC} {Port} {BufSize} {Url}",
                    t.Id, t.MaximumConnections, t.Port, t.ReceiveBufferSize, t.Url);

                return t.Url;
            } else {
                Log.Information("No tunnel. {Url} must be publicly accessible.", localAddress);
                return localAddress;
            }
        }

        async Task StartWebhookAsync(SyncStatus syncStatus) {
            // 1. Start listener.
            Server = await WebServer.Fly(PostProjectHandler(syncStatus), Urls, LinkedCancelTok);

            // 2. Start tunnel.
            // This step is only needed for local development. In a production
            // environment, the webhook event listener would be deployed to a
            // public URL and so no tunnel would be necessary or desirable.
            var listenAddress = await StartTunnel(new Uri(Urls[0]));

            // Note on steps 3 and 4:
            // Deleting the existing webhook is an artifact of the use of the
            // tunnel to workaround the lack of a developer's computer having a
            // publicly accessible URL. In a normal deployment with a static URL,
            // steps 3 and 4 would be replaced with a check that the webhook is
            // not PAUSED OR FAULTED.

            // 3. If existing webhook, delete it.
            if (syncStatus.WebhookXledgerDbId is int webhookDbId) {
                Log.Information("Deleting previous webhook {DbId}...", webhookDbId);
                var result = await GraphQLRetryPolicy.ExecuteAsync((tok) =>
                    GraphQlClient.QueryAsync(
                        Queries.RemoveWebhookMutation,
                        new Dictionary<string, object?> { ["dbId"] = webhookDbId },
                        tok),
                    LinkedCancelTok);
            }

            // 4. Start new webhook.
            {
                Log.Information("Starting webhook...");
                var result = await GraphQLRetryPolicy.ExecuteAsync((tok) =>
                    GraphQlClient.QueryAsync(
                        Queries.RegisterWebhookMutation,
                        new Dictionary<string, object?> {
                            ["description"] = "All project events",
                            ["url"] = $"{listenAddress}projects",
                            ["serializedPayload"] = Queries.ProjectsSubscriptionJson,
                        },
                        tok),
                    LinkedCancelTok);
                webhookDbId = result.SelectToken("$.data.addWebhooks.edges[0].node.dbId")!.ToObject<int>();
                Log.Information("Webhook {DbId} created: {Response}", webhookDbId, result.ToString());
                syncStatus.WebhookXledgerDbId = webhookDbId;
                await syncStatus.SaveAsync(Db, LinkedCancelTok);
            }

            // 5. Poll until new webhook transitions to RUNNING.
            Log.Information("Waiting until webhook {DbId} transitions to RUNNING...", webhookDbId);
            await PollWebhookUntilStateOrFaulted(webhookDbId, TimeSpan.FromSeconds(10),
                "PAUSED", "RECOVERING", "RUNNING");

            // 6. Continually update sync status's AsOfTime.
            _ = Task.Run(async () => {
                try {
                    while (true) {
                        var syncStatus = await SyncStatus.FetchAsync(Db, "Project");
                        syncStatus!.AsOfTime = DateTime.UtcNow;
                        await syncStatus.SaveAsync(Db, LinkedCancelTok);
                        await Task.Delay(TimeSpan.FromMinutes(1));
                    }
                } catch (OperationCanceledException) {
                } catch (Exception ex) {
                    Log.Fatal("Error saving syncStatus AsOfTime: {ex}", ex);
                    InternalCts.Cancel();
                }
            });

            // 7. Poll once per day to ensure webhook isn't FAULTED.
            _ = Task.Run(async () =>
                await PollWebhookUntilStateOrFaulted(webhookDbId, TimeSpan.FromHours(23)));

            Log.Information("Webhook {DbId} running.", webhookDbId);
        }

        async Task RollbackAsOf(TimeSpan duration) {
            try {
                var syncStatus = await SyncStatus.FetchAsync(Db, "Project");
                syncStatus!.AsOfTime = DateTime.UtcNow - duration - TimeSpan.FromMinutes(1);
                await syncStatus.SaveAsync(Db, CancellationToken.None);
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                Log.Fatal("Error saving syncStatus AsOfTime: {ex}", ex);
            }
        }

        async Task PollWebhookUntilStateOrFaulted(int webhookDbId, TimeSpan delay, params string[] states) {
            var pollUntilStates = new HashSet<string>(states);
            while (true) {
                var result = await GraphQLRetryPolicy.ExecuteAsync((tok) =>
                    GraphQlClient.QueryAsync(
                        Queries.WebhookStateQuery,
                        new Dictionary<string, object?> { ["dbId"] = webhookDbId },
                        tok),
                    LinkedCancelTok);
                var state = result.SelectToken("$.data.webhook.state.code")!.ToObject<string>();
                if (state is null or "FAULTED") {
                    if (state is null) {
                        Log.Fatal("Could not retrieve state for webhook {DbId}.", webhookDbId);
                    } else {
                        Log.Fatal("Webhook {DbId} faulted. Cannot recover without intervention.", webhookDbId);
                    }
                    // Cancel first so that everything else closes up and doesn't overwrite rollback AsOf.
                    InternalCts.Cancel();
                    await RollbackAsOf(delay);
                    throw new Exception("Webhook faulted.");
                } else if (pollUntilStates.Contains(state)) {
                    break;
                }

                await Task.Delay(delay, LinkedCancelTok);
            }
        }

        /// <summary>
        /// For a signature to be valid, an HMAC signature generated from
        /// $"{seconds since Unix epoch}.{request body}" must equal the `hmac`
        /// value in sig and the seconds since Unix epoch must
        /// be within 15 minutes of now.
        /// </summary>
        /// <param name="sig">The X-XL-Webhook-Signature header value</param>
        /// <param name="body">The request body to verify the signature against</param>
        /// <returns>true if the signature is valid for the given body and recent, false otherwise</returns>
        bool ValidSignature(WebhookRequest rq) {
            bool equal = false, recent = false;

            try {
                // Compare calculated signature with signature in header.
                var providedBytes = WebEncoders.Base64UrlDecode(rq.Signature);

                // Calculate signature.
                var apiToken = GraphQlClient.Token;
                var apiTokenBytes = WebEncoders.Base64UrlDecode(apiToken);
                using var hmacAlgo = new HMACSHA256(apiTokenBytes);
                var bytes = Encoding.UTF8.GetBytes($"{rq.Date.ToUnixTimeSeconds()}.{rq.Body}");
                var calculatedBytes = hmacAlgo.ComputeHash(bytes);

                // Compare every byte to avoid timing attacks.
                equal = CryptographicOperations.FixedTimeEquals(providedBytes, calculatedBytes);

                // Ensure timestamp in header is within 15 minutes of now.
                var now = DateTimeOffset.UtcNow;
                recent = (now - rq.Date).Duration() <= TimeSpan.FromMinutes(15);
            } catch (Exception) {
            }

            return equal && recent;
        }

        Func<WebhookRequest, Task<IResult>> PostProjectHandler(SyncStatus syncStatus) {
            return async (WebhookRequest rq) => {
                try {
                    if (!ValidSignature(rq)) {
                        Log.Warning("Hacker thwarted (Bad request signature).");
                        return Results.Unauthorized();
                    }
                    var jobj = Json.Deserialize<JObject>(rq.Body)!;
                    var syncVersion = jobj.SelectTokens("$.data.projects.edges[*].syncVersion")
                        .Select(t => t.ToObject<int>())
                        .Max();
                    var batchRes = await ProcessQueryResults(jobj, syncStatus);
                    Log.Information("{TableName} sync up-to-date through {AsOf} (sync version {SyncVersion})",
                        syncStatus.TableName, syncStatus.AsOfTime, syncVersion);
                    return Results.Ok();
                } catch (Exception ex) {
                    Log.Error("Unexpected exception while processing project events. {Ex}", ex);
                    return Results.BadRequest();
                }
            };
        }

        record BatchProcessResult(bool ShouldContinue, string? NextCursor);

        async Task<int> UpsertProjectAsync(SqliteCommand cmd, int xledgerDbId) {
            try {
                cmd.Parameters.AddWithValue("xledgerDbId", xledgerDbId);
                cmd.CommandText = """
                    insert into Project(xledgerDbId)
                    values (@xledgerDbId)
                    on conflict do nothing
                    returning id
                    """;
                {
                    using var rdr = await cmd.ExecuteReaderAsync(LinkedCancelTok);

                    if (rdr.Read()) {
                        return Convert.ToInt32(rdr.GetValue(0));
                    }
                }

                // 'returning' above only returns a value if a record was inserted, updated, or deleted.
                // We only insert, and don't update on conflict, so we may not have got a row (and thus
                // returned) above.
                cmd.CommandText = "select id from Project where xledgerDbId = @xledgerDbId";
                {
                    using var rdr = await cmd.ExecuteReaderAsync(LinkedCancelTok);
                    if (rdr.Read()) {
                        return Convert.ToInt32(rdr.GetValue(0));
                    }
                }
            } finally {
                cmd.Parameters.Clear();
            }
            throw new Exception("Sqlite is broken!"); // Should not be possible
        }

        async Task<int> UpsertObjectValueAsync(SqliteCommand cmd, int xledgerDbId, string? code) {
            try {
                cmd.Parameters.AddWithValue("xledgerDbId", xledgerDbId);
                cmd.Parameters.AddWithValue2("code", code);
                cmd.CommandText = """
                    insert into ObjectValue(xledgerDbId, code)
                    values (@xledgerDbId, @code)
                    on conflict do update
                    set code = excluded.code
                    returning id
                    """;
                {
                    using var rdr = await cmd.ExecuteReaderAsync(LinkedCancelTok);

                    // This should always return a value, since we always either insert or update the code.
                    if (rdr.Read()) {
                        return Convert.ToInt32(rdr.GetValue(0));
                    }
                }

            } finally {
                cmd.Parameters.Clear();
            }
            throw new Exception("Sqlite is broken!"); // Should not be possible
        }

        async Task<BatchProcessResult> ProcessQueryResults(JObject result, SyncStatus syncStatus) {
            Log.Debug("Updated {TableName}: {DbIds}",
                syncStatus.TableName,
                result.SelectTokens("$.data.projects.edges[*].node.dbId")
                    .Select(t => t.ToObject<int>()));
            var shouldContinue =
                result.SelectToken("$.data.projects.pageInfo.hasNextPage")
                ?.ToObject<bool>() ?? false;

            using var conn = await Db.GetOpenConnection(LinkedCancelTok);
            using var tx = await conn.BeginTransactionAsync(LinkedCancelTok);
            string? cursor = await UpsertProjects(conn, result);
            syncStatus.SyncValue = cursor;
            syncStatus.AsOfTime = DateTime.UtcNow;
            await syncStatus.SaveAsync(conn, LinkedCancelTok);
            tx.Commit();
            return new BatchProcessResult(shouldContinue, cursor);
        }

        async Task<string?> UpsertProjects(SqliteConnection conn, JObject result) {
            string? cursor = null;

            using (var cmd = conn.CreateCommand()) {
                var edges = result.SelectTokens("$.data.projects.edges[*]").ToList();

                var projectsIdsByXledgerDbId = new Dictionary<int, int>();
                var objectValueIdsByXledgerDbId = new Dictionary<int, int>();

                // Step 1: Upsert all references
                foreach (var edge in edges) {
                    if (edge["node"] is not JObject node) {
                        throw new Exception("Unexpected response shape");
                    }

                    foreach (var projectRefField in ProjectReferenceFields) {
                        if (node["mainProject"] is JObject mainProject) {
                            var xlDbId = mainProject["dbId"]!.ToObject<int>();
                            if (xlDbId != 0 && !projectsIdsByXledgerDbId.TryGetValue(xlDbId, out var id)) {
                                var projectId = await UpsertProjectAsync(cmd, xlDbId);
                                projectsIdsByXledgerDbId.Add(xlDbId, projectId);
                            }
                        }
                    }

                    foreach (var objectValueFieldName in ObjectValueReferenceFields) {
                        if (node[objectValueFieldName] is JObject objectValue) {
                            var xlDbId = objectValue["dbId"]!.ToObject<int>();
                            if (xlDbId == 0) {
                                continue;
                            }
                            var code = objectValue["code"]?.ToObject<string>();
                            if (!objectValueIdsByXledgerDbId.TryGetValue(xlDbId, out var id)) {
                                var objectValueId = await UpsertObjectValueAsync(cmd, xlDbId, code);
                                objectValueIdsByXledgerDbId.Add(xlDbId, id);
                            }
                        }
                    }
                }

                // Step 2: Prepare upsert project command:
                cmd.CommandText = UpsertProjectQuery.Value;
                cmd.Parameters.Add(new SqliteParameter("xledgerDbId", SqliteType.Integer));
                foreach (var f in ProjectReferenceFields.Concat(ObjectValueReferenceFields)) {
                    cmd.Parameters.Add(new SqliteParameter($"{f}Id", SqliteType.Integer));
                }
                foreach ((var f, var mapping) in FieldMappingByGraphQLFieldName) {
                    cmd.Parameters.Add(new SqliteParameter(f, mapping.SqliteType));
                }

                // Step 3: Insert Projects
                foreach (var edge in edges) {
                    cursor = edge["cursor"]?.ToObject<string>();
                    if (edge["node"] is not JObject node) {
                        throw new Exception("Unexpected response shape");
                    }

                    cmd.Parameters["xledgerDbId"].Value = node["dbId"]!.ToObject<int>();

                    // Step 3.1 Set parameters with references
                    foreach (var projectRefField in ProjectReferenceFields) {
                        var sqlParam = cmd.Parameters[$"{projectRefField}Id"];

                        if (node[projectRefField] is JObject mainProject) {
                            var xlDbId = mainProject["dbId"]!.ToObject<int>();
                            var id = projectsIdsByXledgerDbId[xlDbId];
                            sqlParam.Value = id != 0
                                ? id
                                : Convert.DBNull;
                        } else {
                            sqlParam.Value = Convert.DBNull;
                        }
                    }
                    foreach (var objectValueFieldName in ObjectValueReferenceFields) {
                        var sqlParam = cmd.Parameters[$"{objectValueFieldName}Id"];
                        if (node[objectValueFieldName] is JObject objectValue) {
                            var xlDbId = objectValue["dbId"]!.ToObject<int>();
                            var id = objectValueIdsByXledgerDbId[xlDbId];
                            sqlParam.Value = id != 0
                                ? id
                                : Convert.DBNull;
                        } else {
                            sqlParam.Value = Convert.DBNull;
                        }
                    }

                    // Step 3.2 Set normal fields
                    foreach ((var f, var mapping) in FieldMappingByGraphQLFieldName) {
                        var v = mapping.ReadValueFromGraphQLNode(node, f);
                        if (v is null) {
                            cmd.Parameters[f].Value = Convert.DBNull;
                        } else {
                            mapping.SetSqlParameter(cmd.Parameters[f], v);
                        }
                    }

                    // Step 3.3 Insert the project
                    await cmd.ExecuteNonQueryAsync(LinkedCancelTok);
                }
            }

            return cursor;
        }
    }

    class DisallowTunnelException : Exception { }
}
