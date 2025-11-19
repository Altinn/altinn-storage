CREATE OR REPLACE FUNCTION storage.readdeletedelements_v2()
    RETURNS TABLE (id BIGINT, instance JSONB, element JSONB)
    LANGUAGE 'plpgsql'  
AS $BODY$
BEGIN
    -- Force nested loop join to avoid hash join on large instances table
    -- With only a small count of hard-deleted records, nested loop with index lookups is optimal
    SET LOCAL enable_hashjoin = off;
    SET LOCAL enable_mergejoin = off;

    RETURN QUERY
    SELECT i.id, i.instance, d.element 
    FROM (
        -- Target index dataelements_isharddeleted
        SELECT instanceinternalid, de.element
        FROM storage.dataelements de
        WHERE (de.element -> 'DeleteStatus' -> 'IsHardDeleted')::BOOLEAN
            AND (de.element -> 'DeleteStatus' ->> 'HardDeleted')::TIMESTAMPTZ <= NOW() - INTERVAL '7 days'
        OFFSET 0  -- Optimization fence: prevents subquery flattening which causes wrong join strategy
    ) d
    JOIN storage.instances i ON i.id = d.instanceinternalid
    WHERE i.AltinnMainVersion >= 3;
END;
$BODY$;