(() => {
        const cameraInput = document.getElementById("dashboardCameraInput");
        const uploadInput = document.getElementById("dashboardUploadInput");
        const sourceModal = document.getElementById("invoiceCaptureSourceModal");
        const sourceTrigger = document.getElementById("dashboardScanUploadTrigger");
        const mobileScanTrigger = document.getElementById("dashboardMobileScanInvoiceTrigger");
        const inventoryScanTrigger = document.getElementById("inventoryScanInvoiceTrigger");
        const inventoryUploadTrigger = document.getElementById("inventoryUploadInvoiceTrigger");
        const sourceCamera = document.getElementById("invoiceCaptureSourceCamera");
        const sourceUpload = document.getElementById("invoiceCaptureSourceUpload");
        const sourceClose = document.getElementById("invoiceCaptureSourceClose");
        const sourceBackdrop = document.getElementById("invoiceCaptureSourceBackdrop");
        const cameraModal = document.getElementById("pharmaCameraModal");
        const cameraVideo = document.getElementById("pharmaCameraVideo");
        const cameraCanvas = document.getElementById("pharmaCameraCanvas");
        const cameraClose = document.getElementById("pharmaCameraClose");
        const cameraGallery = document.getElementById("pharmaCameraGallery");
        const cameraShutter = document.getElementById("pharmaCameraShutter");
        const cameraDone = document.getElementById("pharmaCameraDone");
        const cameraPage = document.getElementById("pharmaCameraPage");
        const selected = document.getElementById("stockCaptureSelected");
        const selectedName = document.getElementById("stockCaptureSelectedName");
        const reset = document.getElementById("stockCaptureReset");

        const scanner = document.getElementById("invoiceScannerModal");
        const scannerTitle = document.getElementById("invoiceScannerTitle");
        const scannerPreview = document.getElementById("invoiceScannerPreview");
        const scannerImage = document.getElementById("invoiceScannerImage");
        const scannerPdf = document.getElementById("invoiceScannerPdf");
        const scannerPdfName = document.getElementById("invoiceScannerPdfName");
        const rotateButton = document.getElementById("invoiceRotate");
        const enhanceButton = document.getElementById("invoiceEnhance");
        const useButton = document.getElementById("invoiceScannerUse");
        const retakeButton = document.getElementById("invoiceScannerRetake");
        const closeButton = document.getElementById("invoiceScannerClose");
        const backdrop = document.getElementById("invoiceScannerBackdrop");

        const ocrStatus = document.getElementById("invoiceOcrStatus");
        const ocrStatusTitle = document.getElementById("invoiceOcrStatusTitle");
        const ocrStatusMessage = document.getElementById("invoiceOcrStatusMessage");
        const ocrProgress = document.getElementById("invoiceOcrProgress");
        const ocrProgressBar = document.getElementById("invoiceOcrProgressBar");

        let pendingFile = null;
        let pendingSourceType = "upload";
        let previewUrl = null;
        let rotation = 0;
        let enhanceText = false;
        const MAX_FILE_SIZE = 25 * 1024 * 1024;
        const MAX_PDF_PAGES = 20;
        const MAX_CAMERA_FILES = 20;
        let cameraStream = null;
        let cameraFiles = [];
        let isProcessingInvoices = false;
        let processingFailed = false;
        let retryFiles = [];

        const setBusy = (busy) => {
            [useButton, retakeButton, rotateButton, enhanceButton, closeButton].forEach(button => {
                if (button) button.disabled = busy;
            });
        };

        const revokePreview = () => {
            if (previewUrl) {
                URL.revokeObjectURL(previewUrl);
                previewUrl = null;
            }
        };

        const updateCameraCaptureUi = () => {
            const count = cameraFiles.length;
            if (cameraPage) cameraPage.textContent = "Page " + (count + 1);
            if (cameraDone) {
                cameraDone.hidden = count === 0;
                cameraDone.querySelector("span").textContent = "Page " + (count + 1);
            }
        };

        const stopCamera = () => {
            cameraStream?.getTracks().forEach(track => track.stop());
            cameraStream = null;
            if (cameraVideo) cameraVideo.srcObject = null;
        };

        const clearCameraFiles = () => {
            cameraFiles = [];
            updateCameraCaptureUi();
        };

        const closeCamera = (clearFiles = true) => {
            stopCamera();
            cameraModal?.setAttribute("hidden", "");
            document.body.classList.remove("pharma-camera-open");
            if (clearFiles) clearCameraFiles();
        };

        const openCamera = async () => {
            closeSourceModal();

            if (!navigator.mediaDevices?.getUserMedia) {
                alert("Camera scanning is not supported by this browser. Please use Upload Invoice instead.");
                return;
            }

            try {
                stopCamera();
                cameraStream = await navigator.mediaDevices.getUserMedia({
                    video: {
                        facingMode: { ideal: "environment" },
                        width: { ideal: 1920 },
                        height: { ideal: 1080 }
                    },
                    audio: false
                });

                if (!cameraVideo) throw new Error("Camera preview is unavailable.");

                cameraVideo.srcObject = cameraStream;
                await cameraVideo.play();
                cameraModal?.removeAttribute("hidden");
                document.body.classList.add("pharma-camera-open");
                updateCameraCaptureUi();
                cameraClose?.focus();
            } catch (error) {
                console.error("Unable to open camera.", error);
                stopCamera();

                if (error?.name === "NotAllowedError" || error?.name === "SecurityError") {
                    alert("Camera permission was blocked. Allow camera access for PharmaFlow and try again.");
                } else {
                    alert("The camera could not be opened. Please use Upload Invoice instead.");
                }
            }
        };

        const captureCameraPhoto = async () => {
            if (!cameraVideo?.videoWidth || !cameraVideo?.videoHeight) return;
            if (cameraFiles.length >= MAX_CAMERA_FILES) {
                alert("You can scan up to 20 invoices at a time.");
                return;
            }

            cameraCanvas.width = cameraVideo.videoWidth;
            cameraCanvas.height = cameraVideo.videoHeight;

            const context = cameraCanvas.getContext("2d", { alpha: false });
            if (!context) {
                alert("Camera capture is not supported by this browser.");
                return;
            }

            context.drawImage(
                cameraVideo,
                0,
                0,
                cameraVideo.videoWidth,
                cameraVideo.videoHeight,
                0,
                0,
                cameraCanvas.width,
                cameraCanvas.height
            );

            const blob = await new Promise(resolve =>
                cameraCanvas.toBlob(resolve, "image/jpeg", 0.94)
            );

            if (!blob) {
                alert("The invoice photo could not be captured. Please try again.");
                return;
            }

            cameraFiles.push(
                new File(
                    [blob],
                    "invoice-camera-" + (cameraFiles.length + 1) + ".jpg",
                    {
                        type: "image/jpeg",
                        lastModified: Date.now()
                    }
                )
            );

            updateCameraCaptureUi();
        };

        const finishCameraCapture = async () => {
            if (cameraFiles.length === 0) return;

            const files = [...cameraFiles];
            closeCamera(false);
            handleInputFiles(files, "camera");
            clearCameraFiles();
        };

        const closeSourceModal = () => {
            sourceModal?.setAttribute("hidden", "");
            document.body.classList.remove("invoice-capture-source-open");
        };

        const openSourceModal = () => {
            sourceModal?.removeAttribute("hidden");
            document.body.classList.add("invoice-capture-source-open");
            sourceClose?.focus();
        };

        const chooseSource = input => {
            closeSourceModal();
            if (input) {
                input.value = "";
                input.click();
            }
        };

        const closeScanner = () => {
            // Do not let the user accidentally dismiss the only visible progress
            // indicator while invoice analysis is still running.
            if (isProcessingInvoices) return;

            scanner?.setAttribute("hidden", "");
            scanner?.classList.remove("is-processing", "has-processing-error");
            document.body.classList.remove("invoice-scanner-open");
            revokePreview();
        };

        const showFinalSelection = (file) => {
            if (!file || !selected || !selectedName) return;

            selectedName.textContent = file.name;
            selected.removeAttribute("hidden");

            document.querySelectorAll(".stock-capture-button").forEach(button => {
                button.classList.add("is-muted");
            });
        };

        const updateOcrStatus = (title, message, progress = 0) => {
            ocrStatus?.removeAttribute("hidden");
            if (ocrStatusTitle) ocrStatusTitle.textContent = title;
            if (ocrStatusMessage) ocrStatusMessage.textContent = message;
            const bounded = Math.max(0, Math.min(100, progress));
            if (ocrProgress) ocrProgress.textContent = Math.round(bounded) + "%";
            if (ocrProgressBar) ocrProgressBar.style.width = bounded + "%";
        };

        const resetOcrUi = () => {
            ocrStatus?.setAttribute("hidden", "");
            if (ocrProgressBar) ocrProgressBar.style.width = "0%";
        };

        const resetSelection = () => {
            if (isProcessingInvoices) return;

            if (cameraInput) cameraInput.value = "";
            if (uploadInput) uploadInput.value = "";
            selected?.setAttribute("hidden", "");
            pendingFile = null;
            pendingSourceType = "upload";
            retryFiles = [];
            processingFailed = false;
            window.pharmaFlowInvoiceCapture = null;
            resetOcrUi();
            setBusy(false);
            if (useButton) useButton.textContent = "Use this scan";
            if (retakeButton) retakeButton.textContent = "Retake / Choose another";
            closeScanner();

            document.querySelectorAll(".stock-capture-button").forEach(button => {
                button.classList.remove("is-muted");
            });
        };

        const fitScannerPreviewToImage = () => {
            if (!scannerImage || !scannerPreview || scannerImage.hidden) return;
            if (!scannerImage.naturalWidth || !scannerImage.naturalHeight) return;

            const stage = scannerPreview.closest(".invoice-scanner-stage");
            if (!stage) return;

            const availableWidth = Math.max(1, stage.clientWidth);
            const isMobile = window.matchMedia("(max-width: 575.98px)").matches;
            const availableHeight = Math.max(
                1,
                isMobile
                    ? window.innerHeight - 330
                    : Math.min(window.innerHeight * 0.62, 560)
            );
            const angle = ((rotation % 360) + 360) % 360;
            const rotated = angle === 90 || angle === 270;

            const imageWidth = rotated
                ? scannerImage.naturalHeight
                : scannerImage.naturalWidth;
            const imageHeight = rotated
                ? scannerImage.naturalWidth
                : scannerImage.naturalHeight;

            const scale = Math.min(
                availableWidth / imageWidth,
                availableHeight / imageHeight,
                1
            );

            const displayWidth = Math.max(1, Math.round(imageWidth * scale));
            const displayHeight = Math.max(1, Math.round(imageHeight * scale));
            const sourceDisplayWidth = Math.max(1, Math.round(scannerImage.naturalWidth * scale));
            const sourceDisplayHeight = Math.max(1, Math.round(scannerImage.naturalHeight * scale));

            // Size the actual image using its unrotated dimensions, then rotate it.
            // This keeps the crop corners locked to the visible invoice edges.
            scannerImage.style.width = sourceDisplayWidth + "px";
            scannerImage.style.height = sourceDisplayHeight + "px";

            scannerPreview.classList.add("has-image");
            scannerPreview.style.width = displayWidth + "px";
            scannerPreview.style.height = displayHeight + "px";
            scannerPreview.style.setProperty(
                "--scanner-image-ratio",
                imageWidth + " / " + imageHeight
            );
        };

        const clearScannerImageSizing = () => {
            scannerPreview?.classList.remove("has-image");
            scannerPreview?.style.removeProperty("width");
            scannerPreview?.style.removeProperty("height");
            scannerPreview?.style.removeProperty("--scanner-image-ratio");
            scannerImage?.style.removeProperty("width");
            scannerImage?.style.removeProperty("height");
        };

        const showPreviewBlob = (blob) => {
            revokePreview();
            previewUrl = URL.createObjectURL(blob);
            scannerImage.onload = fitScannerPreviewToImage;
            scannerImage.src = previewUrl;
            scannerImage.hidden = false;
            scannerPdf.hidden = true;
            scannerImage.style.transform = "rotate(0deg)";
            scannerImage.style.filter = "none";
            requestAnimationFrame(fitScannerPreviewToImage);
        };

        const pdfJsModulePromise = () => import(
            "https://cdn.jsdelivr.net/npm/pdfjs-dist@6.3.289/build/pdf.mjs"
        );

        const getPdfDocument = async (file) => {
            const pdfjsLib = await pdfJsModulePromise();
            pdfjsLib.GlobalWorkerOptions.workerSrc =
                "https://cdn.jsdelivr.net/npm/pdfjs-dist@6.3.289/build/pdf.worker.mjs";

            const data = new Uint8Array(await file.arrayBuffer());
            return pdfjsLib.getDocument({ data }).promise;
        };

        const renderPdfPage = async (file, pageNumber, targetCanvas = null) => {
            const pdf = await getPdfDocument(file);

            try {
                const page = await pdf.getPage(pageNumber);
                const unscaled = page.getViewport({ scale: 1 });
                const scale = Math.min(2.2, 2200 / Math.max(unscaled.width, unscaled.height));
                const viewport = page.getViewport({ scale });

                const canvas = targetCanvas || document.createElement("canvas");
                const ratio = window.devicePixelRatio || 1;
                canvas.width = Math.max(1, Math.floor(viewport.width * ratio));
                canvas.height = Math.max(1, Math.floor(viewport.height * ratio));

                const context = canvas.getContext("2d", { alpha: false });
                if (!context) throw new Error("Canvas is not supported by this browser.");

                await page.render({
                    canvasContext: context,
                    viewport,
                    transform: ratio !== 1 ? [ratio, 0, 0, ratio, 0, 0] : null
                }).promise;

                return canvas;
            } finally {
                await pdf.destroy();
            }
        };

        const processImage = async (file) => {
            const image = new Image();
            const url = URL.createObjectURL(file);

            try {
                image.src = url;
                await new Promise((resolve, reject) => {
                    image.onload = resolve;
                    image.onerror = () => reject(new Error("The invoice image could not be opened."));
                });

                const angle = ((rotation % 360) + 360) % 360;
                const swapDimensions = angle === 90 || angle === 270;
                // Keep enough resolution for small pharmacy-table text.
                const maxDimension = 3600;
                const sourceMax = Math.max(image.naturalWidth, image.naturalHeight);
                const scale = Math.min(1, maxDimension / sourceMax);

                const width = Math.max(1, Math.round(image.naturalWidth * scale));
                const height = Math.max(1, Math.round(image.naturalHeight * scale));

                const canvas = document.createElement("canvas");
                canvas.width = swapDimensions ? height : width;
                canvas.height = swapDimensions ? width : height;

                const context = canvas.getContext("2d", { alpha: false });
                if (!context) throw new Error("Canvas is not supported by this browser.");

                context.save();
                context.translate(canvas.width / 2, canvas.height / 2);
                context.rotate(angle * Math.PI / 180);
                context.filter = enhanceText
                    ? "grayscale(1) contrast(1.35) brightness(1.04)"
                    : "grayscale(1) contrast(1.12) brightness(1.02)";
                context.drawImage(image, -width / 2, -height / 2, width, height);
                context.restore();

                const blob = await new Promise(resolve =>
                    canvas.toBlob(resolve, "image/jpeg", 0.92)
                );

                if (!blob) throw new Error("Could not create the processed invoice image.");

                const baseName = (file.name || "invoice").replace(/\.[^.]+$/, "");
                return new File([blob], baseName + "-scan.jpg", {
                    type: "image/jpeg",
                    lastModified: Date.now()
                });
            } finally {
                URL.revokeObjectURL(url);
            }
        };

        const canvasToBlob = canvas => new Promise((resolve, reject) => {
            canvas.toBlob(blob => {
                if (blob) resolve(blob);
                else reject(new Error("Could not prepare the PDF preview."));
            }, "image/jpeg", 0.92);
        });

        const showProcessingStatus = (title = "Analyzing invoice") => {
            scanner?.classList.add("is-processing");
            scanner?.classList.remove("has-processing-error");
            scanner?.removeAttribute("hidden");
            document.body.classList.add("invoice-scanner-open");
            if (scannerTitle) scannerTitle.textContent = title;
            if (useButton) useButton.textContent = "Use this scan";
            if (retakeButton) retakeButton.textContent = "Choose another invoice";
            setBusy(true);
        };

        const showProcessingError = (message) => {
            isProcessingInvoices = false;
            processingFailed = retryFiles.length > 0;
            scanner?.classList.add("is-processing", "has-processing-error");
            scanner?.removeAttribute("hidden");
            document.body.classList.add("invoice-scanner-open");
            if (scannerTitle) scannerTitle.textContent = "Invoice analysis failed";
            setBusy(false);
            if (useButton) useButton.textContent = "Retry analysis";
            if (retakeButton) retakeButton.textContent = "Choose another invoice";
            updateOcrStatus("Invoice analysis failed", message, 0);
        };

        const prepareImageForAnalysis = async (file) => {
            const angle = ((rotation % 360) + 360) % 360;
            if (!file?.type?.startsWith("image/") || (angle === 0 && !enhanceText)) {
                return file;
            }

            const objectUrl = URL.createObjectURL(file);
            try {
                const image = new Image();
                if (typeof image.decode === "function") {
                    image.src = objectUrl;
                    await image.decode();
                } else {
                    await new Promise((resolve, reject) => {
                        image.onload = resolve;
                        image.onerror = () => reject(new Error("The invoice image could not be opened."));
                        image.src = objectUrl;
                    });
                }

                const rotated = angle === 90 || angle === 270;
                const canvas = document.createElement("canvas");
                canvas.width = rotated ? image.naturalHeight : image.naturalWidth;
                canvas.height = rotated ? image.naturalWidth : image.naturalHeight;

                const context = canvas.getContext("2d", { alpha: false });
                if (!context) throw new Error("Image preparation is not supported by this browser.");

                context.fillStyle = "#ffffff";
                context.fillRect(0, 0, canvas.width, canvas.height);
                context.translate(canvas.width / 2, canvas.height / 2);
                context.rotate(angle * Math.PI / 180);
                if (enhanceText) context.filter = "grayscale(1) contrast(1.2) brightness(1.06)";
                context.drawImage(image, -image.naturalWidth / 2, -image.naturalHeight / 2);

                const blob = await new Promise((resolve, reject) => {
                    canvas.toBlob(result => {
                        if (result) resolve(result);
                        else reject(new Error("The prepared invoice image could not be created."));
                    }, "image/jpeg", 0.94);
                });

                const baseName = (file.name || "invoice").replace(/\\.[^.]+$/, "") || "invoice";
                return new File([blob], baseName + "-scan.jpg", {
                    type: "image/jpeg",
                    lastModified: Date.now()
                });
            } finally {
                URL.revokeObjectURL(objectUrl);
            }
        };

        const useScan = async () => {
            if (isProcessingInvoices) return;

            if (processingFailed && retryFiles.length > 0) {
                await processInvoiceFiles([...retryFiles], pendingSourceType);
                return;
            }

            if (!pendingFile) {
                closeScanner();
                openSourceModal();
                return;
            }

            setBusy(true);
            scanner?.classList.add("is-processing");
            scanner?.removeAttribute("hidden");
            document.body.classList.add("invoice-scanner-open");
            if (scannerTitle) scannerTitle.textContent = "Preparing invoice";
            updateOcrStatus("Preparing invoice image...", "Applying the selected rotation and text enhancement.", 10);

            try {
                const file = await prepareImageForAnalysis(pendingFile);
                await processInvoiceFiles([file], pendingSourceType);
            } catch (error) {
                console.error("Could not prepare invoice image.", error);
                retryFiles = [pendingFile];
                showProcessingError(error?.message || "The invoice image could not be prepared. Please try again.");
            }
        };

        const postVisionForParsing = async (file, progressText = null) => {
            const token = document.querySelector(
                '#invoiceCaptureAntiForgeryToken input[name="__RequestVerificationToken"]'
            )?.value;

            if (!token) {
                throw new Error("Security token is missing. Refresh the dashboard and try again.");
            }

            updateOcrStatus(
                progressText || "Analyzing invoice...",
                "AI is reading the pharmacy table and extracting Product, Batch, Expiry and Qty.",
                35
            );

            const formData = new FormData();
            formData.append("invoice", file, file.name || "invoice");
            formData.append("originalFileName", file.name || "invoice");
            formData.append(
                "sourceType",
                pendingSourceType === "camera" ? "camera" : "upload"
            );

            const response = await fetch("/InvoiceCapture/ParseWithVision", {
                method: "POST",
                headers: {
                    "RequestVerificationToken": token
                },
                body: formData
            });

            let payload = null;
            try {
                payload = await response.json();
            } catch {
                // Keep the generic error below.
            }

            if (!response.ok || !payload?.redirectUrl) {
                throw new Error(
                    payload?.message ||
                    "The invoice could not be analyzed. Please try again with the full invoice visible."
                );
            }

            return payload.importId;
        };

        const processInvoiceFiles = async (files, sourceType) => {
            const selectedFiles = Array.from(files || []).filter(Boolean);

            if (selectedFiles.length === 0 || isProcessingInvoices) return;

            if (selectedFiles.length > 20) {
                alert("You can scan or upload up to 20 invoices at a time.");
                return;
            }

            const validFiles = [];
            for (const file of selectedFiles) {
                const isImage = file.type.startsWith("image/");
                const isPdf = file.type === "application/pdf" ||
                    file.name.toLowerCase().endsWith(".pdf");

                if (!isImage && !isPdf) {
                    alert("Please select only invoice images or PDF files.");
                    return;
                }

                if (file.size > MAX_FILE_SIZE) {
                    alert("Each invoice file must be smaller than 25 MB.");
                    return;
                }

                validFiles.push(file);
            }

            if (window.pharmaFlowConnectivity && !window.pharmaFlowConnectivity.isOnline()) {
                window.pharmaFlowConnectivity.requireOnline();
                return;
            }

            pendingSourceType = sourceType;
            retryFiles = [...validFiles];
            processingFailed = false;
            isProcessingInvoices = true;
            resetOcrUi();
            showProcessingStatus(validFiles.length > 1 ? "Analyzing invoices" : "Analyzing invoice");
            updateOcrStatus(
                selectedFiles.length === 1
                    ? "Preparing invoice..."
                    : "Preparing " + selectedFiles.length + " invoices...",
                selectedFiles.length === 1
                    ? "Uploading the invoice for AI analysis."
                    : "Uploading the captured invoices for AI analysis.",
                5
            );

            try {
                const importIds = [];

                for (let index = 0; index < validFiles.length; index++) {
                    const current = index + 1;

                    updateOcrStatus(
                        validFiles.length === 1
                            ? "Analyzing invoice..."
                            : "Analyzing invoice " + current + " of " + validFiles.length + "...",
                        "AI is extracting Product, Batch, Expiry and Quantity.",
                        Math.round((index / validFiles.length) * 90) + 5
                    );

                    const importId = await postVisionForParsing(
                        validFiles[index],
                        validFiles.length === 1
                            ? "Analyzing invoice..."
                            : "Analyzing invoice " + current + " of " + validFiles.length + "..."
                    );

                    importIds.push(importId);
                }

                updateOcrStatus(
                    "Invoice analysis complete",
                    validFiles.length === 1
                        ? "Opening the invoice review screen."
                        : validFiles.length + " invoices are ready for review.",
                    100
                );
                isProcessingInvoices = false;
                processingFailed = false;
                setBusy(false);

                if (importIds.length === 1) {
                    window.location.assign(
                        "/InvoiceCapture/Review?id=" +
                        encodeURIComponent(importIds[0])
                    );
                    return;
                }

                window.location.assign(
                    "/InvoiceCapture/BatchReview?ids=" +
                    encodeURIComponent(importIds.join(","))
                );
            } catch (error) {
                console.error("Invoice AI processing failed.", error);
                showProcessingError(
                    error?.message || "One or more invoices could not be analyzed."
                );
            }
        };

        const previewPdfFirstPage = async (file) => {
            try {
                const canvas = await renderPdfPage(file, 1);
                const blob = await canvasToBlob(canvas);
                showPreviewBlob(blob);
            } catch (error) {
                console.error("PDF preview failed.", error);
                scannerImage.hidden = true;
                scannerPdf.hidden = false;
                scannerPdfName.textContent = file.name;
            }
        };

        const openScanner = (file, sourceType) => {
            pendingFile = file;
            pendingSourceType = sourceType;
            if (scannerTitle) scannerTitle.textContent = "Review invoice scan";
            processingFailed = false;
            retryFiles = [];
            scanner?.classList.remove("is-processing", "has-processing-error");
            setBusy(false);
            if (useButton) useButton.textContent = "Use this scan";
            if (retakeButton) retakeButton.textContent = "Retake / Choose another";
            rotation = 0;
            enhanceText = false;
            resetOcrUi();

            if (enhanceButton) {
                enhanceButton.setAttribute("aria-pressed", "false");
                enhanceButton.classList.remove("is-active");
            }

            if (file.type.startsWith("image/")) {
                revokePreview();
                previewUrl = URL.createObjectURL(file);
                scannerImage.onload = fitScannerPreviewToImage;
                scannerImage.src = previewUrl;
                scannerImage.hidden = false;
                scannerPdf.hidden = true;
                scannerImage.style.transform = "rotate(0deg)";
                scannerImage.style.filter = "none";
                requestAnimationFrame(fitScannerPreviewToImage);

                document.querySelectorAll("[data-image-only]").forEach(button => {
                    button.removeAttribute("hidden");
                });
            } else {
                clearScannerImageSizing();
                scannerImage.onload = null;
                scannerImage.hidden = true;
                scannerPdf.hidden = false;
                scannerPdfName.textContent = file.name;

                document.querySelectorAll("[data-image-only]").forEach(button => {
                    button.setAttribute("hidden", "");
                });

                previewPdfFirstPage(file);
            }

            scanner?.removeAttribute("hidden");
            document.body.classList.add("invoice-scanner-open");
            closeButton?.focus();
        };

        const handleInputFiles = (files, sourceType) => {
            const selectedFiles = Array.from(files || []).filter(Boolean);
            if (selectedFiles.length === 0 || isProcessingInvoices) return;

            // Keep the existing rotate/enhance preview for a single invoice. For
            // multi-file batches, show a visible progress panel and process in order.
            if (selectedFiles.length === 1) {
                openScanner(selectedFiles[0], sourceType);
                return;
            }

            processInvoiceFiles(selectedFiles, sourceType);
        };

        const handleInput = (input, sourceType) => {
            const files = input?.files ? Array.from(input.files) : [];
            if (input) input.value = "";
            handleInputFiles(files, sourceType);
        };

        cameraInput?.addEventListener("change", () =>
            handleInput(cameraInput, "camera")
        );

        uploadInput?.addEventListener("change", () => {
            const files = uploadInput?.files ? Array.from(uploadInput.files) : [];
            if (uploadInput) uploadInput.value = "";

            if (cameraModal && !cameraModal.hasAttribute("hidden")) {
                const images = files.filter(file =>
                    file.type.startsWith("image/") &&
                    file.size <= MAX_FILE_SIZE
                );

                if (images.length > 0) {
                    cameraFiles.push(...images.slice(0, Math.max(0, MAX_CAMERA_FILES - cameraFiles.length)));
                    updateCameraCaptureUi();
                }

                if (files.some(file => !file.type.startsWith("image/") || file.size > MAX_FILE_SIZE)) {
                    alert("Only invoice images smaller than 25 MB can be added to the camera session.");
                }

                if (files.length > images.length) return;
                return;
            }

            handleInputFiles(files, "upload");
        });

        sourceTrigger?.addEventListener("click", openSourceModal);
        sourceClose?.addEventListener("click", closeSourceModal);
        sourceBackdrop?.addEventListener("click", closeSourceModal);
        sourceCamera?.addEventListener("click", openCamera);
        sourceUpload?.addEventListener("click", () => chooseSource(uploadInput));

        // Shared invoice controls work directly from both Dashboard and Inventory.
        mobileScanTrigger?.addEventListener("click", openCamera);
        inventoryScanTrigger?.addEventListener("click", openCamera);
        inventoryUploadTrigger?.addEventListener("click", () => chooseSource(uploadInput));

        cameraClose?.addEventListener("click", () => closeCamera(true));
        cameraGallery?.addEventListener("click", () => {
            if (uploadInput) {
                uploadInput.value = "";
                uploadInput.click();
            }
        });
        cameraShutter?.addEventListener("click", captureCameraPhoto);
        cameraDone?.addEventListener("click", finishCameraCapture);

        reset?.addEventListener("click", resetSelection);
        retakeButton?.addEventListener("click", () => {
            if (isProcessingInvoices) return;
            resetSelection();
            openSourceModal();
        });
        closeButton?.addEventListener("click", closeScanner);
        backdrop?.addEventListener("click", () => {
            if (!isProcessingInvoices) closeScanner();
        });
        useButton?.addEventListener("click", useScan);

        rotateButton?.addEventListener("click", () => {
            rotation = (rotation + 90) % 360;
            scannerImage.style.transform = `rotate(${rotation}deg)`;
            requestAnimationFrame(fitScannerPreviewToImage);
        });

        window.addEventListener("resize", () => {
            if (!scanner?.hasAttribute("hidden") && !scannerImage.hidden) {
                fitScannerPreviewToImage();
            }
        });

        enhanceButton?.addEventListener("click", () => {
            enhanceText = !enhanceText;
            enhanceButton.setAttribute("aria-pressed", String(enhanceText));
            enhanceButton.classList.toggle("is-active", enhanceText);
            scannerImage.style.filter = enhanceText
                ? "grayscale(1) contrast(1.2) brightness(1.06)"
                : "none";
        });

        document.addEventListener("keydown", event => {
            if (event.key === "Escape" && !cameraModal?.hasAttribute("hidden")) {
                closeCamera(true);
                clearCapturedInvoiceFiles();
                return;
            }
            if (event.key === "Escape" && !sourceModal?.hasAttribute("hidden")) {
                closeSourceModal();
                return;
            }
            if (event.key === "Escape" && !scanner?.hasAttribute("hidden")) {
                if (!isProcessingInvoices) closeScanner();
            }
        });

        // Allow the Inventory Add Item picker to reuse the existing invoice
        // camera/upload flow on the dashboard without duplicating scanner code.
        const requestedInvoiceSource = new URLSearchParams(window.location.search).get("openInvoice");
        if (requestedInvoiceSource === "scan" || requestedInvoiceSource === "upload") {
            window.setTimeout(() => {
                openSourceModal();

                if (requestedInvoiceSource === "scan") {
                    sourceCamera?.click();
                } else {
                    sourceUpload?.click();
                }

                const cleanUrl = new URL(window.location.href);
                cleanUrl.searchParams.delete("openInvoice");
                window.history.replaceState({}, document.title, cleanUrl.pathname + cleanUrl.search + cleanUrl.hash);
            }, 120);
        }
    })();
