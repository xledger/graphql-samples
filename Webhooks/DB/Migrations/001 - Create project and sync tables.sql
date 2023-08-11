/* For keeping track of syncronization status, so we can survive program restarts. */
create table SyncStatus(
    tableName text primary key
   ,syncType text not null
   ,syncValue text
   ,startTime real not null
   ,asOfTime real not null
);

/* Since we will encounter references from Projects to ObjectValues, we keep track of these too. */
create table ObjectValue(
    id integer primary key
   ,xledgerDbId integer unique
   ,code text
   ,description text
);

/* 
# Column data type names

    These may be confusing if you don't understand this about Sqlite:

    https://www.sqlite.org/draft/datatype3.html#determination_of_column_affinity

    dateInt: Integer : julian date
    dateTimeReal: Real : julian date
    boolInt: Boolean stored as an integer
    projectInt: Integer foreign key pointing to Project.id
    objectValueInt: Integer foreign key pointing to ObjectValue.id
*/

create table Project(
    id integer primary key
   ,xledgerDbId integer unique
   ,fromDate dateInt
   ,toDate dateInt
   ,code text
   ,description text
   ,createdAt dateTimeReal
   ,modifiedAt dateTimeReal
   ,"text" text
   ,ownerDbId integer
   ,email text
   ,yourReference text
   ,extIdentifier text
   ,external boolInt
   ,billable boolInt
   ,fixedClient boolInt
   ,allowPosting boolInt
   ,timesheetEntry boolInt
   ,accessControl boolInt
   ,assignment boolInt
   ,activity boolInt
   ,extOrder text
   ,contract text
   ,progressDate dateTimeReal
   ,pctCompleted real
   ,overview text
   ,expenseLedger boolInt
   ,fundProject boolInt
   ,invoiceHeader text
   ,invoiceFooter text
   ,totalRevenue real
   ,yearlyRevenue real
   ,contractedRevenue real
   ,totalCost real
   ,yearlyCost real
   ,totalEstimateHours real
   ,yearlyEstimateHours real
   ,budgetCoveragePercent real
   ,shortInfo text
   ,shortInternalInfo text
   ,mainProjectId projectInt
   ,xglId objectValueInt
   ,glObject5Id objectValueInt
   ,glObject4Id objectValueInt
   ,glObject3Id objectValueInt
   ,glObject2Id objectValueInt
   ,glObject1Id objectValueInt
);
