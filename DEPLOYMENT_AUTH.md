# Activation and API Key Deployment

## Effective key load order
The backend loads keys automatically in this order:

1. Environment variables
- `APP_AUTH_API_KEYS`
- `APP_AUTH_ACTIVATION_KEYS`

2. Key files (auto-loaded)
- `Security/keys/api_keys.txt`
- `Security/keys/activation_keys.txt`

3. `appsettings*.json` fallback (`AppAuthorization.ApiKeys` and `AppAuthorization.ActivationKeys`)
Note: for hardened production, startup enforces that at least one external source (env or key files) is present.

## Required headers at runtime
- `X-App-Api-Key`
- `X-Activation-Token`

Frontend injects these automatically from browser storage:
- `localStorage.appApiKey`
- `localStorage.activationToken`

## Production requirements (validated at startup)
- `RequireApiKey = true` must have at least 1 API key.
- `RequireActivation = true` must have at least 1 activation key.
- `BypassInDevelopment` must be `false`.
- `BypassMachineNames` must be empty.
- No key can be placeholder/weak (`CHANGE-ME` or length < 16).

If any rule fails, the API startup will stop with a clear error.

Additionally, in Production the API will fail startup if keys are only in appsettings and not supplied by env/key files.

## Deployment checklist
1. Set environment:
- `ASPNETCORE_ENVIRONMENT=Production`

2. Provide keys using one method:
- Preferred: set `APP_AUTH_API_KEYS` and `APP_AUTH_ACTIVATION_KEYS`
- Or deploy `Security/keys/*.txt`

3. Ensure production bypass is disabled:
- `appsettings.Production.json` keeps strict values by default.

4. Verify health:
- `GET /api/Activation/status` should return:
  - `bypassed = false`
  - `apiKeyRequired = true`
  - `activationRequired = true`

## Local developer bypass
Configured in `appsettings.Development.json` only:
- `BypassInDevelopment = true`
- `BypassMachineNames = ["PIERRE"]`

This is never allowed in Production due startup validation.
