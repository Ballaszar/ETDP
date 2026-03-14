using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ETD.Api.Utils
{
    public static class DotEnvReader
    {
        private static readonly object Sync = new();
        private static Dictionary<string, string>? _cached;
        private static DateTime _lastLoadedUtc;
        private static readonly TimeSpan CacheWindow = TimeSpan.FromSeconds(5);

        public static string? Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;

            // Process environment always has highest priority.
            var envValue = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(envValue))
            {
                return envValue.Trim();
            }

            var map = GetMergedMap();
            return map.TryGetValue(key.Trim(), out var value) ? value : null;
        }

        private static Dictionary<string, string> GetMergedMap()
        {
            lock (Sync)
            {
                var now = DateTime.UtcNow;
                if (_cached != null && (now - _lastLoadedUtc) < CacheWindow)
                {
                    return _cached;
                }

                var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in CandidatePaths())
                {
                    if (!File.Exists(path)) continue;
                    foreach (var pair in ParseFile(path))
                    {
                        merged[pair.Key] = pair.Value;
                    }
                }

                _cached = merged;
                _lastLoadedUtc = now;
                return _cached;
            }
        }

        private static IEnumerable<string> CandidatePaths()
        {
            var appDataEnv = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ETDP",
                ".env");
            var projectEnv = EtdpPaths.CombineProject(".env");
            var currentEnv = Path.Combine(Directory.GetCurrentDirectory(), ".env");

            return new[] { appDataEnv, projectEnv, currentEnv }
                .Select(path => Path.GetFullPath(path))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<KeyValuePair<string, string>> ParseFile(string path)
        {
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = (raw ?? string.Empty).Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
                {
                    line = line[7..].Trim();
                }

                var idx = line.IndexOf('=');
                if (idx <= 0) continue;

                var key = line[..idx].Trim();
                var value = line[(idx + 1)..].Trim();
                if (value.Length >= 2 &&
                    ((value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal)) ||
                     (value.StartsWith("'", StringComparison.Ordinal) && value.EndsWith("'", StringComparison.Ordinal))))
                {
                    value = value[1..^1];
                }

                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) continue;
                yield return new KeyValuePair<string, string>(key, value);
            }
        }
    }
}
