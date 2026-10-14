CREATE OR REPLACE PROCEDURE storage.upsertparty(
    _partyid BIGINT,
    _partyuuid UUID,
    _personnumber TEXT,
    _organisationnumber TEXT,
    _username TEXT,
    _externalidentifier TEXT)
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
    INSERT INTO storage.parties AS p (partyid, partyuuid, personnumber, organisationnumber, username, externalidentifier)
        VALUES (_partyid, _partyuuid, _personnumber, _organisationnumber, _username, _externalidentifier)
    ON CONFLICT (partyid) DO UPDATE SET
        partyuuid = EXCLUDED.partyuuid,
        personnumber = EXCLUDED.personnumber,
        organisationnumber = EXCLUDED.organisationnumber,
        username = EXCLUDED.username,
        externalidentifier = EXCLUDED.externalidentifier,
        updated = now()
    WHERE (p.partyuuid, p.personnumber, p.organisationnumber, p.username, p.externalidentifier)
        IS DISTINCT FROM (EXCLUDED.partyuuid, EXCLUDED.personnumber, EXCLUDED.organisationnumber, EXCLUDED.username, EXCLUDED.externalidentifier);
END;
$BODY$;
