namespace Nipp.Core.Services.Telephony;

/// <summary>
/// Ein Klang, den nipp mitbringt — mit einem Namen für die Oberfläche und
/// einem Pfad, wie er in den Einstellungen steht.
/// </summary>
/// <param name="RelativePath">
/// Relativ zum Ausgabeverzeichnis, mit Schrägstrichen. <b>Absichtlich nicht
/// absolut:</b> in <c>settings.json</c> würde ein absoluter Pfad die
/// Installation festschreiben, und nach einem Update oder einem Umzug des
/// Verzeichnisses zeigte die Wahl ins Leere. Wer eine <b>eigene</b> Datei
/// wählt, speichert dagegen einen absoluten Pfad — die liegt ja irgendwo
/// beim Benutzer.
/// </param>
/// <param name="DisplayName">Wie der Klang in den Einstellungen heisst.</param>
public sealed record BundledSound(string RelativePath, string DisplayName);

/// <summary>
/// Wo die Klänge von nipp liegen und welcher gilt (§9.4).
///
/// <para><b>Warum es diese Stelle gibt.</b> Der Klingelton wurde bisher an
/// einer Stelle im <c>SettingsApplier</c> festverdrahtet, und für das
/// Freizeichen gab es überhaupt keine: <c>Core.Ringback</c> wurde nie gesetzt,
/// der Ton kam allein über den Standardpfad des SDK. Beide Klänge werden jetzt
/// gebraucht — der Klingelton beim SDK, das Freizeichen beim eigenen
/// Tonspieler (siehe <c>RingbackWatch</c>) —, und beide müssen denselben
/// Rückfall kennen.</para>
///
/// <para><b>Der Basispfad ist ein Parameter.</b> Nicht der Bequemlichkeit
/// wegen, sondern weil die Tests sonst vom Ausgabeverzeichnis der App
/// abhängen würden: <c>Assets\Sounds\</c> gehört zu <c>Nipp.App</c> und liegt
/// beim Testlauf nicht daneben.</para>
/// </summary>
public static class NippSounds
{
    /// <summary>Die eigenen Klänge, erzeugt von <c>tools\Build-Sounds.py</c>.</summary>
    public const string OwnRing = "Assets/Sounds/nipp-ring.wav";

    /// <summary>Der eigene Rufton beim Wählen.</summary>
    public const string OwnRingback = "Assets/Sounds/nipp-ringback.wav";

    /// <summary>Die alte Telefonglocke aus dem SDK-Paket — der Ton bis 07.09.2026.</summary>
    public const string SdkOldPhone = "share/sounds/linphone/rings/oldphone-mono.wav";

    /// <summary>Der zweite abspielbare Klang des SDK.</summary>
    public const string SdkToy = "share/sounds/linphone/toy-mono.wav";

    /// <summary>Das Freizeichen des SDK, achtkilohertzig und kurz.</summary>
    public const string SdkRingback = "share/sounds/linphone/ringback.wav";

    /// <summary>
    /// Die Klingeltöne zur Auswahl in den Einstellungen.
    ///
    /// Die sechs sanften Töne des SDK (<c>soft_as_snow</c> und Verwandte)
    /// fehlen hier mit Absicht: sie sind <c>.mkv</c>, und die dafür nötige
    /// <c>bcmatroska2.dll</c> liegt nicht im win64-Prebuilt. Aufgenommen wäre
    /// jeder von ihnen ein Eintrag, der zu Stille führt.
    /// </summary>
    public static readonly IReadOnlyList<BundledSound> BundledRingtones =
    [
        new(OwnRing, "nipp (Standard)"),
        new(SdkOldPhone, "Telefonglocke"),
        new(SdkToy, "Spielzeug"),
    ];

    /// <summary>
    /// Der Klingelton, der gelten soll: die Wahl des Benutzers, sonst der
    /// eigene, sonst der des SDK. <c>null</c> heisst „keiner ist da" — dann
    /// klingelt es am Gerät nicht, und das gehört ins Protokoll.
    /// </summary>
    public static string? Ringtone(string? chosen, string? baseDirectory = null)
    {
        var basis = baseDirectory ?? AppContext.BaseDirectory;

        return FirstExisting(basis, chosen, OwnRing, SdkOldPhone, SdkToy);
    }

    /// <summary>
    /// Der Rufton beim Wählen: der eigene, sonst der des SDK.
    ///
    /// Der eigene ist der Schweizer Rufton (425 Hz, 1 s an, 4 s aus) und zehn
    /// Sekunden lang; der des SDK ist mit 1,5 Sekunden zu kurz, um damit ein
    /// Läuten nachzubilden, und in 8 kHz aufgenommen.
    /// </summary>
    public static string? Ringback(string? baseDirectory = null)
    {
        var basis = baseDirectory ?? AppContext.BaseDirectory;

        return FirstExisting(basis, null, OwnRingback, SdkRingback);
    }

    /// <summary>
    /// Löst einen Pfad aus den Einstellungen auf: ein absoluter bleibt, wie er
    /// ist; ein relativer gilt gegen das Ausgabeverzeichnis.
    /// </summary>
    public static string Resolve(string relativeOrAbsolute, string? baseDirectory = null)
    {
        if (Path.IsPathRooted(relativeOrAbsolute))
        {
            return relativeOrAbsolute;
        }

        var basis = baseDirectory ?? AppContext.BaseDirectory;

        return Path.GetFullPath(Path.Combine(basis, relativeOrAbsolute));
    }

    private static string? FirstExisting(string basis, params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var full = Resolve(candidate, basis);

            if (File.Exists(full))
            {
                return full;
            }
        }

        return null;
    }
}
