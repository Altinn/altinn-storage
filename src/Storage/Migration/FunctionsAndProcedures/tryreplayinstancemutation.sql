CREATE OR REPLACE FUNCTION storage.tryreplayinstancemutation_v2(
    IN _idempotencykey UUID,
    IN _instanceguid UUID,
    IN _previousinstanceversion INT,
    IN _currentinstanceversion INT,
    IN _currentprocessstateversion INT)
    RETURNS TEXT[]
    LANGUAGE plpgsql
AS $BODY$
DECLARE
    _producedinstanceversion INT;
    _createddataelementids TEXT[];
    _storedinstanceguid UUID;
    _storedpreviousinstanceversion INT;
BEGIN
    SELECT
        i.instance,
        i.previous_instance_version,
        i.produced_instance_version,
        i.created_data_element_ids
        INTO
            _storedinstanceguid,
            _storedpreviousinstanceversion,
            _producedinstanceversion,
            _createddataelementids
        FROM storage.instance_mutation_idempotency i
        WHERE i.idempotency_key = _idempotencykey;

    IF NOT FOUND
    THEN
        CALL storage.raiseinstancemutationerror(
            'idempotency_key_not_found',
            _currentinstanceversion,
            _currentprocessstateversion);
    END IF;

    IF _storedinstanceguid IS DISTINCT FROM _instanceguid
    THEN
        CALL storage.raiseinstancemutationerror(
            'idempotency_key_instance_mismatch',
            _currentinstanceversion,
            _currentprocessstateversion);
    END IF;

    IF _storedpreviousinstanceversion IS DISTINCT FROM _previousinstanceversion
    THEN
        CALL storage.raiseinstancemutationerror(
            'instance_version_mismatch',
            _currentinstanceversion,
            _currentprocessstateversion);
    END IF;

    IF _currentinstanceversion > _producedinstanceversion
    THEN
        CALL storage.raiseinstancemutationerror(
            'instance_already_advanced',
            _currentinstanceversion,
            _currentprocessstateversion);
    END IF;

    RETURN _createddataelementids;
END;
$BODY$;
