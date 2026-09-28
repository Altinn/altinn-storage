CREATE OR REPLACE FUNCTION storage.updatedataelement_lockstatus(
    _dataelementGuid UUID,
    _instanceGuid UUID,
    _locked BOOL)
    RETURNS TABLE (updatedElement JSONB, currentblobversion UUID, instanceversion INT, processstateversion INT, currentprocessstatus TEXT, result TEXT)
    LANGUAGE 'plpgsql'
AS $BODY$
DECLARE
    _currentInstanceVersion INT;
    _currentProcessStateVersion INT;
    _currentProcessStatus TEXT;
BEGIN
    SELECT
        i.instance_version,
        i.process_state_version,
        COALESCE(i.instance -> 'Process' ->> 'Status', 'idle')
        INTO _currentInstanceVersion, _currentProcessStateVersion, _currentProcessStatus
        FROM storage.instances i
        WHERE i.alternateid = _instanceGuid
        FOR UPDATE;

    IF NOT FOUND
    THEN
        RETURN QUERY SELECT NULL::JSONB, NULL::UUID, NULL::INT, NULL::INT, NULL::TEXT, 'not_found'::TEXT;
        RETURN;
    END IF;

    PERFORM 1
        FROM storage.dataelements d
        WHERE d.alternateid = _dataelementGuid AND d.instanceguid = _instanceGuid
        FOR UPDATE;

    IF NOT FOUND
    THEN
        RETURN QUERY SELECT NULL::JSONB, NULL::UUID, _currentInstanceVersion, _currentProcessStateVersion, _currentProcessStatus, 'not_found'::TEXT;
        RETURN;
    END IF;

    IF _currentProcessStatus <> 'idle'
    THEN
        RETURN QUERY SELECT NULL::JSONB, NULL::UUID, _currentInstanceVersion, _currentProcessStateVersion, _currentProcessStatus, 'process_status_conflict'::TEXT;
        RETURN;
    END IF;

    RETURN QUERY
        UPDATE storage.dataelements
            SET element = element || jsonb_build_object('Locked', _locked)
            WHERE alternateid = _dataelementGuid AND instanceguid = _instanceGuid
            RETURNING element, storage.dataelements.currentblobversion, _currentInstanceVersion, _currentProcessStateVersion, _currentProcessStatus, 'ok'::TEXT;
END;
$BODY$;
