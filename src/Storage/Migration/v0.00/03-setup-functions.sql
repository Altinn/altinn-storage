-- instances ---------------------------------------------------

CREATE OR REPLACE FUNCTION storage.readInstance(_alternateId UUID)
    RETURNS TABLE (id BIGINT, instance JSONB, element JSONB)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
	SELECT i.id, i.instance, d.element FROM storage.instances i
		LEFT JOIN storage.dataelements d ON i.id = d.instanceInternalId
		WHERE i.alternateId = _alternateId
		ORDER BY d.id;

END;
$BODY$;

CREATE OR REPLACE PROCEDURE storage.insertInstance(_partyId BIGINT, _alternateId UUID, _instance JSONB)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
	INSERT INTO storage.instances(partyId, alternateId, instance) VALUES (_partyId, _alternateId, _instance);
END;
$BODY$;

CREATE OR REPLACE PROCEDURE storage.upsertInstance(_partyId BIGINT, _alternateId UUID, _instance JSONB)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
	INSERT INTO storage.instances(partyId, alternateId, instance) VALUES (_partyId, _alternateId, _instance) ON CONFLICT(alternateId) DO UPDATE SET instance = _instance;
END;
$BODY$;

CREATE OR REPLACE PROCEDURE storage.deleteInstance(_alternateId UUID)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
	DELETE FROM storage.instances WHERE alternateId = _alternateId;
END;
$BODY$;

-- dataElements --------------------------------------------------
CREATE OR REPLACE PROCEDURE storage.insertDataelement(_instanceInternalId BIGINT, _instanceGuid UUID, _alternateId UUID, _element JSONB)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
	INSERT INTO storage.dataElements(instanceInternalId, instanceGuid, alternateId, element) VALUES (_instanceInternalId, _instanceGuid, _alternateId, _element);
END;
$BODY$;

CREATE OR REPLACE PROCEDURE storage.deleteDataelement(_alternateId UUID)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
	DELETE FROM storage.dataElements WHERE alternateId = _alternateId;
END;
$BODY$;

CREATE OR REPLACE PROCEDURE storage.updateDataelement(_alternateId UUID, _element JSONB)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
	UPDATE storage.dataelements SET element = _element WHERE alternateId = _alternateId;
END;
$BODY$;

CREATE OR REPLACE FUNCTION storage.readDataelement(_alternateId UUID)
    RETURNS TABLE (element JSONB)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
	SELECT d.element FROM storage.dataElements d WHERE alternateId = _alternateId;

END;
$BODY$;

CREATE OR REPLACE FUNCTION storage.readAllDataelement(_instanceGuid UUID)
    RETURNS TABLE (element JSONB)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
	SELECT d.element FROM storage.dataelements d WHERE instanceGuid = _instanceGuid;

END;
$BODY$;

CREATE OR REPLACE FUNCTION storage.readAllForMultipleDataelement(_instanceGuid UUID)
    RETURNS TABLE (element JSONB)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
	SELECT d.element FROM storage.dataElements d WHERE instanceGuid = ANY (_instanceGuid);

END;
$BODY$;

-- instanceEvents ----------------------------------------------------------------
CREATE OR REPLACE PROCEDURE storage.insertInstanceEvent(_instance UUID, _alternateId UUID, _event JSONB)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
	INSERT INTO storage.instanceEvents(instance, alternateId, event) VALUES (_instance, _alternateId, _event);
END;
$BODY$;

CREATE OR REPLACE PROCEDURE storage.deleteInstanceEvent(_instance UUID)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
	DELETE FROM storage.instanceEvents WHERE instance = _instance;
END;
$BODY$;

CREATE OR REPLACE FUNCTION storage.readInstanceEvent(_alternateId UUID)
    RETURNS TABLE (event JSONB)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
	SELECT event FROM storage.instanceEvents WHERE alternateId = _alternateId;

END;
$BODY$;

CREATE OR REPLACE FUNCTION storage.filterInstanceEvent(_instance UUID, _from TIMESTAMP, _to TIMESTAMP, _eventType TEXT[])
    RETURNS TABLE (event JSONB)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
	SELECT instance, event
	FROM storage.instanceEvents
	WHERE instance = _instance
		AND (event->>'Created')::TIMESTAMP >= _from
		AND (event->>'Created')::TIMESTAMP <= _to
		AND (_eventType IS NULL OR event->>'EventType' ILIKE ANY (_eventType));

END;
$BODY$;