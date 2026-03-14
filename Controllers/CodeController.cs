using Microsoft.AspNetCore.Mvc;
using System.Text;
using ETD.Api.Utils;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CodeController : ControllerBase
    {
        private static readonly string[] AllowedExt = new[] { ".cs", ".jsx", ".js", ".ts", ".tsx", ".css", ".json", ".md" };
        private static readonly string[] IgnoreDirs = new[] { "node_modules", "dist", "bin", "obj", ".git" };

        [HttpGet("list")]
        public IActionResult List()
        {
            var root = GetRootPath();
            var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(p => AllowedExt.Contains(Path.GetExtension(p).ToLower()))
                .Where(p => !IgnoreDirs.Any(id => p.Contains(Path.DirectorySeparatorChar + id + Path.DirectorySeparatorChar)))
                .Select(p => ToProjectRelativePath(p, root))
                .OrderBy(p => p)
                .ToList();
            return Ok(files);
        }

        [HttpGet("read")]
        public IActionResult Read([FromQuery] string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return BadRequest("path required");
            var root = GetRootPath();
            if (!TryResolvePathInsideProject(path, out var resolvedPath, root))
                return BadRequest($"Path must resolve inside project root: {root}");
            if (!System.IO.File.Exists(resolvedPath)) return NotFound($"File not found: {resolvedPath}");
            var text = System.IO.File.ReadAllText(resolvedPath);
            return Ok(new
            {
                path = ToProjectRelativePath(resolvedPath, root),
                fullPath = resolvedPath,
                content = text
            });
        }

        [HttpGet("search")]
        public IActionResult Search([FromQuery] string text, [FromQuery] int limit = 20)
        {
            text ??= string.Empty;
            var root = GetRootPath();
            var results = new List<object>();
            var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(p => AllowedExt.Contains(Path.GetExtension(p).ToLower()))
                .Where(p => !IgnoreDirs.Any(id => p.Contains(Path.DirectorySeparatorChar + id + Path.DirectorySeparatorChar)))
                .Take(2000)
                .ToList();
            foreach (var f in files)
            {
                var body = System.IO.File.ReadAllText(f);
                var idx = body.IndexOf(text, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    var start = Math.Max(0, idx - 120);
                    var len = Math.Min(240, body.Length - start);
                    var snippet = body.Substring(start, len);
                    results.Add(new { path = ToProjectRelativePath(f, root), fullPath = f, snippet });
                    if (results.Count >= limit) break;
                }
            }
            return Ok(results);
        }

        private static string GetRootPath()
        {
            return Path.GetFullPath(EtdpPaths.GetProjectRoot());
        }

        private static bool TryResolvePathInsideProject(string requestedPath, out string resolvedPath, string rootPath)
        {
            resolvedPath = string.Empty;
            if (string.IsNullOrWhiteSpace(requestedPath))
            {
                return false;
            }

            var trimmed = requestedPath.Trim();
            var candidate = Path.IsPathRooted(trimmed)
                ? trimmed
                : Path.Combine(rootPath, trimmed);

            try
            {
                resolvedPath = Path.GetFullPath(candidate);
            }
            catch
            {
                return false;
            }

            return IsPathInsideRoot(resolvedPath, rootPath);
        }

        private static bool IsPathInsideRoot(string candidatePath, string rootPath)
        {
            var normalizedCandidate = Path.GetFullPath(candidatePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedRoot = Path.GetFullPath(rootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(normalizedCandidate, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
            return normalizedCandidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
        }

        private static string ToProjectRelativePath(string path, string rootPath)
        {
            var relative = Path.GetRelativePath(rootPath, path);
            return relative.Replace('\\', '/');
        }
    }
}
