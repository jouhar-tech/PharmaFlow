-- Optional per-shop photo for the mobile dashboard hero.
-- Run this once in the PharmaHex-Production Supabase SQL Editor before deploying
-- the application build that includes dashboard background uploads.

ALTER TABLE public.profiles
    ADD COLUMN IF NOT EXISTS dashboard_background_url TEXT;

-- A separate bucket keeps shop backgrounds isolated from profile and staff photos.
INSERT INTO storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
VALUES (
    'business-backgrounds',
    'business-backgrounds',
    TRUE,
    5242880,
    ARRAY['image/jpeg', 'image/png', 'image/webp']::text[]
)
ON CONFLICT (id) DO UPDATE
SET public = TRUE,
    file_size_limit = EXCLUDED.file_size_limit,
    allowed_mime_types = EXCLUDED.allowed_mime_types;

DROP POLICY IF EXISTS "business backgrounds owner upload" ON storage.objects;
CREATE POLICY "business backgrounds owner upload"
ON storage.objects
FOR INSERT
TO authenticated
WITH CHECK (
    bucket_id = 'business-backgrounds'
    AND (storage.foldername(name))[1] = auth.uid()::text
);

DROP POLICY IF EXISTS "business backgrounds owner delete" ON storage.objects;
CREATE POLICY "business backgrounds owner delete"
ON storage.objects
FOR DELETE
TO authenticated
USING (
    bucket_id = 'business-backgrounds'
    AND (storage.foldername(name))[1] = auth.uid()::text
    AND (storage.foldername(name))[2] = 'dashboard-backgrounds'
);

DROP POLICY IF EXISTS "business backgrounds public read" ON storage.objects;
CREATE POLICY "business backgrounds public read"
ON storage.objects
FOR SELECT
TO public
USING (bucket_id = 'business-backgrounds');
