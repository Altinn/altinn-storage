CREATE INDEX IF NOT EXISTS instances_partyid_created_id
ON storage.instances USING btree
(partyId ASC NULLS LAST, created ASC NULLS LAST, id ASC NULLS LAST)
TABLESPACE pg_default;
