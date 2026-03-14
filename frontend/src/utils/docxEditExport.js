import { Document, Packer, Paragraph, TextRun } from "docx";

const normalizeText = (value) => String(value || "").replace(/\r\n/g, "\n");
const normalizeFileName = (fileName, fallback = "EditedDocument.docx") => {
  const safeName = String(fileName || "").trim();
  if (!safeName) return fallback;
  return safeName;
};

export const htmlToEditableText = (html) => {
  const source = String(html || "").trim();
  if (!source) return "";
  if (typeof document === "undefined") return source;

  const container = document.createElement("div");
  container.innerHTML = source;
  const text = String(container.textContent || container.innerText || "").trim();
  return text;
};

const toParagraphRuns = (blockText) => {
  const lines = normalizeText(blockText).split("\n");
  const runs = [];

  lines.forEach((line, index) => {
    const clean = String(line || "");
    if (index > 0) runs.push(new TextRun({ text: "", break: 1 }));
    runs.push(new TextRun(clean));
  });

  if (runs.length === 0) runs.push(new TextRun(""));
  return runs;
};

const buildModernDocxChildren = (normalizedText, title) => {
  const blocks = normalizedText
    .split(/\n{2,}/)
    .map((block) => block.trim())
    .filter(Boolean);

  const children = [];
  if (String(title || "").trim()) {
    children.push(
      new Paragraph({
        children: [new TextRun({ text: String(title).trim(), bold: true, size: 28 })],
        spacing: { after: 320 }
      })
    );
  }

  for (const block of blocks) {
    children.push(
      new Paragraph({
        children: toParagraphRuns(block),
        spacing: { after: 220 }
      })
    );
  }

  return children;
};

const buildLegacyDocxChildren = (normalizedText, title) => {
  const lines = normalizeText(normalizedText).split("\n");
  const children = [];

  if (String(title || "").trim()) {
    children.push(new Paragraph({ children: [new TextRun(String(title).trim())] }));
    children.push(new Paragraph({ children: [new TextRun("")] }));
  }

  for (const line of lines) {
    children.push(new Paragraph({ children: [new TextRun(String(line || ""))] }));
  }

  if (children.length === 0) {
    children.push(new Paragraph({ children: [new TextRun("")] }));
  }
  return children;
};

const escapeRtfText = (value) => {
  const text = String(value || "");
  let out = "";

  for (const ch of text) {
    if (ch === "\\") {
      out += "\\\\";
      continue;
    }
    if (ch === "{") {
      out += "\\{";
      continue;
    }
    if (ch === "}") {
      out += "\\}";
      continue;
    }

    const code = ch.codePointAt(0) || 0;
    if (code > 127) {
      const signed = code > 32767 ? code - 65536 : code;
      out += `\\u${signed}?`;
    } else {
      out += ch;
    }
  }

  return out;
};

export const downloadEditedDocxFromText = async ({
  text,
  fileName = "EditedDocument.docx",
  title = "",
  legacyMode = false
}) => {
  const normalized = normalizeText(text).trim();
  if (!normalized) throw new Error("Edited text is empty.");

  const children = legacyMode
    ? buildLegacyDocxChildren(normalized, title)
    : buildModernDocxChildren(normalized, title);

  const doc = new Document({
    sections: [{ properties: {}, children }]
  });

  const blob = await Packer.toBlob(doc);
  const safeName = normalizeFileName(fileName, "EditedDocument.docx");
  const finalName = safeName.toLowerCase().endsWith(".docx") ? safeName : `${safeName}.docx`;

  const url = window.URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = finalName;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  window.URL.revokeObjectURL(url);
};

export const downloadEditedRtfFromText = async ({
  text,
  fileName = "EditedDocument.rtf",
  title = ""
}) => {
  const normalized = normalizeText(text).trim();
  if (!normalized) throw new Error("Edited text is empty.");

  const lines = normalized.split("\n");
  const fragments = [
    "{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0 Calibri;}}\\viewkind4\\uc1\\pard\\f0\\fs22"
  ];

  if (String(title || "").trim()) {
    fragments.push(`\\b ${escapeRtfText(String(title).trim())}\\b0\\par`);
    fragments.push("\\par");
  }

  for (const line of lines) {
    if (!String(line || "").trim()) {
      fragments.push("\\par");
    } else {
      fragments.push(`${escapeRtfText(line)}\\par`);
    }
  }

  fragments.push("}");
  const rtf = fragments.join("\n");
  const blob = new Blob([rtf], { type: "application/rtf;charset=utf-8" });

  const safeName = normalizeFileName(fileName, "EditedDocument.rtf");
  const finalName = safeName.toLowerCase().endsWith(".rtf") ? safeName : `${safeName}.rtf`;
  const url = window.URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = finalName;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  window.URL.revokeObjectURL(url);
};
