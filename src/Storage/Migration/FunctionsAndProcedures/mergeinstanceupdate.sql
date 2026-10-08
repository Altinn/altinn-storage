CREATE OR REPLACE FUNCTION storage.mergeinstanceupdate_v2(
    _instance JSONB,
    _instanceupdate JSONB)
    RETURNS JSONB
    LANGUAGE SQL IMMUTABLE
AS $BODY$
    SELECT _instance || updateparts.toplevelsimpleprops
        || CASE
            WHEN updateparts.datavalues IS NOT NULL THEN
                jsonb_build_object(
                    'DataValues',
                    jsonb_strip_nulls(
                        COALESCE(NULLIF(_instance -> 'DataValues', 'null'::JSONB), '{}'::JSONB)
                            || updateparts.datavalues
                    )
                )
            ELSE
                '{}'::JSONB
        END
        || CASE
            WHEN updateparts.presentationtexts IS NOT NULL THEN
                jsonb_build_object(
                    'PresentationTexts',
                    jsonb_strip_nulls(
                        COALESCE(NULLIF(_instance -> 'PresentationTexts', 'null'::JSONB), '{}'::JSONB)
                            || updateparts.presentationtexts
                    )
                )
            ELSE
                '{}'::JSONB
        END
        || CASE
            WHEN updateparts.completeconfirmations IS NOT NULL THEN
                jsonb_build_object(
                    'CompleteConfirmations',
                    COALESCE(NULLIF(_instance -> 'CompleteConfirmations', 'null'::JSONB), '[]'::JSONB)
                        || (
                            SELECT COALESCE(jsonb_agg(incoming.value ORDER BY incoming.ordinality), '[]'::JSONB)
                            FROM jsonb_array_elements(updateparts.completeconfirmations)
                                WITH ORDINALITY incoming(value, ordinality)
                            WHERE NOT EXISTS (
                                SELECT 1
                                FROM jsonb_array_elements(
                                    COALESCE(NULLIF(_instance -> 'CompleteConfirmations', 'null'::JSONB), '[]'::JSONB)
                                ) existing(value)
                                WHERE existing.value ->> 'StakeholderId'
                                    = incoming.value ->> 'StakeholderId'
                            )
                        )
                )
            ELSE
                '{}'::JSONB
        END
        || CASE
            WHEN updateparts.status IS NOT NULL OR updateparts.substatus IS NOT NULL THEN
                jsonb_build_object(
                    'Status',
                    CASE
                        WHEN updateparts.substatus IS NOT NULL THEN
                            jsonb_set(
                                COALESCE(NULLIF(_instance -> 'Status', 'null'::JSONB), '{}'::JSONB)
                                    || COALESCE(updateparts.status, '{}'::JSONB),
                                '{Substatus}',
                                jsonb_strip_nulls(updateparts.substatus)
                            )
                        ELSE
                            COALESCE(NULLIF(_instance -> 'Status', 'null'::JSONB), '{}'::JSONB)
                                || updateparts.status
                    END
                )
            ELSE
                '{}'::JSONB
        END
        || CASE
            WHEN updateparts.process IS NOT NULL THEN
                jsonb_build_object('Process', jsonb_strip_nulls(updateparts.process))
            ELSE
                '{}'::JSONB
        END
    FROM (
        SELECT
            COALESCE((
                SELECT jsonb_object_agg(property.key, property.value)
                FROM jsonb_each(COALESCE(NULLIF(_instanceupdate, 'null'::JSONB), '{}'::JSONB)) property
                WHERE property.key IN ('Created', 'CreatedBy', 'DueBefore', 'VisibleAfter')
            ), '{}'::JSONB) AS toplevelsimpleprops,
            NULLIF(_instanceupdate -> 'DataValues', 'null'::JSONB) AS datavalues,
            NULLIF(_instanceupdate -> 'PresentationTexts', 'null'::JSONB) AS presentationtexts,
            NULLIF(_instanceupdate -> 'CompleteConfirmations', 'null'::JSONB) AS completeconfirmations,
            NULLIF(_instanceupdate -> 'Status', 'null'::JSONB) - 'Substatus' AS status,
            NULLIF(_instanceupdate -> 'Status' -> 'Substatus', 'null'::JSONB) AS substatus,
            NULLIF(_instanceupdate -> 'Process', 'null'::JSONB) AS process
    ) updateparts;
$BODY$;
