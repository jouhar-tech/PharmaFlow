param(
    [string]$Root = "C:\PharmaFlow-Training"
)

$ErrorActionPreference = "Stop"

$folders = @(
    $Root,
    (Join-Path $Root "inbox"),
    (Join-Path $Root "processing"),
    (Join-Path $Root "processed"),
    (Join-Path $Root "quarantine"),
    (Join-Path $Root "records"),
    (Join-Path $Root "exports"),
    (Join-Path $Root "manifests")
)

foreach ($folder in $folders) {
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
}

@"
PharmaFlow local training storage

inbox       = new invoice images/PDFs waiting for processing
processing  = temporary working files while n8n is processing
processed   = invoices successfully labeled and human-verified
quarantine  = unreadable/invalid/failed items requiring manual review
records     = validated JSON source records
exports     = generated JSONL training exports
manifests   = generated dataset manifests

Never put this directory inside the Git repository.
Never commit real invoices, labels, credentials, or API keys.
"@ | Set-Content -Path (Join-Path $Root "README.txt") -Encoding UTF8

Write-Host ""
Write-Host "PharmaFlow local training storage created:"
Write-Host $Root
Write-Host ""
$folders | ForEach-Object { Write-Host " - $_" }
