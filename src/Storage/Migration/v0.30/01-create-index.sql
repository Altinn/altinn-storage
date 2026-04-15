CREATE INDEX IF NOT EXISTS instances_org_lastchanged_id_filtered_by_ended
ON storage.instances USING btree
(org COLLATE pg_catalog."default" ASC NULLS LAST, lastchanged DESC NULLS FIRST, id ASC NULLS LAST)
TABLESPACE pg_default
WHERE confirmed = false AND altinnmainversion = 3 AND instance -> 'Process' -> 'Ended' IS NOT NULL;
