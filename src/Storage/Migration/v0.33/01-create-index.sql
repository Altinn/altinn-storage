CREATE INDEX IF NOT EXISTS instances_a3ref
ON storage.instances USING btree
((right(alternateid::text, 12)) COLLATE pg_catalog."default" ASC NULLS LAST)
TABLESPACE pg_default
WHERE altinnmainversion = 3;
