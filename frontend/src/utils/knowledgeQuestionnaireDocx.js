import {
  AlignmentType,
  Document,
  HeadingLevel,
  Packer,
  Paragraph,
  TextRun
} from "docx";
import { downloadBlobFile } from "./questionnaireDesigner";

const asText = (value) => String(value ?? "").trim();

const asInt = (value, fallback = 0) => {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? Math.max(0, Math.trunc(parsed)) : fallback;
};

const escapeHtml = (value) => String(value ?? "")
  .replace(/&/g, "&amp;")
  .replace(/</g, "&lt;")
  .replace(/>/g, "&gt;")
  .replace(/"/g, "&quot;")
  .replace(/'/g, "&#39;");

const formatInlineText = (value) => escapeHtml(asText(value)).replace(/\n/g, "<br/>");

const normalizeQuestionType = (value) => (
  String(value ?? "").trim().toLowerCase() === "truefalse"
    ? "True/False"
    : "Multiple Choice"
);

const buildPhaseLabel = (payload) => {
  const name = asText(payload?.phaseName);
  const description = asText(payload?.phaseDescription);
  if (name && description && description.toLowerCase() !== name.toLowerCase()) {
    return `${name} - ${description}`;
  }
  return name || description;
};

const buildQualificationLine = (payload) => {
  const qualificationNumber = asText(payload?.qualificationNumber);
  const qualificationDescription = asText(payload?.qualificationDescription);
  return qualificationDescription || qualificationNumber;
};

const buildCategoryLine = (payload) => {
  const mainCategoryCode = asText(payload?.mainCategoryCode);
  const mainCategoryLabel = asText(payload?.mainCategoryLabel);
  return [mainCategoryCode, mainCategoryLabel].filter(Boolean).join(" - ") || "Main Category";
};

const buildQuestionBlockContext = (question) => {
  const subjectLine = [asText(question?.subjectCode), asText(question?.subjectDescription)].filter(Boolean).join(" - ");
  const topicLine = [asText(question?.topicCode), asText(question?.topicDescription)].filter(Boolean).join(" - ");
  const criterionLine = [asText(question?.assessmentCriteriaNumber), asText(question?.assessmentCriteriaDescription)].filter(Boolean).join(" - ");
  return [subjectLine, topicLine, criterionLine].filter(Boolean);
};

const buildAnswerKey = (question) => {
  const answer = asText(question?.correctAnswer);
  if (answer) return answer;
  if (String(question?.type ?? "").trim().toLowerCase() === "truefalse") {
    return "True / False";
  }
  return "No answer key provided";
};

const buildOptionLines = (question) => {
  const options = Array.isArray(question?.options) ? question.options : [];
  return options
    .map((option, index) => `${String.fromCharCode(65 + index)}. ${asText(option)}`)
    .filter((line) => line !== "A. ");
};

const looksLikeStatementOption = (value) => {
  const text = asText(value);
  if (!text) return false;
  if (/[.!?]$/.test(text)) return true;
  return text.split(/\s+/).filter(Boolean).length >= 5;
};

const shouldRenderTrueFalseRows = (question) => {
  const prompt = asText(question?.prompt).toLowerCase();
  if (normalizeQuestionType(question?.type) === "True/False") return true;
  if (prompt.includes("true or false")) return true;
  const options = Array.isArray(question?.options) ? question.options : [];
  return options.length > 0 && options.every((option) => looksLikeStatementOption(option));
};

const buildHtmlQuestionCard = (question, memorandum) => {
  const contextLines = buildQuestionBlockContext(question);
  const optionLines = buildOptionLines(question);
  const answerKey = buildAnswerKey(question);
  const rationale = asText(question?.rationale);

  return `
    <section style="border:1px solid #d6dbe4;border-radius:8px;padding:16px 18px;margin:0 0 16px 0;background:#fff;">
      <div style="font-size:14px;font-weight:700;color:#10233c;margin-bottom:8px;">
        Question ${asInt(question?.number, 0)} - ${escapeHtml(normalizeQuestionType(question?.type))}
      </div>
      ${contextLines.length > 0 ? `
        <div style="font-size:12px;color:#58677b;line-height:1.5;margin-bottom:10px;">
          ${contextLines.map((line) => `<div>${formatInlineText(line)}</div>`).join("")}
        </div>
      ` : ""}
      <div style="font-size:15px;line-height:1.6;color:#111827;margin-bottom:10px;">${formatInlineText(question?.prompt)}</div>
      ${memorandum ? `
        <div style="font-size:14px;line-height:1.6;color:#111827;margin-bottom:8px;"><strong>Answer key:</strong> ${formatInlineText(answerKey)}</div>
        ${rationale ? `<div style="font-size:14px;line-height:1.6;color:#111827;"><strong>Model answer:</strong> ${formatInlineText(rationale)}</div>` : ""}
      ` : (
        shouldRenderTrueFalseRows(question)
          ? optionLines.length > 0
            ? `<div style="font-size:14px;line-height:1.7;color:#111827;">${optionLines.map((line) => `
                <div style="display:flex;justify-content:space-between;gap:18px;border-bottom:1px solid #e5e7eb;padding:6px 0;">
                  <span>${formatInlineText(line)}</span>
                  <span style="white-space:nowrap;">T&nbsp;&nbsp;&nbsp;F</span>
                </div>`).join("")}
              </div>`
            : `<div style="font-size:14px;line-height:1.6;color:#111827;"><strong>Options:</strong> T   F</div>`
          : optionLines.length > 0
            ? `<ol style="margin:0;padding-left:22px;font-size:14px;line-height:1.65;color:#111827;">${optionLines.map((line) => `<li>${formatInlineText(line.slice(3))}</li>`).join("")}</ol>`
            : ""
      )}
    </section>
  `;
};

export const buildKnowledgeQuestionnairePreviewHtml = (payload, options = {}) => {
  const memorandum = Boolean(options?.memorandum);
  const questions = Array.isArray(payload?.questions)
    ? payload.questions
      .map((question) => ({
        ...question,
        number: asInt(question?.number, 0),
        marks: Math.max(1, asInt(question?.marks, 1))
      }))
      .filter((question) => question.number > 0 && asText(question.prompt))
      .sort((left, right) => left.number - right.number)
    : [];

  const title = asText(payload?.questionnaireTitle) || "Knowledge Questionnaire";
  const qualificationLine = buildQualificationLine(payload);
  const categoryLine = buildCategoryLine(payload);
  const phaseLine = buildPhaseLabel(payload);
  const institutionLine = asText(payload?.learningInstitutionName);
  const trueFalseCount = questions.filter((question) => normalizeQuestionType(question.type) === "True/False").length;
  const multipleChoiceCount = questions.length - trueFalseCount;
  const totalMarks = questions.reduce((sum, question) => sum + Math.max(1, asInt(question?.marks, 1)), 0);

  return `
    <div style="font-family:'Times New Roman',serif;background:#f3f4f6;padding:24px;">
      <section style="background:#fff;color:#111;border:1px solid #d6dbe4;border-radius:6px;padding:180px 42px 140px;text-align:center;margin-bottom:24px;">
        ${institutionLine ? `<div style="font-size:18px;font-weight:700;letter-spacing:0.6px;margin-bottom:18px;">${formatInlineText(institutionLine.toUpperCase())}</div>` : ""}
        ${qualificationLine ? `<div style="font-size:28px;font-weight:700;line-height:1.4;margin-bottom:140px;">${formatInlineText(qualificationLine)}</div>` : ""}
        <div style="font-size:30px;font-weight:700;letter-spacing:0.9px;margin-bottom:18px;">${formatInlineText((memorandum ? `${title} Memorandum` : title).toUpperCase())}</div>
        <div style="font-size:16px;line-height:1.6;margin-bottom:10px;">${formatInlineText(categoryLine)}</div>
        ${phaseLine ? `<div style="font-size:15px;line-height:1.6;">${formatInlineText(`Phase: ${phaseLine}`)}</div>` : ""}
      </section>

      <section style="background:#fff;border:1px solid #d6dbe4;border-radius:8px;padding:24px;margin-bottom:18px;">
        <div style="font-size:28px;font-weight:700;line-height:1.3;margin-bottom:12px;">${formatInlineText((memorandum ? `${title} Memorandum` : title).toUpperCase())}</div>
        ${qualificationLine ? `<div style="font-size:15px;line-height:1.6;">${formatInlineText(qualificationLine)}</div>` : ""}
        <div style="font-size:15px;line-height:1.6;">${formatInlineText(categoryLine)}</div>
        ${phaseLine ? `<div style="font-size:15px;line-height:1.6;">${formatInlineText(`Phase: ${phaseLine}`)}</div>` : ""}
        <div style="font-size:15px;line-height:1.6;margin-top:10px;">Total questions: ${questions.length}</div>
        <div style="font-size:15px;line-height:1.6;">True/False questions: ${trueFalseCount}</div>
        <div style="font-size:15px;line-height:1.6;">Multiple Choice questions: ${multipleChoiceCount}</div>
        <div style="font-size:15px;line-height:1.6;">Total marks: ${totalMarks}</div>
        ${asText(payload?.passMark) ? `<div style="font-size:15px;line-height:1.6;">Pass mark: ${formatInlineText(payload.passMark)}</div>` : ""}
      </section>

      ${!memorandum ? `
        <section style="background:#fff;border:1px solid #d6dbe4;border-radius:8px;padding:24px;margin-bottom:18px;">
          <div style="font-size:18px;font-weight:700;line-height:1.3;margin-bottom:10px;">ASSESSMENT INSTRUCTIONS</div>
          <div style="font-size:15px;line-height:1.7;">1. Answer all questions.</div>
          <div style="font-size:15px;line-height:1.7;">2. Each question carries 1 mark unless otherwise stated.</div>
          <div style="font-size:15px;line-height:1.7;">3. Multiple Choice questions require one correct answer only.</div>
          <div style="font-size:15px;line-height:1.7;">4. True/False questions require you to select either True or False for the statement shown.</div>
          <div style="font-size:15px;line-height:1.7;">5. Read each question carefully before answering.</div>
        </section>
      ` : ""}

      <section>
        ${questions.map((question) => buildHtmlQuestionCard(question, memorandum)).join("")}
      </section>
    </div>
  `;
};

const createParagraph = (text, options = {}) => new Paragraph({
  heading: options.heading,
  alignment: options.alignment ?? AlignmentType.LEFT,
  spacing: options.spacing ?? { after: 160 },
  thematicBreak: Boolean(options.thematicBreak),
  children: [
    new TextRun({
      text: asText(text),
      bold: Boolean(options.bold),
      size: options.size ?? 22,
      font: "Times New Roman",
      allCaps: Boolean(options.allCaps),
      italics: Boolean(options.italics)
    })
  ]
});

const createMultilineParagraphs = (lines, options = {}) => (Array.isArray(lines) ? lines : [lines])
  .map((line) => asText(line))
  .filter(Boolean)
  .map((line) => createParagraph(line, options));

const buildCoverSectionChildren = (payload, memorandum) => {
  const title = asText(payload?.questionnaireTitle) || "Knowledge Questionnaire";
  const lines = [
    { text: asText(payload?.learningInstitutionName).toUpperCase(), size: 26, before: 3968, allCaps: true },
    { text: buildQualificationLine(payload), size: 30, before: 220, allCaps: false },
    { text: (memorandum ? `${title} Memorandum` : title).toUpperCase(), size: 34, before: 2200, allCaps: true },
    { text: buildCategoryLine(payload), size: 24, before: 220, allCaps: false },
    { text: buildPhaseLabel(payload) ? `Phase: ${buildPhaseLabel(payload)}` : "", size: 22, before: 160, allCaps: false }
  ].filter((line) => line.text);

  return lines.map((line) => createParagraph(line.text, {
    alignment: AlignmentType.CENTER,
    bold: true,
    allCaps: line.allCaps,
    size: line.size,
    spacing: { before: line.before, after: 160 }
  }));
};

const buildBodySectionChildren = (payload, memorandum) => {
  const questions = Array.isArray(payload?.questions)
    ? payload.questions
      .map((question) => ({
        ...question,
        number: asInt(question?.number, 0),
        marks: Math.max(1, asInt(question?.marks, 1))
      }))
      .filter((question) => question.number > 0 && asText(question.prompt))
      .sort((left, right) => left.number - right.number)
    : [];

  const title = asText(payload?.questionnaireTitle) || "Knowledge Questionnaire";
  const trueFalseCount = questions.filter((question) => normalizeQuestionType(question.type) === "True/False").length;
  const multipleChoiceCount = questions.length - trueFalseCount;
  const totalMarks = questions.reduce((sum, question) => sum + Math.max(1, asInt(question?.marks, 1)), 0);
  const children = [
    createParagraph(memorandum ? `${title} Memorandum` : title, {
      heading: HeadingLevel.HEADING_1,
      bold: true,
      allCaps: true,
      size: 30,
      spacing: { after: 220 }
    })
  ];

  for (const metaLine of [
    buildQualificationLine(payload),
    buildCategoryLine(payload),
    buildPhaseLabel(payload) ? `Phase: ${buildPhaseLabel(payload)}` : "",
    `Total questions: ${questions.length}`,
    `True/False questions: ${trueFalseCount}`,
    `Multiple Choice questions: ${multipleChoiceCount}`,
    `Total marks: ${totalMarks}`,
    asText(payload?.passMark) ? `Pass mark: ${asText(payload?.passMark)}` : ""
  ]) {
    if (!metaLine) continue;
    children.push(createParagraph(metaLine, { size: 22, spacing: { after: 120 } }));
  }

  if (!memorandum) {
    children.push(createParagraph("ASSESSMENT INSTRUCTIONS", {
      heading: HeadingLevel.HEADING_2,
      bold: true,
      allCaps: true,
      size: 24,
      spacing: { before: 200, after: 120 }
    }));
    for (const line of [
      "1. Answer all questions.",
      "2. Each question carries 1 mark unless otherwise stated.",
      "3. Multiple Choice questions require one correct answer only.",
      "4. True/False questions require you to select either True or False for the statement shown.",
      "5. Read each question carefully before answering."
    ]) {
      children.push(createParagraph(line, { size: 22, spacing: { after: 80 } }));
    }
  }

  let lastSubjectLine = "";
  let lastTopicLine = "";
  for (const question of questions) {
    const subjectLine = [asText(question?.subjectCode), asText(question?.subjectDescription)].filter(Boolean).join(" - ");
    const topicLine = [asText(question?.topicCode), asText(question?.topicDescription)].filter(Boolean).join(" - ");

    if (subjectLine && subjectLine.toLowerCase() !== lastSubjectLine.toLowerCase()) {
      children.push(createParagraph(subjectLine, {
        heading: HeadingLevel.HEADING_2,
        bold: true,
        allCaps: true,
        size: 24,
        spacing: { before: 220, after: 120 }
      }));
      lastSubjectLine = subjectLine;
      lastTopicLine = "";
    }

    if (topicLine && topicLine.toLowerCase() !== lastTopicLine.toLowerCase()) {
      children.push(createParagraph(`Topic: ${topicLine}`, {
        bold: true,
        size: 20,
        spacing: { before: 120, after: 100 }
      }));
      lastTopicLine = topicLine;
    }

    children.push(createParagraph(`Question ${question.number} - ${normalizeQuestionType(question.type)}`, {
      bold: true,
      size: 22,
      spacing: { before: 120, after: 100 }
    }));

    const contextLines = buildQuestionBlockContext(question);
    for (const paragraph of createMultilineParagraphs(contextLines, {
      size: 18,
      italics: true,
      spacing: { after: 80 }
    })) {
      children.push(paragraph);
    }

    children.push(createParagraph(question.prompt, { size: 22, spacing: { after: 100 } }));

    if (memorandum) {
      children.push(createParagraph(`Answer key: ${buildAnswerKey(question)}`, {
        bold: true,
        size: 20,
        spacing: { after: 80 }
      }));
      if (asText(question?.rationale)) {
        children.push(createParagraph(`Model answer: ${asText(question.rationale)}`, {
          size: 20,
          spacing: { after: 100 }
        }));
      }
    } else if (shouldRenderTrueFalseRows(question)) {
      const optionLines = buildOptionLines(question);
      if (optionLines.length === 0) {
        children.push(createParagraph("Options: T   F", { size: 20, spacing: { after: 100 } }));
      } else {
        for (const optionLine of optionLines) {
          children.push(createParagraph(`${optionLine}    T    F`, { size: 20, spacing: { after: 60 } }));
        }
      }
    } else {
      for (const optionLine of buildOptionLines(question)) {
        children.push(createParagraph(optionLine, { size: 20, spacing: { after: 60 } }));
      }
    }

    children.push(createParagraph("", { size: 12, spacing: { after: 80 } }));
  }

  return children;
};

export const downloadKnowledgeQuestionnaireDocx = async (payload, fileName, options = {}) => {
  const memorandum = Boolean(options?.memorandum);
  const sectionProperties = {
    page: {
      margin: {
        top: 1020,
        right: 1020,
        bottom: 1020,
        left: 1020
      }
    }
  };

  const doc = new Document({
    sections: [
      {
        properties: sectionProperties,
        children: buildCoverSectionChildren(payload, memorandum)
      },
      {
        properties: sectionProperties,
        children: buildBodySectionChildren(payload, memorandum)
      }
    ]
  });

  const blob = await Packer.toBlob(doc);
  downloadBlobFile(fileName, blob);
};
