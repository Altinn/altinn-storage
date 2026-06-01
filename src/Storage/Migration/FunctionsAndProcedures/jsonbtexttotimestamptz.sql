CREATE OR REPLACE FUNCTION jsonb_text_to_timestamptz(data jsonb, key1 text, key2 text)
    RETURNS TIMESTAMPTZ
    LANGUAGE 'plpgsql' IMMUTABLE STRICT
AS $BODY$
BEGIN
  RETURN (data -> key1 ->> key2)::TIMESTAMPTZ;
END;
$BODY$;
