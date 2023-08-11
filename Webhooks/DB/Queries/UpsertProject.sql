insert into Project(
   xledgerDbId, fromDate, toDate
   code, description, createdAt
   modifiedAt, "text", ownerDbId
   email, yourReference, extIdentifier
   external, billable, fixedClient
   allowPosting, timesheetEntry, accessControl
   assignment, activity, extOrder
   contract, progressDate, pctCompleted
   overview, expenseLedger, fundProject
   invoiceHeader, invoiceFooter, totalRevenue
   yearlyRevenue, contractedRevenue, totalCost
   yearlyCost, totalEstimateHours, yearlyEstimateHours
   budgetCoveragePercent, mainProjectId, shortInfo
   shortInternalInfo, xglId, glObject1Id
   glObject2Id, glObject3Id, glObject4Id
   glObject5Id
)
values (
   @xledgerDbId, @fromDate, @toDate
   @code, @description, @createdAt
   @modifiedAt, @"text", @ownerDbId
   @email, @yourReference, @extIdentifier
   @external, @billable, @fixedClient
   @allowPosting, @timesheetEntry, @accessControl
   @assignment, @activity, @extOrder
   @contract, @progressDate, @pctCompleted
   @overview, @expenseLedger, @fundProject
   @invoiceHeader, @invoiceFooter, @totalRevenue
   @yearlyRevenue, @contractedRevenue, @totalCost
   @yearlyCost, @totalEstimateHours, @yearlyEstimateHours
   @budgetCoveragePercent, @mainProjectId, @shortInfo
   @shortInternalInfo, @xglId, @glObject1Id
   @glObject2Id, @glObject3Id, @glObject4Id
   @glObject5Id
)
on conflict(xledgerDbId)
do update set
   fromDate = excluded.fromDate
  ,toDate = excluded.toDate
  ,code = excluded.code
  ,description = excluded.description
  ,createdAt = excluded.createdAt
  ,modifiedAt = excluded.modifiedAt
  ,"text" = excluded."text"
  ,ownerDbId = excluded.ownerDbId
  ,email = excluded.email
  ,yourReference = excluded.yourReference
  ,extIdentifier = excluded.extIdentifier
  ,external = excluded.external
  ,billable = excluded.billable
  ,fixedClient = excluded.fixedClient
  ,allowPosting = excluded.allowPosting
  ,timesheetEntry = excluded.timesheetEntry
  ,accessControl = excluded.accessControl
  ,assignment = excluded.assignment
  ,activity = excluded.activity
  ,extOrder = excluded.extOrder
  ,contract = excluded.contract
  ,progressDate = excluded.progressDate
  ,pctCompleted = excluded.pctCompleted
  ,overview = excluded.overview
  ,expenseLedger = excluded.expenseLedger
  ,fundProject = excluded.fundProject
  ,invoiceHeader = excluded.invoiceHeader
  ,invoiceFooter = excluded.invoiceFooter
  ,totalRevenue = excluded.totalRevenue
  ,yearlyRevenue = excluded.yearlyRevenue
  ,contractedRevenue = excluded.contractedRevenue
  ,totalCost = excluded.totalCost
  ,yearlyCost = excluded.yearlyCost
  ,totalEstimateHours = excluded.totalEstimateHours
  ,yearlyEstimateHours = excluded.yearlyEstimateHours
  ,budgetCoveragePercent = excluded.budgetCoveragePercent
  ,mainProjectId = excluded.mainProjectId
  ,shortInfo = excluded.shortInfo
  ,shortInternalInfo = excluded.shortInternalInfo
  ,xglId = excluded.xglId
  ,glObject1Id = excluded.glObject1Id
  ,glObject2Id = excluded.glObject2Id
  ,glObject3Id = excluded.glObject3Id
  ,glObject4Id = excluded.glObject4Id
  ,glObject5Id = excluded.glObject5Id