using System.Text.RegularExpressions;

namespace ETD.Api.Utils
{
    public static class DocumentTextCleaner
    {
        public static string Clean(string? text, bool preservePdfPageMarkers = true)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            var t = text!
                .Replace("\r\n", "\n")
                .Replace("\r", "\n");

            t = Regex.Replace(t, @"(?<=\w)-\s*\n\s*(?=\w)", string.Empty);
            t = Regex.Replace(t, @"PAGEREF\s+_Toc\d+", " ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\b(?:TOC|HYPERLINK|MERGEFORMAT)\b", " ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"_Toc\d+\b", " ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\\h\s*", " ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\\[a-zA-Z]+\b", " ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"<[^>]+>", " ");
            t = System.Web.HttpUtility.HtmlDecode(t);
            t = Regex.Replace(t, @"[ \t]+\n", "\n");
            t = Regex.Replace(t, @"\n{3,}", "\n\n");

            var lines = t.Split('\n');
            var outLines = new List<string>(lines.Length);
            string? previousKey = null;
            foreach (var rawLine in lines)
            {
                var line = Regex.Replace(rawLine ?? string.Empty, @"\s+", " ").Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (preservePdfPageMarkers &&
                    Regex.IsMatch(line, @"^\[Page\s+\d+\]$", RegexOptions.IgnoreCase))
                {
                    outLines.Add(line);
                    previousKey = NormalizeLineKey(line);
                    continue;
                }

                line = StripLinePrefixNoise(line);
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (IsNoiseLine(line)) continue;

                var key = NormalizeLineKey(line);
                if (!string.IsNullOrWhiteSpace(previousKey) &&
                    string.Equals(previousKey, key, StringComparison.Ordinal))
                {
                    continue;
                }

                outLines.Add(line);
                previousKey = key;
            }

            var cleaned = string.Join("\n", outLines);
            cleaned = Regex.Replace(cleaned, @"\n{3,}", "\n\n");
            cleaned = Regex.Replace(cleaned, @"[^\S\r\n]{2,}", " ");
            return cleaned.Trim();
        }

        public static string CleanPdfPageText(string? pageText)
            => Clean(pageText, preservePdfPageMarkers: false);

        public static int WordCount(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            return Regex.Matches(text!, @"\b[\p{L}\p{N}][\p{L}\p{N}\-']*\b").Count;
        }

        public static bool IsLikelyBoilerplateParagraph(string? paragraph)
        {
            if (string.IsNullOrWhiteSpace(paragraph)) return true;
            var p = Regex.Replace(paragraph!, @"\s+", " ").Trim();
            var normalized = p.ToLowerInvariant();
            if (normalized.Length <= 15) return true;

            if (Regex.IsMatch(normalized, @"^(page\s*)?\d{1,4}(\s*(of|/)\s*\d{1,4})?$")) return true;
            if (Regex.IsMatch(p, @"^.{0,180}\.{3,}\s*\d{1,4}\s*$")) return true;
            if (Regex.Matches(p, @"\.{3,}").Count >= 2) return true;
            if (Regex.IsMatch(normalized, @"^(chapter|section)\s+\d+(\.\d+)*\s+.*\s+\d{1,4}$")) return true;

            var hasTocLikeTitle = Regex.IsMatch(normalized, @"\b(table of contents|contents|list of figures|list of tables|index)\b");
            if (hasTocLikeTitle && (p.Length < 240 || Regex.IsMatch(p, @"\.{3,}\s*\d{1,4}"))) return true;

            var hasCoverMeta = Regex.IsMatch(normalized, @"\b(copyright|all rights reserved|isbn|published by|edition|version|author|prepared by|compiled by|doi)\b");
            if (hasCoverMeta && WordCount(p) < 45) return true;

            return false;
        }

        public static HashSet<string> DetectRepeatedBoundaryLineKeys(IReadOnlyList<IReadOnlyList<string>> pages)
        {
            var repeated = new HashSet<string>(StringComparer.Ordinal);
            if (pages == null || pages.Count < 3) return repeated;

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var page in pages)
            {
                if (page == null || page.Count == 0) continue;

                var lines = page
                    .Select(x => Regex.Replace(x ?? string.Empty, @"\s+", " ").Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();

                if (lines.Count == 0) continue;

                var boundary = lines.Take(2)
                    .Concat(lines.Skip(Math.Max(0, lines.Count - 2)))
                    .ToList();

                foreach (var line in boundary)
                {
                    var key = NormalizeLineKey(line);
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    if (key.Length < 4 || key.Length > 120) continue;
                    if (!counts.ContainsKey(key)) counts[key] = 0;
                    counts[key]++;
                }
            }

            var threshold = Math.Max(3, (int)Math.Ceiling(pages.Count * 0.35));
            foreach (var kv in counts)
            {
                if (kv.Value >= threshold)
                {
                    repeated.Add(kv.Key);
                }
            }

            return repeated;
        }

        public static string NormalizeLineKey(string? line)
        {
            var s = (line ?? string.Empty).Trim().ToLowerInvariant();
            s = Regex.Replace(s, @"\d{1,4}", "#");
            s = Regex.Replace(s, @"\s+", " ");
            return s;
        }

        public static bool IsNoiseLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return true;
            var s = Regex.Replace(line!, @"\s+", " ").Trim();
            if (s.Length == 0) return true;
            var lower = s.ToLowerInvariant();

            if (Regex.IsMatch(lower, @"^(?:page\s*)?\d{1,4}(?:\s*(?:of|/)\s*\d{1,4})?$")) return true;
            if (Regex.IsMatch(s, @"^.{0,200}\.{3,}\s*\d{1,4}\s*$")) return true;
            if (Regex.Matches(s, @"\.{3,}").Count >= 2) return true;
            if (Regex.IsMatch(lower, @"^(table of contents|contents|list of figures|list of tables|index|bibliography|references)$")) return true;
            if (Regex.IsMatch(lower, @"^(author|authors|prepared by|compiled by)\s*[:\-].*$") && WordCount(s) <= 14) return true;
            if (Regex.IsMatch(lower, @"\b(all rights reserved|copyright|isbn|issn|doi)\b") && WordCount(s) <= 24) return true;
            if (Regex.IsMatch(lower, @"^(?:www\.|https?://).+$") && WordCount(s) <= 20) return true;
            if (Regex.IsMatch(s, @"^[^A-Za-z0-9]{2,}$")) return true;

            var alphaNum = Regex.Matches(s, @"[A-Za-z0-9]").Count;
            if (alphaNum < 3 && s.Length < 40) return true;

            return false;
        }

        private static string StripLinePrefixNoise(string line)
        {
            var s = line ?? string.Empty;
            s = Regex.Replace(s, @"^\s*[•●▪·◦◆◇◼◻\-\–\—]+\s*", " ");
            s = Regex.Replace(s, @"^\s*\(?[A-Za-z]\)\s+", " ");
            s = Regex.Replace(s, @"^\s*(?:\(?\d{1,3}(?:\.\d{1,3}){0,5}\)?[)\].:-]?\s+)+", " ");
            s = Regex.Replace(s, @"^\s*\(\d{1,3}\)\s+", " ");
            s = Regex.Replace(s, @"\s+", " ");
            return s.Trim();
        }
    }
}
