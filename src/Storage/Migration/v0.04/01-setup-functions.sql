CREATE OR REPLACE FUNCTION storage.insertdataelement_v2(
	IN _instanceinternalid bigint,
	IN _instanceguid uuid,
	IN _alternateid uuid,
	IN _element jsonb)
    RETURNS TABLE (updatedElement JSONB)
    LANGUAGE plpgsql
AS $BODY$
BEGIN
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
        INSERT INTO storage.dataelements(instanceinternalid, instanceGuid, alternateid, element) VALUES (_instanceinternalid, _instanceGuid, _alternateid, jsonb_strip_nulls(_element))
            RETURNING element;
END;
$BODY$;

CREATE OR REPLACE FUNCTION storage.updatedataelement_v2(_dataelementGuid UUID, _instanceGuid UUID, _elementChanges JSONB, _instanceChanges JSONB, _isReadChangedToFalse BOOL, _lastChanged TIMESTAMPTZ)
    RETURNS TABLE (updatedElement JSONB)
    LANGUAGE 'plpgsql'	
AS $BODY$
DECLARE
    _lastChanged6digits TEXT;
BEGIN
    IF _lastChanged IS NOT NULL
    THEN
        -- Make sure that lastChanged has the Postgres precision (6 digits). The timestamp from C# DateTime and then json serialize has 7 digits
        _lastChanged6digits = REPLACE((_lastChanged AT TIME ZONE 'UTC')::TEXT, ' ', 'T') || 'Z';
        _elementChanges := _elementChanges || jsonb_set('{"LastChanged":""}', '{LastChanged}', to_jsonb(_lastChanged6digits));
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
        UPDATE storage.dataelements SET element = element || _elementChanges WHERE alternateid = _dataelementGuid
            RETURNING element;
END;
$BODY$;