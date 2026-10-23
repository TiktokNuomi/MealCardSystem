using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MealCardSystem.Util
{
    /// <summary>CSV 导出。写出 UTF-8 带 BOM 的文件，Excel 双击打开中文不会乱码。</summary>
    public static class Csv
    {
        /// <summary>按 RFC 4180 转义单个字段。</summary>
        public static string Escape(string field)
        {
            if (field == null)
            {
                return string.Empty;
            }

            bool needQuote = field.IndexOf(',') >= 0
                          || field.IndexOf('"') >= 0
                          || field.IndexOf('\n') >= 0
                          || field.IndexOf('\r') >= 0;

            if (!needQuote)
            {
                return field;
            }

            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>把表头与数据行写入文件。</summary>
        public static void WriteFile(string path, string[] headers, IEnumerable<string[]> rows)
        {
            // 带 BOM 的 UTF-8，Excel 才能正确识别中文
            using (StreamWriter w = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                WriteRow(w, headers);
                foreach (string[] row in rows)
                {
                    WriteRow(w, row);
                }
            }
        }

        private static void WriteRow(TextWriter w, string[] fields)
        {
            if (fields == null)
            {
                w.WriteLine();
                return;
            }

            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0)
                {
                    w.Write(',');
                }
                w.Write(Escape(fields[i]));
            }
            w.WriteLine();
        }
    }
}
