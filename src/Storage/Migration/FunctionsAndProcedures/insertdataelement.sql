CREATE OR REPLACE FUNCTION storage.insertdataelement_v3(
    IN _instanceinternalid BIGINT,
    IN _instanceguid UUID,
    IN _alternateid UUID,
    IN _element JSONB,
    IN _currentblobversion UUID)
    RETURNS TABLE (updatedElement JSONB, currentblobversion UUID, result TEXT)
    LANGUAGE plpgsql
AS $BODY$
DECLARE
    _instanceIsHardDeleted BOOL;
BEGIN
    SELECT COALESCE((i.instance -> 'Status' ->> 'IsHardDeleted')::BOOLEAN, FALSE)
        INTO _instanceIsHardDeleted
        FROM storage.instances i
        WHERE i.id = _instanceinternalid
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

    IF _currentblobversion IS NOT NULL
    THEN
        UPDATE storage.dataelementblobversions
            SET attached = NOW()
            WHERE id = _currentblobversion
                AND instanceguid = _instanceguid
                AND dataelementid = _alternateid
                AND attached IS NULL;

        IF NOT FOUND
        THEN
            RETURN QUERY SELECT NULL::JSONB, NULL::UUID, 'blob_version_not_found'::TEXT;
            RETURN;
        END IF;
    END IF;

    -- Make sure that lastChanged has the Postgres precision (6 digits). The timestamp from C# DateTime and then json serialize has 7 digits
    _element := _element || jsonb_set('{"LastChanged":""}', '{LastChanged}', to_jsonb(REPLACE(((_element ->> 'LastChanged')::TIMESTAMPTZ AT TIME ZONE 'UTC')::TEXT, ' ', 'T') || 'Z'));

    IF _element ->> 'IsRead' = 'false' THEN
        UPDATE storage.instances
        SET instance = jsonb_set(instance, '{Status, ReadStatus}', '2')
        WHERE id = _instanceinternalid AND instance -> 'Status' ->> 'ReadStatus' = '1';
    END IF;

    UPDATE storage.instances
        SET lastchanged = (_element ->> 'LastChanged')::TIMESTAMPTZ,
            instance = instance
                || jsonb_set('{"LastChanged":""}', '{LastChanged}', to_jsonb(_element ->> 'LastChanged'))
                || jsonb_set('{"LastChangedBy":""}', '{LastChangedBy}', to_jsonb(_element ->> 'LastChangedBy'))
        WHERE id = _instanceinternalid;

    RETURN QUERY
        INSERT INTO storage.dataelements(instanceinternalid, instanceGuid, alternateid, element, currentblobversion) VALUES (_instanceinternalid, _instanceGuid, _alternateid, jsonb_strip_nulls(_element), _currentblobversion)
            RETURNING element, storage.dataelements.currentblobversion, 'ok'::TEXT;
END;
$BODY$;
