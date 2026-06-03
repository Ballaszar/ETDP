using System.Text;
using System.Text.RegularExpressions;

namespace ETD.Api.Utils
{
    public static class GitLfsPointerResolver
    {
        private static readonly Regex Sha256Regex = new(@"^[0-9a-f]{64}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static GitLfsPointerInfo? TryReadPointer(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > 4096)
            {
                return null;
            }

            var content = File.ReadAllText(path, Encoding.UTF8);
            return ParsePointerContent(content);
        }

        public static async Task<GitLfsPointerInfo?> TryReadPointerAsync(string path, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > 4096)
            {
                return null;
            }

            var content = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
            return ParsePointerContent(content);
        }

        public static string ResolveReadablePath(string path)
        {
            var pointer = TryReadPointer(path);
            return pointer == null
                ? path
                : ResolveLocalObjectPathOrThrow(path, pointer.ObjectId);
        }

        public static async Task<string> ResolveReadablePathAsync(string path, CancellationToken cancellationToken = default)
        {
            var pointer = await TryReadPointerAsync(path, cancellationToken);
            return pointer == null
                ? path
                : ResolveLocalObjectPathOrThrow(path, pointer.ObjectId);
        }

        public static bool CopyResolvedContent(string sourcePath, string destinationPath)
        {
            var resolvedSourcePath = ResolveReadablePath(sourcePath);
            CopyFile(resolvedSourcePath, destinationPath);
            return !string.Equals(resolvedSourcePath, sourcePath, StringComparison.OrdinalIgnoreCase);
        }

        public static async Task<bool> CopyResolvedContentAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
        {
            var resolvedSourcePath = await ResolveReadablePathAsync(sourcePath, cancellationToken);
            await CopyFileAsync(resolvedSourcePath, destinationPath, cancellationToken);
            return !string.Equals(resolvedSourcePath, sourcePath, StringComparison.OrdinalIgnoreCase);
        }

        public static string ResolveLocalObjectPath(string objectId)
        {
            var normalizedObjectId = (objectId ?? string.Empty).Trim().ToLowerInvariant();
            if (normalizedObjectId.Length < 4 || !Sha256Regex.IsMatch(normalizedObjectId))
            {
                return string.Empty;
            }

            return Path.Combine(
                EtdpPaths.GetProjectRoot(),
                ".git",
                "lfs",
                "objects",
                normalizedObjectId[..2],
                normalizedObjectId.Substring(2, 2),
                normalizedObjectId);
        }

        private static GitLfsPointerInfo? ParsePointerContent(string? content)
        {
            var normalized = (content ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');

            var lines = normalized
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (lines.Length < 3 ||
                !lines[0].StartsWith("version https://git-lfs.github.com/spec/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var oidLine = lines.FirstOrDefault(line => line.StartsWith("oid sha256:", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(oidLine))
            {
                return null;
            }

            var objectId = oidLine["oid sha256:".Length..].Trim().ToLowerInvariant();
            if (!Sha256Regex.IsMatch(objectId))
            {
                return null;
            }

            var sizeLine = lines.FirstOrDefault(line => line.StartsWith("size ", StringComparison.OrdinalIgnoreCase));
            long.TryParse(sizeLine?["size ".Length..].Trim(), out var sizeBytes);

            return new GitLfsPointerInfo
            {
                ObjectId = objectId,
                SizeBytes = sizeBytes
            };
        }

        private static string ResolveLocalObjectPathOrThrow(string sourcePath, string objectId)
        {
            var objectPath = ResolveLocalObjectPath(objectId);
            if (!File.Exists(objectPath))
            {
                throw new InvalidOperationException(
                    $"File '{Path.GetFileName(sourcePath)}' is a Git LFS pointer, but the actual file is not available locally. Restore the LFS object or re-upload the document.");
            }

            return objectPath;
        }

        private static void CopyFile(string sourcePath, string destinationPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? Directory.GetCurrentDirectory());
            using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var target = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            source.CopyTo(target);
        }

        private static async Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? Directory.GetCurrentDirectory());
            await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            await using var target = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(target, cancellationToken);
        }
    }

    public sealed class GitLfsPointerInfo
    {
        public string ObjectId { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
    }
}
