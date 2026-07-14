CREATE OR REPLACE PROCEDURE storage.diagnoseinstancemutationupdatefailure(
    _updateelements JSONB,
    _instanceguid UUID,
    _currentinstanceversion INT,
    _currentprocessstateversion INT,
    _updateddataelementids UUID[])
LANGUAGE plpgsql
AS $BODY$
DECLARE
    _faileddataelementid UUID;
    _errorcode TEXT;
BEGIN
    WITH targetdataelements AS (
        SELECT *
        FROM (
            SELECT
                updateelement.ordinality,
                (updateelement.value ->> 'elementId')::UUID AS dataelementid,
                (updateelement.value ->> 'expectedBlobVersion')::UUID AS expectedblobversion,
                COALESCE((updateelement.value ->> 'ignoreLock')::BOOL, false) AS ignorelock
            FROM jsonb_array_elements(_updateelements) WITH ORDINALITY updateelement(value, ordinality)
        ) updateelements
        WHERE NOT COALESCE(
            updateelements.dataelementid = ANY(COALESCE(_updateddataelementids, ARRAY[]::UUID[])),
            false)
    ),
    targetdataelementstates AS (
        SELECT
            targetdataelements.ordinality,
            targetdataelements.dataelementid,
            targetdataelements.expectedblobversion,
            targetdataelements.ignorelock,
            dataelement.alternateid IS NOT NULL AS targetexists,
            dataelement.currentblobversion,
            COALESCE((dataelement.element -> 'DeleteStatus' ->> 'IsHardDeleted')::BOOLEAN, false) AS elementisharddeleted,
            COALESCE((dataelement.element ->> 'Locked')::BOOLEAN, false) AS elementislocked
        FROM targetdataelements
        LEFT JOIN storage.dataelements dataelement
            ON dataelement.instanceguid = _instanceguid
            AND dataelement.alternateid = targetdataelements.dataelementid
    ),
    validationerrors AS (
        SELECT
            targetdataelementstates.ordinality,
            1 AS priority,
            targetdataelementstates.dataelementid,
            'data_element_not_found'::TEXT AS errorcode
        FROM targetdataelementstates
        WHERE NOT targetdataelementstates.targetexists

        UNION ALL

        SELECT
            targetdataelementstates.ordinality,
            2 AS priority,
            targetdataelementstates.dataelementid,
            'blob_version_mismatch'::TEXT AS errorcode
        FROM targetdataelementstates
        WHERE targetdataelementstates.targetexists
            AND targetdataelementstates.expectedblobversion IS NOT NULL
            AND targetdataelementstates.currentblobversion IS DISTINCT FROM targetdataelementstates.expectedblobversion

        UNION ALL

        SELECT
            targetdataelementstates.ordinality,
            3 AS priority,
            targetdataelementstates.dataelementid,
            'data_element_hard_deleted'::TEXT AS errorcode
        FROM targetdataelementstates
        WHERE targetdataelementstates.targetexists
            AND NOT targetdataelementstates.ignorelock
            AND targetdataelementstates.elementisharddeleted

        UNION ALL

        SELECT
            targetdataelementstates.ordinality,
            4 AS priority,
            targetdataelementstates.dataelementid,
            'locked'::TEXT AS errorcode
        FROM targetdataelementstates
        WHERE targetdataelementstates.targetexists
            AND NOT targetdataelementstates.ignorelock
            AND targetdataelementstates.elementislocked
    )
    SELECT validationerrors.dataelementid, validationerrors.errorcode
        INTO _faileddataelementid, _errorcode
        FROM validationerrors
        ORDER BY validationerrors.ordinality, validationerrors.priority
        LIMIT 1;

    IF _errorcode IS NOT NULL
    THEN
        CALL storage.raiseinstancemutationerror(
            _errorcode,
            _currentinstanceversion,
            _currentprocessstateversion,
            _faileddataelementid);
        RETURN;
    END IF;

    RAISE EXCEPTION 'Apply instance mutation update affected fewer data elements than expected, but no failing item could be identified.';
END;
$BODY$;
