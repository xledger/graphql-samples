using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Webhooks.GraphQL {
    static class Queries {
        const string ProjectsSelection = """
    edges {
      cursor
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
          objectKind {
            name
          }
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
    }
    """;
        internal const string ProjectsFullSyncQuery = """
            query ($after: String) {
                projects(first: 1000, after: $after) {
            """
        + ProjectsSelection + "\n"
        + "pageInfo { hasNextPage }\n"
        + "    }\n}";
    }
}
