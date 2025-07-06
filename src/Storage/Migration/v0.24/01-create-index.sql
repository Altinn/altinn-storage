drop index if exists storage.instances_isharddeleted_confirmed;
drop index if exists storage.instances_lastchanged_appid;
drop index if exists storage.instances_lastchanged_filtered;

CREATE INDEX IF NOT EXISTS instances_org_lastchanged_archived_id_filtered
    ON storage.instances USING btree
    (org COLLATE pg_catalog."default" ASC NULLS LAST, lastchanged DESC NULLS FIRST, (((instance -> 'Status'::text) -> 'IsArchived'::text)::boolean) ASC NULLS LAST, id ASC NULLS LAST)
    TABLESPACE pg_default
    WHERE confirmed = false AND altinnmainversion = 3;

CREATE INDEX IF NOT EXISTS instances_org_lastchanged_id_filtered
    ON storage.instances USING btree
    (org COLLATE pg_catalog."default" ASC NULLS LAST, lastchanged DESC NULLS FIRST, id ASC NULLS LAST)
    TABLESPACE pg_default
    WHERE confirmed = false AND altinnmainversion = 3;

CREATE INDEX IF NOT EXISTS instances_lastchanged_org_filtered
    ON storage.instances USING btree
    (lastchanged ASC NULLS LAST, org COLLATE pg_catalog."default" ASC NULLS LAST)
    TABLESPACE pg_default
    WHERE altinnmainversion = 3 AND ((instance -> 'Status'::text) -> 'IsArchived'::text)::boolean = true;