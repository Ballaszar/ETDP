# Backend Security and Activation

## API Base
- `http://localhost:5299` (default local run profile)
- `https://localhost:7280` (optional HTTPS local profile)

## Activation Endpoints
- `GET /api/Activation/status`
- `POST /api/Activation/activate`

## Current Keys
- Keys are intentionally not listed in this repository documentation.
- Load them from:
  - `Security/keys/activation_keys.txt`
  - `Security/keys/api_keys.txt`
  - or the corresponding environment variables
- Treat activation keys as the manual lecturer access codes you distribute by email for now.

## Default Key Files
- `Security/keys/activation_keys.txt`
- `Security/keys/api_keys.txt`

## Runtime Header Requirements
- `X-Activation-Token`
- `X-App-Api-Key`

## Key Load Order
1. Environment variables:
   - `APP_AUTH_API_KEYS`
   - `APP_AUTH_ACTIVATION_KEYS`
2. Key files:
   - `Security/keys/api_keys.txt`
   - `Security/keys/activation_keys.txt`
3. `appsettings*.json` fallback

## Environment Notes
- Development:
  - machine `PIERRE` is bypassed for activation.
- Production:
  - bypass is disallowed by startup validation.
