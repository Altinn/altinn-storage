CREATE OR REPLACE FUNCTION storage.updatedataelement_v2(_dataelementGuid UUID, _instanceGuid UUID, _elementChanges JSONB, _instanceChanges JSONB, _isReadChangedToFalse BOOL, _lastChanged TIMESTAMPTZ)
    RETURNS TABLE (updatedElement JSONB)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
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
                instance = instance || _instanceChanges           
            WHERE alternateid = _instanceGuid;
    END IF;

    RETURN QUERY
        UPDATE storage.dataelements SET element = element || _elementChanges WHERE alternateid = _dataelementGuid
            RETURNING element;
END;
$BODY$;

CREATE OR REPLACE FUNCTION storage.deletedataelement_v2(_alternateid UUID, _instanceGuid UUID, _lastChangedBy TEXT)
    RETURNS INT
    LANGUAGE 'plpgsql'	
AS $BODY$
DECLARE
    _deleteCount INTEGER;
BEGIN
    IF (SELECT COUNT(*) FROM storage.dataelements WHERE element -> 'IsRead' = 'true' AND instanceguid = _instanceGuid) = 0 THEN
        UPDATE storage.instances
        SET instance = jsonb_set(instance, '{Status, ReadStatus}', '0')
        WHERE alternateid = _instanceGuid AND instance -> 'Status' ->> 'ReadStatus' = '1';
    END IF;

    UPDATE storage.instances
        SET lastchanged = NOW(),
            instance = instance
                || jsonb_set('{"LastChanged":""}', '{LastChanged}', to_jsonb(REPLACE((NOW() AT TIME ZONE 'UTC')::TEXT, ' ', 'T') || 'Z'))
                || jsonb_set('{"LastChangedBy":""}', '{LastChangedBy}', to_jsonb(_lastChangedBy))            
        WHERE alternateid = (SELECT instanceguid FROM storage.dataelements WHERE alternateid = _alternateid);

    DELETE FROM storage.dataelements WHERE alternateid = _alternateid;
    GET DIAGNOSTICS _deleteCount = ROW_COUNT;

    RETURN _deleteCount;
END;
$BODY$;

CREATE OR REPLACE FUNCTION storage.deletedataelements(_instanceguid UUID)
    RETURNS INT
    LANGUAGE 'plpgsql'	
AS $BODY$
DECLARE
    _deleteCount INTEGER;
BEGIN
    UPDATE storage.instances
        SET lastchanged = NOW(),
            instance = instance
                || jsonb_set('{"LastChanged":""}', '{LastChanged}', to_jsonb(REPLACE((NOW() AT TIME ZONE 'UTC')::TEXT, ' ', 'T') || 'Z'))
                || jsonb_set('{"LastChangedBy":""}', '{LastChangedBy}', to_jsonb('altinn'::TEXT))   
        WHERE alternateid = _instanceguid;

    DELETE FROM storage.dataelements d
        USING storage.instances i
        WHERE i.alternateid = d.instanceguid AND i.alternateid = _instanceguid;
    GET DIAGNOSTICS _deleteCount = ROW_COUNT;
    RETURN _deleteCount;
END;
$BODY$;