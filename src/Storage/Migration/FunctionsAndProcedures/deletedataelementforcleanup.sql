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

    DELETE FROM storage.dataelementblobversions
        WHERE dataelementid = _alternateid
            AND attached IS NOT NULL;

    RETURN _deleteCount;
END;
$BODY$;
