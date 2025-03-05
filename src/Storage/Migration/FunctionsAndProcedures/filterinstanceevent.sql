CREATE OR REPLACE FUNCTION storage.filterinstanceevent(_instance UUID, _from TIMESTAMPTZ, _to TIMESTAMPTZ, _eventtype TEXT[])
    RETURNS TABLE (event JSONB)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
    SELECT ie.event
    FROM storage.instanceevents ie
    WHERE instance = _instance
        AND (ie.event->>'Created')::TIMESTAMP >= _from
        AND (ie.event->>'Created')::TIMESTAMP <= _to
        AND (_eventtype IS NULL OR ie.event->>'EventType' = ANY (_eventtype))
    ORDER BY ie.event->'Created';
END;
$BODY$;