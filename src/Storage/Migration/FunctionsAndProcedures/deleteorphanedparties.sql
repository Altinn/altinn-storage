-- Rows being upserted by a concurrent instance creation are locked and skipped. The delete runs in a
-- separate statement, with a new snapshot, so it sees instances created after the candidates were locked.
CREATE OR REPLACE FUNCTION storage.deleteorphanedparties(_limit INT)
    RETURNS INT
    LANGUAGE 'plpgsql'
AS $BODY$
DECLARE
    _candidates BIGINT[];
    _deleted INT;
BEGIN
    SELECT array_agg(c.partyid) INTO _candidates
    FROM (
        SELECT p.partyid FROM storage.parties p
        WHERE NOT EXISTS (SELECT 1 FROM storage.instances i WHERE i.partyid = p.partyid)
        LIMIT _limit
        FOR UPDATE SKIP LOCKED
    ) c;

    DELETE FROM storage.parties p
    WHERE p.partyid = ANY(_candidates)
        AND NOT EXISTS (SELECT 1 FROM storage.instances i WHERE i.partyid = p.partyid);

    GET DIAGNOSTICS _deleted = ROW_COUNT;
    RETURN _deleted;
END;
$BODY$;
