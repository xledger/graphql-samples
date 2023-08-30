/* The GraphQL field is named "cursor" so keep closer to that name. */
alter table SyncStatus rename column syncValue to syncCursor;
/* Need to know what the old webhook dbId is to delete across restarts. */
alter table SyncStatus add column webhookXledgerDbId integer;
