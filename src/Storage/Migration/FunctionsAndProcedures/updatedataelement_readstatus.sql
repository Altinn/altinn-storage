CREATE OR REPLACE FUNCTION storage.updatedataelement_readstatus(
    _dataelementGuid UUID,
    _instanceGuid UUID,
    _isRead BOOL)
    RETURNS TABLE (updatedElement JSONB, currentblobversion UUID, instanceversion INT, processstateversion INT, result TEXT)
    LANGUAGE 'plpgsql'
AS $BODY$
DECLARE
    _currentInstanceVersion INT;
    _currentProcessStateVersion INT;
    _updatedElement JSONB;
    _currentBlobVersion UUID;
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

    UPDATE storage.dataelements
        SET element = element || jsonb_build_object('IsRead', _isRead)
        WHERE alternateid = _dataelementGuid AND instanceguid = _instanceGuid
        RETURNING element, storage.dataelements.currentblobversion
            INTO _updatedElement, _currentBlobVersion;

    IF NOT FOUND
    THEN
        RETURN QUERY SELECT NULL::JSONB, NULL::UUID, _currentInstanceVersion, _currentProcessStateVersion, 'not_found'::TEXT;
        RETURN;
    END IF;

    IF _isRead = FALSE
    THEN
        UPDATE storage.instances
            SET instance = jsonb_set(instance, '{Status, ReadStatus}', '0')
            WHERE alternateid = _instanceGuid
                AND instance -> 'Status' ->> 'ReadStatus' = '1'
                AND NOT EXISTS (
                    SELECT 1 FROM storage.dataelements
                        WHERE element -> 'IsRead' = 'true'
                            AND instanceguid = _instanceGuid
                            AND alternateid <> _dataelementGuid
                );
    END IF;

    RETURN QUERY SELECT _updatedElement, _currentBlobVersion, _currentInstanceVersion, _currentProcessStateVersion, 'ok'::TEXT;
END;
$BODY$;
