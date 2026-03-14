using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ETD.Api.Utils
{
    public static class Csv
    {
        public static List<string[]> ReadSemicolonCsv(string path)
            => ReadDelimitedCsv(path, ';');

        public static List<string[]> ReadPipeCsv(string path)
            => ReadDelimitedCsv(path, '|');

        public static List<string[]> ReadDelimitedCsv(string path, char delimiter)
        {
            var rows = new List<string[]>();
            using var reader = new StreamReader(path, Encoding.UTF8);
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            int c;
            while ((c = reader.Read()) != -1)
            {
                char ch = (char)c;
                if (inQuotes)
                {
                    if (ch == '\"')
                    {
                        int peek = reader.Peek();
                        if (peek == '\"')
                        {
                            reader.Read();
                            sb.Append('\"');
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                }
                else
                {
                    if (ch == '\"')
                    {
                        inQuotes = true;
                    }
                    else if (ch == delimiter)
                    {
                        fields.Add(sb.ToString());
                        sb.Clear();
                    }
                    else if (ch == '\r')
                    {
                        // ignore, handle on \n
                    }
                    else if (ch == '\n')
                    {
                        fields.Add(sb.ToString());
                        sb.Clear();
                        rows.Add(fields.ToArray());
                        fields.Clear();
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                }
            }
            if (sb.Length > 0 || fields.Count > 0)
            {
                fields.Add(sb.ToString());
                rows.Add(fields.ToArray());
            }
            return rows;
        }
    }
}
