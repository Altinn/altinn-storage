CREATE OR REPLACE FUNCTION storage.get_studio_daily_metrics(_org TEXT, _date DATE)
    RETURNS TABLE (
        appid TEXT,
        instances_created BIGINT,
        instances_completed BIGINT,
        seconds_to_complete_p50 INTEGER)
    LANGUAGE 'plpgsql'

AS $BODY$
DECLARE
    -- The day is a UTC day, like storage.get_instance_metrics.
    _day_start TIMESTAMPTZ := MAKE_TIMESTAMPTZ(EXTRACT(YEAR FROM _date)::INT, EXTRACT(MONTH FROM _date)::INT, EXTRACT(DAY FROM _date)::INT, 0, 0, 0, 'UTC');
    _day_end TIMESTAMPTZ := _day_start + INTERVAL '1 day';
BEGIN
    -- Everything describes what happened on the day, so the function reads one day's rows through the
    -- indexes on created and on the archived timestamp, however large the table is.
    RETURN QUERY
    WITH created AS (
        SELECT i.appid AS app, count(*) AS n
        FROM storage.instances i
        WHERE i.org = _org
            AND i.altinnmainversion = 3
            AND i.created >= _day_start
            AND i.created < _day_end
        GROUP BY i.appid
    ),
    completed AS (
        -- Same predicate as storage.get_instance_metrics and the index instances_archived_appid_id_filtered.
        SELECT i.appid AS app,
            count(*) AS n,
            percentile_cont(0.5) WITHIN GROUP (ORDER BY EXTRACT(EPOCH FROM (p.ended - p.started))) AS p50
        FROM storage.instances i
        CROSS JOIN LATERAL (
            SELECT storage.jsonb_text_to_timestamptz(i.instance, 'Process', 'Started') AS started,
                storage.jsonb_text_to_timestamptz(i.instance, 'Process', 'Ended') AS ended
        ) p
        WHERE i.altinnmainversion = 3
            AND (i.instance -> 'Status' ->> 'IsArchived')::BOOLEAN = TRUE
            AND (i.instance -> 'Status') ? 'Archived'
            AND NULLIF(i.instance -> 'Status' ->> 'Archived', '') IS NOT NULL
            AND storage.jsonb_text_to_timestamptz(i.instance, 'Status', 'Archived') >= _day_start
            AND storage.jsonb_text_to_timestamptz(i.instance, 'Status', 'Archived') < _day_end
            AND i.org = _org
        GROUP BY i.appid
    ),
    apps AS (
        SELECT c.app FROM created c
        UNION
        SELECT d.app FROM completed d
    )
    SELECT a.app,
        COALESCE(c.n, 0),
        COALESCE(d.n, 0),
        d.p50::INTEGER
    FROM apps a
    LEFT JOIN created c ON c.app = a.app
    LEFT JOIN completed d ON d.app = a.app
    ORDER BY a.app;
END;
$BODY$;
