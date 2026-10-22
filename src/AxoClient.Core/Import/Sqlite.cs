using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using AxoClient.Core.Common;

namespace AxoClient.Core.Import;

/// <summary>
/// Liest SQLite-Datenbanken anderer Launcher (Modrinth App, Lunar, NoRisk) über das in Windows eingebaute
/// <c>winsqlite3.dll</c> – ohne zusätzliches Paket. Gelesen wird immer eine Kopie: Der andere Launcher darf gerade
/// laufen und die Datei sperren, und an seiner Datenbank wird nie etwas verändert.
/// </summary>
internal static partial class Sqlite
{
    private const int OpenReadOnly = 1;
    private const int Row = 100;

    [LibraryImport("winsqlite3")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);

    [LibraryImport("winsqlite3")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial int sqlite3_close(IntPtr db);

    [LibraryImport("winsqlite3")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial int sqlite3_prepare_v2(IntPtr db, byte[] sql, int bytes, out IntPtr statement, IntPtr tail);

    [LibraryImport("winsqlite3")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial int sqlite3_step(IntPtr statement);

    [LibraryImport("winsqlite3")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial IntPtr sqlite3_column_text(IntPtr statement, int column);

    [LibraryImport("winsqlite3")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial int sqlite3_column_count(IntPtr statement);

    [LibraryImport("winsqlite3")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial int sqlite3_finalize(IntPtr statement);

    /// <summary>
    /// Führt pro Gruppe die erste Abfrage aus, die funktioniert (ältere Launcher-Versionen haben andere Spalten).
    /// Fehlt die Datei oder schlägt alles fehl, ist das Ergebnis der Gruppe leer.
    /// </summary>
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
            {
                if (!File.Exists(file + suffix))
                    continue;
                using var from = new FileStream(file + suffix, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var to = File.Create(copy + suffix);
                from.CopyTo(to);
            }
            for (var i = 0; i < queryGroups.Length; i++)
            foreach (var sql in queryGroups[i])
            {
                if (Query(copy, sql) is not { } rows)
                    continue;
                results[i] = rows;
                break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException)
        {
            AppLog.Warn($"Datenbank {Path.GetFileName(file)} eines anderen Launchers nicht lesbar", ex);
        }
        finally
        {
            try
            {
                Directory.Delete(temp, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        return results;
    }

    private static List<string?[]>? Query(string file, string sql)
    {
        if (sqlite3_open_v2(Utf8(file), out var db, OpenReadOnly, IntPtr.Zero) != 0)
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
