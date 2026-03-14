This folder contains the small shared Azure/Moderator artifacts that ETDP references at runtime or during operator workflows.

Committed here:

- `MODERATOR4_BOOTSTRAP_PROTOCOL.md`
- `MODERATOR4_GITHUB_ACCESS.md`
- `MODERATOR4_RESOURCE_PROTOCOL.md`
- `build-runbook.md`
- `smoke-test-agent.ps1`
- `azure-foundry-API_key.md`

Intentionally git-ignored:

- local auth logs
- large bot/workbench checkouts
- device activation payloads
- local API keys, tokens, and other operator-only notes

Store live secrets in environment variables or other local secret stores, not in this repository.
