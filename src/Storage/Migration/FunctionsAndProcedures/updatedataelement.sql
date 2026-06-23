CREATE OR REPLACE FUNCTION storage.updatedataelement_v3(
    _dataelementGuid UUID,
    _instanceGuid UUID,
    _elementChanges JSONB,
    _instanceChanges JSONB,
    _isReadChangedToFalse BOOL,
    _lastChanged TIMESTAMPTZ,
    _newcurrentblobversion UUID,
    _expectedcurrentblobversion UUID,
    _enforceLockCheck BOOL)
    RETURNS TABLE (updatedElement JSONB, currentblobversion UUID, result TEXT)
    LANGUAGE 'plpgsql'	
AS $BODY$
DECLARE
    _lastChanged6digits TEXT;
    _instanceIsHardDeleted BOOL;
    _dataElementIsHardDeleted BOOL;
    _dataElementIsLocked BOOL;
    _dataElementCurrentBlobVersion UUID;
BEGIN
    SELECT COALESCE((i.instance -> 'Status' ->> 'IsHardDeleted')::BOOLEAN, FALSE)
        INTO _instanceIsHardDeleted
        FROM storage.instances i
        WHERE i.alternateid = _instanceGuid
        FOR UPDATE;

    IF NOT FOUND
    THEN
        RETURN QUERY SELECT NULL::JSONB, NULL::UUID, 'not_found'::TEXT;
        RETURN;
    END IF;

    IF _instanceIsHardDeleted
    THEN
        RETURN QUERY SELECT NULL::JSONB, NULL::UUID, 'hard_deleted'::TEXT;
        RETURN;
    END IF;

    SELECT COALESCE((d.element -> 'DeleteStatus' ->> 'IsHardDeleted')::BOOLEAN, FALSE),
        COALESCE((d.element ->> 'Locked')::BOOLEAN, FALSE),
        d.currentblobversion
        INTO _dataElementIsHardDeleted, _dataElementIsLocked, _dataElementCurrentBlobVersion
        FROM storage.dataelements d
        WHERE d.alternateid = _dataelementGuid AND d.instanceguid = _instanceGuid
        FOR UPDATE;

    IF NOT FOUND
    THEN
        RETURN QUERY SELECT NULL::JSONB, NULL::UUID, 'not_found'::TEXT;
        RETURN;
    END IF;

    IF _expectedcurrentblobversion IS NOT NULL AND _dataElementCurrentBlobVersion IS DISTINCT FROM _expectedcurrentblobversion
    THEN
        RETURN QUERY SELECT NULL::JSONB, NULL::UUID, 'version_mismatch'::TEXT;
        RETURN;
    END IF;

    IF _enforceLockCheck AND _dataElementIsHardDeleted
    THEN
        RETURN QUERY SELECT NULL::JSONB, NULL::UUID, 'hard_deleted'::TEXT;
        RETURN;
    END IF;

    IF _enforceLockCheck AND _dataElementIsLocked
    THEN
        RETURN QUERY SELECT NULL::JSONB, NULL::UUID, 'locked'::TEXT;
        RETURN;
    END IF;

    IF _lastChanged IS NOT NULL
    THEN
        -- Make sure that lastChanged has the Postgres precision (6 digits). The timestamp from C# DateTime and then json serialize has 7 digits
        _lastChanged6digits = REPLACE((_lastChanged AT TIME ZONE 'UTC')::TEXT, ' ', 'T') || 'Z';
        _elementChanges := _elementChanges || jsonb_set('{"LastChanged":""}', '{LastChanged}', to_jsonb(_lastChanged6digits));
    END IF;

    IF _newcurrentblobversion IS NOT NULL
    THEN
        UPDATE storage.dataelementblobversions
            SET attached = NOW()
            WHERE id = _newcurrentblobversion
                AND instanceguid = _instanceGuid
                AND dataelementid = _dataelementGuid
                AND attached IS NULL;

        IF NOT FOUND
        THEN
            RETURN QUERY SELECT NULL::JSONB, NULL::UUID, 'blob_version_not_found'::TEXT;
            RETURN;
        END IF;
    END IF;

    IF _isReadChangedToFalse = true AND
        (SELECT COUNT(*) FROM storage.dataelements
            WHERE element -> 'IsRead' = 'true' AND instanceguid = _instanceGuid AND alternateid <> _dataelementGuid) = 0
    THEN
        UPDATE storage.instances
        SET instance = jsonb_set(instance, '{Status, ReadStatus}', '0')
        WHERE alternateid = _instanceGuid AND instance -> 'Status' ->> 'ReadStatus' = '1';
    END IF;

    IF _lastChanged IS NOT NULL
    THEN
        UPDATE storage.instances
            SET lastchanged = _lastChanged,
                instance = instance || _instanceChanges || jsonb_set('{"LastChanged":""}', '{LastChanged}', to_jsonb(_lastChanged6digits))   
            WHERE alternateid = _instanceGuid;
    END IF;

    RETURN QUERY
        UPDATE storage.dataelements
            SET element = element || _elementChanges,
                currentblobversion = COALESCE(_newcurrentblobversion, storage.dataelements.currentblobversion)
            WHERE alternateid = _dataelementGuid AND instanceguid = _instanceGuid
            RETURNING element, storage.dataelements.currentblobversion, 'ok'::TEXT;
END;
$BODY$;
