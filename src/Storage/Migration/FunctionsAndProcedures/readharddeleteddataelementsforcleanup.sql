CREATE OR REPLACE FUNCTION storage.readharddeleteddataelementsforcleanup()
    RETURNS TABLE (
        id BIGINT,
        instance JSONB,
        element JSONB,
        blobversioninstanceguid UUID,
        blobversionappid TEXT,
        blobversionblobstorageorg TEXT,
        blobversionstorageaccountnumber INT,
        blobversions UUID[])
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
    -- Force nested loop join to avoid hash join on large instances table
    -- With only a small count of hard-deleted records, nested loop with index lookups is optimal
    SET LOCAL enable_hashjoin = off;
    SET LOCAL enable_mergejoin = off;

    RETURN QUERY
    SELECT
        i.id,
        i.instance,
        d.element,
        v.instanceguid,
        v.appid,
        v.blobstorageorg,
        v.storageaccountnumber,
        COALESCE(v.blobversions, ARRAY[]::UUID[])
    FROM (
        -- Target index dataelements_isharddeleted
        SELECT instanceinternalid, de.alternateid, de.element
        FROM storage.dataelements de
        WHERE (de.element -> 'DeleteStatus' -> 'IsHardDeleted')::BOOLEAN
            AND (de.element -> 'DeleteStatus' ->> 'HardDeleted')::TIMESTAMPTZ <= NOW() - INTERVAL '7 days'
        OFFSET 0  -- Optimization fence: prevents subquery flattening which causes wrong join strategy
    ) d
    JOIN storage.instances i ON i.id = d.instanceinternalid
    LEFT JOIN LATERAL (
        SELECT
            bv.instanceguid,
            bv.appid,
            bv.blobstorageorg,
            bv.storageaccountnumber,
            array_agg(bv.id ORDER BY bv.created, bv.id) AS blobversions
        FROM storage.dataelementblobversions bv
        WHERE bv.instanceguid = i.alternateid
            AND bv.dataelementid = d.alternateid
            AND bv.detachedat IS NULL
        GROUP BY bv.instanceguid, bv.appid, bv.blobstorageorg, bv.storageaccountnumber
    ) v ON TRUE
    WHERE i.AltinnMainVersion >= 3
        -- Callers of this version resolve the account from org and number only and then delete the
        -- element. Leaving out just its named-account version would return the element as one without
        -- versions, so it would be deleted without its blob; such an element is left out entirely
        -- and stays until a _v2 caller cleans it up.
        AND NOT EXISTS (
            SELECT 1
            FROM storage.dataelementblobversions nv
            WHERE nv.instanceguid = i.alternateid
                AND nv.dataelementid = d.alternateid
                AND nv.detachedat IS NULL
                AND nv.storageaccountid IS NOT NULL
        );
END;
$BODY$;

CREATE OR REPLACE FUNCTION storage.readharddeleteddataelementsforcleanup_v2()
    RETURNS TABLE (
        id BIGINT,
        instance JSONB,
        element JSONB,
        blobversioninstanceguid UUID,
        blobversionappid TEXT,
        blobversionblobstorageorg TEXT,
        blobversionstorageaccountnumber INT,
        blobversionstorageaccountid SMALLINT,
        blobversions UUID[])
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
    -- Force nested loop join to avoid hash join on large instances table
    -- With only a small count of hard-deleted records, nested loop with index lookups is optimal
    SET LOCAL enable_hashjoin = off;
    SET LOCAL enable_mergejoin = off;

    RETURN QUERY
    SELECT
        i.id,
        i.instance,
        d.element,
        v.instanceguid,
        v.appid,
        v.blobstorageorg,
        v.storageaccountnumber,
        v.storageaccountid,
        COALESCE(v.blobversions, ARRAY[]::UUID[])
    FROM (
        -- Target index dataelements_isharddeleted
        SELECT instanceinternalid, de.alternateid, de.element
        FROM storage.dataelements de
        WHERE (de.element -> 'DeleteStatus' -> 'IsHardDeleted')::BOOLEAN
            AND (de.element -> 'DeleteStatus' ->> 'HardDeleted')::TIMESTAMPTZ <= NOW() - INTERVAL '7 days'
        OFFSET 0  -- Optimization fence: prevents subquery flattening which causes wrong join strategy
    ) d
    JOIN storage.instances i ON i.id = d.instanceinternalid
    LEFT JOIN LATERAL (
        SELECT
            bv.instanceguid,
            bv.appid,
            bv.blobstorageorg,
            bv.storageaccountnumber,
            bv.storageaccountid,
            array_agg(bv.id ORDER BY bv.created, bv.id) AS blobversions
        FROM storage.dataelementblobversions bv
        WHERE bv.instanceguid = i.alternateid
            AND bv.dataelementid = d.alternateid
            AND bv.detachedat IS NULL
        GROUP BY bv.instanceguid, bv.appid, bv.blobstorageorg, bv.storageaccountnumber, bv.storageaccountid
    ) v ON TRUE
    WHERE i.AltinnMainVersion >= 3;
END;
$BODY$;
