-- Rename the existing primary key column from id to FeedBackID when upgrading
-- an already-created feedback table.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'feedback'
          AND column_name = 'id'
    )
    AND NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'feedback'
          AND column_name = 'FeedBackID'
    ) THEN
        ALTER TABLE public.feedback RENAME COLUMN id TO "FeedBackID";
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS public.feedback (
    "FeedBackID" BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    profile_id BIGINT NOT NULL REFERENCES public.profiles(id) ON DELETE CASCADE,
    message TEXT NOT NULL CHECK (char_length(trim(message)) BETWEEN 5 AND 2000),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS feedback_profile_id_idx
    ON public.feedback(profile_id);

CREATE INDEX IF NOT EXISTS feedback_created_at_idx
    ON public.feedback(created_at DESC);
