using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ETD.Api.Utils
{
    public static class EtdpPaths
    {
        private const int MaxProbeDepth = 8;

        public static string GetProjectRoot()
        {
            var configuredRoot = (Environment.GetEnvironmentVariable("ETDP_ROOT_PATH") ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(configuredRoot) && Directory.Exists(configuredRoot))
            {
                return Path.GetFullPath(configuredRoot);
            }

            foreach (var candidate in GetCandidateRoots())
            {
                if (LooksLikeProjectRoot(candidate))
                {
                    return candidate;
                }
            }

            return Path.GetFullPath(Directory.GetCurrentDirectory());
        }

        public static string GetImportsRoot()
        {
            var configuredImports = FirstNonEmpty(
                Environment.GetEnvironmentVariable("LOCAL_LIBRARY_PATH"),
                Environment.GetEnvironmentVariable("ETDP_IMPORTS_PATH"));
            if (!string.IsNullOrWhiteSpace(configuredImports))
            {
                return Path.GetFullPath(configuredImports.Trim());
            }

            var projectImports = CombineProject("Imports");
            if (Directory.Exists(projectImports))
            {
                return projectImports;
            }

            var appDataImports = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ETDP",
                "Imports");
            if (Directory.Exists(appDataImports))
            {
                return appDataImports;
            }

            return projectImports;
        }

        public static string GetExportsRoot()
        {
            return CombineProject("Exports");
        }

        public static string CombineProject(params string[] segments)
        {
            var path = GetProjectRoot();
            foreach (var segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment))
                {
                    continue;
                }

                path = Path.Combine(path, segment);
            }

            return path;
        }

        private static IEnumerable<string> GetCandidateRoots()
        {
            var candidates = new List<string>();
            AddCandidateWithParents(candidates, Directory.GetCurrentDirectory());
            AddCandidateWithParents(candidates, AppContext.BaseDirectory);
            AddCandidateWithParents(candidates, Path.GetDirectoryName(typeof(EtdpPaths).Assembly.Location));

            return candidates
                .Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static void AddCandidateWithParents(List<string> targets, string? startPath)
        {
            if (string.IsNullOrWhiteSpace(startPath))
            {
                return;
            }

            var current = Path.GetFullPath(startPath);
            for (var depth = 0; depth <= MaxProbeDepth; depth++)
            {
                targets.Add(current);
                var parent = Directory.GetParent(current);
                if (parent == null)
                {
                    break;
                }

                current = parent.FullName;
            }
        }

        private static bool LooksLikeProjectRoot(string path)
        {
            if (!Directory.Exists(path))
            {
                return false;
            }

            if (File.Exists(Path.Combine(path, "ETDP.csproj")))
            {
                return true;
            }

            return Directory.Exists(Path.Combine(path, "Controllers"))
                && Directory.Exists(Path.Combine(path, "Imports"));
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
