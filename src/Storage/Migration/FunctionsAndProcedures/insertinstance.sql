CREATE OR REPLACE PROCEDURE storage.insertinstance_v2(_partyid BIGINT, _alternateid UUID, _instance JSONB, _created TIMESTAMPTZ, _lastchanged TIMESTAMPTZ, _org TEXT, _appid TEXT, _taskid TEXT, _altinnmainversion INT)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
    INSERT INTO storage.instances(partyid, alternateid, instance, created, lastchanged, org, appid, taskid, altinnmainversion)
        VALUES (_partyid, _alternateid, jsonb_strip_nulls(_instance), _created, _lastchanged, _org, _appid, _taskid, _altinnmainversion);
END;
$BODY$;