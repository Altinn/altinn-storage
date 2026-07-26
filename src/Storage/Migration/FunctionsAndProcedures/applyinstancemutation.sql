CREATE OR REPLACE FUNCTION storage.applyinstancemutation(
    _instanceguid UUID,
    _instanceinternalid BIGINT,
    _expectedinstanceversion INT,
    _expectedprocessstateversion INT,
    _idempotencykey UUID,
    _lastchanged TIMESTAMPTZ,
    _lastchangedby TEXT,
    _createelements JSONB,
    _updateelements JSONB,
    _deleteelements JSONB,
    _instanceupdate JSONB,
    _events JSONB,
    _outbox JSONB)
    RETURNS TABLE (
        replayed BOOLEAN,
        createddataelementids TEXT[],
        id BIGINT,
        instance JSONB,
        instanceversion INT,
        processstateversion INT,
        element JSONB,
        currentblobversion UUID)
    LANGUAGE plpgsql
AS $BODY$
DECLARE
    _currentinstanceversion INT;
    _currentprocessstateversion INT;
    _currentprocessstatus TEXT;
    _replaycreateddataelementids TEXT[];
    _committedcreateddataelementids TEXT[];
    _updatedids UUID[];
    _deletedids UUID[];
    -- Predicted post-mutation versions, computed upfront. The idempotency record stores
    -- the instance version; the final instance UPDATE assigns both. AM001 errors use the
    -- locked current versions instead, since the exception rolls back the mutation.
    _newinstanceversion INT;
    _newprocessstateversion INT;
    _composedinstance JSONB;
BEGIN
    SELECT
        i.instance,
        i.instance_version,
        i.process_state_version
        INTO _composedinstance, _currentinstanceversion, _currentprocessstateversion
        FROM storage.instances i
        WHERE i.id = _instanceinternalid
        FOR UPDATE;

    IF NOT FOUND
    THEN
        CALL storage.raiseinstancemutationerror(
            'instance_not_found',
            NULL::INT,
            NULL::INT);
    END IF;

    _currentprocessstatus := COALESCE(_composedinstance -> 'Process' ->> 'Status', 'idle');

    _createelements := COALESCE(_createelements, '[]'::JSONB);
    _updateelements := COALESCE(_updateelements, '[]'::JSONB);
    _deleteelements := COALESCE(_deleteelements, '[]'::JSONB);
    _events := COALESCE(_events, '[]'::JSONB);
    _committedcreateddataelementids := ARRAY[]::TEXT[];

    _newinstanceversion := CASE
        WHEN jsonb_array_length(_createelements) + jsonb_array_length(_updateelements) + jsonb_array_length(_deleteelements) > 0
            OR _instanceupdate IS NOT NULL
            THEN _currentinstanceversion + 1
        ELSE _currentinstanceversion
    END;

    _newprocessstateversion := CASE
        WHEN NULLIF(_instanceupdate -> 'process', 'null'::JSONB) IS NOT NULL
            THEN _currentprocessstateversion + 1
        ELSE _currentprocessstateversion
    END;

    SELECT COALESCE(array_agg(createelement.value ->> 'elementId' ORDER BY createelement.ordinality), ARRAY[]::TEXT[])
        INTO _committedcreateddataelementids
        FROM jsonb_array_elements(_createelements) WITH ORDINALITY createelement(value, ordinality);


    IF _idempotencykey IS NOT NULL AND _expectedinstanceversion IS NOT NULL
    THEN
        INSERT INTO storage.instance_mutation_idempotency
            (instance, previous_instance_version, idempotency_key, produced_instance_version, created_data_element_ids)
        VALUES
            (_instanceguid, _expectedinstanceversion, _idempotencykey, _newinstanceversion, _committedcreateddataelementids)
        ON CONFLICT (idempotency_key) DO NOTHING;

        IF NOT FOUND
        THEN
            _replaycreateddataelementids := storage.tryreplayinstancemutation(
                _idempotencykey,
                _instanceguid,
                _expectedinstanceversion,
                _currentinstanceversion,
                _currentprocessstateversion);

            RETURN QUERY
                SELECT true, _replaycreateddataelementids, r.id, r.instance, r.instanceversion, r.processstateversion, r.element, r.currentblobversion
                FROM storage.readinstance_v2(_instanceguid) r;
            RETURN;
        END IF;
    END IF;

    IF _expectedinstanceversion IS NOT NULL
        AND _currentinstanceversion <> _expectedinstanceversion
    THEN
        CALL storage.raiseinstancemutationerror(
            'instance_version_mismatch',
            _currentinstanceversion,
            _currentprocessstateversion);
    END IF;

    IF _expectedprocessstateversion IS NOT NULL
        AND _currentprocessstateversion <> _expectedprocessstateversion
    THEN
        CALL storage.raiseinstancemutationerror(
            'process_state_version_mismatch',
            _currentinstanceversion,
            _currentprocessstateversion);
    END IF;

    IF _currentprocessstatus <> 'idle'
        AND _expectedinstanceversion IS NULL
        AND _expectedprocessstateversion IS NULL
    THEN
        CALL storage.raiseinstancemutationerror(
            'process_status_conflict',
            _currentinstanceversion,
            _currentprocessstateversion,
            NULL::UUID,
            _currentprocessstatus);
    END IF;

    IF COALESCE((_composedinstance -> 'Status' ->> 'IsHardDeleted')::BOOLEAN, FALSE)
    THEN
        CALL storage.raiseinstancemutationerror(
            'instance_hard_deleted',
            _currentinstanceversion,
            _currentprocessstateversion);
    END IF;

    IF jsonb_array_length(_createelements) > 0
    THEN
        INSERT INTO storage.dataelements(instanceinternalid, instanceguid, alternateid, element, currentblobversion)
            SELECT
                _instanceinternalid,
                _instanceguid,
                createelement.dataelementid,
                jsonb_strip_nulls(
                    createelement.element
                        || jsonb_build_object(
                            'LastChanged',
                            storage.timestamptz_to_jsonb_utc(_lastchanged),
                            'LastChangedBy',
                            _lastchangedby
                        )
                ),
                createelement.blobversion
            FROM (
                SELECT
                    createelement.ordinality,
                    (createelement.value ->> 'elementId')::UUID AS dataelementid,
                    createelement.value -> 'element' AS element,
                    (createelement.value ->> 'blobVersion')::UUID AS blobversion
                FROM jsonb_array_elements(_createelements) WITH ORDINALITY createelement(value, ordinality)
            ) createelement
            ORDER BY createelement.ordinality;

        UPDATE storage.dataelementblobversions dataelementblobversion
            SET attached = true
            FROM (
                SELECT
                    (createelement.value ->> 'elementId')::UUID AS dataelementid,
                    (createelement.value ->> 'blobVersion')::UUID AS blobversion
                FROM jsonb_array_elements(_createelements) createelement(value)
                WHERE createelement.value ->> 'blobVersion' IS NOT NULL
            ) createelement
            WHERE dataelementblobversion.id = createelement.blobversion
                AND dataelementblobversion.instanceguid = _instanceguid
                AND dataelementblobversion.dataelementid = createelement.dataelementid
                AND dataelementblobversion.attached = false;
    END IF;

    IF jsonb_array_length(_updateelements) > 0
    THEN
        -- Under READ COMMITTED, UPDATE re-evaluates this WHERE on the latest row
        -- version after taking the row lock (EvalPlanQual), so each row's
        -- check-and-write is atomic without a separate lock/validate statement.
        WITH updateelements AS (
            SELECT
                updateelement.ordinality,
                (updateelement.value ->> 'elementId')::UUID AS dataelementid,
                (updateelement.value ->> 'expectedBlobVersion')::UUID AS expectedblobversion,
                (updateelement.value ->> 'newBlobVersion')::UUID AS newblobversion,
                COALESCE((updateelement.value ->> 'ignoreLock')::BOOL, false) AS ignorelock,
                COALESCE(NULLIF(updateelement.value -> 'elementChanges', 'null'::JSONB), '{}'::JSONB) AS elementchanges
            FROM jsonb_array_elements(_updateelements) WITH ORDINALITY updateelement(value, ordinality)
        ),
        updated AS (
            UPDATE storage.dataelements dataelement
            SET element = dataelement.element
                    || updateelements.elementchanges
                    || jsonb_build_object(
                        'LastChanged',
                        storage.timestamptz_to_jsonb_utc(_lastchanged),
                        'LastChangedBy',
                        COALESCE(to_jsonb(_lastchangedby), 'null'::JSONB)
                    ),
                currentblobversion = COALESCE(updateelements.newblobversion, dataelement.currentblobversion)
            FROM updateelements
            WHERE dataelement.instanceguid = _instanceguid
                AND dataelement.alternateid = updateelements.dataelementid
                AND (
                    updateelements.expectedblobversion IS NULL
                    OR dataelement.currentblobversion IS NOT DISTINCT FROM updateelements.expectedblobversion
                )
                AND (
                    updateelements.ignorelock
                    OR (
                        NOT COALESCE((dataelement.element ->> 'Locked')::BOOLEAN, FALSE)
                        AND NOT COALESCE((dataelement.element -> 'DeleteStatus' ->> 'IsHardDeleted')::BOOLEAN, FALSE)
                    )
                )
            RETURNING dataelement.alternateid
        )
        SELECT COALESCE(array_agg(updated.alternateid), ARRAY[]::UUID[])
            INTO _updatedids
            FROM updated;

        IF cardinality(_updatedids) < jsonb_array_length(_updateelements)
        THEN
            CALL storage.diagnoseinstancemutationupdatefailure(
                _updateelements,
                _instanceguid,
                _currentinstanceversion,
                _currentprocessstateversion,
                _updatedids);
        END IF;

        UPDATE storage.dataelementblobversions dataelementblobversion
            SET attached = true
            FROM (
                SELECT
                    (updateelement.value ->> 'elementId')::UUID AS dataelementid,
                    (updateelement.value ->> 'newBlobVersion')::UUID AS newblobversion
                FROM jsonb_array_elements(_updateelements) updateelement(value)
                WHERE updateelement.value ->> 'newBlobVersion' IS NOT NULL
            ) updateelement
            WHERE dataelementblobversion.id = updateelement.newblobversion
                AND dataelementblobversion.instanceguid = _instanceguid
                AND dataelementblobversion.dataelementid = updateelement.dataelementid
                AND dataelementblobversion.attached = false;
    END IF;

    IF jsonb_array_length(_deleteelements) > 0
    THEN
        WITH deleteelements AS (
            SELECT
                (deleteelement.value ->> 'elementId')::UUID AS dataelementid,
                COALESCE((deleteelement.value ->> 'ignoreLock')::BOOL, false) AS ignorelock
            FROM jsonb_array_elements(_deleteelements) deleteelement(value)
        ),
        deleted AS (
            DELETE FROM storage.dataelements dataelement
            USING deleteelements deleteelement
            WHERE dataelement.instanceguid = _instanceguid
                AND dataelement.alternateid = deleteelement.dataelementid
                AND (
                    deleteelement.ignorelock
                    OR NOT COALESCE((dataelement.element ->> 'Locked')::BOOLEAN, FALSE)
                )
            RETURNING dataelement.alternateid
        )
        SELECT COALESCE(array_agg(deleted.alternateid), ARRAY[]::UUID[])
            INTO _deletedids
            FROM deleted;

        IF cardinality(_deletedids) < jsonb_array_length(_deleteelements)
        THEN
            CALL storage.diagnoseinstancemutationdeletefailure(
                _deleteelements,
                _instanceguid,
                _currentinstanceversion,
                _currentprocessstateversion,
                _deletedids);
        END IF;

        UPDATE storage.dataelementblobversions dataelementblobversion
            SET attached = false
            FROM (
                SELECT (deleteelement.value ->> 'elementId')::UUID AS dataelementid
                FROM jsonb_array_elements(_deleteelements) deleteelement(value)
            ) deleteelement
            WHERE dataelementblobversion.instanceguid = _instanceguid
                AND dataelementblobversion.dataelementid = deleteelement.dataelementid
                AND dataelementblobversion.attached = true;
    END IF;

    -- The upfront instance-version prediction is the any-operations signal.
    IF _newinstanceversion <> _currentinstanceversion
    THEN
        IF _composedinstance -> 'Status' ->> 'ReadStatus' = '1'
        THEN
            IF EXISTS (
                SELECT 1
                FROM jsonb_array_elements(_createelements) createelement(value)
                WHERE createelement.value -> 'element' ->> 'IsRead' = 'false'
            )
            THEN
                _composedinstance := jsonb_set(_composedinstance, '{Status, ReadStatus}', '2');
            ELSIF NOT EXISTS (
                SELECT 1
                FROM storage.dataelements dataelement
                WHERE dataelement.instanceguid = _instanceguid
                    AND dataelement.element -> 'IsRead' = 'true'
            )
            THEN
                _composedinstance := jsonb_set(_composedinstance, '{Status, ReadStatus}', '0');
            END IF;
        END IF;

        IF _instanceupdate IS NOT NULL
        THEN
            _composedinstance := storage.mergeinstanceupdate(_composedinstance, _instanceupdate);
        END IF;

        _composedinstance := _composedinstance
            || jsonb_set('{"LastChanged":""}', '{LastChanged}', storage.timestamptz_to_jsonb_utc(_lastchanged))
            || jsonb_set('{"LastChangedBy":""}', '{LastChangedBy}', COALESCE(to_jsonb(_lastchangedby), 'null'::JSONB));

        UPDATE storage.instances i
            SET instance = _composedinstance,
                lastchanged = _lastchanged,
                instance_version = _newinstanceversion,
                process_state_version = _newprocessstateversion,
                confirmed = COALESCE((_instanceupdate ->> 'confirmed')::BOOLEAN, i.confirmed),
                taskid = CASE
                    WHEN NULLIF(_instanceupdate -> 'process', 'null'::JSONB) IS NOT NULL THEN _instanceupdate ->> 'taskid'
                    ELSE i.taskid
                END
            WHERE i.id = _instanceinternalid;
    END IF;

    IF jsonb_array_length(_events) > 0
    THEN
        CALL storage.insertinstanceevents(_instanceguid, _events);
    END IF;

    IF _outbox IS NOT NULL
    THEN
        INSERT INTO storage.outbox
            (instanceid, appid, partyid, validfrom, instancecreated, ismigration, instanceeventtype)
        VALUES
            (
                _instanceguid,
                _outbox ->> 'appid',
                (_outbox ->> 'partyid')::BIGINT,
                clock_timestamp() + make_interval(secs => COALESCE((_outbox ->> 'delaySeconds')::DOUBLE PRECISION, 0)),
                (_outbox ->> 'instancecreated')::TIMESTAMPTZ,
                (_outbox ->> 'ismigration')::BOOLEAN,
                (_outbox ->> 'instanceeventtype')::SMALLINT
            )
        ON CONFLICT (instanceid) DO UPDATE SET
            validfrom = excluded.validfrom
        WHERE excluded.validfrom < storage.outbox.validfrom;
    END IF;

    RETURN QUERY
        SELECT false, _committedcreateddataelementids, r.id, r.instance, r.instanceversion, r.processstateversion, r.element, r.currentblobversion
        FROM storage.readinstance_v2(_instanceguid) r;
END;
$BODY$;
