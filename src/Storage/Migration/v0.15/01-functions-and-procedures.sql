-- This script is autogenerated from the tool DbTools. Do not edit manually.

-- inserta1migrationstate.sql:
CREATE OR REPLACE PROCEDURE storage.inserta1migrationstate (_a1archiveReference BIGINT)
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
	INSERT INTO storage.a1migrationstate (a1archivereference) VALUES
		(_a1archiveReference)
	ON CONFLICT (a1archivereference) DO NOTHING;
END;
$BODY$;

-- insertdataelement.sql:
CREATE OR REPLACE FUNCTION storage.insertdataelement_v2(
	IN _instanceinternalid bigint,
	IN _instanceguid uuid,
	IN _alternateid uuid,
	IN _element jsonb)
    RETURNS TABLE (updatedElement JSONB)
    LANGUAGE plpgsql
AS $BODY$
BEGIN
    -- Make sure that lastChanged has the Postgres precision (6 digits). The timestamp from C# DateTime and then json serialize has 7 digits
    _element := _element || jsonb_set('{"LastChanged":""}', '{LastChanged}', to_jsonb(REPLACE(((_element ->> 'LastChanged')::TIMESTAMPTZ AT TIME ZONE 'UTC')::TEXT, ' ', 'T') || 'Z'));

    IF _element ->> 'IsRead' = 'false' THEN
        UPDATE storage.instances
        SET instance = jsonb_set(instance, '{Status, ReadStatus}', '2')
        WHERE id = _instanceinternalid AND instance -> 'Status' ->> 'ReadStatus' = '1';
    END IF;

    UPDATE storage.instances
        SET lastchanged = (_element ->> 'LastChanged')::TIMESTAMPTZ,
            instance = instance
                || jsonb_set('{"LastChanged":""}', '{LastChanged}', to_jsonb(_element ->> 'LastChanged'))
                || jsonb_set('{"LastChangedBy":""}', '{LastChangedBy}', to_jsonb(_element ->> 'LastChangedBy'))
        WHERE id = _instanceinternalid;

    RETURN QUERY
        INSERT INTO storage.dataelements(instanceinternalid, instanceGuid, alternateid, element) VALUES (_instanceinternalid, _instanceGuid, _alternateid, jsonb_strip_nulls(_element))
            RETURNING element;
END;
$BODY$;

-- readinstancenoelements.sql:
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

-- updatemigrationstatecompleted.sql:
CREATE OR REPLACE PROCEDURE storage.updatemigrationstatecompleted (_instanceguid UUID)
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
	UPDATE storage.a1migrationstate SET completed = now()
		WHERE instanceguid = _instanceguid;
	UPDATE storage.a2migrationstate SET completed = now()
		WHERE instanceguid = _instanceguid;
END;
$BODY$;

-- insertinstanceevents.sql:

CREATE OR REPLACE PROCEDURE storage.insertinstanceevents(_events JSONB)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
	-- Dummy comment to verify that new migration solution works end to end
	INSERT INTO storage.instanceevents (instance, alternateid, event)
	SELECT (evs->>'Instance')::UUID, (evs->>'AlternateId')::UUID, jsonb_strip_nulls((evs->>'Event')::JSONB) 
	FROM jsonb_array_elements(_events) evs;
END;
$BODY$;

-- reada2codelist.sql:
CREATE OR REPLACE FUNCTION storage.reada2codelist(_name TEXT, _language TEXT)
    RETURNS TABLE (codelist TEXT)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY
    SELECT c.codelist FROM storage.a2codelists c
        WHERE name = _name AND language = _language
        ORDER BY version DESC LIMIT 1;
END;
$BODY$;

-- updatedataelement.sql:
CREATE OR REPLACE FUNCTION storage.updatedataelement_v2(_dataelementGuid UUID, _instanceGuid UUID, _elementChanges JSONB, _instanceChanges JSONB, _isReadChangedToFalse BOOL, _lastChanged TIMESTAMPTZ)
    RETURNS TABLE (updatedElement JSONB)
    LANGUAGE 'plpgsql'	
AS $BODY$
DECLARE
    _lastChanged6digits TEXT;
BEGIN
    IF _lastChanged IS NOT NULL
    THEN
        -- Make sure that lastChanged has the Postgres precision (6 digits). The timestamp from C# DateTime and then json serialize has 7 digits
        _lastChanged6digits = REPLACE((_lastChanged AT TIME ZONE 'UTC')::TEXT, ' ', 'T') || 'Z';
        _elementChanges := _elementChanges || jsonb_set('{"LastChanged":""}', '{LastChanged}', to_jsonb(_lastChanged6digits));
    END IF;

    IF _isReadChangedToFalse = true AND
        (SELECT COUNT(*) FROM storage.dataelements
            WHERE element -> 'IsRead' = 'true' AND instanceguid = _instanceGuid AND alternateid <> _dataelementGuid) = 0
    THEN
        UPDATE storage.instances
        SET instance = jsonb_set(instance, '{Status, ReadStatus}', '0')
        WHERE alternateid = _instanceGuid AND instance -> 'Status' ->> 'ReadStatus' = '1';
    END IF;

    IF _lastChanged IS NOT NULL
    THEN
        UPDATE storage.instances
            SET lastchanged = _lastChanged,
                instance = instance || _instanceChanges || jsonb_set('{"LastChanged":""}', '{LastChanged}', to_jsonb(_lastChanged6digits))   
            WHERE alternateid = _instanceGuid;
    END IF;

    RETURN QUERY
        UPDATE storage.dataelements SET element = element || _elementChanges WHERE alternateid = _dataelementGuid
            RETURNING element;
END;
$BODY$;

-- reada2image.sql:
CREATE OR REPLACE FUNCTION storage.reada2image(_name TEXT)
    RETURNS TABLE (image BYTEA)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY
    SELECT
        CASE
            WHEN c.image IS NOT NULL THEN c.image
            ELSE (SELECT p.image FROM storage.a2images p WHERE p.id = c.parentid)
        END
    FROM storage.a2images c WHERE c.name = _name; 
END;
$BODY$;

-- readinstanceevent.sql:
CREATE OR REPLACE FUNCTION storage.readinstanceevent(_alternateid UUID)
    RETURNS TABLE (event JSONB)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
    SELECT ie.event FROM storage.instanceevents ie WHERE alternateid = _alternateid;

END;
$BODY$;

-- readdataelement.sql:
CREATE OR REPLACE FUNCTION storage.readdataelement(_alternateid UUID)
    RETURNS TABLE (element JSONB)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
    SELECT d.element FROM storage.dataelements d WHERE alternateid = _alternateid;

END;
$BODY$;

-- deleteinstance.sql:
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

-- readinstance.sql:
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


-- updatea1migrationstatestarted.sql:
CREATE OR REPLACE PROCEDURE storage.updatea1migrationstatestarted (_a1archivereference BIGINT, _instanceguid UUID)
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
	UPDATE storage.a1migrationstate SET instanceguid = _instanceguid, started = now()
		WHERE a1archivereference = _a1archivereference;
END;
$BODY$;

-- updatea2migrationstatestarted.sql:
CREATE OR REPLACE PROCEDURE storage.updatea2migrationstatestarted (_a2archivereference BIGINT, _instanceguid UUID)
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
	UPDATE storage.a2migrationstate SET instanceguid = _instanceguid, started = now()
		WHERE a2archivereference = _a2archivereference;
END;
$BODY$;

-- reada2xsls.sql:
CREATE OR REPLACE FUNCTION storage.reada2xsls(_org TEXT, _app TEXT, _lformid INT, _language TEXT, _xsltype INT)
    RETURNS TABLE (xsl TEXT, isportrait BOOL)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
    SELECT
        CASE
            WHEN x.xsl IS NOT NULL THEN x.xsl
            ELSE (SELECT p.xsl FROM storage.a2xsls p WHERE p.id = x.parentid)
        END,
        x.isportrait
        FROM storage.a2xsls x
        WHERE x.org = _org and x.app = _app and x.lformid = _lformid and x.language = _language and x.xsltype = _xsltype
        ORDER BY pagenumber;

END;
$BODY$;

-- reada1migrationstate.sql:
CREATE OR REPLACE FUNCTION storage.reada1migrationstate(_a1archivereference BIGINT)
    RETURNS TABLE (instanceguid UUID)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
    SELECT ms.instanceguid FROM storage.a1migrationstate ms WHERE ms.a1archivereference = _a1archivereference;
END;
$BODY$;

-- inserta2codelist.sql:
CREATE OR REPLACE PROCEDURE storage.inserta2codelist (_name TEXT, _language TEXT, _version INT, _codelist TEXT)
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
    INSERT INTO storage.a2codelists (name, language, version, codelist) VALUES
        (_name, _language, _version, _codelist)
		ON CONFLICT (name, language, version) DO UPDATE SET codelist = _codelist;	
END;
$BODY$;

-- filterinstanceevent.sql:
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

-- inserta2xsl.sql:
CREATE OR REPLACE PROCEDURE storage.inserta2xsl (_org TEXT, _app TEXT, _lformid INT, _language TEXT, _pagenumber INT, _xsl TEXT, _xsltype INT, _isportrait BOOL)
    LANGUAGE 'plpgsql'
AS $BODY$
DECLARE
    _parentId INTEGER;
    _appId INTEGER;
    _applicationinternalid INTEGER;
BEGIN
    SELECT id into _applicationinternalid FROM storage.applications WHERE org = _org AND app = _app;
    SELECT id into _parentId FROM storage.a2xsls WHERE org = _org AND app = _app AND xsl = _xsl AND xsltype = _xsltype;
    SELECT id into _appId FROM storage.applications WHERE org = _org AND app = _app;
    IF _parentId IS NOT NULL THEN
        INSERT INTO storage.a2xsls (org, app, applicationinternalid, parentid, lformid, language, pagenumber, xsl, xsltype, isportrait) VALUES
            (_org, _app, _applicationinternalid, _parentId, _lformid, _language, _pagenumber, NULL, _xsltype, _isportrait)
            ON CONFLICT (app, org, lformid, pagenumber, language, xsltype) DO NOTHING;
    ELSE
        INSERT INTO storage.a2xsls (org, app, applicationinternalid, parentid, lformid, language, pagenumber, xsl, xsltype, isportrait) VALUES
            (_org, _app, _applicationinternalid, NULL, _lformid, _language, _pagenumber, _xsl, _xsltype, _isportrait)
            ON CONFLICT (app, org, lformid, pagenumber, language, xsltype) DO UPDATE SET xsl = _xsl;			
    END IF;
END;
$BODY$;

-- deleteinstanceevent.sql:
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

-- readdeletedinstances.sql:
CREATE OR REPLACE FUNCTION storage.readdeletedinstances()
    RETURNS TABLE (instance JSONB)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY
    -- Make sure that part of the where clause is exactly as in filtered index instances_isharddeleted_and_more
    SELECT i.instance FROM storage.instances i
    WHERE (i.instance -> 'Status' -> 'IsHardDeleted')::BOOLEAN AND
    (
        NOT (i.instance -> 'Status' -> 'IsArchived')::BOOLEAN
        OR (i.instance -> 'CompleteConfirmations') IS NOT NULL AND (i.instance -> 'Status' ->> 'HardDeleted')::TIMESTAMPTZ <= (NOW() - (7 ||' days')::INTERVAL)
    );
END;
$BODY$;


-- reada2migrationstate.sql:
CREATE OR REPLACE FUNCTION storage.reada2migrationstate(_a2archivereference BIGINT)
    RETURNS TABLE (instanceguid UUID)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
    SELECT ms.instanceguid FROM storage.a2migrationstate ms WHERE ms.a2archivereference = _a2archivereference;
END;
$BODY$;

-- insertinstance.sql:
CREATE OR REPLACE PROCEDURE storage.insertinstance_v2(_partyid BIGINT, _alternateid UUID, _instance JSONB, _created TIMESTAMPTZ, _lastchanged TIMESTAMPTZ, _org TEXT, _appid TEXT, _taskid TEXT, _altinnmainversion INT)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
    INSERT INTO storage.instances(partyid, alternateid, instance, created, lastchanged, org, appid, taskid, altinnmainversion)
        VALUES (_partyid, _alternateid, jsonb_strip_nulls(_instance), _created, _lastchanged, _org, _appid, _taskid, _altinnmainversion);
END;
$BODY$;

-- deletemigrationstate.sql:
CREATE OR REPLACE PROCEDURE storage.deletemigrationstate (_instanceguid UUID)
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
	DELETE FROM storage.a1migrationstate WHERE instanceguid = _instanceguid;
	DELETE FROM storage.a2migrationstate WHERE instanceguid = _instanceguid;
END;
$BODY$;

-- insertinstanceevent.sql:
CREATE OR REPLACE PROCEDURE storage.insertinstanceevent(_instance UUID, _alternateid UUID, _event JSONB)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
	-- Dummy comment to verify that new migration solution works end to end
	INSERT INTO storage.instanceevents(instance, alternateid, event) VALUES (_instance, _alternateid, jsonb_strip_nulls(_event));
END;
$BODY$;

-- updateinstance.sql:
CREATE OR REPLACE FUNCTION storage.updateinstance_v2(
        _alternateid UUID,
        _toplevelsimpleprops JSONB,
        _datavalues JSONB,
        _completeconfirmations JSONB,
        _presentationtexts JSONB,
        _status JSONB,
        _substatus JSONB,
        _process JSONB,
        _lastchanged TIMESTAMPTZ,
        _taskid TEXT)
    RETURNS TABLE (updatedInstance JSONB)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
    IF _datavalues IS NOT NULL THEN
        RETURN QUERY
            UPDATE storage.instances SET
                instance = instance || _toplevelsimpleprops ||
                    jsonb_strip_nulls(
                        jsonb_set(
                            '{"DataValues":""}',
                            '{DataValues}',
                            CASE WHEN instance -> 'DataValues' IS NOT NULL THEN
                                instance -> 'DataValues' || _datavalues
                            ELSE
                                _datavalues
                            END
                        )
                    ),
                lastchanged = _lastchanged
            WHERE _alternateid = alternateid
            RETURNING instance;
    ELSIF _presentationtexts IS NOT NULL THEN
        RETURN QUERY
            UPDATE storage.instances SET
                instance = instance || _toplevelsimpleprops ||
                    jsonb_strip_nulls(
                        jsonb_set(
                            '{"PresentationTexts":""}',
                            '{PresentationTexts}',
                            CASE WHEN instance -> 'PresentationTexts' IS NOT NULL THEN
                                instance -> 'PresentationTexts' || _presentationtexts
                            ELSE
                                _presentationtexts
                            END
                        )
                    ),
                lastchanged = _lastchanged
            WHERE _alternateid = alternateid
            RETURNING instance;
    ELSIF _completeconfirmations IS NOT NULL THEN
        RETURN QUERY
            UPDATE storage.instances SET
                instance = instance || _toplevelsimpleprops ||
                    jsonb_set(
                        '{"CompleteConfirmations":""}',
                        '{CompleteConfirmations}',
                        CASE WHEN instance -> 'CompleteConfirmations' IS NOT NULL THEN
                            instance -> 'CompleteConfirmations' || _completeconfirmations
                        ELSE
                            _completeconfirmations
                        END
                    ),
                lastchanged = _lastchanged
            WHERE _alternateid = alternateid
            RETURNING instance;
    ELSIF _status IS NOT NULL AND _process IS NULL THEN
        RETURN QUERY
            UPDATE storage.instances SET
                instance = instance ||
                    jsonb_set(
                        instance || _toplevelsimpleprops,
                        '{Status}',
                        CASE WHEN instance -> 'Status' IS NOT NULL THEN
                            instance -> 'Status' || _status
                        ELSE
                            _status
                        END
                    ),
                lastchanged = _lastchanged
            WHERE _alternateid = alternateid
            RETURNING instance;
    ELSIF _substatus IS NOT NULL THEN
        RETURN QUERY
            UPDATE storage.instances SET
                instance = instance ||
                    jsonb_set(
                        instance || _toplevelsimpleprops,
                        '{Status, Substatus}',
                        jsonb_strip_nulls(_substatus)
                    ),
                lastchanged = _lastchanged
            WHERE _alternateid = alternateid
            RETURNING instance;
    ELSIF _process IS NOT NULL AND _status IS NOT NULL THEN
        RETURN QUERY
            UPDATE storage.instances SET
                instance = instance ||
                    jsonb_set(
                        instance || _toplevelsimpleprops,
                        '{Process}',
                        jsonb_strip_nulls(_process)
                    ) ||
                    jsonb_set(
                        '{"Status":""}',
                        '{Status}',
                        CASE WHEN instance -> 'Status' IS NOT NULL THEN
                            instance -> 'Status' || _status
                        ELSE
                            _status
                        END
                    ),                
                lastchanged = _lastchanged,
                taskid = _taskid
            WHERE _alternateid = alternateid
            RETURNING instance;
    ELSIF _process IS NOT NULL THEN
        RETURN QUERY
            UPDATE storage.instances SET
                instance = instance ||
                    jsonb_set(
                        instance || _toplevelsimpleprops,
                        '{Process}',
                        jsonb_strip_nulls(_process)
                    ),               
                lastchanged = _lastchanged,
                taskid = _taskid
            WHERE _alternateid = alternateid
            RETURNING instance;                
    ELSE
        RAISE EXCEPTION 'Unexpected parameters to update instance';
    END IF;
END;
$BODY$;


-- readdeletedelements.sql:
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

-- inserta2migrationstate.sql:
CREATE OR REPLACE PROCEDURE storage.inserta2migrationstate (_a2archiveReference BIGINT)
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
	INSERT INTO storage.a2migrationstate (a2archivereference) VALUES
		(_a2archiveReference)
	ON CONFLICT (a2archivereference) DO NOTHING;
END;
$BODY$;

-- deletedataelements.sql:
CREATE OR REPLACE FUNCTION storage.deletedataelements(_instanceguid UUID)
    RETURNS INT
    LANGUAGE 'plpgsql'	
AS $BODY$
DECLARE
    _deleteCount INTEGER;
BEGIN
    UPDATE storage.instances
        SET lastchanged = NOW(),
            instance = instance
                || jsonb_set('{"LastChanged":""}', '{LastChanged}', to_jsonb(REPLACE((NOW() AT TIME ZONE 'UTC')::TEXT, ' ', 'T') || 'Z'))
                || jsonb_set('{"LastChangedBy":""}', '{LastChangedBy}', to_jsonb('altinn'::TEXT))   
        WHERE alternateid = _instanceguid;

    DELETE FROM storage.dataelements d
        USING storage.instances i
        WHERE i.alternateid = d.instanceguid AND i.alternateid = _instanceguid;
    GET DIAGNOSTICS _deleteCount = ROW_COUNT;
    RETURN _deleteCount;
END;
$BODY$;

-- readinstancefromquery.sql:
CREATE OR REPLACE FUNCTION storage.readinstancefromquery_v5(
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
    _includeElements BOOL DEFAULT TRUE,
    _instanceOwner_partyId INTEGER DEFAULT NULL,
    _instanceOwner_partyIds INTEGER[] DEFAULT NULL,
    _lastChanged_eq TIMESTAMPTZ DEFAULT NULL,
    _lastChanged_gt TIMESTAMPTZ DEFAULT NULL,
    _lastChanged_gte TIMESTAMPTZ DEFAULT NULL,
    _lastChanged_idx TIMESTAMPTZ DEFAULT NULL,
    _lastChanged_lt TIMESTAMPTZ DEFAULT NULL,
    _lastChanged_lte TIMESTAMPTZ DEFAULT NULL,
    _mainVersionInclude SMALLINT DEFAULT NULL,
    _mainVersionExclude SMALLINT DEFAULT NULL,
    _msgBoxInterval_eq TIMESTAMPTZ DEFAULT NULL,
    _msgBoxInterval_gt TIMESTAMPTZ DEFAULT NULL,
    _msgBoxInterval_gte TIMESTAMPTZ DEFAULT NULL,
    _msgBoxInterval_lt TIMESTAMPTZ DEFAULT NULL,
    _msgBoxInterval_lte TIMESTAMPTZ DEFAULT NULL,
    _org TEXT DEFAULT NULL,
    _process_currentTask TEXT DEFAULT NULL,
    _process_ended_eq TEXT DEFAULT NULL,
    _process_ended_gt TEXT DEFAULT NULL,
    _process_ended_gte TEXT DEFAULT NULL,
    _process_ended_lt TEXT DEFAULT NULL,
    _process_ended_lte TEXT DEFAULT NULL,
    _process_isComplete BOOLEAN DEFAULT NULL,
    _search_string TEXT DEFAULT NULL,
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
            AND (_mainVersionInclude IS NULL OR i.altinnmainversion = _mainVersionInclude)
            AND (_mainVersionExclude IS NULL OR i.altinnmainversion <> _mainVersionExclude)
            AND (_msgBoxInterval_gte IS NULL OR ((i.instance -> 'Status' -> 'IsArchived')::boolean = false AND i.created >= _msgBoxInterval_gte OR (i.instance -> 'Status' -> 'IsArchived')::boolean = true  AND i.lastchanged >= _msgBoxInterval_gte))
            AND (_msgBoxInterval_gt  IS NULL OR ((i.instance -> 'Status' -> 'IsArchived')::boolean = false AND i.created >  _msgBoxInterval_gt  OR (i.instance -> 'Status' -> 'IsArchived')::boolean = true  AND i.lastchanged >  _msgBoxInterval_gt))
            AND (_msgBoxInterval_lte IS NULL OR ((i.instance -> 'Status' -> 'IsArchived')::boolean = false AND i.created <= _msgBoxInterval_lte OR (i.instance -> 'Status' -> 'IsArchived')::boolean = true  AND i.lastchanged <= _msgBoxInterval_lte))
            AND (_msgBoxInterval_lt  IS NULL OR ((i.instance -> 'Status' -> 'IsArchived')::boolean = false AND i.created <  _msgBoxInterval_lt  OR (i.instance -> 'Status' -> 'IsArchived')::boolean = true  AND i.lastchanged <  _msgBoxInterval_lt))
            AND (_msgBoxInterval_eq  IS NULL OR ((i.instance -> 'Status' -> 'IsArchived')::boolean = false AND i.created =  _msgBoxInterval_eq  OR (i.instance -> 'Status' -> 'IsArchived')::boolean = true  AND i.lastchanged =  _msgBoxInterval_eq))
            AND (_org IS NULL OR i.org = _org)
            AND (_process_currentTask IS NULL OR i.instance -> 'Process' -> 'CurrentTask' ->> 'ElementId' = _process_currentTask)
            AND (_process_ended_gte IS NULL OR i.instance -> 'Process' ->> 'Ended' >= _process_ended_gte)
            AND (_process_ended_gt  IS NULL OR i.instance -> 'Process' ->> 'Ended' >  _process_ended_gt)
            AND (_process_ended_lte IS NULL OR i.instance -> 'Process' ->> 'Ended' <= _process_ended_lte)
            AND (_process_ended_lt  IS NULL OR i.instance -> 'Process' ->> 'Ended' <  _process_ended_lt)
            AND (_process_ended_eq  IS NULL OR i.instance -> 'Process' ->> 'Ended' =  _process_ended_eq)
            AND (_process_isComplete IS NULL OR (_process_isComplete = TRUE AND i.instance -> 'Process' -> 'Ended' IS NOT NULL) OR (_process_isComplete = FALSE AND i.instance -> 'Process' -> 'CurrentTask' IS NOT NULL))
            AND (_search_string IS NULL OR (i.appid = ANY(_appIds) OR (EXISTS (SELECT value FROM jsonb_each_text(i.instance -> 'PresentationTexts') WHERE value ilike _search_string))))
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
        SELECT filteredInstances.id, filteredInstances.instance, d.element FROM filteredInstances
            LEFT JOIN storage.dataelements d ON filteredInstances.id = d.instanceInternalId AND _includeElements = TRUE
        ORDER BY
            (CASE WHEN _sort_ascending = true  THEN filteredInstances.lastChanged END) ASC,
            (CASE WHEN _sort_ascending = false THEN filteredInstances.lastChanged END) DESC,
            filteredInstances.id;
END;
$BODY$;

-- inserta2image.sql:
CREATE OR REPLACE PROCEDURE storage.inserta2image (_name TEXT, _image BYTEA)
    LANGUAGE 'plpgsql'
AS $BODY$
DECLARE
    _parentId INTEGER;
BEGIN
    SELECT id into _parentId FROM storage.a2images WHERE md5(_image) = md5(image);
    IF _parentId IS NOT NULL THEN
        INSERT INTO storage.a2images (name, parentid, image) VALUES
            (_name, _parentId, null)
			ON CONFLICT (name) DO NOTHING;
    ELSE
        INSERT INTO storage.a2images (name, parentid, image) VALUES
            (_name, null, _image)
			ON CONFLICT (name) DO UPDATE SET image = _image;
    END IF;
END;
$BODY$;

-- deletedataelement.sql:
CREATE OR REPLACE FUNCTION storage.deletedataelement_v2(_alternateid UUID, _instanceGuid UUID, _lastChangedBy TEXT)
    RETURNS INT
    LANGUAGE 'plpgsql'	
AS $BODY$
DECLARE
    _deleteCount INTEGER;
BEGIN
    IF (SELECT COUNT(*) FROM storage.dataelements WHERE element -> 'IsRead' = 'true' AND instanceguid = _instanceGuid) = 0 THEN
        UPDATE storage.instances
        SET instance = jsonb_set(instance, '{Status, ReadStatus}', '0')
        WHERE alternateid = _instanceGuid AND instance -> 'Status' ->> 'ReadStatus' = '1';
    END IF;

    UPDATE storage.instances
        SET lastchanged = NOW(),
            instance = instance
                || jsonb_set('{"LastChanged":""}', '{LastChanged}', to_jsonb(REPLACE((NOW() AT TIME ZONE 'UTC')::TEXT, ' ', 'T') || 'Z'))
                || jsonb_set('{"LastChangedBy":""}', '{LastChangedBy}', to_jsonb(_lastChangedBy))            
        WHERE alternateid = (SELECT instanceguid FROM storage.dataelements WHERE alternateid = _alternateid);

    DELETE FROM storage.dataelements WHERE alternateid = _alternateid;
    GET DIAGNOSTICS _deleteCount = ROW_COUNT;

    RETURN _deleteCount;
END;
$BODY$;

