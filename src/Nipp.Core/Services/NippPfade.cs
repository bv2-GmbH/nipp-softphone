namespace Nipp.Core.Services;

/// <summary>
/// Wo nipp seine Daten ablegt — und das entscheidet <b>genau diese Stelle</b>
/// (ADR-079).
///
/// <para><b>Warum es sie gibt.</b> Der Pfad stand an elf Stellen im Code, jedes
/// Mal von Hand aus <c>LocalApplicationData</c> und dem Literal «nipp»
/// zusammengesetzt. Das war nicht nur Wiederholung: es machte den Ort
/// unverhandelbar, weil ihn zu ändern hiess, elf Stellen zu finden — und eine
/// davon zu übersehen hiesse, dass nipp seine Anrufliste an einem Ort sucht
/// und an einem anderen schreibt.</para>
///
/// <para><b>Warum der Ort gewechselt hat</b> (09.10.2026, gemessen am
/// Installationsprogramm): Velopack packt nipp mit <c>packId nipp</c> und
/// installiert damit nach <c>%LOCALAPPDATA%\nipp</c> — genau dorthin, wo die
/// Daten lagen. Das Setup fand das Verzeichnis vor, hielt es für eine
/// bestehende Installation und fragte, ob es überschreiben solle:</para>
///
/// <code>
/// Installation Directory: "C:\Users\...\AppData\Local\nipp"
/// Overwrite/repair dialog timed out, treating as cancel.
/// Directory already exists, and user cancelled overwrite.
/// </code>
///
/// <para><b>Eine Installation war damit auf jedem Rechner unmöglich, auf dem
/// nipp schon einmal gelaufen war</b> — und schlimmer: bei einer
/// Deinstallation räumt Velopack sein Installationsverzeichnis auf, und das
/// hätte Anrufliste und Zugangsdaten mitgenommen. <c>docs/updates.md</c>
/// versprach derweil, Benutzerdaten lägen «ausserhalb und überleben jedes
/// Update».</para>
///
/// <para><b>Was hier bewusst NICHT liegt:</b> die Einstellungen. Sie stehen
/// weiterhin unter <c>%APPDATA%\nipp</c> (<c>settings.json</c>,
/// <c>linphonerc</c>, <c>integrations.json</c>), und das ist kein Versehen —
/// dorthin installiert Velopack nicht, es gab also nie einen Konflikt. Einen
/// zweiten Umzug ohne Not anzufangen hiesse, eine zweite Migration zu
/// schreiben, die irgendwann niemand mehr braucht.</para>
/// </summary>
public static class NippPfade
{
    /// <summary>
    /// Der Herstellerordner. Er ist der ganze Trick: <c>%LOCALAPPDATA%\bv2</c>
    /// kollidiert mit keiner <c>packId</c>, und Velopack kann darunter nichts
    /// anlegen, weil es nur den Paketnamen kennt.
    /// </summary>
    private const string Hersteller = "bv2";

    private const string Anwendung = "nipp";

    /// <summary>Wo die Daten heute liegen.</summary>
    public static string Daten { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Hersteller,
        Anwendung);

    /// <summary>
    /// Wo sie bis zum 09.10.2026 lagen — gebraucht für die einmalige
    /// Übernahme und <b>sonst für nichts</b>.
    /// </summary>
    public static string DatenAlt { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Anwendung);

    /// <summary>Die Protokolle (§10).</summary>
    public static string Logs => Path.Combine(Daten, "logs");

    /// <summary>Die Diagnosepakete (§9.6).</summary>
    public static string Diagnose => Path.Combine(Daten, "diagnostics");

    /// <summary>Die Gesprächsaufnahmen (§8.2).</summary>
    public static string Aufnahmen => Path.Combine(Daten, "recordings");

    /// <summary>Die Anrufliste.</summary>
    public static string Anrufliste => Path.Combine(Daten, "history.db");

    /// <summary>Die verschlüsselte Ablage der Zugangsdaten (§10, DPAPI).</summary>
    public static string Geheimnisse => Path.Combine(Daten, "secrets.dat");

    /// <summary>Die mitgelieferten Wurzelzertifikate.</summary>
    public static string Wurzelzertifikate => Path.Combine(Daten, "rootca.pem");
}
