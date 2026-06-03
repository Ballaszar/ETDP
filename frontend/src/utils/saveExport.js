const sanitizeFilename = (value, fallback = 'etdp-export') => {
  const cleaned = String(value || '')
    .trim()
    .replace(/[<>:"/\\|?*\u0000-\u001f]/g, '-')
    .replace(/\s+/g, ' ')
    .replace(/^\.+$/, '')
    .slice(0, 180);
  return cleaned || fallback;
};

const csvCell = (value) => {
  const text = value == null ? '' : String(value);
  return `"${text.replace(/"/g, '""')}"`;
};

export const toCsv = (rows, columns) => {
  const safeRows = Array.isArray(rows) ? rows : [];
  const header = columns.map((column) => csvCell(column.label));
  const body = safeRows.map((row) =>
    columns.map((column) => csvCell(typeof column.value === 'function' ? column.value(row) : row?.[column.value])).join(',')
  );
  return [header.join(','), ...body].join('\r\n');
};

export const buildMetadataCsv = (sections) => {
  const rows = [];
  (Array.isArray(sections) ? sections : []).forEach((section, sectionIndex) => {
    if (sectionIndex > 0) rows.push('');
    if (section?.title) rows.push(csvCell(section.title));
    (section?.rows || []).forEach(([label, value]) => rows.push(`${csvCell(label)},${csvCell(value)}`));
  });
  return rows.join('\r\n');
};

export const saveTextFile = async ({ text, filename, mimeType = 'text/csv;charset=utf-8' }) => {
  const safeFilename = sanitizeFilename(filename);
  const blob = new Blob([text], { type: mimeType });

  if (typeof window !== 'undefined' && typeof window.showSaveFilePicker === 'function') {
    try {
      const handle = await window.showSaveFilePicker({
        suggestedName: safeFilename,
        types: [
          {
            description: mimeType.includes('csv') ? 'CSV file' : 'Text file',
            accept: { [mimeType.split(';')[0]]: [safeFilename.toLowerCase().endsWith('.txt') ? '.txt' : '.csv'] }
          }
        ]
      });
      const writable = await handle.createWritable();
      await writable.write(blob);
      await writable.close();
      return { mode: 'save-picker', filename: safeFilename };
    } catch (error) {
      if (error?.name === 'AbortError') return { mode: 'cancelled', filename: safeFilename };
      throw error;
    }
  }

  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = safeFilename;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
  return { mode: 'download', filename: safeFilename };
};

export const todayStamp = () => new Date().toISOString().slice(0, 10);
