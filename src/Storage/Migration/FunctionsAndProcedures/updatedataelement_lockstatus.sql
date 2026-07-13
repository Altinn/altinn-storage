CREATE OR REPLACE FUNCTION storage.updatedataelement_lockstatus(
    _dataelementGuid UUID,
    _instanceGuid UUID,
    _locked BOOL)
    RETURNS TABLE (updatedElement JSONB, currentblobversion UUID, instanceversion INT, processstateversion INT, result TEXT)
    LANGUAGE 'plpgsql'
AS $BODY$
DECLARE
    _currentInstanceVersion INT;
    _currentProcessStateVersion INT;
BEGIN
    SELECT i.instance_version, i.process_state_version
        INTO _currentInstanceVersion, _currentProcessStateVersion
        FROM storage.instances i
        WHERE i.alternateid = _instanceGuid
        FOR UPDATE;

    IF NOT FOUND
    THEN
        RETURN QUERY SELECT NULL::JSONB, NULL::UUID, NULL::INT, NULL::INT, 'not_found'::TEXT;
        RETURN;
    END IF;

    RETURN QUERY
        UPDATE storage.dataelements
            SET element = element || jsonb_build_object('Locked', _locked)
            WHERE alternateid = _dataelementGuid AND instanceguid = _instanceGuid
            RETURNING element, storage.dataelements.currentblobversion, _currentInstanceVersion, _currentProcessStateVersion, 'ok'::TEXT;

    IF NOT FOUND
    THEN
        RETURN QUERY SELECT NULL::JSONB, NULL::UUID, _currentInstanceVersion, _currentProcessStateVersion, 'not_found'::TEXT;
    END IF;
END;
$BODY$;
