-- An instance owner resolved from Register (identified by its PartyUuid) is stored once per party in
-- storage.parties, leaving only the PartyId in the instance json. Other owners are stored as given.
CREATE OR REPLACE PROCEDURE storage.insertinstance_v4(_partyid BIGINT, _alternateid UUID, _instance JSONB, _created TIMESTAMPTZ, _lastchanged TIMESTAMPTZ, _org TEXT, _appid TEXT, _taskid TEXT, _altinnmainversion INT, _confirmed BOOLEAN)
    LANGUAGE 'plpgsql'
AS $BODY$
DECLARE
    _owner JSONB := _instance -> 'InstanceOwner';
BEGIN
    IF _owner ->> 'PartyUuid' IS NOT NULL THEN
        CALL storage.upsertparty(
            _partyid,
            (_owner ->> 'PartyUuid')::UUID,
            _owner ->> 'PersonNumber',
            _owner ->> 'OrganisationNumber',
            _owner ->> 'Username',
            _owner ->> 'ExternalIdentifier');
        _instance := jsonb_set(_instance, '{InstanceOwner}', jsonb_build_object('PartyId', _owner -> 'PartyId'));
    END IF;

    INSERT INTO storage.instances(partyid, alternateid, instance, created, lastchanged, org, appid, taskid, altinnmainversion, confirmed)
        VALUES (_partyid, _alternateid, jsonb_strip_nulls(_instance), _created, _lastchanged, _org, _appid, _taskid, _altinnmainversion, _confirmed);
END;
$BODY$;
