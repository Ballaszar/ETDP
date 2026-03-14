using System;
using System.IO;

namespace ETD.Api.Utils
{
    public static class AiRuntime
    {
        public const string ModeOffline = "offline";
        public const string ModeHybrid = "hybrid";
        public const string ModeCloud = "cloud";

        public static string GetMode()
        {
            var raw = (Environment.GetEnvironmentVariable("AI_MODE") ?? string.Empty).Trim().ToLowerInvariant();
            if (raw == ModeHybrid || raw == ModeCloud || raw == ModeOffline)
            {
                return raw;
            }

            // Default to offline to keep ETDP independent from cloud services.
            return ModeOffline;
        }

        public static bool IsOfflineMode() => string.Equals(GetMode(), ModeOffline, StringComparison.OrdinalIgnoreCase);
        public static bool IsHybridMode() => string.Equals(GetMode(), ModeHybrid, StringComparison.OrdinalIgnoreCase);
        public static bool IsCloudMode() => string.Equals(GetMode(), ModeCloud, StringComparison.OrdinalIgnoreCase);

        public static bool AllowCloudProviders() => !IsOfflineMode();
        // Microsoft Foundry is intentionally disabled to keep ETDP independent of Azure resources.
        public static bool AllowFoundry() => false;
        public static bool AllowOpenAi() => IsHybridMode() || IsCloudMode();
        public static bool PreferLocalFirst() => !IsCloudMode();

        public static string GetLocalLlmEndpoint()
        {
            var endpoint = FirstNonEmpty(
                Environment.GetEnvironmentVariable("LOCAL_LLM_ENDPOINT"),
                Environment.GetEnvironmentVariable("LOCAL_CHAT_COMPLETIONS_ENDPOINT"),
                Environment.GetEnvironmentVariable("PARAPHRASE_WRAPPER_ENDPOINT"),
                Environment.GetEnvironmentVariable("LOCAL_PARAPHRASE_ENDPOINT"));

            return endpoint ?? string.Empty;
        }

        public static string GetLocalLlmModel()
        {
            var model = FirstNonEmpty(
                Environment.GetEnvironmentVariable("LOCAL_LLM_MODEL"),
                Environment.GetEnvironmentVariable("PARAPHRASE_WRAPPER_MODEL"));

            return string.IsNullOrWhiteSpace(model) ? "local-edu-assistant" : model;
        }

        public static string GetLocalLlmApiKey()
        {
            return FirstNonEmpty(
                Environment.GetEnvironmentVariable("LOCAL_LLM_API_KEY"),
                Environment.GetEnvironmentVariable("PARAPHRASE_WRAPPER_API_KEY")) ?? string.Empty;
        }

        public static string GetLocalLibraryPath()
        {
            var explicitPath = FirstNonEmpty(
                Environment.GetEnvironmentVariable("LOCAL_LIBRARY_PATH"),
                Environment.GetEnvironmentVariable("ETDP_IMPORTS_PATH"));
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                return Path.GetFullPath(explicitPath);
            }

            return EtdpPaths.GetImportsRoot();
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                var normalized = (value ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    return normalized;
                }
            }

            return null;
        }
    }
}
