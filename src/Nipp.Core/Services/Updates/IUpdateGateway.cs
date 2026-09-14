using Nipp.Core.Services.Settings;

namespace Nipp.Core.Services.Updates;

/// <summary>
/// Der Zugang zur Update-Quelle — und die einzige Stelle, an der Velopack
/// vorkommt (ADR-038, ADR-039).
///
/// <para><b>Warum eine Schnittstelle:</b> alles Interessante am Update ist die
/// Logik darum herum — welcher Kanal, wann gefragt wird, wann angewandt werden
/// darf. Die soll ohne Netz prüfbar sein, und der <c>UpdateManager</c> ist
/// eine konkrete Klasse mit Dateisystem- und HTTP-Zugriff. Also eine schmale
/// Naht: vier Aufrufe, kein Zustand.</para>
///
/// <para><b>Und ein zweiter Grund:</b> die Update-Quelle wandert. Heute ein
/// privates GitHub-Repo mit Token, nach dem Öffentlichmachen dasselbe Repo
/// ohne, und für eine Kundenverteilung womöglich ein Webserver bei bv2
/// (docs/plans/RELEASE-PLAN.md R7, R10). Steht der Zugriff an einer Stelle, ist das
/// jeweils eine Zeile.</para>
/// </summary>
public interface IUpdateGateway
{
    /// <summary>
    /// Ob nipp überhaupt installiert ist — im Entwicklungslauf aus dem
    /// Ausgabeverzeichnis heraus ist es das nicht, und dann gibt es nichts zu
    /// aktualisieren. <b>Ohne diese Prüfung wirft Velopack</b>, statt nichts
    /// zu finden.
    /// </summary>
    bool IsInstalled { get; }

    /// <summary>Die laufende Fassung, wie die Update-Ablage sie sieht.</summary>
    string? CurrentVersion { get; }

    /// <summary>
    /// Nachsehen. <c>null</c> heisst „nichts Neues"; eine Ausnahme heisst
    /// „nicht erreichbar" und wird vom Aufrufer als Zustand behandelt, nicht
    /// als Fehler.
    /// </summary>
    Task<AvailableUpdate?> CheckAsync(UpdateChannel channel, CancellationToken cancellationToken);

    /// <summary>Herunterladen. <paramref name="progress"/> bekommt 0 bis 100.</summary>
    Task DownloadAsync(AvailableUpdate update, Action<int>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Anwenden und nipp neu starten. <b>Kehrt nicht zurück.</b> Der Aufrufer
    /// hat vorher sicherzustellen, dass kein Gespräch läuft.
    /// </summary>
    void ApplyAndRestart(AvailableUpdate update);
}
