namespace Webhooks.GraphQL {
    static class Queries {
        const string ProjectsNodeSelection = """
          node {
            fromDate
            toDate
            dbId
            code
            description
            createdAt
            modifiedAt
            text
            ownerDbId
            email
            yourReference
            extIdentifier
            external
            billable
            fixedClient
            allowPosting
            timesheetEntry
            accessControl
            assignment
            activity
            extOrder
            contract
            progressDate
            pctCompleted
            overview
            expenseLedger
            fundProject
            invoiceHeader
            invoiceFooter
            totalRevenue
            yearlyRevenue
            contractedRevenue
            totalCost
            yearlyCost
            totalEstimateHours
            yearlyEstimateHours
            budgetCoveragePercent
            mainProjectDbId
            shortInfo
            shortInternalInfo
            xgl {
                dbId
                code
            }
            glObject1 {
                dbId
                code
            }
            glObject2 {
                dbId
                code
            }
            glObject3 {
                dbId
                code
            }
            glObject4 {
                dbId
                code
            }
            glObject5 {
                dbId
                code
            }
          }
        """;
        const string ProjectsSelection = """
        edges {
          cursor
        """
        + ProjectsNodeSelection + "\n"
        + """
        }
        """;
        internal const string ProjectsFullSyncQuery = """
            query ($after: String) {
                projects(first: 10000, after: $after) {
            """
        + ProjectsSelection + "\n"
        + "pageInfo { hasNextPage }\n"
        + "    }\n}";

        internal const string ProjectsLatestChangesSyncQuery = """
            query ($after: String, $since: DateTimeString) {
                projects(first: 10000, after: $after, filter: { modifiedAt_gte: $since }) {
            """
        + ProjectsSelection + "\n"
        + "pageInfo { hasNextPage }\n"
        + "    }\n}";

        internal const string ProjectsSubscription = """
            subscription {
                projects: projectsMutated {
                    edges {
                        syncVersion
            """
            + ProjectsNodeSelection + "\n"
            + """
                    }
                }
            }
            """;

        internal static readonly string ProjectsSubscriptionJson =
            "{ \"query\": \"" + ProjectsSubscription.Replace("\r\n", " ").Replace("\n", " ") + "\" }";

        internal const string WebhookSelection = """
            edges {
                node {
                    dbId
                    description
                    url
                    state { code }
                    serializedPayload
                    lastSuccessfulPost
                    lastFailedPost
                }
            }
            """;

        internal const string RegisterWebhookMutation = """
            mutation ($description: String, $url: String, $serializedPayload: String) {
                addWebhooks(inputs: [
                    {
                        node: {
                            description: $description,
                            url: $url,
                            serializedPayload: $serializedPayload,
                        }
                    }
                ]) {
                    edges {
                        node {
                            dbId
                            description
                            url
                            state { code }
                            serializedPayload
                            lastSuccessfulPost
                            lastFailedPost
                        }
                    }
                }
            }
            """;

        internal const string RemoveWebhookMutation = """
            mutation($dbId: Int64String!) {
              removeWebhooks(dbIds: [$dbId]) {
                numAffected
              }
            }
            """;

        internal const string WebhookStateQuery = """
            query($dbId: ID) {
              webhook(dbId: $dbId) {
                dbId
                state { code }
              }
            }
            """;
    }
}
