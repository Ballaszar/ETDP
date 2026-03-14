# Backend Security and Activation

## API Base
- `http://localhost:5299` (default local run profile)
- `https://localhost:7280` (optional HTTPS local profile)

## Activation Endpoints
- `GET /api/Activation/status`
- `POST /api/Activation/activate`

## Current Keys
- Activation Key:
  - `lMHTxGvtLVUSzrJRugKiNOcyqjIA7f6kewP59hComEZaQFdY`
- API Key:
  - `H5RMz2fXsmjVgS4NBWDeOGY69LJ8i0yAEUwTpIqPCQZnubvr`

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
