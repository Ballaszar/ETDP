export const WORKFLOW_STEP_META = {
  demographics: { label: 'Demographics', path: '/demographics' },
  phases: { label: 'Curriculum Phases', path: '/phases' },
  subjects: { label: 'Subjects', path: '/subjects' },
  topics: { label: 'Topics', path: '/topics' },
  criteria: { label: 'Assessment Criteria', path: '/topics' },
  toolkit: { label: 'Lesson Plan Content (LPN source)', path: '/lecturer-toolkit' },
  'lesson-plan-review': { label: 'Lesson Plan Review', path: '/lesson-plan-review' }
};

export const PAGE_REQUIREMENTS = {
  phases: ['demographics'],
  subjects: ['phases'],
  'subjects-capture': ['phases'],
  'subjects-list': ['phases'],
  topics: ['subjects'],
  'topics-list': ['subjects'],
  'topics-review': ['subjects'],
  library: ['topics'],
  'lecturer-toolkit': ['subjects'],
  'content-builder': ['topics', 'criteria'],
  'lesson-plan-review': ['topics', 'criteria', 'toolkit'],
  'print-menu': ['topics', 'criteria', 'toolkit'],
  'learning-material': ['topics', 'criteria', 'toolkit']
};

const isStepComplete = (stepKey, status) => {
  const counts = status?.counts || {};
  switch (stepKey) {
    case 'demographics':
      return Number(counts.demographics || 0) > 0;
    case 'phases':
      return (
        Number(counts.phaseLinks || 0) > 0 ||
        Number(counts.subjects || 0) > 0 ||
        Number(counts.topics || 0) > 0
      );
    case 'subjects':
      return Number(counts.subjects || 0) > 0;
    case 'topics':
      return Number(counts.topics || 0) > 0;
    case 'criteria':
      return Number(counts.criteria || 0) > 0;
    case 'toolkit':
      return Number(counts.toolkit || 0) > 0;
    default:
      return true;
  }
};

export const getMissingPrerequisites = (pageKey, status) => {
  const required = [...(PAGE_REQUIREMENTS[pageKey] || [])];
  const unique = Array.from(new Set(required));
  return unique
    .filter((key) => !isStepComplete(key, status))
    .map((key) => ({ key, ...(WORKFLOW_STEP_META[key] || { label: key, path: '/' }) }));
};
