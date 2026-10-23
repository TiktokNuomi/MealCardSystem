using System;
using System.Text;

namespace MealCardSystem.Util
{
    /// <summary>十六进制与字节数组之间的转换。读卡器返回的 EPC / 数据块都用十六进制文本表示。</summary>
    public static class Hex
    {
        /// <summary>字节数组转大写十六进制字符串，如 {0xE2,0x00} → "E200"。</summary>
        public static string ToHexString(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return string.Empty;
            }
            StringBuilder sb = new StringBuilder(data.Length * 2);
            for (int i = 0; i < data.Length; i++)
            {
                sb.Append(data[i].ToString("X2"));
            }
            return sb.ToString();
        }

        /// <summary>十六进制字符串转字节数组。忽略空格与常见的分隔符。</summary>
        public static byte[] FromHexString(string hex)
        {
            if (hex == null)
            {
                return new byte[0];
            }

            StringBuilder clean = new StringBuilder(hex.Length);
            for (int i = 0; i < hex.Length; i++)
            {
                char c = hex[i];
                if (Uri.IsHexDigit(c))
                {
                    clean.Append(c);
                }
                else if (c == ' ' || c == '-' || c == ':' || c == ',')
                {
                    continue; // 允许多种分隔写法
                }
                else
                {
                    throw new FormatException("十六进制字符串含有非法字符：" + c);
                }
            }

            if (clean.Length % 2 != 0)
            {
                throw new FormatException("十六进制字符串长度必须为偶数（每字节两位）。");
            }

            byte[] result = new byte[clean.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = Convert.ToByte(clean.ToString(i * 2, 2), 16);
            }
            return result;
        }

        /// <summary>
        /// 规整 EPC 文本：去掉空格与分隔符、统一大写。
        /// 用于把读卡器读到的 EPC 与数据库中的记录做可靠比对。
        /// </summary>
        public static string NormalizeEpc(string epc)
        {
            if (string.IsNullOrEmpty(epc))
            {
                return string.Empty;
            }
            StringBuilder sb = new StringBuilder(epc.Length);
            for (int i = 0; i < epc.Length; i++)
            {
                char c = epc[i];
                if (Uri.IsHexDigit(c))
                {
                    sb.Append(char.ToUpperInvariant(c));
                }
            }
            return sb.ToString();
        }

        /// <summary>判断字符串是否为合法的十六进制 EPC（偶数长度、至少 2 字节）。</summary>
        public static bool IsValidEpc(string epc)
        {
            string s = NormalizeEpc(epc);
            return s.Length >= 4 && s.Length % 2 == 0;
        }
    }
}
