Local authorization keys belong in this directory, but the actual key files are intentionally git-ignored.

Create these files locally when needed:

- `Security/keys/api_keys.txt`
- `Security/keys/activation_keys.txt`

Put one key per line, or provide the keys through these environment variables instead:

- `APP_AUTH_API_KEYS`
- `APP_AUTH_ACTIVATION_KEYS`

Production should use externally managed secrets, not committed files.
