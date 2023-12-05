-- instances ---------------------------------------------------
CREATE OR REPLACE FUNCTION storage.readinstancefromquery(
    _appId TEXT DEFAULT NULL,
    _appIds TEXT[] DEFAULT NULL,
    _archiveReference TEXT DEFAULT NULL,
    _continue_idx BIGINT DEFAULT 0,
    _created_eq TIMESTAMPTZ DEFAULT NULL,
    _created_gt TIMESTAMPTZ DEFAULT NULL,
    _created_gte TIMESTAMPTZ DEFAULT NULL,
    _created_lt TIMESTAMPTZ DEFAULT NULL,
    _created_lte TIMESTAMPTZ DEFAULT NULL,
    _dueBefore_eq TEXT DEFAULT NULL,
    _dueBefore_gt TEXT DEFAULT NULL,
    _dueBefore_gte TEXT DEFAULT NULL,
    _dueBefore_lt TEXT DEFAULT NULL,
    _dueBefore_lte TEXT DEFAULT NULL,
    _excludeConfirmedBy JSONB[] DEFAULT NULL,
    _instanceOwner_partyId INTEGER DEFAULT NULL,
    _instanceOwner_partyIds INTEGER[] DEFAULT NULL,
    _lastChanged_eq TIMESTAMPTZ DEFAULT NULL,
    _lastChanged_gt TIMESTAMPTZ DEFAULT NULL,
    _lastChanged_gte TIMESTAMPTZ DEFAULT NULL,
    _lastChanged_idx TIMESTAMPTZ DEFAULT NULL,
    _lastChanged_lt TIMESTAMPTZ DEFAULT NULL,
    _lastChanged_lte TIMESTAMPTZ DEFAULT NULL,
    _org TEXT DEFAULT NULL,
    _process_currentTask TEXT DEFAULT NULL,
    _process_ended_eq TEXT DEFAULT NULL,
    _process_ended_gt TEXT DEFAULT NULL,
    _process_ended_gte TEXT DEFAULT NULL,
    _process_ended_lt TEXT DEFAULT NULL,
    _process_ended_lte TEXT DEFAULT NULL,
    _process_isComplete BOOLEAN DEFAULT NULL,
    _size INTEGER DEFAULT 100,
    _sort_ascending BOOLEAN DEFAULT FALSE,
    _status_isActiveOrSoftDeleted BOOLEAN  DEFAULT NULL,
    _status_isArchived BOOLEAN DEFAULT NULL,
    _status_isArchivedOrSoftDeleted BOOLEAN DEFAULT NULL,
    _status_isHardDeleted BOOLEAN DEFAULT NULL,
    _status_isSoftDeleted BOOLEAN DEFAULT NULL,
    _visibleAfter_eq TEXT DEFAULT NULL,
    _visibleAfter_gt TEXT DEFAULT NULL,
    _visibleAfter_gte TEXT DEFAULT NULL,
    _visibleAfter_lt TEXT DEFAULT NULL,
    _visibleAfter_lte TEXT DEFAULT NULL
    )
    RETURNS TABLE (id BIGINT, instance JSONB, element JSONB)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
    IF _sort_ascending IS NULL THEN
        _sort_ascending := false;
    END IF;

    RETURN QUERY
    WITH filteredInstances AS
    (
        SELECT i.id, i.instance, i.lastchanged FROM storage.instances i
        WHERE 1 = 1
            AND (_continue_idx <= 0 OR
                (_continue_idx > 0 AND _sort_ascending = true  AND (i.lastchanged > _lastChanged_idx OR (i.lastchanged = _lastChanged_idx AND i.id > _continue_idx))) OR
                (_continue_idx > 0 AND _sort_ascending = false AND (i.lastchanged < _lastChanged_idx OR (i.lastchanged = _lastChanged_idx AND i.id < _continue_idx))))
            AND (_appId IS NULL OR i.appid = _appId)
            AND (_appIds IS NULL OR i.appid = ANY(_appIds))
            AND (_archiveReference IS NULL OR i.instance ->> 'Id' like '%' || _archiveReference)		
            AND (_created_gte IS NULL OR i.created >= _created_gte)
            AND (_created_gt  IS NULL OR i.created >  _created_gt)
            AND (_created_lte IS NULL OR i.created <= _created_lte)
            AND (_created_lt  IS NULL OR i.created <  _created_lt)
            AND (_created_eq  IS NULL OR i.created =  _created_eq)
            AND (_dueBefore_gte IS NULL OR i.instance ->> 'DueBefore' >= _dueBefore_gte)
            AND (_dueBefore_gt  IS NULL OR i.instance ->> 'DueBefore' >  _dueBefore_gt)
            AND (_dueBefore_lte IS NULL OR i.instance ->> 'DueBefore' <= _dueBefore_lte)
            AND (_dueBefore_lt  IS NULL OR i.instance ->> 'DueBefore' <  _dueBefore_lt)
            AND (_dueBefore_eq   IS NULL OR i.instance ->> 'DueBefore' =  _dueBefore_eq)
            AND (_excludeConfirmedBy IS NULL OR i.instance -> 'CompleteConfirmations' IS NULL OR NOT i.instance -> 'CompleteConfirmations' @> ANY (_excludeConfirmedBy))
            AND (_instanceOwner_partyId IS NULL OR partyId = _instanceOwner_partyId)
            AND (_instanceOwner_partyIds IS NULL OR partyId = ANY(_instanceOwner_partyIds))
            AND (_lastChanged_gte IS NULL OR i.lastchanged >= _lastChanged_gte)
            AND (_lastChanged_gt  IS NULL OR i.lastchanged >  _lastChanged_gt)
            AND (_lastChanged_lte IS NULL OR i.lastchanged <= _lastChanged_lte)
            AND (_lastChanged_lt  IS NULL OR i.lastchanged <  _lastChanged_lt)
            AND (_lastChanged_eq  IS NULL OR i.lastchanged =  _lastChanged_eq)
            AND (_org IS NULL OR i.org = _org)
            AND (_process_currentTask IS NULL OR i.instance -> 'Process' -> 'CurrentTask' ->> 'ElementId' = _process_currentTask)
            AND (_process_ended_gte IS NULL OR i.instance -> 'Process' ->> 'Ended' >= _process_ended_gte)
            AND (_process_ended_gt  IS NULL OR i.instance -> 'Process' ->> 'Ended' >  _process_ended_gt)
            AND (_process_ended_lte IS NULL OR i.instance -> 'Process' ->> 'Ended' <= _process_ended_lte)
            AND (_process_ended_lt  IS NULL OR i.instance -> 'Process' ->> 'Ended' <  _process_ended_lt)
            AND (_process_ended_eq  IS NULL OR i.instance -> 'Process' ->> 'Ended' =  _process_ended_eq)
            AND (_process_isComplete IS NULL OR (_process_isComplete = TRUE AND i.instance -> 'Process' -> 'Ended' IS NOT NULL) OR (_process_isComplete = FALSE AND i.instance -> 'Process' -> 'CurrentTask' IS NOT NULL))
            AND ((_status_isActiveOrSoftDeleted IS NULL OR _status_isActiveOrSoftDeleted = false) OR ((i.instance -> 'Status' -> 'IsArchived')::boolean = false OR (i.instance -> 'Status' -> 'IsSoftDeleted')::boolean = true))           
            AND (_status_isArchived IS NULL OR  (i.instance -> 'Status' -> 'IsArchived')::boolean = _status_isArchived)
            AND ((_status_isArchivedOrSoftDeleted IS NULL OR _status_isArchivedOrSoftDeleted = false) OR ((i.instance -> 'Status' -> 'IsArchived')::boolean = true OR (i.instance -> 'Status' -> 'IsSoftDeleted')::boolean = true))                
            AND (_status_isHardDeleted IS NULL OR  (i.instance -> 'Status' -> 'IsHardDeleted')::boolean = _status_isHardDeleted)
            AND (_status_isSoftDeleted IS NULL OR  (i.instance -> 'Status' -> 'IsSoftDeleted')::boolean = _status_isSoftDeleted)                     
            AND (_visibleAfter_gte IS NULL OR i.instance ->> 'VisibleAfter' >= _visibleAfter_gte)
            AND (_visibleAfter_gt  IS NULL OR i.instance ->> 'VisibleAfter' >  _visibleAfter_gt)
            AND (_visibleAfter_lte IS NULL OR i.instance ->> 'VisibleAfter' <= _visibleAfter_lte)
            AND (_visibleAfter_lt  IS NULL OR i.instance ->> 'VisibleAfter' <  _visibleAfter_lt)
            AND (_visibleAfter_eq  IS NULL OR i.instance ->> 'VisibleAfter' =  _visibleAfter_eq)
        ORDER BY
            (CASE WHEN _sort_ascending = true  THEN i.lastChanged END) ASC,
            (CASE WHEN _sort_ascending = false THEN i.lastChanged END) DESC,
            i.id
        FETCH FIRST _size ROWS ONLY
    )
        SELECT filteredInstances.id, filteredInstances.instance, d.element FROM filteredInstances LEFT JOIN storage.dataelements d ON filteredInstances.id = d.instanceInternalId
        ORDER BY
            (CASE WHEN _sort_ascending = true  THEN filteredInstances.lastChanged END) ASC,
            (CASE WHEN _sort_ascending = false THEN filteredInstances.lastChanged END) DESC,
            filteredInstances.id;
END;
$BODY$;

CREATE OR REPLACE FUNCTION storage.readdeletedinstances()
    RETURNS TABLE (instance JSONB)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY
    -- Make sure that part of the where clause is exactly as in filtered index instances_isharddeleted_confirmed
    SELECT i.instance FROM storage.instances i
    WHERE (i.instance -> 'Status' -> 'IsHardDeleted')::BOOLEAN
        AND (i.instance -> 'CompleteConfirmations') IS NOT NULL
        AND (i.instance -> 'Status' ->> 'HardDeleted')::TIMESTAMPTZ <= (NOW() - (7 ||' days')::INTERVAL);
END;
$BODY$;

CREATE OR REPLACE FUNCTION storage.readdeletedelements()
    RETURNS TABLE (id BIGINT, instance JSONB, element JSONB)
    LANGUAGE 'plpgsql'  
AS $BODY$
BEGIN
RETURN QUERY
    -- Use materialized cte to force join order
    -- Target index dataelements_deletestatus_harddeleted. This index has a where clause that must match
    -- the where clause in the data_elements query
    WITH data_elements AS MATERIALIZED
        (SELECT d.instanceinternalid, d.element FROM storage.dataelements d
            WHERE (d.element -> 'DeleteStatus' -> 'IsHardDeleted')::BOOLEAN
                AND (d.element -> 'DeleteStatus' ->> 'HardDeleted')::TIMESTAMPTZ <= NOW() - (7 ||' days')::interval
        )
    SELECT i.id, i.instance, data_elements.element FROM  data_elements JOIN storage.instances i ON i.id = data_elements.instanceinternalid; 
    END;
$BODY$;

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

CREATE OR REPLACE FUNCTION storage.deleteinstance(_alternateid UUID)
    RETURNS INT
    LANGUAGE 'plpgsql'	
AS $BODY$
DECLARE
    _deleteCount INTEGER;
BEGIN
    DELETE FROM storage.instances WHERE alternateid = _alternateid;
    GET DIAGNOSTICS _deleteCount = ROW_COUNT;
    RETURN _deleteCount;
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

CREATE OR REPLACE FUNCTION storage.deletedataelement(_alternateid UUID)
    RETURNS INT
    LANGUAGE 'plpgsql'	
AS $BODY$
DECLARE
    _deleteCount INTEGER;
BEGIN
    DELETE FROM storage.dataelements WHERE alternateid = _alternateid;
    GET DIAGNOSTICS _deleteCount = ROW_COUNT;
    RETURN _deleteCount;
END;
$BODY$;

CREATE OR REPLACE FUNCTION storage.deletedataelements(_instanceguid UUID)
    RETURNS INT
    LANGUAGE 'plpgsql'	
AS $BODY$
DECLARE
    _deleteCount INTEGER;
BEGIN
    DELETE FROM storage.dataelements d
        USING storage.instances i
        WHERE i.alternateid = d.instanceguid AND i.alternateid = _instanceguid;
    GET DIAGNOSTICS _deleteCount = ROW_COUNT;
    RETURN _deleteCount;
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

CREATE OR REPLACE FUNCTION storage.readallformultipledataelement(_instanceGuid UUID[])
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

CREATE OR REPLACE FUNCTION storage.deleteinstanceevent(_instance UUID)
    RETURNS INT
    LANGUAGE 'plpgsql'	
AS $BODY$
DECLARE
    _deleteCount INTEGER;
BEGIN
    DELETE FROM storage.instanceevents WHERE instance = _instance;
    GET DIAGNOSTICS _deleteCount = ROW_COUNT;
    RETURN _deleteCount;
END;
$BODY$;

CREATE OR REPLACE FUNCTION storage.readinstanceevent(_alternateid UUID)
    RETURNS TABLE (event JSONB)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
    SELECT ie.event FROM storage.instanceevents ie WHERE alternateid = _alternateid;

END;
$BODY$;

CREATE OR REPLACE FUNCTION storage.filterinstanceevent(_instance UUID, _from TIMESTAMP, _to TIMESTAMP, _eventtype TEXT[])
    RETURNS TABLE (event JSONB)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
    SELECT ie.event
    FROM storage.instanceevents ie
    WHERE instance = _instance
        AND (ie.event->>'Created')::TIMESTAMP >= _from
        AND (ie.event->>'Created')::TIMESTAMP <= _to
        AND (_eventtype IS NULL OR ie.event->>'EventType' = ANY (_eventtype))
    ORDER BY ie.event->'Created';
END;
$BODY$;