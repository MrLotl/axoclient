namespace AxoClient.DataModel.Instances
{
    /// <summary>Mod-Schutz einer Instanz. Betrifft nur Mods – Ressourcepacks, Shader, Welten und Server bleiben frei.</summary>
    public enum ModLockLevel
    {
        /// <summary>Frei: alle Mods lassen sich ändern.</summary>
        Off,

        /// <summary>Pack geschützt: die Mods des Packs bleiben, eigene lassen sich dazulegen und wieder entfernen.</summary>
        Pack,

        /// <summary>Komplett gesperrt: Mods lassen sich weder hinzufügen, entfernen, umschalten noch aktualisieren.</summary>
        Full
    }
}
