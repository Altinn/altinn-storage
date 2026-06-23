CREATE OR REPLACE FUNCTION storage.readharddeleteddataelementsforcleanup()
    RETURNS TABLE (
        id BIGINT,
        instance JSONB,
        element JSONB,
        currentblobversion UUID,
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
        d.currentblobversion,
        v.instanceguid,
        v.appid,
        v.blobstorageorg,
        v.storageaccountnumber,
        COALESCE(v.blobversions, ARRAY[]::UUID[])
    FROM (
        -- Target index dataelements_isharddeleted
        SELECT instanceinternalid, de.alternateid, de.element, de.currentblobversion
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
        WHERE bv.dataelementid = d.alternateid
            AND bv.attached IS NOT NULL
        GROUP BY bv.instanceguid, bv.appid, bv.blobstorageorg, bv.storageaccountnumber
    ) v ON TRUE
    WHERE i.AltinnMainVersion >= 3;
END;
$BODY$;
