using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ETD.Api.Utils
{
    public static class Secrets
    {
        public static string? GetOpenAIKey()
        {
            var env = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (!string.IsNullOrWhiteSpace(env)) return env;

            try
            {
                var currentDir = Directory.GetCurrentDirectory();
                var envCandidates = new[]
                {
                    Path.Combine(currentDir, ".env"),
                    EtdpPaths.CombineProject(".env"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ETDP", ".env")
                }
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase);

                foreach (var envPath in envCandidates)
                {
                    if (!File.Exists(envPath)) continue;

                    var lines = File.ReadAllLines(envPath);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("OPENAI_API_KEY=", StringComparison.OrdinalIgnoreCase))
                        {
                            var val = trimmed.Substring(15).Trim().Trim('"').Trim('\'');
                            if (!string.IsNullOrWhiteSpace(val)) return val;
                        }
                    }
                }
            }
            catch { }

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "ETDP");
            var plainPath = Path.Combine(dir, "openai.key");
            var protectedPath = Path.Combine(dir, "openai.protected");
            try
            {
                if (File.Exists(plainPath))
                {
                    return File.ReadAllText(plainPath).Trim();
                }
                if (File.Exists(protectedPath))
                {
                    if (OperatingSystem.IsWindows())
                    {
                        var bytes = File.ReadAllBytes(protectedPath);
                        var unprotected = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                        return Encoding.UTF8.GetString(unprotected).Trim();
                    }
                }
            }
            catch
            {
                // ignore
            }
            return null;
        }
    }
}
