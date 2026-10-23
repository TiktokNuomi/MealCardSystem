using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using MealCardSystem.Model;

namespace MealCardSystem.Data
{
    /// <summary>操作员与登录口令的存取。</summary>
    public class OperatorRepository
    {
        private readonly SqliteConnection _conn;

        public OperatorRepository(SqliteConnection conn)
        {
            _conn = conn;
        }

        // ---------- 口令哈希 ----------

        /// <summary>生成 16 字节随机盐（十六进制）。</summary>
        public static string NewSalt()
        {
            byte[] buf = new byte[16];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(buf);
            }
            return ToHex(buf);
        }

        /// <summary>计算 SHA256(salt + 密码) 的十六进制摘要。</summary>
        public static string HashPassword(string password, string salt)
        {
            if (password == null)
            {
                password = string.Empty;
            }
            byte[] data = Encoding.UTF8.GetBytes(salt + password);
            using (SHA256 sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(data));
            }
        }

        private static string ToHex(byte[] data)
        {
            StringBuilder sb = new StringBuilder(data.Length * 2);
            for (int i = 0; i < data.Length; i++)
            {
                sb.Append(data[i].ToString("x2"));
            }
            return sb.ToString();
        }

        /// <summary>定长比较，避免通过响应时间推断口令。</summary>
        private static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                diff |= a[i] ^ b[i];
            }
            return diff == 0;
        }

        // ---------- 查询与校验 ----------

        /// <summary>校验账号口令，通过返回操作员，否则返回 null。</summary>
        public Operator Authenticate(string username, string password)
        {
            if (string.IsNullOrEmpty(username))
            {
                return null;
            }

            List<object[]> rows = _conn.Query(
                "SELECT id, username, password_hash, salt, display_name, is_admin FROM operators WHERE username = ?1",
                username.Trim());

            if (rows.Count == 0)
            {
                return null;
            }

            object[] r = rows[0];
            string hash = Convert.ToString(r[2]);
            string salt = Convert.ToString(r[3]);
            string computed = HashPassword(password, salt);

            if (!FixedTimeEquals(hash, computed))
            {
                return null;
            }

            return new Operator
            {
                Id = Convert.ToInt64(r[0]),
                Username = Convert.ToString(r[1]),
                DisplayName = Convert.ToString(r[4]),
                IsAdmin = Convert.ToInt64(r[5]) != 0
            };
        }

        public List<Operator> List()
        {
            List<Operator> result = new List<Operator>();
            List<object[]> rows = _conn.Query(
                "SELECT id, username, display_name, is_admin FROM operators ORDER BY id");

            foreach (object[] r in rows)
            {
                result.Add(new Operator
                {
                    Id = Convert.ToInt64(r[0]),
                    Username = Convert.ToString(r[1]),
                    DisplayName = Convert.ToString(r[2]),
                    IsAdmin = Convert.ToInt64(r[3]) != 0
                });
            }
            return result;
        }

        public bool Exists(string username)
        {
            return _conn.QueryInt64("SELECT COUNT(*) FROM operators WHERE username = ?1", username) > 0;
        }

        public int Count()
        {
            return (int)_conn.QueryInt64("SELECT COUNT(*) FROM operators");
        }

        // ---------- 增删改 ----------

        public long Create(string username, string password, string displayName, bool isAdmin)
        {
            if (string.IsNullOrEmpty(username) || username.Trim().Length == 0)
            {
                throw new ArgumentException("账号不能为空。");
            }
            if (string.IsNullOrEmpty(password) || password.Length < 4)
            {
                throw new ArgumentException("密码至少 4 位。");
            }
            if (Exists(username.Trim()))
            {
                throw new InvalidOperationException("账号 " + username + " 已存在。");
            }

            string salt = NewSalt();
            string hash = HashPassword(password, salt);

            return _conn.ExecuteInsert(
                "INSERT INTO operators (username, password_hash, salt, display_name, is_admin, created_at) " +
                "VALUES (?1, ?2, ?3, ?4, ?5, ?6)",
                username.Trim(), hash, salt,
                displayName == null ? "" : displayName.Trim(),
                isAdmin ? 1 : 0,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        /// <summary>重设指定操作员的密码。</summary>
        public void ChangePassword(long operatorId, string newPassword)
        {
            if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 4)
            {
                throw new ArgumentException("密码至少 4 位。");
            }

            string salt = NewSalt();
            string hash = HashPassword(newPassword, salt);
            int n = _conn.ExecuteNonQuery(
                "UPDATE operators SET password_hash = ?1, salt = ?2 WHERE id = ?3",
                hash, salt, operatorId);

            if (n == 0)
            {
                throw new InvalidOperationException("未找到该操作员，密码未修改。");
            }
        }

        public int Delete(long operatorId)
        {
            return _conn.ExecuteNonQuery("DELETE FROM operators WHERE id = ?1", operatorId);
        }

        public int CountAdmins()
        {
            return (int)_conn.QueryInt64("SELECT COUNT(*) FROM operators WHERE is_admin = 1");
        }

        /// <summary>
        /// 首次运行时创建默认管理员 admin / admin123。
        /// 返回 true 表示本次确实新建了账号（界面据此提示尽快改密）。
        /// </summary>
        public bool EnsureDefaultAdmin()
        {
            if (Count() > 0)
            {
                return false;
            }
            Create("admin", "admin123", "系统管理员", true);
            return true;
        }
    }
}
