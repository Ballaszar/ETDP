let mammothBrowserModulePromise = null;

const parseDownloadFileName = (contentDisposition, fallbackName) => {
  const raw = String(contentDisposition || "");
  if (!raw) return fallbackName;

  const utf8Match = raw.match(/filename\*=UTF-8''([^;]+)/i);
  if (utf8Match?.[1]) {
    try {
      return decodeURIComponent(utf8Match[1]);
    } catch {
      return utf8Match[1];
    }
  }

  const asciiMatch = raw.match(/filename="?([^\";]+)"?/i);
  if (asciiMatch?.[1]) return asciiMatch[1];
  return fallbackName;
};

const loadMammothBrowser = async () => {
  if (!mammothBrowserModulePromise) {
    mammothBrowserModulePromise = import("mammoth/mammoth.browser");
  }
  return mammothBrowserModulePromise;
};

export const fetchDocxPreview = async (url, fallbackName = "document.docx", requestInit = {}) => {
  const response = await fetch(String(url || ""), {
    ...requestInit,
    cache: requestInit?.cache ?? "no-store"
  });
  if (!response.ok) {
    const text = await response.text().catch(() => "");
    throw new Error(text || `Preview request failed (${response.status}).`);
  }

  const fileName = parseDownloadFileName(response.headers.get("content-disposition"), fallbackName);
  const blob = await response.blob();
  const arrayBuffer = await blob.arrayBuffer();

  const mammoth = await loadMammothBrowser();
  const converted = await mammoth.convertToHtml({ arrayBuffer });

  const warnings = Array.isArray(converted?.messages)
    ? converted.messages
      .map((msg) => String(msg?.message || "").trim())
      .filter(Boolean)
    : [];

  return {
    html: String(converted?.value || "").trim(),
    warnings,
    fileName
  };
};
