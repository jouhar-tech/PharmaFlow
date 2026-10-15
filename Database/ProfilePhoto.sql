-- Add optional owner profile photo URL.
-- Profile photo image files are stored in the existing public Supabase Storage bucket.
ALTER TABLE public.profiles
    ADD COLUMN IF NOT EXISTS profile_photo_url TEXT;
