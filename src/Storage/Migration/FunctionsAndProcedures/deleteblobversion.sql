CREATE OR REPLACE FUNCTION storage.deleteblobversion(_dataelementid UUID, _blobversionid UUID)
    RETURNS INT
    LANGUAGE 'plpgsql'
AS $BODY$
DECLARE
    _deleteCount INTEGER;
BEGIN
    DELETE FROM storage.dataelementblobversions
        WHERE id = _blobversionid
            AND dataelementid = _dataelementid
            AND detachedat IS NOT NULL;
    GET DIAGNOSTICS _deleteCount = ROW_COUNT;

    RETURN _deleteCount;
END;
$BODY$;
