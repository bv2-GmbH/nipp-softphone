using Nipp.Core.Services.Settings;

namespace Nipp.Core.Services.Updates;

/// <summary>
/// Die eine Stelle, an der aus einem <see cref="UpdateChannel"/> ein
/// Kanalname wird.
///
/// <para>Der Name steht gleichlautend im Release-Skript
/// (<c>build\Release-Nipp.ps1</c>) und bestimmt, welche
/// <c>releases.*.json</c> die installierte Fassung liest. Wer ihn hier ändert,
/// ändert ihn dort mit — sonst sucht die App einen Feed, den niemand
/// hochlädt, und meldet wahrheitsgemäss „kein Update".</para>
///
/// <para>Steht bewusst nicht im Gateway: der Kanal ist ein Begriff von nipp,
/// nicht von der Update-Bibliothek. Sonst müsste der <c>UpdateService</c>, um
/// einen Namen ins Protokoll zu schreiben, die Implementierung kennen, die er
/// gerade nicht kennen soll.</para>
/// </summary>
public static class UpdateChannels
{
    /// <summary>Der Kanalname, wie ihn Feed und Release-Skript benutzen.</summary>
    public static string NameOf(UpdateChannel channel) => channel switch
    {
        UpdateChannel.Beta => "win-beta",
        _ => "win-stable",
    };
}

/// <summary>
/// Wo die Update-Prüfung gerade steht (ADR-039).
///
/// <para>Die Reihenfolge ist die des Ablaufs, nicht die des Alphabets:
/// <see cref="Unknown"/> → <see cref="UpToDate"/> oder <see cref="Available"/>
/// → <see cref="Downloading"/> → <see cref="Ready"/>. <see cref="Failed"/> ist
/// von überall erreichbar und kein Endzustand — der nächste Start prüft
/// wieder.</para>
/// </summary>
public enum UpdateState
{
    /// <summary>Noch nicht nachgesehen, oder die Prüfung ist abgeschaltet.</summary>
    Unknown,

    /// <summary>Wird gerade nachgesehen.</summary>
    Checking,

    /// <summary>Nachgesehen, nichts Neues da.</summary>
    UpToDate,

    /// <summary>Eine neuere Fassung liegt bereit. <b>Noch nichts geladen</b> — ADR-039.</summary>
    Available,

    /// <summary>Wird geladen.</summary>
    Downloading,

    /// <summary>Geladen und bereit. Angewandt wird beim Neustart.</summary>
    Ready,

    /// <summary>
    /// Der Abruf ist gescheitert. Kein Netz, kein Token, GitHub hustet — für
    /// die Anzeige ist das dasselbe, und für die Telefonie ist es belanglos.
    /// </summary>
    Failed,
}

/// <summary>
/// Eine gefundene Fassung.
///
/// <para><see cref="Handle"/> trägt das, was die Update-Bibliothek zum
/// Herunterladen und Anwenden braucht. Es steht hier als <c>object</c>, damit
/// das Modell — und mit ihm der ganze <see cref="UpdateService"/> — ohne
/// Velopack-Typen auskommt und in Tests mit einer Attrappe läuft.</para>
/// </summary>
/// <param name="Version">Die neue Fassung, wie sie dem Benutzer angezeigt wird.</param>
/// <param name="IsDowngrade">
/// Ob es zurück geht. Das ist der Normalfall beim Wechsel von beta nach
/// stable und deshalb kein Fehler.
/// </param>
/// <param name="Handle">Undurchsichtig für alle ausser dem Gateway.</param>
public sealed record AvailableUpdate(string Version, bool IsDowngrade, object Handle);
