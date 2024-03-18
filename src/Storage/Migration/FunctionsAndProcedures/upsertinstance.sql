CREATE OR REPLACE PROCEDURE storage.upsertinstance(_partyid BIGINT, _alternateid UUID, _instance JSONB, _created TIMESTAMPTZ, _lastchanged TIMESTAMPTZ, _org TEXT, _appid TEXT, _taskid TEXT)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
	INSERT INTO storage.instances(partyid, alternateid, instance, created, lastchanged, org, appid, taskid) VALUES (_partyid, _alternateid, jsonb_strip_nulls(_instance), _created, _lastchanged, _org, _appid, _taskid)
    ON CONFLICT(alternateid) DO UPDATE SET instance = jsonb_strip_nulls(_instance), lastchanged = _lastchanged, taskid = _taskid;
END;
$BODY$;