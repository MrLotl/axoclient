using System.Runtime.InteropServices;
using System.Text;

namespace AxoClient.Platform;

public static class Sqlite
{
    private const int OpenReadWrite = 2;
    private const int Row = 100;

    [DllImport("winsqlite3", CallingConvention = CallingConvention.StdCall)]
    private static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);

    [DllImport("winsqlite3", CallingConvention = CallingConvention.StdCall)]
    private static extern int sqlite3_close(IntPtr db);

    [DllImport("winsqlite3", CallingConvention = CallingConvention.StdCall)]
    private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int bytes, out IntPtr statement, IntPtr tail);

    [DllImport("winsqlite3", CallingConvention = CallingConvention.StdCall)]
    private static extern int sqlite3_step(IntPtr statement);

    [DllImport("winsqlite3", CallingConvention = CallingConvention.StdCall)]
    private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);

    [DllImport("winsqlite3", CallingConvention = CallingConvention.StdCall)]
    private static extern int sqlite3_column_count(IntPtr statement);

    [DllImport("winsqlite3", CallingConvention = CallingConvention.StdCall)]
    private static extern int sqlite3_finalize(IntPtr statement);

    public static List<List<string?[]>> ReadCopy(string file, params string[][] queryGroups)
    {
        var results = queryGroups.Select(_ => new List<string?[]>()).ToList();
        if (!File.Exists(file))
            return results;
        var temp = Path.Combine(Path.GetTempPath(), "axoclient-db-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(temp);
            var copy = Path.Combine(temp, "copy.db");
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                if (File.Exists(file + suffix))
                {
                    using var from = new FileStream(file + suffix, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    using var to = File.Create(copy + suffix);
                    from.CopyTo(to);
                }
            for (var i = 0; i < queryGroups.Length; i++)
                foreach (var sql in queryGroups[i])
                    if (Query(copy, sql) is { } rows)
                    {
                        results[i] = rows;
                        break;
                    }
        }
        catch (Exception ex)
        {
            ErrorReport.Log($"Datenbank \"{file}\" lesen", ex);
        }
        finally
        {
            FileOps.TryDelete(temp);
        }
        return results;
    }

    private static List<string?[]>? Query(string file, string sql)
    {
        if (sqlite3_open_v2(Utf8(file), out var db, OpenReadWrite, IntPtr.Zero) != 0)
        {
            sqlite3_close(db);
            return null;
        }
        try
        {
            if (sqlite3_prepare_v2(db, Utf8(sql), -1, out var statement, IntPtr.Zero) != 0)
                return null;
            try
            {
                var rows = new List<string?[]>();
                var columns = sqlite3_column_count(statement);
                while (sqlite3_step(statement) == Row)
                {
                    var row = new string?[columns];
                    for (var i = 0; i < columns; i++)
                        row[i] = Marshal.PtrToStringUTF8(sqlite3_column_text(statement, i));
                    rows.Add(row);
                }
                return rows;
            }
            finally
            {
                sqlite3_finalize(statement);
            }
        }
        finally
        {
            sqlite3_close(db);
        }
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text + "\0");
}
