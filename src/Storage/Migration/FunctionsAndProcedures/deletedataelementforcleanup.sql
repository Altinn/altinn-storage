CREATE OR REPLACE FUNCTION storage.deletedataelementforcleanup(_alternateid UUID)
    RETURNS INT
    LANGUAGE 'plpgsql'
AS $BODY$
DECLARE
    _deleteCount INTEGER;
BEGIN
    DELETE FROM storage.dataelements
        WHERE alternateid = _alternateid;
    GET DIAGNOSTICS _deleteCount = ROW_COUNT;

    UPDATE storage.dataelementblobversions
        SET attached = false
        WHERE dataelementid = _alternateid
            AND attached = true;

    RETURN _deleteCount;
END;
$BODY$;
