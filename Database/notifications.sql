-- PharmaFlow Web Push notifications + notification delivery log + savings ledger
-- Run this once in the Supabase SQL Editor.

CREATE TABLE IF NOT EXISTS public.push_device_subscriptions (
    subscription_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    profile_id BIGINT NOT NULL REFERENCES public.profiles(id) ON DELETE CASCADE,
    user_role VARCHAR(20) NOT NULL DEFAULT 'Owner',
    endpoint TEXT NOT NULL UNIQUE,
    p256dh TEXT NOT NULL,
    auth TEXT NOT NULL,
    user_agent TEXT,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_success_at TIMESTAMPTZ,
    last_failure_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS push_device_subscriptions_profile_idx
    ON public.push_device_subscriptions(profile_id);

CREATE INDEX IF NOT EXISTS push_device_subscriptions_active_idx
    ON public.push_device_subscriptions(profile_id, is_active);

CREATE TABLE IF NOT EXISTS public.notification_dispatch_log (
    notification_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    profile_id BIGINT NOT NULL REFERENCES public.profiles(id) ON DELETE CASCADE,
    notification_type VARCHAR(40) NOT NULL,
    period_key VARCHAR(20) NOT NULL,
    sent_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (profile_id, notification_type, period_key)
);

CREATE INDEX IF NOT EXISTS notification_dispatch_log_profile_idx
    ON public.notification_dispatch_log(profile_id, sent_at DESC);

CREATE TABLE IF NOT EXISTS public.pharmaflow_savings_events (
    event_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    profile_id BIGINT NOT NULL REFERENCES public.profiles(id) ON DELETE CASCADE,
    category VARCHAR(50) NOT NULL,
    amount NUMERIC(14,2) NOT NULL CHECK (amount >= 0),
    description TEXT,
    source_type VARCHAR(80),
    source_id VARCHAR(160),
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS pharmaflow_savings_events_profile_date_idx
    ON public.pharmaflow_savings_events(profile_id, occurred_at DESC);

CREATE UNIQUE INDEX IF NOT EXISTS pharmaflow_savings_events_source_idx
    ON public.pharmaflow_savings_events(profile_id, source_type, source_id)
    WHERE source_type IS NOT NULL AND source_id IS NOT NULL;
