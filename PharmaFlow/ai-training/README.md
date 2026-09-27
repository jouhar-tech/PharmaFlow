# PharmaFlow Invoice AI Training Dataset

This directory is the source-of-truth contract for the custom invoice extraction model.

## Objective

Train a vision-language model to extract exactly four stock fields from pharmacy purchase invoices:

1. Product Name
2. Batch Number
3. Expiry Date
4. Billed Quantity

The dataset is designed around the exact output PharmaFlow already consumes.

## Important privacy rule

Do **not** commit real pharmacy invoices, patient/customer information, supplier addresses, phone numbers, GSTINs, invoice numbers, or API keys to this Git repository.

Keep real images in private storage and let n8n reference them or copy them into the private training workspace.

The repository contains only:
- schema
- workflow contract
- synthetic examples
- dataset documentation

## Dataset lifecycle

```text
Raw invoice
    ↓
Private image storage
    ↓
Gemini-assisted draft label (optional)
    ↓
Human verification / correction
    ↓
Validated source record
    ↓
Train / validation / test split
    ↓
Training export
```

## Required source record

Every labeled invoice must have:

- stable record ID
- image reference
- dataset version
- split
- source layout identifier
- ground-truth items
- labeling metadata

See `dataset/schema.json`.

## Splitting rules

Use:

- 80% train
- 10% validation
- 10% test

Keep supplier/layout leakage out of the test set where practical. In particular, do not allow near-duplicate photographs of the same invoice into multiple splits.

## Labeling rules

Only label genuine stock/product rows.

Extract:

- `productName`: item description only
- `batchNumber`: batch/lot number only
- `expiryDate`: normalized to `YYYY-MM-DD`
- `quantity`: billed quantity only

Do not use Free quantity.

Do not copy HSN, rack number, MRP, rates, tax values, invoice totals, supplier details, or footer notices into a product row.

If a field cannot be established from the invoice, label it as null and mark the row for review rather than guessing.

For month-only expiry such as `05/28`, store the last calendar day of the month: `2028-05-31`.

## Quality gate

A source record is eligible for training only when:

- image is readable
- all genuine stock rows are represented
- required fields were checked by a human
- no row was invented
- billed quantity is distinguished from free quantity
- expiry is normalized correctly
- product wording is faithful to the invoice

Records that fail the quality gate belong in a review/quarantine queue, not the training set.

## Files

- `schema.json` — canonical source-record schema
- `examples/sample-record.json` — synthetic example
- `n8n/step1-dataset-builder.md` — n8n workflow to create/validate records
- `manifests/manifest.template.json` — training run manifest
