-- PharmaFlow performance indexes
-- Run once in Supabase SQL Editor.
-- These indexes do not change application behavior; they reduce scan cost for
-- the existing inventory, billing, customer, purchase and reporting queries.

-- Inventory lookup/listing.
CREATE INDEX IF NOT EXISTS ix_products_profile_active_name
    ON public.products(profile_id, is_active, product_name);

CREATE INDEX IF NOT EXISTS ix_products_profile_barcode
    ON public.products(profile_id, barcode)
    WHERE barcode IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_product_batches_product_active_expiry
    ON public.product_batches(product_id, is_active, is_quarantined, expiry_date);

CREATE INDEX IF NOT EXISTS ix_product_batches_product_stock
    ON public.product_batches(product_id, is_active, is_quarantined, quantity_on_hand);

-- Billing and bill history.
CREATE INDEX IF NOT EXISTS ix_sales_bills_profile_status_created
    ON public.sales_bills(profile_id, status, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_sales_bill_items_bill_product_name
    ON public.sales_bill_items(bill_id, product_name);

-- Customer ledger/reminders.
CREATE INDEX IF NOT EXISTS ix_customer_ledger_profile_customer_created
    ON public.customer_ledger_entries(profile_id, customer_id, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_customer_reminders_profile_status_date
    ON public.customer_reminders(profile_id, status, reminder_date);

-- Login/admin lookups and case-insensitive username searches.
CREATE INDEX IF NOT EXISTS ix_profiles_lower_username
    ON public.profiles(lower(username));

CREATE INDEX IF NOT EXISTS ix_profiles_lower_email
    ON public.profiles(lower(email))
    WHERE email IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_profiles_phone
    ON public.profiles(phone_number)
    WHERE phone_number IS NOT NULL;

-- Purchase page/distributor drill-down.
CREATE INDEX IF NOT EXISTS ix_invoice_imports_profile_status_date
    ON public.invoice_imports(profile_id, status, invoice_date DESC);

CREATE INDEX IF NOT EXISTS ix_invoice_imports_profile_lower_distributor
    ON public.invoice_imports(profile_id, lower(distributor_name))
    WHERE distributor_name IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_invoice_import_items_import_row
    ON public.invoice_import_items(import_id, row_number);

-- Catalog lookups.
CREATE INDEX IF NOT EXISTS ix_product_catalog_source_external
    ON public.product_catalog(source, external_id);

-- Optional PostgreSQL trigram acceleration for substring searches.
-- Enable only when supported by your Supabase project.
CREATE EXTENSION IF NOT EXISTS pg_trgm;

CREATE INDEX IF NOT EXISTS ix_products_product_name_trgm
    ON public.products USING gin (product_name gin_trgm_ops);

CREATE INDEX IF NOT EXISTS ix_products_generic_name_trgm
    ON public.products USING gin (generic_name gin_trgm_ops);

CREATE INDEX IF NOT EXISTS ix_products_brand_name_trgm
    ON public.products USING gin (brand_name gin_trgm_ops);

CREATE INDEX IF NOT EXISTS ix_sales_bill_items_product_name_trgm
    ON public.sales_bill_items USING gin (product_name gin_trgm_ops);

CREATE INDEX IF NOT EXISTS ix_customers_full_name_trgm
    ON public.customers USING gin (full_name gin_trgm_ops);

CREATE INDEX IF NOT EXISTS ix_customers_phone_trgm
    ON public.customers USING gin (phone_number gin_trgm_ops);

CREATE INDEX IF NOT EXISTS ix_invoice_imports_distributor_trgm
    ON public.invoice_imports USING gin (distributor_name gin_trgm_ops);

-- Verification.
SELECT indexname
FROM pg_indexes
WHERE schemaname = 'public'
  AND (
      indexname LIKE 'ix_products_%'
      OR indexname LIKE 'ix_product_batches_%'
      OR indexname LIKE 'ix_sales_bills_%'
      OR indexname LIKE 'ix_sales_bill_items_%'
      OR indexname LIKE 'ix_customer_ledger_%'
      OR indexname LIKE 'ix_customer_reminders_%'
      OR indexname LIKE 'ix_profiles_%'
      OR indexname LIKE 'ix_invoice_imports_%'
      OR indexname LIKE 'ix_invoice_import_items_%'
      OR indexname LIKE 'ix_product_catalog_%'
      OR indexname LIKE 'ix_customers_%'
  )
ORDER BY indexname;
