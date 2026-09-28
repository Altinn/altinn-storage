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