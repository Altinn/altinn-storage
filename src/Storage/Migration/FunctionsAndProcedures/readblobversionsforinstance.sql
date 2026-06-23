CREATE OR REPLACE FUNCTION storage.readblobversionsforinstance(_instanceguid UUID)
    RETURNS TABLE (dataelementid UUID, instanceguid UUID, appid TEXT, blobstorageorg TEXT, storageaccountnumber INT, blobversions UUID[])
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
    RETURN QUERY
        SELECT
            bv.dataelementid,
            bv.instanceguid,
            bv.appid,
            bv.blobstorageorg,
            bv.storageaccountnumber,
            array_agg(bv.id ORDER BY bv.created, bv.id) AS blobversions
        FROM storage.dataelementblobversions bv
        WHERE bv.instanceguid = _instanceguid
            AND bv.attached IS NOT NULL
        GROUP BY bv.dataelementid, bv.instanceguid, bv.appid, bv.blobstorageorg, bv.storageaccountnumber
        ORDER BY min(bv.created), min(bv.id::TEXT);
END;
$BODY$;
