-- instances ---------------------------------------------------
CREATE OR REPLACE FUNCTION storage.readinstancefromquery(
_appId TEXT,
_appIds TEXT[],
_archiveReference TEXT,
_continue_idx BIGINT,
_created_eq TIMESTAMPTZ,
_created_gt TIMESTAMPTZ,
_created_gte TIMESTAMPTZ,
_created_lt TIMESTAMPTZ,
_created_lte TIMESTAMPTZ,
_dueBefore_eq TEXT,
_dueBefore_gt TEXT,
_dueBefore_gte TEXT,
_dueBefore_lt TEXT,
_dueBefore_lte TEXT,
_excludeConfirmedBy TEXT[],
_instanceOwner_partyId INTEGER,
_instanceOwner_partyIds INTEGER[],
_lastChanged_eq TIMESTAMPTZ,
_lastChanged_gt TIMESTAMPTZ,
_lastChanged_gte TIMESTAMPTZ,
_lastChanged_lt TIMESTAMPTZ,
_lastChanged_lte TIMESTAMPTZ,
_org TEXT,
_process_currentTask TEXT,
_process_ended_eq TEXT,
_process_ended_gt TEXT,
_process_ended_gte TEXT,
_process_ended_lt TEXT,
_process_ended_lte TEXT,
_process_isComplete BOOLEAN,
_size INTEGER,
_sortBy BOOLEAN,
_status_isActiveOrSoftDeleted BOOLEAN,
_status_isArchived BOOLEAN,
_status_isArchivedOrSoftDeleted BOOLEAN,
_status_isHardDeleted BOOLEAN,
_status_isSoftDeleted BOOLEAN,
_visibleAfter_eq TEXT,
_visibleAfter_gt TEXT,
_visibleAfter_gte TEXT,
_visibleAfter_lt TEXT,
_visibleAfter_lte TEXT
    )
    RETURNS TABLE (id BIGINT, instance JSONB, element JSONB)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY
    WITH filteredInstances AS
    (
        SELECT i.id, i.instance FROM storage.instances i
        WHERE 1 = 1
            AND (_continue_idx <= 0 OR i.id > _continue_idx)
			AND (_appId IS NULL OR i.appid = _appId)
            AND (_appIds IS NULL OR i.appid = ANY(_appIds))
			AND (_archiveReference IS NULL OR i.instance ->> 'ArchiveReference' like '%' || _archiveReference)		
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
			AND (_excludeConfirmedBy IS NULL OR NOT i.instance -> 'CompleteConfirmations' -> 'StakeholderId' ?| _excludeConfirmedBy)
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
            AND (_status_isActiveOrSoftDeleted IS NULL OR ((i.instance -> 'Status' -> 'IsArchived')::boolean = false OR (i.instance -> 'Status' -> 'IsSoftDeleted')::boolean = true))           
            AND (_status_isArchived IS NULL OR  (i.instance -> 'Status' -> 'IsArchived')::boolean = _status_isArchived)
            AND (_status_isArchivedOrSoftDeleted IS NULL OR ((i.instance -> 'Status' -> 'IsArchived')::boolean = true OR (i.instance -> 'Status' -> 'IsSoftDeleted')::boolean = true))                
            AND (_status_isHardDeleted IS NULL OR  (i.instance -> 'Status' -> 'IsHardDeleted')::boolean = _status_isHardDeleted)
            AND (_status_isSoftDeleted IS NULL OR  (i.instance -> 'Status' -> 'IsSoftDeleted')::boolean = _status_isSoftDeleted)                     
            AND (_visibleAfter_gte IS NULL OR i.instance ->> 'VisibleAfter' >= _visibleAfter_gte)
            AND (_visibleAfter_gt  IS NULL OR i.instance ->> 'VisibleAfter' >  _visibleAfter_gt)
            AND (_visibleAfter_lte IS NULL OR i.instance ->> 'VisibleAfter' <= _visibleAfter_lte)
            AND (_visibleAfter_lt  IS NULL OR i.instance ->> 'VisibleAfter' <  _visibleAfter_lt)
            AND (_visibleAfter_eq  IS NULL OR i.instance ->> 'VisibleAfter' =  _visibleAfter_eq)
        ORDER BY i.id
        FETCH FIRST _size ROWS ONLY
    )
        SELECT filteredInstances.id, filteredInstances.instance, d.element FROM filteredInstances LEFT JOIN storage.dataelements d ON filteredInstances.id = d.instanceInternalId
        ORDER BY (CASE WHEN _sortBy IS NOT NULL AND _sortby = true THEN filteredInstances.id END) desc,
			     (CASE WHEN _sortBy IS NULL OR _sortby = false THEN filteredInstances.id END) desc;
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