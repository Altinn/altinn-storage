CREATE OR REPLACE FUNCTION storage.deleteblobversions(_dataelementid UUID, _blobversionids UUID[])
    RETURNS INT
    LANGUAGE 'plpgsql'
AS $BODY$
DECLARE
    _deleteCount INTEGER;
BEGIN
    DELETE FROM storage.dataelementblobversions
        WHERE id = ANY(_blobversionids)
            AND dataelementid = _dataelementid
            AND attached = false;
    GET DIAGNOSTICS _deleteCount = ROW_COUNT;

    RETURN _deleteCount;
END;
$BODY$;
