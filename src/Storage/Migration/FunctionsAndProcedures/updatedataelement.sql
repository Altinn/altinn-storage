CREATE OR REPLACE FUNCTION storage.updatedataelement_v3(
    _dataelementGuid UUID,
    _instanceGuid UUID,
    _elementChanges JSONB,
    _instanceChanges JSONB,
    _isReadChangedToFalse BOOL,
    _lastChanged TIMESTAMPTZ,
    _newcurrentblobversion UUID,
    _expectedcurrentblobversion UUID,
    _ignoreLock BOOL,
    _expectedinstanceversion INT DEFAULT NULL,
    _expectedprocessstateversion INT DEFAULT NULL)
    RETURNS TABLE (updatedElement JSONB, currentblobversion UUID, instanceversion INT, processstateversion INT, currentprocessstatus TEXT, result TEXT)
    LANGUAGE 'plpgsql'
AS $BODY$
DECLARE
    _lastChanged6digits TEXT;
    _instanceIsHardDeleted BOOL;
    _currentInstanceVersion INT;
    _currentProcessStateVersion INT;
    _currentProcessStatus TEXT;
    _newInstanceVersion INT;
    _dataElementIsHardDeleted BOOL;
    _dataElementIsLocked BOOL;
    _dataElementCurrentBlobVersion UUID;
BEGIN
    SELECT
        COALESCE((i.instance -> 'Status' ->> 'IsHardDeleted')::BOOLEAN, FALSE),
        i.instance_version,
        i.process_state_version,
        COALESCE(i.instance -> 'Process' ->> 'Status', 'idle')
        INTO _instanceIsHardDeleted, _currentInstanceVersion, _currentProcessStateVersion, _currentProcessStatus
        FROM storage.instances i
        WHERE i.alternateid = _instanceGuid
        FOR UPDATE;

    IF NOT FOUND
    THEN
        RETURN QUERY SELECT NULL::JSONB, NULL::UUID, NULL::INT, NULL::INT, NULL::TEXT, 'not_found'::TEXT;
        RETURN;
    END IF;

    IF _expectedinstanceversion IS NOT NULL AND _currentInstanceVersion <> _expectedinstanceversion
    THEN
        RETURN QUERY SELECT NULL::JSONB, NULL::UUID, _currentInstanceVersion, _currentProcessStateVersion, _currentProcessStatus, 'instance_version_mismatch'::TEXT;
        RETURN;
    END IF;

    IF _expectedprocessstateversion IS NOT NULL AND _currentProcessStateVersion <> _expectedprocessstateversion
    THEN
        RETURN QUERY SELECT NULL::JSONB, NULL::UUID, _currentInstanceVersion, _currentProcessStateVersion, _currentProcessStatus, 'process_state_version_mismatch'::TEXT;
        RETURN;
    END IF;

    IF _instanceIsHardDeleted
    THEN
        RETURN QUERY SELECT NULL::JSONB, NULL::UUID, _currentInstanceVersion, _currentProcessStateVersion, _currentProcessStatus, 'hard_deleted'::TEXT;
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
        RETURN QUERY SELECT NULL::JSONB, NULL::UUID, _currentInstanceVersion, _currentProcessStateVersion, _currentProcessStatus, 'not_found'::TEXT;
        RETURN;
    END IF;

    IF _expectedcurrentblobversion IS NOT NULL AND _dataElementCurrentBlobVersion IS DISTINCT FROM _expectedcurrentblobversion
    THEN
        RETURN QUERY SELECT NULL::JSONB, NULL::UUID, _currentInstanceVersion, _currentProcessStateVersion, _currentProcessStatus, 'version_mismatch'::TEXT;
        RETURN;
    END IF;

    IF _dataElementIsHardDeleted
    THEN
        RETURN QUERY SELECT NULL::JSONB, NULL::UUID, _currentInstanceVersion, _currentProcessStateVersion, _currentProcessStatus, 'hard_deleted'::TEXT;
        RETURN;
    END IF;

    IF NOT _ignoreLock AND _dataElementIsLocked
    THEN
        RETURN QUERY SELECT NULL::JSONB, NULL::UUID, _currentInstanceVersion, _currentProcessStateVersion, _currentProcessStatus, 'locked'::TEXT;
        RETURN;
    END IF;

    IF _lastChanged IS NOT NULL
    THEN
        -- Make sure that lastChanged has the Postgres precision (6 digits). The timestamp from C# DateTime and then json serialize has 7 digits
        _lastChanged6digits = REPLACE((_lastChanged AT TIME ZONE 'UTC')::TEXT, ' ', 'T') || 'Z';
        _elementChanges := _elementChanges || jsonb_set('{"LastChanged":""}', '{LastChanged}', to_jsonb(_lastChanged6digits));
    END IF;

    IF _currentProcessStatus <> 'idle'
    THEN
        RETURN QUERY SELECT NULL::JSONB, NULL::UUID, _currentInstanceVersion, _currentProcessStateVersion, _currentProcessStatus, 'process_status_conflict'::TEXT;
        RETURN;
    END IF;

    IF _newcurrentblobversion IS NOT NULL
    THEN
        UPDATE storage.dataelementblobversions
            SET attached = true
            WHERE id = _newcurrentblobversion
                AND instanceguid = _instanceGuid
                AND dataelementid = _dataelementGuid
                AND attached = false;

        IF NOT FOUND
        THEN
            RETURN QUERY SELECT NULL::JSONB, NULL::UUID, _currentInstanceVersion, _currentProcessStateVersion, _currentProcessStatus, 'blob_version_not_found'::TEXT;
            RETURN;
        END IF;
    END IF;

    UPDATE storage.instances
        SET lastchanged = COALESCE(_lastChanged, lastchanged),
            instance_version = instance_version + 1,
            instance = (
                CASE
                    WHEN _isReadChangedToFalse = true AND
                        (SELECT COUNT(*) FROM storage.dataelements
                            WHERE element -> 'IsRead' = 'true' AND instanceguid = _instanceGuid AND alternateid <> _dataelementGuid) = 0
                        AND instance -> 'Status' ->> 'ReadStatus' = '1'
                    THEN jsonb_set(instance, '{Status, ReadStatus}', '0')
                    ELSE instance
                END
            )
            || CASE
                WHEN _lastChanged IS NOT NULL
                THEN _instanceChanges || jsonb_set('{"LastChanged":""}', '{LastChanged}', to_jsonb(_lastChanged6digits))
                ELSE '{}'::JSONB
            END
        WHERE alternateid = _instanceGuid
        RETURNING storage.instances.instance_version INTO _newInstanceVersion;

    RETURN QUERY
        UPDATE storage.dataelements
            SET element = element || _elementChanges,
                currentblobversion = COALESCE(_newcurrentblobversion, storage.dataelements.currentblobversion)
            WHERE alternateid = _dataelementGuid AND instanceguid = _instanceGuid
            RETURNING element, storage.dataelements.currentblobversion, _newInstanceVersion, _currentProcessStateVersion, _currentProcessStatus, 'ok'::TEXT;
END;
$BODY$;
