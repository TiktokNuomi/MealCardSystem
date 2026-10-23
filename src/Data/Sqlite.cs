using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace MealCardSystem.Data
{
    /// <summary>
    /// SQLite 操作异常。
    /// </summary>
    public sealed class SqliteException : Exception
    {
        public int Code { get; private set; }

        public SqliteException(int code, string message) : base(message)
        {
            Code = code;
        }
    }

    /// <summary>
    /// 对 Windows 内置 winsqlite3.dll 的 P/Invoke 封装。
    ///
    /// 之所以不引用 System.Data.SQLite：本机没有 NuGet / .NET SDK，无法引入第三方驱动；
    /// 而 winsqlite3.dll 是 Windows 10/11 自带的原生 SQLite（导出标准 297 个 C API），
    /// 直接 P/Invoke 即可获得完整数据库能力，零外部依赖。
    ///
    /// 若将来要换成官方 System.Data.SQLite，只需替换本文件。
    /// </summary>
    internal static class Native
    {
        // 该 DLL 在 System32（x64 进程）与 SysWOW64（x86 进程）下均存在，
        // WOW64 会自动重定向，因此 32/64 位进程都能正常加载。
        public const string Dll = "winsqlite3.dll";

        public const int SQLITE_OK = 0;
        public const int SQLITE_ROW = 100;
        public const int SQLITE_DONE = 101;

        // sqlite3_open_v2 的打开标志
        public const int SQLITE_OPEN_READWRITE = 0x00000002;
        public const int SQLITE_OPEN_CREATE = 0x00000004;
        public const int SQLITE_OPEN_FULLMUTEX = 0x00010000;

        /// <summary>告诉 SQLite 自行复制传入的内存（等价于 C 宏 SQLITE_TRANSIENT）。</summary>
        public static readonly IntPtr SQLITE_TRANSIENT = new IntPtr(-1);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_open_v2")]
        public static extern int OpenV2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_close_v2")]
        public static extern int CloseV2(IntPtr db);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_exec")]
        public static extern int Exec(IntPtr db, byte[] sql, IntPtr callback, IntPtr arg, out IntPtr errmsg);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_prepare_v2")]
        public static extern int PrepareV2(IntPtr db, byte[] sql, int nBytes, out IntPtr stmt, out IntPtr tail);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_step")]
        public static extern int Step(IntPtr stmt);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_reset")]
        public static extern int Reset(IntPtr stmt);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_finalize")]
        public static extern int Finalize(IntPtr stmt);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_errmsg")]
        public static extern IntPtr ErrMsg(IntPtr db);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_free")]
        public static extern void Free(IntPtr ptr);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_busy_timeout")]
        public static extern int BusyTimeout(IntPtr db, int ms);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_changes")]
        public static extern int Changes(IntPtr db);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_last_insert_rowid")]
        public static extern long LastInsertRowId(IntPtr db);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_bind_null")]
        public static extern int BindNull(IntPtr stmt, int index);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_bind_int64")]
        public static extern int BindInt64(IntPtr stmt, int index, long value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_bind_double")]
        public static extern int BindDouble(IntPtr stmt, int index, double value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_bind_text")]
        public static extern int BindText(IntPtr stmt, int index, byte[] value, int nBytes, IntPtr destructor);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_bind_blob")]
        public static extern int BindBlob(IntPtr stmt, int index, byte[] value, int nBytes, IntPtr destructor);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_count")]
        public static extern int ColumnCount(IntPtr stmt);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_type")]
        public static extern int ColumnType(IntPtr stmt, int index);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_int64")]
        public static extern long ColumnInt64(IntPtr stmt, int index);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_double")]
        public static extern double ColumnDouble(IntPtr stmt, int index);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_text")]
        public static extern IntPtr ColumnText(IntPtr stmt, int index);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_bytes")]
        public static extern int ColumnBytes(IntPtr stmt, int index);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_libversion")]
        public static extern IntPtr LibVersion();
    }

    /// <summary>SQLite 列的数据类型（对应 C 宏 SQLITE_INTEGER 等）。</summary>
    public static class SqliteColumnType
    {
        public const int Integer = 1;
        public const int Float = 2;
        public const int Text = 3;
        public const int Blob = 4;
        public const int Null = 5;
    }

    /// <summary>
    /// 一条已预编译的 SQL 语句。参数用 ?1 ?2 ... 占位，通过 Bind 系列方法按位置绑定。
    /// </summary>
    public sealed class SqliteStatement : IDisposable
    {
        private readonly SqliteConnection _owner;
        private IntPtr _stmt;
        private bool _disposed;

        internal SqliteStatement(SqliteConnection owner, IntPtr stmt)
        {
            _owner = owner;
            _stmt = stmt;
        }

        public int ColumnCount
        {
            get { return Native.ColumnCount(_stmt); }
        }

        /// <summary>绑定第 index 个参数（从 1 开始计数）。</summary>
        public SqliteStatement Bind(int index, object value)
        {
            int rc;
            if (value == null || value == DBNull.Value)
            {
                rc = Native.BindNull(_stmt, index);
            }
            else if (value is string)
            {
                byte[] bytes = Utf8((string)value);
                rc = Native.BindText(_stmt, index, bytes, bytes.Length - 1, Native.SQLITE_TRANSIENT);
            }
            else if (value is bool)
            {
                rc = Native.BindInt64(_stmt, index, ((bool)value) ? 1L : 0L);
            }
            else if (value is int || value is long || value is short || value is byte)
            {
                rc = Native.BindInt64(_stmt, index, Convert.ToInt64(value));
            }
            else if (value is double || value is float || value is decimal)
            {
                rc = Native.BindDouble(_stmt, index, Convert.ToDouble(value));
            }
            else if (value is byte[])
            {
                byte[] blob = (byte[])value;
                rc = Native.BindBlob(_stmt, index, blob, blob.Length, Native.SQLITE_TRANSIENT);
            }
            else if (value is DateTime)
            {
                // 统一以可排序的 ISO 文本存储
                string s = ((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss");
                byte[] bytes = Utf8(s);
                rc = Native.BindText(_stmt, index, bytes, bytes.Length - 1, Native.SQLITE_TRANSIENT);
            }
            else
            {
                byte[] bytes = Utf8(Convert.ToString(value));
                rc = Native.BindText(_stmt, index, bytes, bytes.Length - 1, Native.SQLITE_TRANSIENT);
            }

            if (rc != Native.SQLITE_OK)
            {
                throw _owner.NewException(rc);
            }
            return this;
        }

        public SqliteStatement BindAll(object[] values)
        {
            if (values != null)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    Bind(i + 1, values[i]);
                }
            }
            return this;
        }

        /// <summary>执行一步。返回 true 表示取到一行数据，false 表示语句执行完毕。</summary>
        public bool Step()
        {
            int rc = Native.Step(_stmt);
            if (rc == Native.SQLITE_ROW)
            {
                return true;
            }
            if (rc == Native.SQLITE_DONE)
            {
                return false;
            }
            throw _owner.NewException(rc);
        }

        /// <summary>执行一条不返回结果集的语句，返回受影响行数。</summary>
        public int ExecuteNonQuery()
        {
            while (Step())
            {
                // 丢弃可能返回的行
            }
            return Native.Changes(_owner.Handle);
        }

        public bool IsNull(int index)
        {
            return Native.ColumnType(_stmt, index) == SqliteColumnType.Null;
        }

        /// <summary>列的数据类型，取值见 <see cref="SqliteColumnType"/>。</summary>
        public int ColumnType(int index)
        {
            return Native.ColumnType(_stmt, index);
        }

        public long GetInt64(int index)
        {
            return Native.ColumnInt64(_stmt, index);
        }

        public int GetInt32(int index)
        {
            return (int)Native.ColumnInt64(_stmt, index);
        }

        public double GetDouble(int index)
        {
            return Native.ColumnDouble(_stmt, index);
        }

        public string GetString(int index)
        {
            if (IsNull(index))
            {
                return null;
            }
            IntPtr ptr = Native.ColumnText(_stmt, index);
            if (ptr == IntPtr.Zero)
            {
                return null;
            }
            int len = Native.ColumnBytes(_stmt, index);
            if (len <= 0)
            {
                return string.Empty;
            }
            byte[] buf = new byte[len];
            Marshal.Copy(ptr, buf, 0, len);
            return Encoding.UTF8.GetString(buf);
        }

        public byte[] GetBlob(int index)
        {
            if (IsNull(index))
            {
                return null;
            }
            IntPtr ptr = Native.ColumnText(_stmt, index);
            int len = Native.ColumnBytes(_stmt, index);
            if (ptr == IntPtr.Zero || len <= 0)
            {
                return new byte[0];
            }
            byte[] buf = new byte[len];
            Marshal.Copy(ptr, buf, 0, len);
            return buf;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                if (_stmt != IntPtr.Zero)
                {
                    Native.Finalize(_stmt);
                    _stmt = IntPtr.Zero;
                }
            }
        }

        internal static byte[] Utf8(string s)
        {
            // 尾部补 0，既作 C 字符串终止符，也便于按 Length-1 传入字节数
            byte[] raw = Encoding.UTF8.GetBytes(s);
            byte[] z = new byte[raw.Length + 1];
            Array.Copy(raw, z, raw.Length);
            return z;
        }
    }

    /// <summary>
    /// SQLite 连接。非线程安全，请勿跨线程共享。
    /// </summary>
    public sealed class SqliteConnection : IDisposable
    {
        private IntPtr _db;
        private bool _disposed;
        private bool _inTransaction;

        private SqliteConnection(IntPtr db)
        {
            _db = db;
            // 数据库被其它进程短暂占用时自动等待，避免"database is locked"
            Native.BusyTimeout(_db, 5000);
        }

        /// <summary>库内 SQLite 版本，用于自检确认真能连上。</summary>
        public static string LibraryVersion
        {
            get
            {
                IntPtr p = Native.LibVersion();
                return p == IntPtr.Zero ? "unknown" : Marshal.PtrToStringAnsi(p);
            }
        }

        public static SqliteConnection Open(string path)
        {
            IntPtr db;
            int flags = Native.SQLITE_OPEN_READWRITE | Native.SQLITE_OPEN_CREATE | Native.SQLITE_OPEN_FULLMUTEX;
            // 预建一个空串（含 0 结尾），故意让 SQLite 自己再复制一份，避免托管/非托管内存生命周期问题
            byte[] file = SqliteStatement.Utf8(path);
            int rc = Native.OpenV2(file, out db, flags, IntPtr.Zero);
            if (rc != Native.SQLITE_OK)
            {
                if (db != IntPtr.Zero)
                {
                    Native.CloseV2(db);
                }
                throw new SqliteException(rc, "无法打开数据库文件：" + path + "（错误码 " + rc + "）");
            }
            return new SqliteConnection(db);
        }

        internal IntPtr Handle
        {
            get { return _db; }
        }

        internal SqliteException NewException(int rc)
        {
            string msg = "SQLite 错误（码 " + rc + "）";
            if (_db != IntPtr.Zero)
            {
                IntPtr p = Native.ErrMsg(_db);
                if (p != IntPtr.Zero)
                {
                    msg = Marshal.PtrToStringAnsi(p);
                }
            }
            return new SqliteException(rc, msg);
        }

        /// <summary>
        /// 执行一段可包含多条语句的 SQL 脚本（用于建表）。
        /// </summary>
        public void ExecuteScript(string sql)
        {
            IntPtr errmsg;
            byte[] bytes = SqliteStatement.Utf8(sql);
            int rc = Native.Exec(_db, bytes, IntPtr.Zero, IntPtr.Zero, out errmsg);
            string detail = null;
            if (errmsg != IntPtr.Zero)
            {
                detail = Marshal.PtrToStringAnsi(errmsg);
                Native.Free(errmsg);
            }
            if (rc != Native.SQLITE_OK)
            {
                throw new SqliteException(rc, detail != null ? detail : "执行 SQL 脚本失败");
            }
        }

        public SqliteStatement Prepare(string sql)
        {
            IntPtr stmt;
            IntPtr tail;
            byte[] bytes = SqliteStatement.Utf8(sql);
            int rc = Native.PrepareV2(_db, bytes, bytes.Length - 1, out stmt, out tail);
            if (rc != Native.SQLITE_OK)
            {
                throw NewException(rc);
            }
            return new SqliteStatement(this, stmt);
        }

        public int ExecuteNonQuery(string sql, params object[] args)
        {
            using (SqliteStatement st = Prepare(sql))
            {
                st.BindAll(args);
                return st.ExecuteNonQuery();
            }
        }

        /// <summary>执行 INSERT 并返回新行的 rowid。</summary>
        public long ExecuteInsert(string sql, params object[] args)
        {
            using (SqliteStatement st = Prepare(sql))
            {
                st.BindAll(args);
                st.ExecuteNonQuery();
            }
            return Native.LastInsertRowId(_db);
        }

        /// <summary>查询多行，每行是一个 object[]（列顺序与 SELECT 一致）。</summary>
        public List<object[]> Query(string sql, params object[] args)
        {
            List<object[]> rows = new List<object[]>();
            using (SqliteStatement st = Prepare(sql))
            {
                st.BindAll(args);
                int cols = st.ColumnCount;
                while (st.Step())
                {
                    object[] row = new object[cols];
                    for (int i = 0; i < cols; i++)
                    {
                        if (st.IsNull(i))
                        {
                            row[i] = null;
                        }
                        else
                        {
                            switch (st.ColumnType(i))
                            {
                                case SqliteColumnType.Integer:
                                    row[i] = st.GetInt64(i);
                                    break;
                                case SqliteColumnType.Float:
                                    row[i] = st.GetDouble(i);
                                    break;
                                case SqliteColumnType.Blob:
                                    row[i] = st.GetBlob(i);
                                    break;
                                default:
                                    row[i] = st.GetString(i);
                                    break;
                            }
                        }
                    }
                    rows.Add(row);
                }
            }
            return rows;
        }

        /// <summary>查询单个值（首行首列），无结果返回 null。</summary>
        public object QueryScalar(string sql, params object[] args)
        {
            List<object[]> rows = Query(sql, args);
            if (rows.Count == 0 || rows[0].Length == 0)
            {
                return null;
            }
            return rows[0][0];
        }

        public long QueryInt64(string sql, params object[] args)
        {
            object v = QueryScalar(sql, args);
            if (v == null || v == DBNull.Value)
            {
                return 0L;
            }
            return Convert.ToInt64(v);
        }

        public string QueryString(string sql, params object[] args)
        {
            object v = QueryScalar(sql, args);
            if (v == null || v == DBNull.Value)
            {
                return null;
            }
            return Convert.ToString(v);
        }

        /// <summary>
        /// 开启立即写事务。IMMEDIATE 会立刻取得写锁，
        /// 保证"读余额 → 校验 → 扣款 → 记流水"这一串不会被其它连接穿插。
        /// </summary>
        public void BeginTransaction()
        {
            if (_inTransaction)
            {
                throw new InvalidOperationException("事务已开启，不支持嵌套。");
            }
            ExecuteScript("BEGIN IMMEDIATE;");
            _inTransaction = true;
        }

        public void Commit()
        {
            if (!_inTransaction)
            {
                throw new InvalidOperationException("当前没有进行中的事务。");
            }
            ExecuteScript("COMMIT;");
            _inTransaction = false;
        }

        public void Rollback()
        {
            if (!_inTransaction)
            {
                return;
            }
            try
            {
                ExecuteScript("ROLLBACK;");
            }
            catch (SqliteException)
            {
                // 回滚失败时不再掩盖原始异常
            }
            _inTransaction = false;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                if (_inTransaction)
                {
                    Rollback();
                }
                if (_db != IntPtr.Zero)
                {
                    Native.CloseV2(_db);
                    _db = IntPtr.Zero;
                }
            }
        }
    }
}
