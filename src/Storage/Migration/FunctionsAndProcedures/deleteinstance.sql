CREATE OR REPLACE FUNCTION storage.deleteinstance(_alternateid UUID)
    RETURNS INT
    LANGUAGE 'plpgsql'	
AS $BODY$
DECLARE
    _deleteCount INTEGER;
BEGIN
    DELETE FROM storage.instances WHERE alternateid = _alternateid;
    GET DIAGNOSTICS _deleteCount = ROW_COUNT;
    RETURN _deleteCount;
END;
$BODY$;