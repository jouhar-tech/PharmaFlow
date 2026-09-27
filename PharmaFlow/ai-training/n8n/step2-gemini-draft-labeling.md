# Step 2 — Gemini-assisted draft labeling

Step 2 adds a secure backend bridge so n8n can submit a private invoice image/PDF and receive a **draft** structured label from the existing Gemini invoice-vision service.

This step does **not** make Gemini ground truth. Human verification remains mandatory.

## Architecture

```text
Private invoice storage
        ↓
       n8n
        ↓
POST /InvoiceTraining/DraftLabel
        ↓
PharmaFlow
        ↓
GeminiInvoiceVisionService
        ↓
Draft JSON
        ↓
n8n normalization / human review
        ↓
validated source record
```

## Backend endpoint

PharmaFlow exposes:

```http
POST /InvoiceTraining/DraftLabel
Content-Type: multipart/form-data
X-PharmaFlow-Training-Key: <shared secret>
```

Form fields:

- `invoice` — binary image/PDF
- `originalFileName` — original filename
- `sourceType` — `camera`, `scan`, `pdf`, or `upload`

The endpoint:

- requires a dedicated API key
- accepts JPEG, PNG, WebP, and PDF
- enforces a 25 MB file limit
- limits returned rows to 200
- returns only the fields needed for dataset labeling plus row confidence
- normalizes expiry to ISO `YYYY-MM-DD`
- does not persist the uploaded invoice
- does not return raw OCR or Gemini internals
- logs only operational failure information, not invoice contents

### Security configuration

Set the backend secret as an environment variable:

```text
PHARMAFLOW_TRAINING_API_KEY=<long-random-secret>
```

The existing Gemini service continues to use:

```text
GEMINI_API_KEY=<Gemini API key>
```

Never put either secret in Git or in an n8n Code node.

## n8n sub-workflow contract

Build this as a reusable sub-workflow so the private storage connector can be changed later without touching the Gemini integration.

Input to the sub-workflow:

```json
{
  "json": {
    "recordId": "invoice-001",
    "imageUri": "private://invoice-001.jpg",
    "originalFileName": "invoice-001.jpg",
    "sourceType": "upload"
  },
  "binary": {
    "data": "<invoice binary>"
  }
}
```

The binary property must be named `data`.

### Node 1 — Execute Workflow Trigger

Use the incoming item from the storage workflow.

### Node 2 — HTTP Request

Configure:

```text
Method: POST
URL: https://<your-pharmaflow-host>/InvoiceTraining/DraftLabel
```

Authentication:

Use a header credential or environment-backed value:

```text
X-PharmaFlow-Training-Key: <PHARMAFLOW_TRAINING_API_KEY>
```

Body type:

```text
multipart/form-data
```

Form fields:

```text
invoice           = binary field "data"
originalFileName  = {{$json.originalFileName}}
sourceType        = {{$json.sourceType}}
```

Do not paste the API key into a Code node.

### Expected response

```json
{
  "draft": true,
  "fileName": "invoice-001.jpg",
  "sourceType": "upload",
  "generatedAtUtc": "2026-09-27T12:00:00Z",
  "items": [
    {
      "rowNumber": 1,
      "productName": "PARACETAMOL 500MG TAB",
      "batchNumber": "ABC123",
      "expiryDate": "2028-05-31",
      "quantity": 20,
      "confidence": 94
    }
  ]
}
```

Missing/unreadable fields are returned as `null`; they must be reviewed rather than guessed.

## Node 3 — Draft normalization

Before human review, add an n8n Code node that:

1. trims product/batch whitespace
2. rejects empty product or batch values
3. requires a normalized expiry date
4. requires quantity > 0
5. rejects quantity > 1,000,000
6. preserves Gemini confidence
7. keeps a row-level `needsReview` flag when any required field is missing
8. never fills missing values with guessed data

Suggested normalized row:

```json
{
  "productName": "PARACETAMOL 500MG TAB",
  "batchNumber": "ABC123",
  "expiryDate": "2028-05-31",
  "quantity": 20,
  "confidence": 94,
  "needsReview": false
}
```

## Node 4 — Human verification

For every initial training invoice:

- visually verify every genuine stock row
- correct Product Name, Batch Number, Expiry Date, and Billed Quantity
- remove header/footer/non-stock rows
- confirm that Free Quantity was not used
- mark the invoice verified only after all rows were checked

Gemini output is a draft only.

## Storage connector

Keep the storage side provider-specific. The first implementation can use:

- Google Drive
- OneDrive
- Azure Blob Storage
- private S3-compatible storage
- self-hosted n8n local files

The storage node must end by producing binary property `data` plus the metadata shown above.

## Failure handling

For HTTP 401:
- the shared training key is missing/wrong
- do not retry automatically

For HTTP 400:
- file type/size/request validation failed
- send the invoice to review

For HTTP 422:
- Gemini could not produce usable rows, or Gemini rejected the request
- mark the draft as `needsReview`
- keep the invoice for manual labeling

For HTTP 429:
- Gemini quota/rate limit was reached
- do not create a duplicate invoice record
- retry later with exponential backoff or route that invoice to manual labeling

For HTTP 5xx:
- retry with bounded backoff
- stop after a small number of attempts
- do not endlessly replay the same invoice

## Dataset rule

Only after human verification should n8n create the Step 1 source record and include the invoice in train/validation/test data.

No fine-tuning starts in Step 2.
