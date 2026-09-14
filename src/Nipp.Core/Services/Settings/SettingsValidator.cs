using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Services.Settings;

/// <summary>
/// Prüft Einstellungen, egal woher sie kommen.
///
/// <para><b>Der Anlass.</b> Dieselben Prüfungen standen bisher nur im
/// <c>SettingsViewModel</c> — also auf genau einem der drei Wege, auf denen
/// Einstellungen in nipp gelangen. Beim Einlesen einer Datei
/// (<c>SettingsService.TryImport</c>) wurde allein die Codec-Regel geprüft, und
/// die Provisionierung (§11) übernahm ihre Werte roh. Ein Profil mit
/// <c>sip-port = 0</c> oder eine Sicherungsdatei mit einer Aufbewahrung von
/// −5 Tagen wurde gespeichert und wirkte beim nächsten Start.</para>
///
/// <para>Deshalb hier, SDK-frei und ohne Oberfläche: eine Regel, drei
/// Aufrufer. Die Meldungen sind nach §15 gebaut — sie nennen, was nicht stimmt
/// <b>und</b> was zu tun ist, weil sie sowohl einem Benutzer in den
/// Einstellungen als auch einem Administrator im Protokoll begegnen.</para>
/// </summary>
public static class SettingsValidator
{
    /// <summary>
    /// Alle Beanstandungen, in der Reihenfolge der Abschnitte aus §9. Leer
    /// heisst brauchbar.
    /// </summary>
    public static IReadOnlyList<string> Validate(NippSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var issues = new List<string>();

        ValidateNetwork(settings.Network, issues);
        ValidateCodecs(settings.Codecs, issues);
        ValidateAdvanced(settings.Advanced, issues);

        return issues;
    }

    /// <summary>Die erste Beanstandung als fertiger Satz, oder <c>null</c>.</summary>
    public static string? FirstIssue(NippSettings settings)
    {
        var issues = Validate(settings);

        return issues.Count > 0 ? issues[0] : null;
    }

    private static void ValidateNetwork(NetworkSettings network, List<string> issues)
    {
        if (network.SipPort is < 1 or > 65535)
        {
            issues.Add(
                $"Der SIP-Port {network.SipPort} liegt ausserhalb des zulässigen Bereichs "
                    + "(1 bis 65535). Üblich sind 5060 für UDP und TCP, 5061 für TLS.");
        }

        if (network.KeepAliveSeconds < 0)
        {
            issues.Add("Das Keep-Alive-Intervall kann nicht negativ sein. 0 schaltet es ab.");
        }

        ValidatePortRange(network, issues);
    }

    private static void ValidatePortRange(NetworkSettings nat, List<string> issues)
    {
        if (nat.RtpPortMin is < 1 or > 65535 || nat.RtpPortMax is < 1 or > 65535)
        {
            issues.Add(
                $"Der RTP-Portbereich {nat.RtpPortMin} bis {nat.RtpPortMax} liegt ausserhalb "
                    + "von 1 bis 65535.");
        }
        else if (nat.RtpPortMin > nat.RtpPortMax)
        {
            // Ohne diese Prüfung übernimmt das SDK den Bereich verkehrt herum,
            // und der Fehler zeigt sich erst als Gespräch ohne Ton.
            issues.Add(
                $"Der RTP-Portbereich ist verkehrt herum: {nat.RtpPortMin} ist grösser als "
                    + $"{nat.RtpPortMax}. Die kleinere Zahl gehört nach vorn.");
        }
    }

    private static void ValidateCodecs(CodecSettings codecs, List<string> issues)
    {
        if (!codecs.IsValid)
        {
            // §9.5. Die Regel gab es schon in SettingsService.Write, dort aber
            // als geworfene Ausnahme — die traf beim Provisionieren mitten in
            // einem halb angewendeten Profil ein.
            issues.Add(
                "PCMA und PCMU dürfen nicht beide abgeschaltet sein — sonst scheitert die "
                    + "Aushandlung mit vielen Anlagen. Mindestens einen der "
                    + "beiden einschalten.");
        }
    }

    private static void ValidateAdvanced(AdvancedSettings advanced, List<string> issues)
    {
        if (new NumberNormalizer(advanced.CountryPrefix).CountryPrefix is null)
        {
            issues.Add(
                $"Das Länderpräfix «{advanced.CountryPrefix}» ist nicht verwendbar. Erwartet "
                    + "wird etwas wie +41, 41 oder 0041 — ein Pluszeichen und Ziffern.");
        }

        if (advanced.HistoryRetentionDays < 0)
        {
            issues.Add(
                "Die Aufbewahrung der Anrufliste kann nicht negativ sein. "
                    + "0 heisst: nie aufräumen.");
        }

        if (!string.IsNullOrWhiteSpace(advanced.ProvisioningUri)
            && (!Uri.TryCreate(advanced.ProvisioningUri.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)))
        {
            issues.Add(
                $"Die Provisioning-Adresse «{advanced.ProvisioningUri}» ist keine gültige "
                    + "http- oder https-Adresse.");
        }
    }

    /// <summary>
    /// Was an einer Domain offensichtlich nicht stimmt — oder <c>null</c>.
    ///
    /// <para>Bewusst nachsichtig: geprüft wird nur, was sicher falsch ist.
    /// Eine Anlage kann im Intranet stehen und „pbx" heissen, deshalb ist ein
    /// fehlender Punkt <b>kein</b> Grund. Ein <c>sip:</c>-Präfix, ein
    /// Leerzeichen, ein Schrägstrich oder ein At-Zeichen dagegen schon — das
    /// sind die vier Formen, in denen jemand eine SIP-Adresse statt einer
    /// Domain einträgt.</para>
    /// </summary>
    public static string? DescribeDomainIssue(string domain)
    {
        var wert = domain.Trim();

        if (wert.StartsWith("sip:", StringComparison.OrdinalIgnoreCase)
            || wert.StartsWith("sips:", StringComparison.OrdinalIgnoreCase))
        {
            return "In die Domain gehört nur der Teil nach dem @ — ohne «sip:».";
        }

        if (wert.Contains('@', StringComparison.Ordinal))
        {
            return "In die Domain gehört nur der Teil nach dem @. Der Teil davor "
                + "ist der Benutzername.";
        }

        if (wert.Contains(' ', StringComparison.Ordinal))
        {
            return "In der Domain steht ein Leerzeichen.";
        }

        if (wert.Contains('/', StringComparison.Ordinal))
        {
            return "Die Domain ist keine Adresse mit Pfad — etwa «pbx.example.ch».";
        }

        return null;
    }
}
