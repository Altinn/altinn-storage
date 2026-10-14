-- Replaces InstanceOwner in the instance json with the owner stored in storage.parties.
-- Instances whose party has no row yet keep the owner stored in their own json.
CREATE OR REPLACE FUNCTION storage.withinstanceowner(_instance JSONB, _partyid BIGINT)
    RETURNS JSONB
    LANGUAGE 'sql'
    STABLE
AS $BODY$
    SELECT COALESCE(
        (SELECT jsonb_set(
            _instance,
            '{InstanceOwner}',
            jsonb_strip_nulls(jsonb_build_object(
                'PartyId', p.partyid::TEXT,
                'PartyUuid', p.partyuuid,
                'PersonNumber', p.personnumber,
                'OrganisationNumber', p.organisationnumber,
                'Username', p.username,
                'ExternalIdentifier', p.externalidentifier)))
        FROM storage.parties p
        WHERE p.partyid = _partyid),
        _instance);
$BODY$;
