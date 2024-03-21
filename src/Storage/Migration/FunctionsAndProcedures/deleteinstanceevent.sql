CREATE OR REPLACE FUNCTION storage.deleteinstanceevent(_instance UUID)
    RETURNS INT
    LANGUAGE 'plpgsql'	
AS $BODY$
DECLARE
    _deleteCount INTEGER;
BEGIN
    DELETE FROM storage.instanceevents WHERE instance = _instance;
    GET DIAGNOSTICS _deleteCount = ROW_COUNT;
    RETURN _deleteCount;
END;
$BODY$;