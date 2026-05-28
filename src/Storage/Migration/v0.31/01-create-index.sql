CREATE INDEX CONCURRENTLY IF NOT EXISTS instances_archived_appid_id_filtered
ON storage.instances USING btree
(
    jsonb_text_to_timestamptz(instance, 'Status', 'Archived') DESC NULLS LAST,
    appid ASC NULLS LAST,
    id ASC NULLS LAST
)
TABLESPACE pg_default
WHERE altinnmainversion = 3
    AND (instance -> 'Status' ->> 'IsArchived')::boolean = True
    AND (instance -> 'Status') ? 'Archived'
    AND NULLIF(instance -> 'Status' ->> 'Archived', '') IS NOT NULL;
