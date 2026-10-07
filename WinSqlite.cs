using System.Runtime.InteropServices;
using System.Text;

namespace FinanceTracker;

internal sealed class WinSqliteConnection : IDisposable
{
    private IntPtr _db;
    private bool _disposed;

    private const int SQLITE_OK = 0;
    private const int SQLITE_ROW = 100;
    private const int SQLITE_DONE = 101;
    private const int SQLITE_INTEGER = 1;
    private const int SQLITE_FLOAT = 2;
    private const int SQLITE_TEXT = 3;
    private const int SQLITE_NULL = 5;
    private const int SQLITE_OPEN_READWRITE = 0x00000002;
    private const int SQLITE_OPEN_CREATE = 0x00000004;
    private const int SQLITE_OPEN_FULLMUTEX = 0x00010000;
    private static readonly IntPtr SQLITE_TRANSIENT = new(-1);

    public WinSqliteConnection(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var rc = Native.sqlite3_open_v2(Utf8Z(path), out _db,
            SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_FULLMUTEX, IntPtr.Zero);
        if (rc != SQLITE_OK)
        {
            var message = _db == IntPtr.Zero ? $"SQLite error {rc}" : GetError();
            if (_db != IntPtr.Zero) Native.sqlite3_close(_db);
            _db = IntPtr.Zero;
            throw new InvalidOperationException($"Не удалось открыть базу данных: {message}");
        }

        ExecuteNonQuery("PRAGMA foreign_keys = ON;");
        ExecuteNonQuery("PRAGMA journal_mode = WAL;");
        ExecuteNonQuery("PRAGMA synchronous = NORMAL;");
        ExecuteNonQuery("PRAGMA busy_timeout = 5000;");
    }

    public long LastInsertRowId => Native.sqlite3_last_insert_rowid(_db);

    public void ExecuteScript(string sql)
    {
        foreach (var part in SplitSqlScript(sql))
        {
            if (!string.IsNullOrWhiteSpace(part)) ExecuteNonQuery(part);
        }
    }

    public int ExecuteNonQuery(string sql, params object?[] args)
    {
        using var stmt = Prepare(sql, args);
        while (true)
        {
            var rc = Native.sqlite3_step(stmt.Handle);
            if (rc == SQLITE_DONE) break;
            if (rc == SQLITE_ROW) continue; // Some PRAGMA statements return a row even when used as setters.
            throw new InvalidOperationException($"Ошибка SQLite: {GetError()}\nSQL: {sql}");
        }
        return Native.sqlite3_changes(_db);
    }

    public object? ExecuteScalar(string sql, params object?[] args)
    {
        using var stmt = Prepare(sql, args);
        var rc = Native.sqlite3_step(stmt.Handle);
        if (rc == SQLITE_ROW) return ReadColumn(stmt.Handle, 0);
        if (rc == SQLITE_DONE) return null;
        throw new InvalidOperationException($"Ошибка SQLite: {GetError()}\nSQL: {sql}");
    }

    public List<Dictionary<string, object?>> Query(string sql, params object?[] args)
    {
        using var stmt = Prepare(sql, args);
        var result = new List<Dictionary<string, object?>>();
        while (true)
        {
            var rc = Native.sqlite3_step(stmt.Handle);
            if (rc == SQLITE_DONE) break;
            if (rc != SQLITE_ROW) throw new InvalidOperationException($"Ошибка SQLite: {GetError()}\nSQL: {sql}");

            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var count = Native.sqlite3_column_count(stmt.Handle);
            for (var i = 0; i < count; i++)
            {
                var name = PtrToUtf8(Native.sqlite3_column_name(stmt.Handle, i));
                row[name] = ReadColumn(stmt.Handle, i);
            }
            result.Add(row);
        }
        return result;
    }

    public void BeginTransaction() => ExecuteNonQuery("BEGIN IMMEDIATE;");
    public void Commit() => ExecuteNonQuery("COMMIT;");
    public void Rollback()
    {
        try { ExecuteNonQuery("ROLLBACK;"); } catch { /* ignore */ }
    }

    private Statement Prepare(string sql, object?[] args)
    {
        var rc = Native.sqlite3_prepare_v2(_db, Utf8Z(sql), -1, out var stmt, IntPtr.Zero);
        if (rc != SQLITE_OK) throw new InvalidOperationException($"Ошибка подготовки SQLite: {GetError()}\nSQL: {sql}");
        try
        {
            for (var i = 0; i < args.Length; i++) Bind(stmt, i + 1, args[i]);
            return new Statement(stmt);
        }
        catch
        {
            Native.sqlite3_finalize(stmt);
            throw;
        }
    }

    private void Bind(IntPtr stmt, int index, object? value)
    {
        int rc;
        switch (value)
        {
            case null:
                rc = Native.sqlite3_bind_null(stmt, index);
                break;
            case bool b:
                rc = Native.sqlite3_bind_int64(stmt, index, b ? 1 : 0);
                break;
            case byte or sbyte or short or ushort or int or uint or long:
                rc = Native.sqlite3_bind_int64(stmt, index, Convert.ToInt64(value));
                break;
            case ulong ul when ul <= long.MaxValue:
                rc = Native.sqlite3_bind_int64(stmt, index, (long)ul);
                break;
            case decimal dec:
                rc = Native.sqlite3_bind_double(stmt, index, (double)dec);
                break;
            case float or double:
                rc = Native.sqlite3_bind_double(stmt, index, Convert.ToDouble(value));
                break;
            case DateTime dt:
                rc = Native.sqlite3_bind_text(stmt, index, Utf8Z(dt.ToString("yyyy-MM-dd HH:mm:ss")), -1, SQLITE_TRANSIENT);
                break;
            default:
                rc = Native.sqlite3_bind_text(stmt, index, Utf8Z(Convert.ToString(value) ?? ""), -1, SQLITE_TRANSIENT);
                break;
        }
        if (rc != SQLITE_OK) throw new InvalidOperationException($"Ошибка привязки параметра SQLite: {GetError()}");
    }

    private static object? ReadColumn(IntPtr stmt, int index)
    {
        return Native.sqlite3_column_type(stmt, index) switch
        {
            SQLITE_INTEGER => Native.sqlite3_column_int64(stmt, index),
            SQLITE_FLOAT => Native.sqlite3_column_double(stmt, index),
            SQLITE_TEXT => PtrToUtf8(Native.sqlite3_column_text(stmt, index)),
            SQLITE_NULL => null,
            _ => PtrToUtf8(Native.sqlite3_column_text(stmt, index))
        };
    }

    private string GetError() => PtrToUtf8(Native.sqlite3_errmsg(_db));

    private static byte[] Utf8Z(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        var z = new byte[bytes.Length + 1];
        Buffer.BlockCopy(bytes, 0, z, 0, bytes.Length);
        return z;
    }

    private static string PtrToUtf8(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return "";
        var length = 0;
        while (Marshal.ReadByte(ptr, length) != 0) length++;
        var bytes = new byte[length];
        Marshal.Copy(ptr, bytes, 0, length);
        return Encoding.UTF8.GetString(bytes);
    }

    private static IEnumerable<string> SplitSqlScript(string sql)
    {
        var sb = new StringBuilder();
        var inString = false;
        for (var i = 0; i < sql.Length; i++)
        {
            var ch = sql[i];
            if (ch == '\'' && (i == 0 || sql[i - 1] != '\\')) inString = !inString;
            if (ch == ';' && !inString)
            {
                yield return sb.ToString();
                sb.Clear();
            }
            else sb.Append(ch);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_db != IntPtr.Zero)
        {
            Native.sqlite3_close(_db);
            _db = IntPtr.Zero;
        }
    }

    private sealed class Statement : IDisposable
    {
        public IntPtr Handle { get; private set; }
        public Statement(IntPtr handle) => Handle = handle;
        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                Native.sqlite3_finalize(Handle);
                Handle = IntPtr.Zero;
            }
        }
    }

    private static class Native
    {
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr zVfs);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_close(IntPtr db);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int nByte, out IntPtr stmt, IntPtr tail);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_step(IntPtr stmt);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_finalize(IntPtr stmt);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_null(IntPtr stmt, int index);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_int64(IntPtr stmt, int index, long value);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_double(IntPtr stmt, int index, double value);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_text(IntPtr stmt, int index, byte[] value, int n, IntPtr destructor);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_column_count(IntPtr stmt);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_column_name(IntPtr stmt, int index);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_column_type(IntPtr stmt, int index);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern long sqlite3_column_int64(IntPtr stmt, int index);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern double sqlite3_column_double(IntPtr stmt, int index);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_column_text(IntPtr stmt, int index);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_errmsg(IntPtr db);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern long sqlite3_last_insert_rowid(IntPtr db);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_changes(IntPtr db);
    }
}
