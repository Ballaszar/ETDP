using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;

namespace ETD.Api.Services
{
    public sealed class VisualStorageResult
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
    }

    public class VisualStorageService
    {
        private readonly IWebHostEnvironment _env;

        public VisualStorageService(IWebHostEnvironment env)
        {
            _env = env ?? throw new ArgumentNullException(nameof(env));
        }

        public VisualStorageResult StoreFile(string sourcePath, string relativeOutputFolder = "visual_archive")
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                throw new ArgumentException("Source file not found", nameof(sourcePath));

            var wwwRoot = string.IsNullOrWhiteSpace(_env.WebRootPath) ? Path.Combine(_env.ContentRootPath, "wwwroot") : _env.WebRootPath;
            var outDir = Path.Combine(wwwRoot, relativeOutputFolder);
            Directory.CreateDirectory(outDir);

            var fileName = Path.GetFileName(sourcePath) ?? Guid.NewGuid().ToString();
            var destPath = Path.Combine(outDir, fileName);
            destPath = EnsureUniquePath(destPath);
            File.Copy(sourcePath, destPath);

            var url = "/" + Path.Combine(relativeOutputFolder, Path.GetFileName(destPath)).Replace("\\", "/");

            return new VisualStorageResult
            {
                FilePath = destPath,
                FileName = Path.GetFileName(destPath) ?? fileName,
                Url = url,
                FileType = Path.GetExtension(destPath).TrimStart('.')
            };
        }

        private static string EnsureUniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path) ?? string.Empty;
            var stem = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            var counter = 2;
            string candidate;
            do
            {
                candidate = Path.Combine(dir, $"{stem}_{counter}{ext}");
                counter++;
            } while (File.Exists(candidate));

            return candidate;
        }
    }
}
