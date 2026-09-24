CREATE TABLE IF NOT EXISTS public.feedback (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    profile_id BIGINT NOT NULL REFERENCES public.profiles(id) ON DELETE CASCADE,
    message TEXT NOT NULL CHECK (char_length(trim(message)) BETWEEN 5 AND 2000),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS feedback_profile_id_idx
    ON public.feedback(profile_id);

CREATE INDEX IF NOT EXISTS feedback_created_at_idx
    ON public.feedback(created_at DESC);
