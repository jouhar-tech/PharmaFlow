# Local storage setup for Step 2

We will use a Windows folder outside the Git repository as the private invoice staging area.

Recommended root:

```text
C:\PharmaFlow-Training\
├── inbox\
├── processing\
├── processed\
├── quarantine\
├── records\
├── exports\
└── manifests\
```

## Create the folders

Run the repository script from PowerShell:

```powershell
.PharmaFlow\ai-training\local-storage\setup-local-training-storage.ps1
```

To use another location:

```powershell
.PharmaFlow\ai-training\local-storage\setup-local-training-storage.ps1 -Root "D:\PharmaFlow-Training"
```

Keep this folder **outside the Git repository**.

## What each folder is for

- `inbox` — new invoice images/PDFs
- `processing` — invoice currently being handled
- `processed` — successfully processed source files
- `quarantine` — unreadable/invalid/problem files
- `records` — validated source JSON records
- `exports` — generated JSONL training files
- `manifests` — generated dataset manifests

The actual invoice stays private. Git contains only the dataset schema and workflow documentation.

## n8n requirement

For local filesystem processing, n8n must be self-hosted on the machine that can access this directory, or the host directory must be explicitly mounted into the n8n container.

n8n provides filesystem nodes including **Local File Trigger** and **Read/Write Files from Disk**. citeturn974756search7

### Windows/self-hosted n8n

Use the following workflow:

```text
Local File Trigger
       ↓
Read/Write Files from Disk
       ↓
HTTP Request → PharmaFlow /InvoiceTraining/DraftLabel
       ↓
Normalize draft
       ↓
Human verification
       ↓
Write record JSON
       ↓
Move source invoice to processed/quarantine
```

### Docker n8n

Do not use a Windows path directly from inside the container.

Example concept:

```text
Windows:
C:\PharmaFlow-Training

        mounted as

Container:
/files/pharmaflow-training
```

Then all n8n filesystem nodes use:

```text
/files/pharmaflow-training/inbox
```

The host-to-container mount must be configured when starting n8n.

## File naming

Use a stable, non-sensitive identifier rather than putting customer information into filenames.

Recommended:

```text
PFINV-000001.jpg
PFINV-000002.jpg
PFINV-000003.pdf
```

Do not use:

```text
CustomerName_InvoiceNumber_Phone.jpg
```

## Processing rules

1. Put a new invoice into `inbox`.
2. n8n picks it up.
3. Move/copy it into `processing` before calling the API.
4. Call `POST /InvoiceTraining/DraftLabel`.
5. Validate the returned rows.
6. Send the draft to human verification.
7. Save only the verified source record under `records`.
8. Move the source invoice to `processed` after successful labeling.
9. Move failed/unreadable invoices to `quarantine`.
10. Never treat an AI draft as training ground truth until human verification is complete.

## Concurrency

Start with **one invoice at a time** while building the first dataset. This makes quota usage and failures easy to trace.

Later, when the custom model is deployed, the same local queue can feed multiple workers. The queue architecture should be kept separate from the model itself.

## Security

Use the dedicated request header:

```text
X-PharmaFlow-Training-Key
```

Keep its value in an n8n credential/environment secret. Never store it in this repository.

Also keep:

```text
GEMINI_API_KEY
```

outside Git and outside Code-node source.

n8n's own security audit can report filesystem-related nodes and other security issues, so run an audit before exposing the self-hosted instance beyond your local network. citeturn974756search12

## Step 2 checkpoint

After this setup we should be able to:

```text
drop invoice into C:\PharmaFlow-Training\inbox
            ↓
n8n sees it
            ↓
PharmaFlow receives it securely
            ↓
Gemini returns draft rows
            ↓
human corrects/approves rows
            ↓
verified JSON record is saved
```

No model training happens yet.
