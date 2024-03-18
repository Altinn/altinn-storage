CREATE OR REPLACE FUNCTION storage.readdeletedelements()
    RETURNS TABLE (id BIGINT, instance JSONB, element JSONB)
    LANGUAGE 'plpgsql'  
AS $BODY$
BEGIN
RETURN QUERY
    -- Use materialized cte to force join order
    -- Target index dataelements_deletestatus_harddeleted. This index has a where clause that must match
    -- the where clause in the data_elements query
    WITH data_elements AS MATERIALIZED
        (SELECT d.instanceinternalid, d.element FROM storage.dataelements d
            WHERE (d.element -> 'DeleteStatus' -> 'IsHardDeleted')::BOOLEAN
                AND (d.element -> 'DeleteStatus' ->> 'HardDeleted')::TIMESTAMPTZ <= NOW() - (7 ||' days')::interval
        )
    SELECT i.id, i.instance, data_elements.element FROM  data_elements JOIN storage.instances i ON i.id = data_elements.instanceinternalid; 
    END;
$BODY$;