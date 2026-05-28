CREATE OR REPLACE FUNCTION jsonb_text_to_timestamptz(data jsonb, key1 text, key2 text)
RETURNS TIMESTAMPTZ AS $$
  SELECT (data -> key1 ->> key2)::TIMESTAMP AT TIME ZONE 'UTC';
$$ LANGUAGE SQL IMMUTABLE STRICT;
