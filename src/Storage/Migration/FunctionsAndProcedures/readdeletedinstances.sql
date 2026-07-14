CREATE OR REPLACE FUNCTION storage.readdeletedinstances()
    RETURNS TABLE (instance JSONB)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY
    -- Make sure that part of the where clause is exactly as in filtered index instances_isharddeleted_and_more
    SELECT i.instance FROM storage.instances i
    WHERE (i.instance -> 'Status' -> 'IsHardDeleted')::BOOLEAN AND
    (
        NOT (i.instance -> 'Status' -> 'IsArchived')::BOOLEAN OR (i.instance -> 'CompleteConfirmations') IS NOT NULL
    )
    -- Keep hard-deleted instance rows past the workflow callback retry window so delete mutations can replay.
    AND (i.instance -> 'Status' ->> 'HardDeleted')::TIMESTAMPTZ <= (NOW() - INTERVAL '7 days')
    AND i.AltinnMainVersion >= 3;
END;
$BODY$;
