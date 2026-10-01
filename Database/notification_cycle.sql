-- 30-day notification cycle anchor
-- Run once in Supabase SQL Editor.

ALTER TABLE public.profiles
ADD COLUMN IF NOT EXISTS notification_cycle_start_at TIMESTAMPTZ;

-- Existing accounts: use their earliest known activity as the initial anchor.
-- This only fills NULL values and does not change already-set anchors.
UPDATE public.profiles
SET notification_cycle_start_at = COALESCE(last_login_at, created_at)
WHERE notification_cycle_start_at IS NULL;

CREATE INDEX IF NOT EXISTS profiles_notification_cycle_start_idx
ON public.profiles(notification_cycle_start_at);
