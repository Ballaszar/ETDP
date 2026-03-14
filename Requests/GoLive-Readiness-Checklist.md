# Go-Live Readiness Checklist

## Environment
- Backend starts cleanly and responds on expected base URL.
- Frontend build and runtime start without blocking errors.
- No stale `ETDP.exe` lock from previous process.
- Required folders exist (`Imports`, exports paths, requests assets).

## Qualification Control
- Active qualification is selected and visible.
- Qualification ID and code resolve correctly from API.
- Route navigation preserves qualification context.

## Workflow Data Integrity
- Demographics present.
- Curriculum phase links present.
- Subject records present.
- Topic records present.
- Lecturer Toolkit records present for the active qualification.

## Content and Knowledge
- Knowledge hierarchy sync completes or degrades gracefully.
- Local source and developer knowledge uploads tested.
- Mira character blueprint loaded (`/api/Knowledge/mira-character`).
- Chat context sources verified (`/api/Knowledge/chat-context-sources`).

## Exports
- Lesson Plan Review renders for active qualification.
- Print Menu loads and at least one export is successful.
- Slide export works for topic and batch scope.
- Text-to-Video editor can generate and export storyboard artifacts.

## Demo Reliability
- Prepare fallback route if one page fails.
- Keep one known-good qualification for live demo.
- Keep sample files ready for rapid import testing.
- Use clear narrative: context -> workflow -> quality -> outputs.

## Final Pre-Roadshow Validation
- Rehearse full run with exact audience-facing script.
- Capture known limitations with mitigation notes.
- Confirm presenter roles and handover points.
