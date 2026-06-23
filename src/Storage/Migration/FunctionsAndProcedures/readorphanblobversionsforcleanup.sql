CREATE OR REPLACE FUNCTION storage.readorphanblobversionsforcleanup()
    RETURNS TABLE (instanceguid UUID, appid TEXT, blobstorageorg TEXT, storageaccountnumber INT, blobversions UUID[])
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
    RETURN QUERY
        SELECT
            v.instanceguid,
            v.appid,
            v.blobstorageorg,
            v.storageaccountnumber,
            array_agg(v.id ORDER BY v.created, v.id) AS blobversions
        FROM storage.dataelementblobversions v
        WHERE v.attached IS NULL
            AND v.created <= NOW() - INTERVAL '7 days'
        GROUP BY v.instanceguid, v.appid, v.blobstorageorg, v.storageaccountnumber;
END;
$BODY$;
