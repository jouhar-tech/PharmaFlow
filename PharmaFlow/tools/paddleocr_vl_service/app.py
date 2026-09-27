import asyncio
import os
import shutil
import tempfile
from pathlib import Path

from fastapi import FastAPI, File, Header, HTTPException, UploadFile

from paddleocr import PaddleOCRVL


MAX_FILE_BYTES = 25 * 1024 * 1024
ALLOWED_EXTENSIONS = {".jpg", ".jpeg", ".png", ".webp", ".pdf"}

DEVICE = os.getenv("PADDLEOCR_DEVICE", "cpu").strip() or "cpu"
PIPELINE_VERSION = os.getenv("PADDLEOCR_PIPELINE_VERSION", "v1.5").strip() or "v1.5"
API_KEY = os.getenv("PADDLEOCR_API_KEY", "").strip()

app = FastAPI(
    title="PharmaFlow PaddleOCR-VL Invoice Service",
    version="1.0.0",
)

_pipeline = None
_pipeline_lock = asyncio.Lock()


def get_pipeline():
    global _pipeline

    if _pipeline is None:
        kwargs = {
            "pipeline_version": PIPELINE_VERSION,
            "device": DEVICE,
            "use_doc_orientation_classify": True,
            "use_doc_unwarping": True,
            "use_layout_detection": True,
            "use_ocr_for_image_block": True,
        }
        _pipeline = PaddleOCRVL(**kwargs)

    return _pipeline


def validate_api_key(received_key: str | None) -> None:
    if API_KEY and received_key != API_KEY:
        raise HTTPException(status_code=401, detail="Invalid PaddleOCR API key.")


async def save_upload(upload: UploadFile, destination: Path) -> int:
    size = 0

    with destination.open("wb") as target:
        while True:
            chunk = await upload.read(1024 * 1024)
            if not chunk:
                break

            size += len(chunk)
            if size > MAX_FILE_BYTES:
                raise HTTPException(
                    status_code=413,
                    detail="The invoice file is too large. Maximum size is 25 MB.",
                )

            target.write(chunk)

    return size


def collect_markdown(output_directory: Path) -> str:
    markdown_files = sorted(output_directory.glob("*.md"))

    if not markdown_files:
        raise RuntimeError("PaddleOCR did not create a Markdown result.")

    parts = []

    for markdown_file in markdown_files:
        content = markdown_file.read_text(encoding="utf-8", errors="replace").strip()

        if content:
            parts.append(content)

    if not parts:
        raise RuntimeError("PaddleOCR created an empty document result.")

    return "\n\n".join(parts)


def run_pipeline(input_path: str, extension: str) -> tuple[str, int]:
    pipeline = get_pipeline()
    output_directory = Path(tempfile.mkdtemp(prefix="pharmaflow_paddle_"))

    try:
        page_results = list(pipeline.predict(input=input_path))

        if not page_results:
            raise RuntimeError("PaddleOCR returned no document pages.")

        page_count = len(page_results)

        if extension == ".pdf" and page_count > 1:
            page_results = list(
                pipeline.restructure_pages(
                    page_results,
                    merge_tables=True,
                    relevel_titles=True,
                    concatenate_pages=True,
                )
            )

        for result in page_results:
            result.save_to_markdown(save_path=output_directory)

        markdown = collect_markdown(output_directory)
        return markdown, page_count
    finally:
        shutil.rmtree(output_directory, ignore_errors=True)


@app.get("/health")
async def health():
    return {
        "status": "ok",
        "model": f"PaddleOCR-VL-{PIPELINE_VERSION}",
        "device": DEVICE,
    }


@app.post("/extract")
async def extract_invoice(
    invoice: UploadFile = File(...),
    x_paddleocr_api_key: str | None = Header(default=None),
):
    validate_api_key(x_paddleocr_api_key)

    filename = Path(invoice.filename or "invoice.jpg").name
    extension = Path(filename).suffix.lower()

    if extension not in ALLOWED_EXTENSIONS:
        raise HTTPException(
            status_code=400,
            detail="Unsupported file type. Use JPG, PNG, WEBP or PDF.",
        )

    content_type = (invoice.content_type or "").lower()

    if extension == ".pdf":
        if content_type not in {"", "application/pdf", "application/octet-stream"}:
            raise HTTPException(status_code=400, detail="Invalid PDF content type.")
    elif content_type and not content_type.startswith("image/"):
        raise HTTPException(status_code=400, detail="Invalid image content type.")

    with tempfile.TemporaryDirectory(prefix="pharmaflow_invoice_") as temp_dir:
        input_path = Path(temp_dir) / ("invoice" + extension)
        size = await save_upload(invoice, input_path)

        if size <= 0:
            raise HTTPException(status_code=400, detail="The invoice file is empty.")

        try:
            async with _pipeline_lock:
                markdown, page_count = await asyncio.to_thread(
                    run_pipeline,
                    str(input_path),
                    extension,
                )

        except HTTPException:
            raise
        except Exception as exc:
            raise HTTPException(
                status_code=502,
                detail=f"PaddleOCR invoice extraction failed: {exc}",
            ) from exc

    return {
        "success": True,
        "pageCount": page_count,
        "markdown": markdown,
    }
