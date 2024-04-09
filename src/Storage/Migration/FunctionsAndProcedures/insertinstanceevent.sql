CREATE OR REPLACE PROCEDURE storage.insertinstanceevent(_instance UUID, _alternateid UUID, _event JSONB)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
	-- Dummy comment to verify that new migration solution works end to end
	INSERT INTO storage.instanceevents(instance, alternateid, event) VALUES (_instance, _alternateid, jsonb_strip_nulls(_event));
END;
$BODY$;