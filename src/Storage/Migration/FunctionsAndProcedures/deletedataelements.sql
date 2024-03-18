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