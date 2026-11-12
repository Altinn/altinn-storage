CREATE OR REPLACE FUNCTION storage.readdetachedblobversions(_dataelementid UUID)
    RETURNS TABLE (instanceguid UUID, appid TEXT, blobstorageorg TEXT, storageaccountnumber INT, blobversions UUID[])
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
    RETURN QUERY
        SELECT
            bv.instanceguid,
            bv.appid,
            bv.blobstorageorg,
            bv.storageaccountnumber,
            array_agg(bv.id ORDER BY bv.created, bv.id) AS blobversions
        FROM storage.dataelementblobversions bv
        WHERE bv.dataelementid = _dataelementid
            AND bv.detachedat IS NOT NULL
            -- Callers of this version resolve the account from org and number only; a version in a
            -- named account would be looked up in the wrong account, its blob reported as missing
            -- and its row deleted, leaking the blob. Only _v2 callers handle these versions.
            AND bv.storageaccountid IS NULL
        GROUP BY bv.instanceguid, bv.appid, bv.blobstorageorg, bv.storageaccountnumber
        ORDER BY min(bv.created), min(bv.id::TEXT);
END;
$BODY$;

CREATE OR REPLACE FUNCTION storage.readdetachedblobversions_v2(_dataelementid UUID)
    RETURNS TABLE (instanceguid UUID, appid TEXT, blobstorageorg TEXT, storageaccountnumber INT, storageaccountid SMALLINT, blobversions UUID[])
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
    RETURN QUERY
        SELECT
            bv.instanceguid,
            bv.appid,
            bv.blobstorageorg,
            bv.storageaccountnumber,
            bv.storageaccountid,
            array_agg(bv.id ORDER BY bv.created, bv.id) AS blobversions
        FROM storage.dataelementblobversions bv
        WHERE bv.dataelementid = _dataelementid
            AND bv.detachedat IS NOT NULL
        GROUP BY bv.instanceguid, bv.appid, bv.blobstorageorg, bv.storageaccountnumber, bv.storageaccountid
        ORDER BY min(bv.created), min(bv.id::TEXT);
END;
$BODY$;
