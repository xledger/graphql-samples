using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Serilog;
using Webhooks.DB;
using Webhooks.DB.Models;
using Polly;
using Polly.Retry;
using Newtonsoft.Json.Linq;
using Microsoft.Data.Sqlite;

namespace Webhooks.GraphQL {
    /// <summary>
    /// Sync projects by following this strategy:
    /// 
    /// 1. Sync all projects by enumerating all pages of the project connection. No filter. Keep track of when the syncing began.
    /// 2. Start the webhook.
    /// 3. Repeat step one, except filter by dateModified > SYNC_START - DATE. 
    /// </summary>
    class ProjectSyncer {
        static Lazy<string> UpsertProjectQuery = new(() => {
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

            FieldMappingByGraphQLFieldName = m;
        }

        internal enum ProjectSyncerState {
            NotStarted,
            Initializing,
            FullCursorSyncing,
            RecentChangesCursorSyncing,
        }

        Database Db { get; }
        CancellationToken CancelTok { get; }
        CancellationTokenSource InternalCts { get; }
        CancellationToken LinkedCancelTok { get; }
        GraphQLClient GraphQlClient { get; }
        AsyncRetryPolicy GraphQLRetryPolicy { get; }

        internal ProjectSyncerState State { get; private set; }

        string? nextCursor;

        internal ProjectSyncer(Database db, GraphQLClient graphQlClient, CancellationToken tok) {
            Db = db;
            GraphQlClient = graphQlClient;
            GraphQLRetryPolicy =
                Policy.Handle<Exception>()
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
                    onRetryAsync: async (ex, _i, delay, _ctx) => {
                        Log.Warning("ProjectSyncer GraphQL API exception. \"{m}\" Waiting {delay} to retry", 
                            ex.Message, 
                            delay);
                    });

            CancelTok = tok;
            State = ProjectSyncerState.NotStarted;
            InternalCts = new();
            LinkedCancelTok = CancellationTokenSource.CreateLinkedTokenSource(InternalCts.Token, tok).Token;
        }

        internal async Task Run() {
            try {
                State = ProjectSyncerState.Initializing;
                Log.Information("ProjectSyncer: Initializing");
                await Initialize();
            } catch (OperationCanceledException) {
                Log.Information("ProjectSyncer: Cancelled. Shutting down.");
            } catch (Exception ex) {
                Log.Fatal("ProjectSyncer: Unexpected exception. Shutting down.\n{ex}", ex);
                throw;
            } finally {
                InternalCts.Cancel();
            }
        }

        async Task Initialize() {
            var syncStatus = await SyncStatus.Fetch(Db, "Project");
            if (syncStatus is null || syncStatus.Type == SyncStatus.SyncType.FullCursorSyncing) {
                nextCursor = syncStatus?.SyncValue;
                await FullCursorSync(syncStatus);
                return;
            }
            if (syncStatus is not null) {

            }
        }

        /// <summary>
        /// See Step 1 of the class documentation.
        /// </summary>
        async Task FullCursorSync(SyncStatus? initialSyncStatus) {
            var syncStatus = initialSyncStatus;

            if (syncStatus is null) {
                var now = DateTime.UtcNow;
                syncStatus = new SyncStatus("Project", SyncStatus.SyncType.FullCursorSyncing, nextCursor, now, now);
                await syncStatus.SaveAsync(Db);
            }

            while (true) {
                var result = await GraphQLRetryPolicy.ExecuteAsync(() =>
                    GraphQlClient.QueryAsync(
                        Queries.ProjectsFullSyncQuery,
                        nextCursor is null
                            ? null
                            : new Dictionary<string, object?> { ["cursor"] = nextCursor },
                        LinkedCancelTok));

                var processResult = await ProcessQueryResults(result, syncStatus);

                if (processResult.ShouldContinue) {
                    await Task.Delay(100, LinkedCancelTok);
                } else {
                    break;
                }
            }
        }

        record BatchProcessResult(bool ShouldContinue);

        async Task<int> UpsertProjectAsync(SqliteCommand cmd, int xledgerDbId) {
            try {
                cmd.Parameters.AddWithValue("xledgerDbId", xledgerDbId);
                cmd.CommandText = "insert into Project(xledgerDbId) values (@xledgerDbId) on conflict do nothing returning id";
                {
                    using var rdr = await cmd.ExecuteReaderAsync(LinkedCancelTok);
                    
                    if (rdr.Read()) {
                        return Convert.ToInt32(rdr.GetValue(0));
                    }
                }

                // 'returning' above only returns a value if a record was inserted, updated, or deleted.
                // We only insert, and don't update on conflict, so execution can flow here.
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
                cmd.Parameters.AddWithValue("code", code);
                cmd.CommandText = @"insert into ObjectValue(xledgerDbId, code)
values (@xledgerDbId, @code) 
on conflict do update
set code = excluded.code
returning id";
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
            var shouldContinue = 
                result.SelectToken("$.data.projects.pageInfo.hasNextPage")
                ?.ToObject<bool>() ?? false;

            using var conn = await Db.GetOpenConnection(LinkedCancelTok);
            using var tx = await conn.BeginTransactionAsync(LinkedCancelTok);
            string? cursor = null;

            using (var cmd = conn.CreateCommand()) {
                cmd.CommandText = UpsertProjectQuery.Value;
                var edges = result.SelectTokens("$.data.projects.edges[*]").ToList();

                var projectsIdsByXledgerDbId = new Dictionary<int, int>();
                var objectValueIdsByXledgerDbId = new Dictionary<int, int>();

                // Step 1: Upsert all references
                foreach (var edge in edges) {
                    if (edge["node"] is not JObject node) {
                        throw new Exception("Unexpected response shape");
                    }

                    foreach (var projectRefField in ProjectReferenceFields) {
                        if (node.TryGetValue("mainProject", out var mainProject)
                        && mainProject is not null) {
                            var xlDbId = mainProject["dbId"]!.ToObject<int>();
                            if (!projectsIdsByXledgerDbId.TryGetValue(xlDbId, out var id)) {
                                var projectId = await UpsertProjectAsync(cmd, xlDbId);
                                projectsIdsByXledgerDbId.Add(xlDbId, projectId);
                            }
                        }
                    }

                    foreach (var objectValueFieldName in ObjectValueReferenceFields) {
                        if (node.TryGetValue(objectValueFieldName, out var objectValue)
                            && objectValue is not null) {
                            var xlDbId = objectValue["dbId"]!.ToObject<int>();
                            var code = objectValue["code"]?.ToObject<string>();
                            if (!objectValueIdsByXledgerDbId.TryGetValue(xlDbId, out var id)) {
                                var objectValueId = await UpsertObjectValueAsync(cmd, xlDbId, code);
                                objectValueIdsByXledgerDbId.Add(xlDbId, id);
                            }
                        }
                    }
                }

                // Step 2: Create sql parameters to hold values
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

                    // Step 3.1 Set parameters with references
                    foreach (var projectRefField in ProjectReferenceFields) {
                        if (node.TryGetValue(projectRefField, out var mainProject)
                            && mainProject is not null) {
                            var xlDbId = mainProject["dbId"]!.ToObject<int>();
                            var id = projectsIdsByXledgerDbId[xlDbId];
                            cmd.Parameters[$"{projectRefField}Id"].Value = id;
                        } else {
                            cmd.Parameters[$"{projectRefField}Id"].Value = null;
                        }
                    }
                    foreach (var objectValueFieldName in ObjectValueReferenceFields) {
                        if (node.TryGetValue(objectValueFieldName, out var objectValue)
                            && objectValue is not null) {
                            var xlDbId = objectValue["dbId"]!.ToObject<int>();
                            var id = objectValueIdsByXledgerDbId[xlDbId];
                            cmd.Parameters[$"{objectValueFieldName}Id"].Value = id;
                        } else {
                            cmd.Parameters[$"{objectValueFieldName}Id"].Value = null;
                        }
                    }

                    // Step 3.2 Set normal fields
                    foreach ((var f, var mapping) in FieldMappingByGraphQLFieldName) {
                        var v = mapping.ReadValueFromGraphQLNode(node, f);
                        if (v is null) {
                            cmd.Parameters[f].Value = null;
                        } else {
                            mapping.SetSqlParameter(cmd.Parameters[f], v);
                        }
                    }

                    // Step 3.3 Insert the project
                    await cmd.ExecuteNonQueryAsync(LinkedCancelTok);
                }
            }
            nextCursor = cursor;
            syncStatus.SyncValue = cursor;
            syncStatus.AsOfTime = DateTime.UtcNow;
            await syncStatus.SaveAsync(conn, LinkedCancelTok);
            tx.Commit();
            return new BatchProcessResult(shouldContinue);
        }
    }
}
