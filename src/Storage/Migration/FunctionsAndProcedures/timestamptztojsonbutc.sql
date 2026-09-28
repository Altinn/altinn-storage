CREATE OR REPLACE FUNCTION storage.timestamptz_to_jsonb_utc(_timestamp TIMESTAMPTZ)
    RETURNS JSONB
    LANGUAGE 'plpgsql' IMMUTABLE
AS $BODY$
BEGIN
  RETURN COALESCE(to_jsonb(REPLACE((_timestamp AT TIME ZONE 'UTC')::TEXT, ' ', 'T') || 'Z'), 'null'::JSONB);
END;
$BODY$;
