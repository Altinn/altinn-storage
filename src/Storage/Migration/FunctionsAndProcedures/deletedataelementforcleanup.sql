CREATE OR REPLACE FUNCTION storage.deletedataelementforcleanup(_alternateid UUID, _instanceguid UUID)
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
        SET detachedat = NOW()
        WHERE instanceguid = _instanceguid
            AND dataelementid = _alternateid
            AND detachedat IS NULL;

    RETURN _deleteCount;
END;
$BODY$;
