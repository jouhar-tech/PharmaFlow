-- PharmaFlow invoice purchase price fix
-- Run this once in the Supabase SQL editor after deploying the matching code.
-- Safe to re-run: the column uses IF NOT EXISTS and only zero prices are backfilled.

BEGIN;

ALTER TABLE public.invoice_import_items
    ADD COLUMN IF NOT EXISTS purchase_unit_price NUMERIC(14,2) NULL;

-- Previous versions extracted MRP onto invoice_import_items but created
-- product_batches with selling_unit_price = 0. Backfill from saved invoice rows.
WITH latest_saved_mrp AS
(
    SELECT DISTINCT ON (saved_batch_id)
        saved_batch_id,
        mrp
    FROM public.invoice_import_items
    WHERE saved_batch_id IS NOT NULL
      AND validation_status = 'saved'
      AND mrp IS NOT NULL
      AND mrp > 0
    ORDER BY saved_batch_id, updated_at DESC, import_item_id DESC
)
UPDATE public.product_batches AS batch
SET selling_unit_price = latest_saved_mrp.mrp,
    updated_at = NOW()
FROM latest_saved_mrp
WHERE batch.batch_id = latest_saved_mrp.saved_batch_id
  AND COALESCE(batch.selling_unit_price, 0) = 0;

-- Apply stored purchase prices if any exist. Older imports normally have NULL
-- here because the old AI schema never extracted purchase unit price.
WITH latest_saved_purchase_price AS
(
    SELECT DISTINCT ON (saved_batch_id)
        saved_batch_id,
        purchase_unit_price
    FROM public.invoice_import_items
    WHERE saved_batch_id IS NOT NULL
      AND validation_status = 'saved'
      AND purchase_unit_price IS NOT NULL
      AND purchase_unit_price > 0
    ORDER BY saved_batch_id, updated_at DESC, import_item_id DESC
)
UPDATE public.product_batches AS batch
SET purchase_unit_price = latest_saved_purchase_price.purchase_unit_price,
    updated_at = NOW()
FROM latest_saved_purchase_price
WHERE batch.batch_id = latest_saved_purchase_price.saved_batch_id
  AND COALESCE(batch.purchase_unit_price, 0) = 0;

COMMIT;

-- Historical purchase price cannot be recovered when it was never extracted or
-- stored. For those older batches, re-scan the original invoice or edit Purchase
-- Price manually in Inventory > Product Details.
