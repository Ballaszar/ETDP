export const LEARNING_MATERIAL_STEPS = [
  {
    key: 'roll-out-plan',
    title: 'Create Roll Out Plan',
    path: '/learning-material/rollout-plan',
    directoryName: 'Project Roll Out Plan',
    description: 'Preview and export the roll-out plan.'
  },
  {
    key: 'learning-schedule',
    title: 'Create Learning Schedule',
    path: '/learning-material/schedule',
    directoryName: 'Learning Schedule',
    description: 'Preview rows and export schedule files.'
  },
  {
    key: 'learner-guide',
    title: 'Create Learner Guide',
    path: '/learning-material/learner-guide',
    directoryName: 'Learner Guide',
    description: 'Preview and export learner guide output.'
  },
  {
    key: 'summative-assessment',
    title: 'Create Summative Assessment',
    path: '/learning-material/summative-assessment',
    directoryName: 'Summative Assessment',
    description: 'Review the consolidated phase-wide Knowledge Questionnaire draft, generate rows with SMI, and export metadata/question CSVs.'
  },
  {
    key: 'summative-memoranda',
    title: 'Summative Memoranda',
    path: '/learning-material/summative-memoranda',
    directoryName: 'Summative Memoranda',
    description: 'Preview and export summative memoranda.'
  },
  {
    key: 'workbook',
    title: 'Create Workbook',
    path: '/learning-material/workbook',
    directoryName: 'Workbooks',
    description: 'Preview and export workbook outputs.'
  },
  {
    key: 'workbook-memoranda',
    title: 'Workbook Memoranda',
    path: '/learning-material/workbook-memoranda',
    directoryName: 'Workbook Memoranda',
    description: 'Preview and export workbook memoranda.'
  },
  {
    key: 'slides',
    title: 'PowerPoint Slides',
    path: '/learning-material/slides',
    directoryName: 'SlideShows',
    description: 'Preview and export slide decks.'
  },
  {
    key: 'learner-registration',
    title: 'Learner Registration',
    path: '/learning-material/learner-registration',
    directoryName: 'LearnerRegistration',
    description: 'Manage learner registration templates and uploads.'
  },
  {
    key: 'logbook',
    title: 'Create Logbook',
    path: '/learning-material/logbook',
    directoryName: 'Logbook',
    description: 'Preview and print logbook pages.'
  },
  {
    key: 'progress-report',
    title: 'Create Progress Report',
    path: '/learning-material/progress-report',
    directoryName: 'Progress Report',
    description: 'Preview and print progress reports.'
  },
  {
    key: 'template-uploads',
    title: 'Template Uploads',
    path: '/learning-material/template-uploads',
    directoryName: 'TemplateUploads',
    description: 'Download upload templates and conversion guidance.'
  },
  {
    key: 'flow-diagrams',
    title: 'Create Flow Diagrams',
    path: '/learning-material/flow-diagrams',
    directoryName: 'Flow Diagrams',
    description: 'Preview and export flow diagrams.'
  }
];

export const getLearningMaterialStep = (stepKey) =>
  LEARNING_MATERIAL_STEPS.find((step) => step.key === stepKey) || null;

export const getLearningMaterialStepIndex = (stepKey) =>
  LEARNING_MATERIAL_STEPS.findIndex((step) => step.key === stepKey);

export const getNextLearningMaterialStep = (stepKey) => {
  const idx = getLearningMaterialStepIndex(stepKey);
  if (idx < 0 || idx >= LEARNING_MATERIAL_STEPS.length - 1) return null;
  return LEARNING_MATERIAL_STEPS[idx + 1];
};

export const getPreviousLearningMaterialStep = (stepKey) => {
  const idx = getLearningMaterialStepIndex(stepKey);
  if (idx <= 0) return null;
  return LEARNING_MATERIAL_STEPS[idx - 1];
};
