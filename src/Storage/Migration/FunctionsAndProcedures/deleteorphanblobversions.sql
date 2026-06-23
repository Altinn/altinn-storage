CREATE OR REPLACE FUNCTION storage.deleteorphanblobversions(_versions UUID[])
    RETURNS INT
    LANGUAGE 'plpgsql'
AS $BODY$
DECLARE
    _deleteCount INTEGER;
BEGIN
    DELETE FROM storage.dataelementblobversions
        WHERE id = ANY(_versions)
            AND attached IS NULL;
    GET DIAGNOSTICS _deleteCount = ROW_COUNT;

    RETURN _deleteCount;
END;
$BODY$;
