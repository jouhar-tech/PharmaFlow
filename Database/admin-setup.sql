-- PharmaFlow - Admin profile and subscription fields
-- Run once in Supabase SQL Editor.

ALTER TABLE public.profiles
    ADD COLUMN IF NOT EXISTS full_name VARCHAR(120) NULL,
    ADD COLUMN IF NOT EXISTS address VARCHAR(500) NULL,
    ADD COLUMN IF NOT EXISTS subscription_plan VARCHAR(20) NOT NULL DEFAULT 'free',
    ADD COLUMN IF NOT EXISTS subscription_starts_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS subscription_ends_at TIMESTAMPTZ NULL;

UPDATE public.profiles
SET subscription_plan = 'free'
WHERE subscription_plan IS NULL OR TRIM(subscription_plan) = '';

ALTER TABLE public.profiles
    DROP CONSTRAINT IF EXISTS ck_profiles_subscription_plan;

ALTER TABLE public.profiles
    ADD CONSTRAINT ck_profiles_subscription_plan
    CHECK (subscription_plan IN ('free', 'unlimited'));

CREATE INDEX IF NOT EXISTS ix_profiles_subscription_plan
    ON public.profiles(subscription_plan);

CREATE INDEX IF NOT EXISTS ix_profiles_subscription_ends_at
    ON public.profiles(subscription_ends_at);

CREATE INDEX IF NOT EXISTS ix_profiles_active_status
    ON public.profiles(active_status);
