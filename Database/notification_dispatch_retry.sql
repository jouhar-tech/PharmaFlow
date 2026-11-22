-- Retryable notification dispatch state for PharmaHex.
-- Run this once in the PharmaHex production Supabase SQL Editor BEFORE deploying
-- the application version that uses these columns.

ALTER TABLE public.notification_dispatch_log
    ADD COLUMN IF NOT EXISTS status VARCHAR(20) NOT NULL DEFAULT 'sent',
    ADD COLUMN IF NOT EXISTS attempted_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS attempt_token UUID NULL;

-- Rows created by the previous implementation represent completed deliveries
-- unless the previous process was terminated between claiming and sending.
-- Keep existing rows as sent to avoid re-sending historical notifications.
UPDATE public.notification_dispatch_log
SET status = 'sent'
WHERE status IS NULL OR status NOT IN ('sent', 'processing');

CREATE INDEX IF NOT EXISTS notification_dispatch_log_processing_idx
    ON public.notification_dispatch_log(status, attempted_at)
    WHERE status = 'processing';
