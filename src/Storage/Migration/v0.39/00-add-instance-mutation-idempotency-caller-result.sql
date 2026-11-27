-- The caller's result of an idempotent mutation, returned when the mutation is replayed. Nullable, since most
-- mutations carry none and the previous applyinstancemutation keeps inserting rows without it during a rollout.
ALTER TABLE storage.instance_mutation_idempotency
ADD COLUMN IF NOT EXISTS caller_result TEXT NULL;
