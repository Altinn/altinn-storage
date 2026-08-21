CREATE OR REPLACE FUNCTION storage.insertdataelement_v3(
    IN _instanceinternalid BIGINT,
    IN _instanceguid UUID,
    IN _alternateid UUID,
    IN _element JSONB,
    IN _currentblobversion UUID,
    IN _expectedinstanceversion INT DEFAULT NULL,
    IN _expectedprocessstateversion INT DEFAULT NULL)
    RETURNS TABLE (updatedElement JSONB, currentblobversion UUID, instanceversion INT, processstateversion INT, currentprocessstatus TEXT, result TEXT)
    LANGUAGE plpgsql
AS $BODY$
DECLARE
    _instanceIsHardDeleted BOOL;
    _currentInstanceVersion INT;
    _currentProcessStateVersion INT;
    _currentProcessStatus TEXT;
    _newInstanceVersion INT;
BEGIN
    SELECT
        COALESCE((i.instance -> 'Status' ->> 'IsHardDeleted')::BOOLEAN, FALSE),
        i.instance_version,
        i.process_state_version,
        COALESCE(i.instance -> 'Process' ->> 'Status', 'idle')
        INTO _instanceIsHardDeleted, _currentInstanceVersion, _currentProcessStateVersion, _currentProcessStatus
        FROM storage.instances i
        WHERE i.id = _instanceinternalid
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

    IF _currentProcessStatus <> 'idle'
    THEN
        RETURN QUERY SELECT NULL::JSONB, NULL::UUID, _currentInstanceVersion, _currentProcessStateVersion, _currentProcessStatus, 'process_status_conflict'::TEXT;
        RETURN;
    END IF;

    IF _currentblobversion IS NOT NULL
    THEN
        UPDATE storage.dataelementblobversions
            SET attached = true
            WHERE id = _currentblobversion
                AND instanceguid = _instanceguid
                AND dataelementid = _alternateid
                AND attached = false;

        IF NOT FOUND
        THEN
            RETURN QUERY SELECT NULL::JSONB, NULL::UUID, _currentInstanceVersion, _currentProcessStateVersion, _currentProcessStatus, 'blob_version_not_found'::TEXT;
            RETURN;
        END IF;
    END IF;

    -- Make sure that lastChanged has the Postgres precision (6 digits). The timestamp from C# DateTime and then json serialize has 7 digits
    _element := _element || jsonb_set('{"LastChanged":""}', '{LastChanged}', to_jsonb(REPLACE(((_element ->> 'LastChanged')::TIMESTAMPTZ AT TIME ZONE 'UTC')::TEXT, ' ', 'T') || 'Z'));

    UPDATE storage.instances
        SET lastchanged = (_element ->> 'LastChanged')::TIMESTAMPTZ,
            instance_version = instance_version + 1,
            instance = (
                CASE
                    WHEN _element ->> 'IsRead' = 'false' AND instance -> 'Status' ->> 'ReadStatus' = '1'
                    THEN jsonb_set(instance, '{Status, ReadStatus}', '2')
                    ELSE instance
                END
            )
                || jsonb_set('{"LastChanged":""}', '{LastChanged}', to_jsonb(_element ->> 'LastChanged'))
                || jsonb_set('{"LastChangedBy":""}', '{LastChangedBy}', to_jsonb(_element ->> 'LastChangedBy'))
        WHERE id = _instanceinternalid
        RETURNING storage.instances.instance_version INTO _newInstanceVersion;

    RETURN QUERY
        INSERT INTO storage.dataelements(instanceinternalid, instanceGuid, alternateid, element, currentblobversion)
            VALUES (_instanceinternalid, _instanceGuid, _alternateid, jsonb_strip_nulls(_element), _currentblobversion)
            RETURNING element, storage.dataelements.currentblobversion, _newInstanceVersion, _currentProcessStateVersion, _currentProcessStatus, 'ok'::TEXT;
END;
$BODY$;
