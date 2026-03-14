# Moderator 4 GitHub Knowledge Access

## API Route
- `POST /api/Content/import-github-repo`

## Request Shape
```json
{
  "repoUrl": "https://github.com/<owner>/<repo>.git",
  "branch": "main",
  "qualificationId": 0,
  "qualificationDescription": "Mechanical Engineering",
  "maxFiles": 400,
  "maxFileSizeKb": 20480,
  "includeCodeFiles": true
}
```

## Example 1
```json
{
  "repoUrl": "https://github.com/mveeramani1979/open-mechanical-ai.git",
  "branch": "main",
  "qualificationDescription": "Mechanical Engineering",
  "maxFiles": 500,
  "includeCodeFiles": true
}
```

## Example 2
```json
{
  "repoUrl": "https://github.com/jonathanmcclurg/Mechanical_Engineering_Agents.git",
  "branch": "main",
  "qualificationDescription": "Mechanical Engineering",
  "maxFiles": 500,
  "includeCodeFiles": true
}
```

## Post-Import Verification
1. Call `GET /api/Content/knowledge-pools`.
2. Confirm `github_repo` pool count increased.
3. Run `POST /api/Content/search-paragraphs` with `knowledgePool: "github_repo"`.
