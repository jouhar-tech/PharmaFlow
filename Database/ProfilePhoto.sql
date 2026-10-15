-- Add optional owner profile photo URL.
ALTER TABLE public.profiles
    ADD COLUMN IF NOT EXISTS profile_photo_url TEXT;

-- Store owner profile photos in the same public bucket used by staff photos.
-- This keeps uploads on Supabase Storage instead of the app server's local disk.
INSERT INTO storage.buckets (id, name, public)
VALUES ('staff-photos', 'staff-photos', true)
ON CONFLICT (id) DO UPDATE SET public = true;

DROP POLICY IF EXISTS "staff photos authenticated upload" ON storage.objects;
CREATE POLICY "staff photos authenticated upload"
ON storage.objects
FOR INSERT
TO authenticated
WITH CHECK (bucket_id = 'staff-photos');

DROP POLICY IF EXISTS "staff photos public read" ON storage.objects;
CREATE POLICY "staff photos public read"
ON storage.objects
FOR SELECT
TO public
USING (bucket_id = 'staff-photos');
