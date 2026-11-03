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
        WHERE v.detachedat <= NOW() - INTERVAL '7 days'
            -- Callers of this version resolve the account from org and number only; a version in a
            -- named account would be looked up in the wrong account, its blob reported as missing
            -- and its row deleted, leaking the blob. Only _v2 callers handle these versions.
            AND v.storageaccountid IS NULL
        GROUP BY v.instanceguid, v.appid, v.blobstorageorg, v.storageaccountnumber;
END;
$BODY$;

CREATE OR REPLACE FUNCTION storage.readorphanblobversionsforcleanup_v2()
    RETURNS TABLE (instanceguid UUID, appid TEXT, blobstorageorg TEXT, storageaccountnumber INT, storageaccountid SMALLINT, blobversions UUID[])
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
    RETURN QUERY
        SELECT
            v.instanceguid,
            v.appid,
            v.blobstorageorg,
            v.storageaccountnumber,
            v.storageaccountid,
            array_agg(v.id ORDER BY v.created, v.id) AS blobversions
        FROM storage.dataelementblobversions v
        WHERE v.detachedat <= NOW() - INTERVAL '7 days'
        GROUP BY v.instanceguid, v.appid, v.blobstorageorg, v.storageaccountnumber, v.storageaccountid;
END;
$BODY$;
