CREATE INDEX IF NOT EXISTS instances_a2archref
ON storage.instances USING btree
((instance -> 'DataValues' ->> 'A2ArchRef') COLLATE pg_catalog."default" ASC NULLS LAST)
TABLESPACE pg_default
WHERE (instance -> 'DataValues' ->> 'A2ArchRef') IS NOT NULL and altinnmainversion = 2;
