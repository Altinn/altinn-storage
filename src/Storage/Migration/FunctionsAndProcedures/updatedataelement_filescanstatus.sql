CREATE OR REPLACE FUNCTION storage.updatedataelement_filescanstatus(
    _dataelementGuid UUID,
    _instanceGuid UUID,
    _elementChanges JSONB,
    _expectedcurrentblobversion UUID)
    RETURNS TABLE (updatedElement JSONB, currentblobversion UUID, instanceversion INT, processstateversion INT, result TEXT)
    LANGUAGE 'plpgsql'
AS $BODY$
DECLARE
    _instanceIsHardDeleted BOOL;
    _currentInstanceVersion INT;
    _currentProcessStateVersion INT;
    _dataElementCurrentBlobVersion UUID;
BEGIN
    SELECT
        COALESCE((i.instance -> 'Status' ->> 'IsHardDeleted')::BOOLEAN, FALSE),
        i.instance_version,
        i.process_state_version
        INTO _instanceIsHardDeleted, _currentInstanceVersion, _currentProcessStateVersion
        FROM storage.instances i
        WHERE i.alternateid = _instanceGuid
        FOR UPDATE;

    IF NOT FOUND
    THEN
        RETURN QUERY SELECT NULL::JSONB, NULL::UUID, NULL::INT, NULL::INT, 'not_found'::TEXT;
        RETURN;
    END IF;

    IF _instanceIsHardDeleted
    THEN
        RETURN QUERY SELECT NULL::JSONB, NULL::UUID, _currentInstanceVersion, _currentProcessStateVersion, 'hard_deleted'::TEXT;
        RETURN;
    END IF;

    SELECT d.currentblobversion
        INTO _dataElementCurrentBlobVersion
        FROM storage.dataelements d
        WHERE d.alternateid = _dataelementGuid AND d.instanceguid = _instanceGuid
        FOR UPDATE;

    IF NOT FOUND
    THEN
        RETURN QUERY SELECT NULL::JSONB, NULL::UUID, _currentInstanceVersion, _currentProcessStateVersion, 'not_found'::TEXT;
        RETURN;
    END IF;

    IF _expectedcurrentblobversion IS NOT NULL AND _dataElementCurrentBlobVersion IS DISTINCT FROM _expectedcurrentblobversion
    THEN
        RETURN QUERY SELECT NULL::JSONB, NULL::UUID, _currentInstanceVersion, _currentProcessStateVersion, 'version_mismatch'::TEXT;
        RETURN;
    END IF;

    RETURN QUERY
        UPDATE storage.dataelements
            SET element = element || _elementChanges
            WHERE alternateid = _dataelementGuid AND instanceguid = _instanceGuid
            RETURNING element, storage.dataelements.currentblobversion, _currentInstanceVersion, _currentProcessStateVersion, 'ok'::TEXT;
END;
$BODY$;
