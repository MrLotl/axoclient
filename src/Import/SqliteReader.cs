using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using AxoClient.Common;
using AxoClient.Import.Contract;

namespace AxoClient.Import
{
    /// <summary>
    /// Liest SQLite-Datenbanken anderer Launcher (Modrinth App, Lunar, NoRisk) über das in Windows eingebaute
    /// <c>winsqlite3.dll</c> – ohne zusätzliches Paket. Gelesen wird immer eine Kopie: Der andere Launcher darf gerade
    /// laufen und die Datei sperren, und an seiner Datenbank wird nie etwas verändert.
    /// </summary>
    public partial class SqliteReader : ISqliteReader
    {
        private readonly IAppLog _appLog;
        private const int OpenReadOnly = 1;
        private const int Row = 100;

        public SqliteReader(IAppLog appLog)
        {
            _appLog = appLog;
        }

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
        public List<List<string?[]>> ReadCopy(string file, params string[][] queryGroups)
        {
            var results = queryGroups.Select(_ => new List<string?[]>()).ToList();
            if (!File.Exists(file))
            {
                return results;
            }

            var temporary = Path.Combine(Path.GetTempPath(), "axoclient-db-" + Guid.NewGuid().ToString("N")[..8]);
            try
            {
                Directory.CreateDirectory(temporary);
                var copy = Path.Combine(temporary, "copy.db");
                foreach (var suffix in new[] { "", "-wal", "-shm" })
                {
                    if (!File.Exists(file + suffix))
                    {
                        continue;
                    }

                    using var from = new FileStream(file + suffix, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var to = File.Create(copy + suffix);
                    from.CopyTo(to);
                }
                for (var index = 0; index < queryGroups.Length; index++)
                {
                    foreach (var sql in queryGroups[index])
                    {
                        if (query(copy, sql) is not { } rows)
                        {
                            continue;
                        }

                        results[index] = rows;
                        break;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException)
            {
                _appLog.Warn($"Datenbank {Path.GetFileName(file)} eines anderen Launchers nicht lesbar", exception);
            }
            finally
            {
                try
                {
                    Directory.Delete(temporary, true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
            return results;
        }

        private List<string?[]>? query(string file, string sql)
        {
            if (sqlite3_open_v2(utf8(file), out var database, OpenReadOnly, IntPtr.Zero) != 0)
            {
                sqlite3_close(database);
                return null;
            }
            try
            {
                if (sqlite3_prepare_v2(database, utf8(sql), -1, out var statement, IntPtr.Zero) != 0)
                {
                    return null;
                }

                try
                {
                    var rows = new List<string?[]>();
                    var columns = sqlite3_column_count(statement);
                    while (sqlite3_step(statement) == Row)
                    {
                        var row = new string?[columns];
                        for (var index = 0; index < columns; index++)
                        {
                            row[index] = Marshal.PtrToStringUTF8(sqlite3_column_text(statement, index));
                        }

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
                sqlite3_close(database);
            }
        }

        private byte[] utf8(string text) => Encoding.UTF8.GetBytes(text + "\0");
    }
}
