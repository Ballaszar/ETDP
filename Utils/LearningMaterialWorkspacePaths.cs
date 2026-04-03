using ETD.Api.Models;

namespace ETD.Api.Utils
{
    public static class LearningMaterialWorkspacePaths
    {
        public static string ResolveRootPath(Qualification qualification, int qualificationId)
        {
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(documentsPath))
            {
                throw new InvalidOperationException("Unable to resolve My Documents path.");
            }

            var rootDirectoryName = BuildRootDirectoryName(
                qualification?.QualificationNumber,
                qualification?.QualificationDescription,
                qualificationId);

            var rootPath = Path.Combine(documentsPath, rootDirectoryName);
            Directory.CreateDirectory(rootPath);
            return rootPath;
        }

        public static string ResolveMaterialDirectory(Qualification qualification, int qualificationId, string subDirectoryName)
        {
            var rootPath = ResolveRootPath(qualification, qualificationId);
            var materialDirectory = Path.Combine(rootPath, subDirectoryName);
            Directory.CreateDirectory(materialDirectory);
            return materialDirectory;
        }

        public static string SaveBytes(Qualification qualification, int qualificationId, string subDirectoryName, string fileName, byte[] bytes)
        {
            var directory = ResolveMaterialDirectory(qualification, qualificationId, subDirectoryName);
            var fullPath = GetUniquePath(directory, fileName);
            File.WriteAllBytes(fullPath, bytes);
            return fullPath;
        }

        public static string GetUniquePath(string directory, string fileName)
        {
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            var fullPath = Path.Combine(directory, fileName);
            var index = 1;
            while (File.Exists(fullPath))
            {
                fullPath = Path.Combine(directory, $"{baseName}_{index:00}{extension}");
                index += 1;
            }

            return fullPath;
        }

        private static string BuildRootDirectoryName(string? qualificationNumber, string? qualificationDescription, int qualificationId)
        {
            var safeNumber = MakeSafePathPart(qualificationNumber);
            var safeDescription = MakeSafePathPart(qualificationDescription);

            if (safeNumber.Length == 0 && safeDescription.Length == 0)
            {
                return $"Qualification_{qualificationId}";
            }

            if (safeDescription.Length == 0)
            {
                return safeNumber;
            }

            if (safeNumber.Length == 0)
            {
                return safeDescription;
            }

            return $"{safeNumber} - {safeDescription}";
        }

        private static string MakeSafePathPart(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.Trim().ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0)
                {
                    chars[i] = '_';
                }
            }

            var cleaned = new string(chars);
            while (cleaned.Contains("  ", StringComparison.Ordinal))
            {
                cleaned = cleaned.Replace("  ", " ", StringComparison.Ordinal);
            }

            return cleaned.Trim();
        }
    }
}
