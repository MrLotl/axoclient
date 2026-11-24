using AxoClient.Common;
using AxoClient.DataModel.Game;
using CmlLib.Core;

namespace AxoClient.Game.Contract
{
    public interface IMinecraftInstaller
    {
        /// <summary>Gemeinsame Bibliotheken, Versionen und Assets; Spielordner pro Instanz.</summary>
        MinecraftPath PathFor(string gameDirectory);

        MinecraftLauncher CreateLauncher(MinecraftPath path);

        /// <summary>Versions-ID, falls Version und Loader schon installiert sind (für Offline-Starts).</summary>
        string? InstalledVersionName(string minecraftVersion, LoaderType loader, string? loaderVersion);

        /// <summary>Sind Client-Jar und Versions-JSON vorhanden? (Grobe Offline-Prüfung.)</summary>
        bool IsVersionPresent(string versionName);

        /// <summary>Loader installieren (falls nötig) und alle Spieldateien laden bzw. reparieren.</summary>
        Task<string> Install(MinecraftPath path, string minecraftVersion, LoaderType loader, string? loaderVersion, JobContext job);
    }
}
