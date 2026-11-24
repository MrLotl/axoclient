namespace AxoClient.Launch.Contract
{
    public interface IVersionRange
    {
        /// <param name="unknown">Ergebnis für Schreibweisen, die nicht verstanden werden.</param>
        bool Satisfies(string version, string? range, bool unknown = true);

        /// <summary>
        /// Wie <see cref="Satisfies"/>, aber nachsichtig: nennt die Bedingung eine Version derselben Reihe (z. B. 1.21.x
        /// für 1.21.1), gilt sie als erfüllt. Viele Mods schreiben "[1.21, 1.21.1)" und meinen "bis 1.21.1"; der Loader
        /// startet sie trotzdem. Gewarnt wird nur, wenn die Mod für eine ganz andere Reihe ist (z. B. 1.20 auf 1.21).
        /// </summary>
        bool SatisfiesLoosely(string version, string? range);

        int Compare(string firstVersion, string secondVersion);
    }
}
