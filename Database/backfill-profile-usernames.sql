-- Backfill profiles created with a NULL/blank username.
-- Prefer full_name; append a numeric suffix only when that username is already taken.
DO $$
DECLARE
    profile_record RECORD;
    base_username TEXT;
    candidate_username TEXT;
    suffix INTEGER;
BEGIN
    FOR profile_record IN
        SELECT id, full_name
        FROM public.profiles
        WHERE username IS NULL OR BTRIM(username) = ''
        ORDER BY id
    LOOP
        base_username := COALESCE(
            NULLIF(BTRIM(profile_record.full_name), ''),
            'user' || profile_record.id::TEXT
        );
        candidate_username := base_username;
        suffix := 1;

        WHILE EXISTS (
            SELECT 1
            FROM public.profiles existing
            WHERE existing.id <> profile_record.id
              AND existing.username IS NOT NULL
              AND LOWER(BTRIM(existing.username)) = LOWER(candidate_username)
        ) LOOP
            candidate_username := base_username || suffix::TEXT;
            suffix := suffix + 1;
        END LOOP;

        UPDATE public.profiles
        SET username = candidate_username
        WHERE id = profile_record.id;
    END LOOP;
END $$;
