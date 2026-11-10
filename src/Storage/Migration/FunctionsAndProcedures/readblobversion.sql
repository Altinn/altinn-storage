CREATE OR REPLACE FUNCTION storage.readblobversion(_instanceguid UUID, _dataelementid UUID, _blobversion UUID)
    RETURNS TABLE (
        appid TEXT,
        blobstorageorg TEXT,
        storageaccountnumber INT,
        created TIMESTAMPTZ,
        detachedat TIMESTAMPTZ,
        datatype TEXT
    )
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
    RETURN QUERY
        SELECT
            bv.appid,
            bv.blobstorageorg,
            bv.storageaccountnumber,
            bv.created,
            bv.detachedat,
            bv.datatype
        FROM storage.dataelementblobversions bv
        WHERE bv.id = _blobversion
            AND bv.instanceguid = _instanceguid
            AND bv.dataelementid = _dataelementid;
END;
$BODY$;
