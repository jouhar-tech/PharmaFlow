# Step 1 n8n workflow — build the labeled invoice dataset

This is the orchestration contract for the first n8n workflow. Keep the workflow private because invoice images can contain business-sensitive information.

## Workflow

```text
Manual Trigger
    ↓
Load new invoice metadata/image reference
    ↓
Optional Gemini draft extraction
    ↓
Normalize the four required fields
    ↓
Human verification
    ↓
Quality gate
    ↓
Assign split
    ↓
Write validated JSON record
    ↓
Append/update dataset manifest
```

## Recommended n8n nodes

### 1. Manual Trigger

Use this while the dataset is being built. Later replace it with a Schedule Trigger or event-driven workflow.

### 2. Load invoice

Use one private source of truth for images, for example:

- Google Drive
- OneDrive
- Azure Blob Storage
- a private S3-compatible bucket
- local disk when n8n is self-hosted

Do not store the actual invoice image in Git.

The node should produce:

```json
{
  "recordId": "invoice-unique-id",
  "imageUri": "private://...",
  "mimeType": "image/jpeg",
  "layoutId": "supplier-layout-id",
  "captureType": "camera"
}
```

### 3. Optional Gemini draft label

Send the invoice to the existing Gemini invoice extraction endpoint/workflow only to generate a **draft** label.

The draft output must contain only:

```json
{
  "items": [
    {
      "productName": "...",
      "batchNumber": "...",
      "expiryDate": "YYYY-MM-DD",
      "quantity": 3
    }
  ]
}
```

Never treat Gemini output as ground truth without human verification.

### 4. Normalize

Use an n8n Code node to:

- trim product and batch strings
- normalize whitespace
- convert month-only expiry to the last calendar day
- make quantity numeric
- reject quantity <= 0
- reject quantities > 1,000,000
- reject empty product/batch/expiry values
- cap rows at 200

The Code node should not invent missing values.

### 5. Human verification

For the initial 100–500 invoices, verify every row.

The human must check:

```text
Product Name
Batch Number
Expiry Date
Billed Quantity
```

The verifier may correct Gemini's draft. The corrected value becomes the ground truth.

### 6. Quality gate

A record passes only when:

- every genuine stock row is present
- no footer/header row was included
- billed quantity is used instead of free quantity
- expiry is normalized to ISO format
- product and batch are visually verified
- the human confirms the record

Anything else goes to `quarantine`.

### 7. Assign split

Use a deterministic split, not random re-splitting on every run.

Recommended initial proportions:

```text
80% train
10% validation
10% test
```

Keep the test set locked once a training cycle starts.

Avoid putting near-duplicate images or the same invoice in multiple splits.

### 8. Write the source record

Write one JSON document per invoice using `schema.json`.

Recommended storage layout:

```text
private-training/
  v1/
    images/
      invoice-001.jpg
      invoice-002.jpg
    records/
      invoice-001.json
      invoice-002.json
    manifests/
      manifest-v1.json
```

### 9. Build JSONL for training

Only after source records pass the quality gate, export the training set into JSONL.

Do not hand-edit the generated JSONL. Regenerate it from the source records so the dataset remains reproducible.

## Security rules

- Never put API keys in Code nodes.
- Use n8n credentials/environment variables for Gemini and storage.
- Do not commit real invoices to Git.
- Keep customer/supplier information outside the training label unless it is genuinely required for the task.
- Remove invoice metadata and footer text from the label.
- Keep the test dataset private.

## Deliverable from Step 1

At the end of Step 1 we should have:

```text
100+ verified invoice images
100+ validated source records
80/10/10 split
dataset manifest
reproducible JSONL export
```

Do not start fine-tuning until this quality gate is satisfied.
