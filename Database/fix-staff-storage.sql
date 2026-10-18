-- Repair staff storage before deploying the updated application.
-- Staff identity details belong in public.staff, not public.profiles.
BEGIN;

-- Add the staff username field on existing deployments.
ALTER TABLE public.staff
    ADD COLUMN IF NOT EXISTS username varchar(120);

-- Backfill the new required field for any existing staff rows.
UPDATE public.staff
SET username = COALESCE(NULLIF(BTRIM(username), ''), NULLIF(BTRIM(full_name), ''), 'staff' || staff_id::text)
WHERE username IS NULL OR BTRIM(username) = '';

ALTER TABLE public.staff
    ALTER COLUMN username SET NOT NULL;

-- Remove profile rows created for staff Auth users. The pharmacy owner's
-- profile remains because staff.profile_id refers to the owner's profile.
DELETE FROM public.profiles p
USING auth.users u
WHERE p.user_id = u.id
  AND u.raw_user_meta_data ->> 'account_type' = 'staff';

-- Clean up orphaned staff Auth users left behind when an earlier staff insert
-- failed. Only deletes accounts tagged as staff that have no public.staff row.
DELETE FROM auth.users u
WHERE u.raw_user_meta_data ->> 'account_type' = 'staff'
  AND NOT EXISTS (
      SELECT 1
      FROM public.staff s
      WHERE s.auth_user_id = u.id
  );

COMMIT;
