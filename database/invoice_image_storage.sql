-- PharmaHex: private storage metadata for scanned purchase invoices.
-- Run this in the Supabase SQL Editor before deploying the application update.
-- Create the private Storage bucket separately in Supabase Dashboard:
-- Storage > New bucket > Name: invoice-scans > Public bucket: OFF
-- File size limit: 25 MB
-- Allowed MIME types: image/jpeg, image/png, image/webp, application/pdf

ALTER TABLE public.invoice_imports
    ADD COLUMN IF NOT EXISTS storage_path TEXT NULL,
    ADD COLUMN IF NOT EXISTS storage_content_type VARCHAR(100) NULL;

CREATE INDEX IF NOT EXISTS ix_invoice_imports_profile_storage_path
    ON public.invoice_imports(profile_id, import_id)
    WHERE storage_path IS NOT NULL;

-- Existing invoices remain valid with NULL storage_path because their original
-- image/PDF bytes were not retained by the previous invoice capture flow.
