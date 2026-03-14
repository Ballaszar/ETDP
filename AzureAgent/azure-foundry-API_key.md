SECURITY NOTICE

This file no longer stores secrets.

Set your Foundry key in environment variables instead:

PowerShell (current session):
$env:FOUNDRY_API_KEY = "<your-foundry-key>"

PowerShell (persist for current user):
[Environment]::SetEnvironmentVariable("FOUNDRY_API_KEY", "<your-foundry-key>", "User")

Optional:
[Environment]::SetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT", "https://etdp-azure-ai.services.ai.azure.com/api/projects/etdp-azure-ai-project", "User")
