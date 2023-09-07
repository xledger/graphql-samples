/* Need to know what the old webhook dbId is to delete across restarts. */
alter table SyncStatus add column webhookXledgerDbId integer;
