using System.Globalization;

namespace Nipp.Core.Services.Settings;

/// <summary>
/// Ein Eintrag des Katalogs: ein Profilpfad und was er tut.
/// </summary>
/// <param name="Path">
/// Der Pfad, wie er im Profil steht — kleingeschrieben, etwa
/// <c>network.sip-port</c>.
/// </param>
/// <param name="Read">
/// Liest den aktuellen Wert aus den Einstellungen, als Zeichenfolge.
/// <c>null</c> heisst „nicht gesetzt". Gebraucht wird das, um festzustellen,
/// ob der Benutzer etwas geändert hat (ADR-054).
/// </param>
/// <param name="Apply">
/// Wendet einen Rohwert an. <c>null</c> heisst „unbrauchbar" — dann bleiben
/// die Einstellungen, wie sie waren, und der Aufrufer protokolliert.
/// </param>
/// <param name="FactoryOnly">
/// Ob nur die mitgelieferte Konfiguration diesen Pfad setzen darf (ADR-012).
/// </param>
public sealed record ProvisioningEntry(
    string Path,
    Func<NippSettings, string?> Read,
    Func<NippSettings, string, NippSettings?> Apply,
    bool FactoryOnly = false);

/// <summary>
/// Welche Pfade ein Provisionierungsprofil kennt, und was jeder bedeutet
/// (ADR-054).
///
/// <para><b>Warum es diese Klasse gibt.</b> Die Liste stand zweimal: als
/// <c>switch</c> in <c>ProvisioningService.ApplyValues</c> mit 29 Pfaden und
/// als Zeichenkettenliste in <c>nippprov</c> mit 25. Der Kommentar dort gab es
/// zu — «beide können auseinanderlaufen» —, und sie waren es bereits:
/// <c>audio.ringtone</c>, <c>update.channel</c>, <c>update.check-on-start</c>
/// und <c>update.token</c> fehlten dem Generator. Ein gültiges Update-Profil
/// meldete er als unbekannt, und <c>nippprov neu</c> erzeugte kein
/// Vollprofil.</para>
///
/// <para><b>Und warum jetzt.</b> Ein Katalog aus Pfad plus <c>Read</c> ist
/// mehr als Aufräumarbeit: <c>Read</c> ist die Voraussetzung dafür, dass die
/// Einstellungen überhaupt sagen können, welchen Wert der Benutzer selbst
/// geändert hat. Ohne ihn liesse sich «der Benutzer gewinnt» nicht bauen
/// (ADR-054).</para>
///
/// <para><b>Was hier nicht steht:</b> Konten, Nebenstellen und Gruppen. Sie
/// sind keine einzelnen Werte, sondern Listen, und <c>ApplyProfile</c>
/// behandelt sie eigens. Ihre Pfade stehen trotzdem in
/// <see cref="ListPaths"/>, weil auch für sie gilt, was der Benutzer geändert
/// hat.</para>
/// </summary>
public static class ProvisioningCatalog
{
    /// <summary>
    /// Die Pfade der Listen, die <c>ApplyProfile</c> gesondert behandelt.
    ///
    /// <para>Sie tragen keinen <c>Read</c>: eine Kontenliste als Zeichenfolge
    /// darzustellen, nur um sie zu vergleichen, wäre eine zweite Wahrheit über
    /// ihren Inhalt. Ob der Benutzer sie angefasst hat, entscheidet
    /// <c>SettingsService</c> am Vergleich der Listen selbst.</para>
    /// </summary>
    public const string AccountsPath = "accounts";

    /// <inheritdoc cref="AccountsPath"/>
    public const string TeamPath = "contacts.team";

    /// <inheritdoc cref="AccountsPath"/>
    public const string GroupsPath = "contacts.groups";

    /// <summary>Alle Pfade des Katalogs, in der Reihenfolge der Einträge.</summary>
    public static IReadOnlyList<ProvisioningEntry> Entries { get; } = Build();

    /// <summary>
    /// Die Pfade der Listen — sie stehen nicht in <see cref="Entries"/>, aber
    /// eine Sperre und eine Benutzeränderung gelten für sie genauso.
    /// </summary>
    public static IReadOnlyList<string> ListPaths { get; } =
        [AccountsPath, TeamPath, GroupsPath];

    /// <summary>
    /// Alle Pfade, die ein Profil überhaupt nennen darf — Einzelwerte und
    /// Listen. Das ist die Liste, die <c>nippprov schema</c> ausgibt.
    /// </summary>
    public static IReadOnlyList<string> AllPaths { get; } =
        [.. Entries.Select(static e => e.Path), .. ListPaths];

    private static readonly Dictionary<string, ProvisioningEntry> ByPath =
        Entries.ToDictionary(static e => e.Path, StringComparer.OrdinalIgnoreCase);

    /// <summary>Der Eintrag zu einem Pfad, oder <c>null</c>.</summary>
    public static ProvisioningEntry? Find(string? path) =>
        path is { Length: > 0 } && ByPath.TryGetValue(path, out var entry) ? entry : null;

    /// <summary>
    /// Ob dieser Pfad überhaupt bekannt ist — Einzelwert oder Liste.
    /// </summary>
    public static bool Knows(string? path) =>
        path is { Length: > 0 }
        && (ByPath.ContainsKey(path)
            || ListPaths.Contains(path, StringComparer.OrdinalIgnoreCase));

    private static List<ProvisioningEntry> Build() =>
    [
        // ---------------------------------------------------------- Netzwerk
        new("network.sip-port",
            static s => Text(s.Network.SipPort),
            static (s, v) => Int(v) is { } p
                ? s with { Network = s.Network with { SipPort = p } }
                : null),

        new("network.verify-certificate",
            static s => Text(s.Network.VerifyServerCertificate),
            static (s, v) => Bool(v) is { } b
                ? s with { Network = s.Network with { VerifyServerCertificate = b } }
                : null),

        new("network.keep-alive-seconds",
            static s => Text(s.Network.KeepAliveSeconds),
            static (s, v) => Int(v) is { } k
                ? s with { Network = s.Network with { KeepAliveSeconds = k } }
                : null),

        new("network.rtp-port-min",
            static s => Text(s.Network.RtpPortMin),
            static (s, v) => Int(v) is { } lo
                ? s with { Network = s.Network with { RtpPortMin = lo } }
                : null),

        new("network.rtp-port-max",
            static s => Text(s.Network.RtpPortMax),
            static (s, v) => Int(v) is { } hi
                ? s with { Network = s.Network with { RtpPortMax = hi } }
                : null),

        // ------------------------------------------------------- NAT, Medien
        new("nat.stun-server",
            static s => s.NatMedia.StunServer,
            static (s, v) => s with { NatMedia = s.NatMedia with { StunServer = v } }),

        new("nat.enable-ice",
            static s => Text(s.NatMedia.EnableIce),
            static (s, v) => Bool(v) is { } ice
                ? s with { NatMedia = s.NatMedia with { EnableIce = ice } }
                : null),

        new("nat.encryption",
            static s => s.NatMedia.Encryption.ToString(),
            static (s, v) => Enum.TryParse<MediaEncryptionSetting>(v, true, out var enc)
                ? s with { NatMedia = s.NatMedia with { Encryption = enc } }
                : null),

        new("nat.encryption-mandatory",
            static s => Text(s.NatMedia.EncryptionMandatory),
            static (s, v) => Bool(v) is { } m
                ? s with { NatMedia = s.NatMedia with { EncryptionMandatory = m } }
                : null),

        // ------------------------------------------------------------ Codecs
        new("codecs.order",
            static s => Join(s.Codecs.Order),
            static (s, v) => s with { Codecs = s.Codecs with { Order = Split(v) } }),

        new("codecs.enabled",
            static s => Join(s.Codecs.Enabled),
            static (s, v) => s with { Codecs = s.Codecs with { Enabled = Split(v) } }),

        new("codecs.dtmf",
            static s => s.Codecs.Dtmf.ToString(),
            static (s, v) => Enum.TryParse<DtmfMode>(v, true, out var d)
                ? s with { Codecs = s.Codecs with { Dtmf = d } }
                : null),

        // ------------------------------------------------------------- Audio
        // Der Klingelton trägt seine eigene Prüfung: ein Profil aus dem Netz
        // darf keinen absoluten Pfad setzen. Sie steht im Dienst, weil sie das
        // Vertrauen in die Quelle braucht — der Katalog kennt es nicht.
        new("audio.ringtone",
            static s => s.Audio.RingtonePath,
            static (s, v) => s with { Audio = s.Audio with { RingtonePath = v } }),

        // ---------------------------------------------------------- Kontakte
        new("contacts.use-outlook",
            static s => Text(s.Contacts.UseOutlook),
            static (s, v) => Bool(v) is { } o
                ? s with { Contacts = s.Contacts with { UseOutlook = o } }
                : null),

        new("contacts.enable-blf",
            static s => Text(s.Contacts.EnableBlf),
            static (s, v) => Bool(v) is { } b
                ? s with { Contacts = s.Contacts with { EnableBlf = b } }
                : null),

        // --------------------------------------------------------- Erweitert
        new("advanced.country-prefix",
            static s => s.Advanced.CountryPrefix,
            static (s, v) => s with { Advanced = s.Advanced with { CountryPrefix = v } }),

        new("advanced.start-with-windows",
            static s => Text(s.Advanced.StartWithWindows),
            static (s, v) => Bool(v) is { } a
                ? s with { Advanced = s.Advanced with { StartWithWindows = a } }
                : null),

        new("advanced.start-minimized",
            static s => Text(s.Advanced.StartMinimized),
            static (s, v) => Bool(v) is { } m
                ? s with { Advanced = s.Advanced with { StartMinimized = m } }
                : null),

        new("advanced.auto-answer",
            static s => Text(s.Advanced.AutoAnswer),
            static (s, v) => Bool(v) is { } a
                ? s with { Advanced = s.Advanced with { AutoAnswer = a } }
                : null),

        // Der Notausgang aus H5 (ADR-068). Er steht hier, weil ein Gerät, das
        // sich anders verhält als die beiden gemessenen, sonst eine neue
        // Fassung bräuchte — über ein Profil ist er an einem Arbeitsplatz in
        // Minuten gesetzt.
        new("advanced.send-headset-signals",
            static s => Text(s.Advanced.SendHeadsetSignals),
            static (s, v) => Bool(v) is { } h
                ? s with { Advanced = s.Advanced with { SendHeadsetSignals = h } }
                : null),

        new("advanced.global-hotkey",
            static s => s.Advanced.GlobalHotkey,
            static (s, v) => s with { Advanced = s.Advanced with { GlobalHotkey = v } }),

        new("advanced.history-retention-days",
            static s => Text(s.Advanced.HistoryRetentionDays),
            static (s, v) => Int(v) is { } d
                ? s with { Advanced = s.Advanced with { HistoryRetentionDays = d } }
                : null),

        new("advanced.theme",
            static s => s.Advanced.Theme.ToString(),
            static (s, v) => Enum.TryParse<AppTheme>(v, true, out var t)
                ? s with { Advanced = s.Advanced with { Theme = t } }
                : null),

        new("advanced.always-on-top",
            static s => Text(s.Advanced.AlwaysOnTop),
            static (s, v) => Bool(v) is { } t
                ? s with { Advanced = s.Advanced with { AlwaysOnTop = t } }
                : null),

        new("advanced.logging",
            static s => s.Advanced.Logging.ToString(),
            static (s, v) => Enum.TryParse<LogVerbosity>(v, true, out var l)
                ? s with { Advanced = s.Advanced with { Logging = l } }
                : null),

        new("advanced.provisioning-uri",
            static s => s.Advanced.ProvisioningUri,
            static (s, v) => s with { Advanced = s.Advanced with { ProvisioningUri = v } },
            FactoryOnly: true),

        new("advanced.allow-insecure-provisioning",
            static s => Text(s.Advanced.AllowInsecureProvisioning),
            static (s, v) => Bool(v) is { } i
                ? s with { Advanced = s.Advanced with { AllowInsecureProvisioning = i } }
                : null,
            FactoryOnly: true),

        // ------------------------------------------------------------ Update
        // ADR-039: der Kanal gehört ins Profil, weil er je Arbeitsplatz
        // verschieden sein darf — beta für den, der ausprobiert, stable für
        // alle anderen.
        new("update.check-on-start",
            static s => Text(s.Update.CheckOnStart),
            static (s, v) => Bool(v) is { } c
                ? s with { Update = s.Update with { CheckOnStart = c } }
                : null),

        new("update.channel",
            static s => s.Update.Channel.ToString(),
            static (s, v) => Enum.TryParse<UpdateChannel>(v, true, out var ch)
                ? s with { Update = s.Update with { Channel = ch } }
                : null),

        // Das Token ist ein Geheimnis und landet über DPAPI im SecretStore,
        // nicht in settings.json (ADR-039). Der Katalog kennt es, damit
        // «nippprov pruefen» es nicht als unbekannt meldet; angewendet wird es
        // im Dienst, vor dem Katalog — hier fällt ein Seiteneffekt an, und der
        // gehört nicht in eine reine Abbildung.
        new("update.token",
            static _ => null,
            static (s, _) => s,
            FactoryOnly: true),
    ];

    private static string Text(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string Text(bool value) =>
        value ? "true" : "false";

    private static string? Join(IReadOnlyList<string>? values) =>
        values is null ? null : string.Join(',', values);

    private static int? Int(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static bool? Bool(string value) => value.Trim().ToUpperInvariant() switch
    {
        "TRUE" or "1" or "JA" or "YES" or "EIN" => true,
        "FALSE" or "0" or "NEIN" or "NO" or "AUS" => false,
        _ => null,
    };

    private static List<string> Split(string value) =>
        [.. value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
