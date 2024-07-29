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