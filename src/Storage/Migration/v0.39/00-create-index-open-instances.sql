-- Open instances (taskid IS NOT NULL) are a small fraction of the table. The index lets the Studio
-- metrics count open instances per app, per task and by age without touching archived rows.
CREATE INDEX IF NOT EXISTS instances_org_appid_taskid_created_open
ON storage.instances USING btree (org, appid, taskid, created)
WHERE taskid IS NOT NULL;
