
CREATE OR REPLACE PROCEDURE storage.insertinstanceevents(_instance UUID, _events JSONB)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
	-- Dummy comment to verify that new migration solution works end to end
	INSERT INTO storage.instanceevents (instance, alternateid, event)
	SELECT _instance, (evs->>'Id')::UUID, jsonb_strip_nulls(evs) 
	FROM jsonb_array_elements(_events) evs;
END;
$BODY$;