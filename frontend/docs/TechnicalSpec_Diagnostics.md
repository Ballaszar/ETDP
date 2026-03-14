# Technical Specification: Frontend Diagnostics & Qualification Resolution System

## 1. Overview
This document specifies the implementation of the **Error Diagnostics and Qualification Resolution System** for the ETDP Frontend. This component is critical for identifying data integrity issues (missing dropdown values) and API communication failures in real-time.

## 2. Scope
The functionality covers two primary modules:
1.  **Engine (ContentBuilderPage)**: Focus on resolving Qualification Numbers to internal IDs and cascading dropdowns.
2.  **Lecturer Toolkit**: Focus on bulk import validation and payload integrity.

## 3. Functional Requirements

### 3.1. Diagnostics Panel
*   **Requirement**: A persistent, red-styled error panel must appear at the top of the page if any critical error occurs.
*   **Content**: Must display timestamped log entries for:
    *   Failed API calls (HTTP status + endpoint).
    *   Failed data resolution (e.g., "Subject not found for QualID=X").
    *   Validation errors (e.g., "Missing required field: Title").
*   **Behavior**: Auto-expands on error; clearable by user (optional).

### 3.2. Qualification Resolution Logic
*   **Input**: `qualificationId` (from Context/URL) which may be an internal ID (Integer) or legacy Index (String).
*   **Resolution Strategy**:
    1.  **Direct ID Check**: If numeric > 0, query `/api/Qualifications/{id}`.
    2.  **Legacy Index Lookup**: If string/index, query `/api/Qualifications` (list) and find index match.
    3.  **Search Fallback**: If list fails, query `/api/Qualifications/search?q={term}`.
*   **Success Criteria**: Returns a valid internal `Id` (Int32).
*   **Failure Handling**: Logs `[Qualification] Resolution failed` to Diagnostics Panel.

### 3.3. Cascading Data Flow
*   **Dependency Chain**: `Qualification` -> `Subject` -> `Topic` -> `Criteria`.
*   **Constraint**: Child dropdowns must reset to 0/Empty when a parent changes.
*   **Error State**: If a parent selection yields 0 children (e.g., No Subjects), an error is logged: `[Subject] No subjects found for qualificationId={id}`.

## 4. Technical Implementation

### 4.1. Architecture
*   **Framework**: React 18 functional components.
*   **State Management**: Local `useState` for `errors` array (`[{ id, message, timestamp }]`).
*   **API Layer**: Native `fetch` with relative paths (`/api/...`) to support Vite proxying.

### 4.2. Data Structures

#### Error Log Entry
```typescript
interface LogEntry {
  source: 'Qualification' | 'Subject' | 'Topic' | 'Toolkit';
  message: string;
  details?: any;
  timestamp: string;
}
```

#### Resolution Flow (Pseudocode)
```javascript
async function resolveQualification(input) {
  try {
    if (isNumeric(input)) return fetchById(input);
    const list = await fetchAll();
    const match = list.find(q => q.Index === input || q.Number === input);
    if (match) return match.Id;
    throw new Error('No match found');
  } catch (e) {
    logError('Resolution failed', e);
    return 0; // Fallback
  }
}
```

## 5. Error Handling & Edge Cases

| Scenario | System Behavior | User Feedback |
| :--- | :--- | :--- |
| **API Timeout** | Retry once, then fail. | "Server error: Timeout accessing /api/Subject" |
| **Empty Dropdown** | Prevent selection of "null". | "No items available. Please check Qualification setup." |
| **Safari/iOS** | CSS `backdrop-filter` fallback. | UI remains usable (no visual glitches). |
| **Invalid Payload** | Block `POST` request. | "Cannot save: Missing field 'SubjectCode'." |

## 6. Integration Points
*   **Vite Proxy**: All requests routed via `vite.config.js` proxy to Backend (`http://localhost:5169`).
*   **QualificationContext**: Source of truth for the active Qualification ID.
*   **Browser Storage**: Fallback to `localStorage` if Context is empty.

## 7. Success Metrics
*   **Resolution Rate**: 100% of valid Qualification Numbers resolve to IDs.
*   **Error Visibility**: 100% of HTTP 4xx/5xx errors appear in the Diagnostics Panel.
*   **User Feedback**: Users can cite specific error messages (e.g., "Subject fetch failed") instead of "It doesn't work".

## 8. Testing Scenarios
1.  **Happy Path**: Select Qual -> Subj -> Topic loads. No red text.
2.  **Resolution Failure**: Manually corrupt URL ID. Expect: "Resolution failed" in red panel.
3.  **Network Failure**: Stop backend. Reload page. Expect: "Fetch failed" in red panel.
4.  **Empty Data**: Select Qualification with no Subjects. Expect: "No subjects found" warning.

## 9. Documentation Standards
*   Code must include JSDoc for complex resolution logic.
*   UI components must include `aria-live="polite"` for error regions.
*   Specs (this document) stored in `/docs/TechnicalSpec_Diagnostics.md`.
