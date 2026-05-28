CREATE OR REPLACE FUNCTION jsonb_text_to_timestamptz(data jsonb, key1 text, key2 text)
    RETURNS TIMESTAMPTZ
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
  RETURN SELECT (data -> key1 ->> key2)::TIMESTAMPTZ;
END;
$BODY$;
