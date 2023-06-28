-- instances ---------------------------------------------------

CREATE OR REPLACE FUNCTION storage.readinstance(_alternateid UUID)
    RETURNS TABLE (id BIGINT, instance JSONB, element JSONB)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
	SELECT i.id, i.instance, d.element FROM storage.instances i
		LEFT JOIN storage.dataelements d ON i.id = d.instanceinternalid
		WHERE i.alternateid = _alternateid
		ORDER BY d.id;

END;
$BODY$;

CREATE OR REPLACE FUNCTION storage.readinstancenoelements(_alternateid UUID)
    RETURNS TABLE (id BIGINT, instance JSONB)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
	SELECT i.id, i.instance FROM storage.instances i
		WHERE i.alternateid = _alternateid;
END;
$BODY$;

CREATE OR REPLACE PROCEDURE storage.insertinstance(_partyid BIGINT, _alternateid UUID, _instance JSONB, _created TIMESTAMPTZ, _lastchanged TIMESTAMPTZ, _org TEXT, _appid TEXT, _taskid TEXT)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
	INSERT INTO storage.instances(partyid, alternateid, instance, created, lastchanged, org, appid, taskid) VALUES (_partyid, _alternateid, jsonb_strip_nulls(_instance), _created, _lastchanged, _org, _appid, _taskid);
END;
$BODY$;

CREATE OR REPLACE PROCEDURE storage.upsertinstance(_partyid BIGINT, _alternateid UUID, _instance JSONB, _created TIMESTAMPTZ, _lastchanged TIMESTAMPTZ, _org TEXT, _appid TEXT, _taskid TEXT)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
	INSERT INTO storage.instances(partyid, alternateid, instance, created, lastchanged, org, appid, taskid) VALUES (_partyid, _alternateid, jsonb_strip_nulls(_instance), _created, _lastchanged, _org, _appid, _taskid)
    ON CONFLICT(alternateid) DO UPDATE SET instance = jsonb_strip_nulls(_instance), lastchanged = _lastchanged, taskid = _taskid;
END;
$BODY$;

CREATE OR REPLACE PROCEDURE storage.deleteinstance(_alternateid UUID)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
	DELETE FROM storage.instances WHERE alternateid = _alternateid;
END;
$BODY$;

-- dataelements --------------------------------------------------
CREATE OR REPLACE PROCEDURE storage.insertdataelement(_instanceinternalid BIGINT, _instanceGuid UUID, _alternateid UUID, _element JSONB)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
	INSERT INTO storage.dataelements(instanceinternalid, instanceGuid, alternateid, element) VALUES (_instanceinternalid, _instanceGuid, _alternateid, jsonb_strip_nulls(_element));
END;
$BODY$;

CREATE OR REPLACE PROCEDURE storage.upsertdataelement(_instanceinternalid BIGINT, _instanceGuid UUID, _alternateid UUID, _element JSONB)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
	INSERT INTO storage.dataelements(instanceinternalid, instanceGuid, alternateid, element) VALUES (_instanceinternalid, _instanceGuid, _alternateid, jsonb_strip_nulls(_element))
    ON CONFLICT(alternateid) DO UPDATE SET element = jsonb_strip_nulls(_element);
END;
$BODY$;

CREATE OR REPLACE PROCEDURE storage.deletedataelement(_alternateid UUID)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
	DELETE FROM storage.dataelements WHERE alternateid = _alternateid;
END;
$BODY$;

CREATE OR REPLACE PROCEDURE storage.updatedataelement(_alternateid UUID, _element JSONB)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
	UPDATE storage.dataelements SET element = jsonb_strip_nulls(_element) WHERE alternateid = _alternateid;
END;
$BODY$;

CREATE OR REPLACE FUNCTION storage.readdataelement(_alternateid UUID)
    RETURNS TABLE (element JSONB)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
	SELECT d.element FROM storage.dataelements d WHERE alternateid = _alternateid;

END;
$BODY$;

CREATE OR REPLACE FUNCTION storage.readalldataelement(_instanceGuid UUID)
    RETURNS TABLE (element JSONB)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
	SELECT d.element FROM storage.dataelements d WHERE instanceGuid = _instanceGuid;

END;
$BODY$;

CREATE OR REPLACE FUNCTION storage.readallformultipledataelement(_instanceGuid UUID)
    RETURNS TABLE (element JSONB)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
	SELECT d.element FROM storage.dataelements d WHERE instanceGuid = ANY (_instanceGuid);

END;
$BODY$;

-- instanceevents ----------------------------------------------------------------
CREATE OR REPLACE PROCEDURE storage.insertinstanceevent(_instance UUID, _alternateid UUID, _event JSONB)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
	INSERT INTO storage.instanceevents(instance, alternateid, event) VALUES (_instance, _alternateid, jsonb_strip_nulls(_event));
END;
$BODY$;

CREATE OR REPLACE PROCEDURE storage.deleteinstanceevent(_instance UUID)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
	DELETE FROM storage.instanceevents WHERE instance = _instance;
END;
$BODY$;

CREATE OR REPLACE FUNCTION storage.readinstanceevent(_alternateid UUID)
    RETURNS TABLE (event JSONB)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
	SELECT event FROM storage.instanceevents WHERE alternateid = _alternateid;

END;
$BODY$;

CREATE OR REPLACE FUNCTION storage.filterinstanceevent(_instance UUID, _from TIMESTAMP, _to TIMESTAMP, _eventtype TEXT[])
    RETURNS TABLE (event JSONB)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
	SELECT instance, event
	FROM storage.instanceevents
	WHERE instance = _instance
		AND (event->>'Created')::TIMESTAMP >= _from
		AND (event->>'Created')::TIMESTAMP <= _to
		AND (_eventtype IS NULL OR event->>'eventtype' ILIKE ANY (_eventtype));

END;
$BODY$;