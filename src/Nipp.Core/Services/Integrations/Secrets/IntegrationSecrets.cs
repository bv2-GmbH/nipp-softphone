using Nipp.Core.Services.Settings;

namespace Nipp.Core.Services.Integrations.Secrets;

/// <summary>
/// Die Zugangsdaten der Integrationen (§21.2).
///
/// <b>Eine dünne Hülle über dem bestehenden <see cref="SecretStore"/></b> und
/// bewusst nicht mehr: dort liegen schon die SIP-Passwörter, unter DPAPI an
/// Benutzerkonto und Rechner gebunden (§10). Ein zweiter Speicher wäre ein
/// zweiter Ort zum Vergessen — beim Zurücksetzen, beim Diagnosepaket, beim
/// Löschen eines Kontos.
///
/// Die Hülle leistet zwei Dinge:
/// <list type="bullet">
///   <item>
///     Sie stellt jedem Schlüssel <c>integration:</c> voran. Damit kann eine
///     Integrationskonfiguration kein SIP-Passwort überschreiben, auch nicht
///     mit einem <c>secretRef</c>, der zufällig wie eine SIP-Identität
///     aussieht.
///   </item>
///   <item>
///     Sie beantwortet die Frage, die die Oberfläche wirklich stellt:
///     <see cref="IsConfigured"/> — <b>ob</b> ein Geheimnis hinterlegt ist,
///     ohne es zu lesen.
///   </item>
/// </list>
///
/// <b>Folge für den Alltag, die niemanden überraschen darf:</b> DPAPI bindet
/// an Gerät und Benutzerkonto. Ein Schlüssel muss auf jedem Arbeitsplatz
/// einmal eingetragen werden — oder über das Provisionierungsprofil kommen,
/// genau wie das SIP-Passwort heute (docs/provisioning.md).
/// </summary>
public sealed class IntegrationSecrets(SecretStore store)
{
    /// <summary>
    /// Trennt die Integrationsgeheimnisse von den SIP-Passwörtern, die unter
    /// ihrer Identität (<c>sip:151@…</c>) abgelegt sind.
    /// </summary>
    private const string Prefix = "integration:";

    /// <summary>Ob zu diesem Verweis ein Wert hinterlegt ist.</summary>
    public bool IsConfigured(string? reference) =>
        !string.IsNullOrWhiteSpace(reference) && store.Get(Prefix + reference) is { Length: > 0 };

    /// <summary>
    /// Der Wert, oder <c>null</c>. Nur der HTTP-Client ruft das — und er
    /// schreibt das Ergebnis in eine Kopfzeile, nie in ein Protokoll.
    /// </summary>
    public string? Get(string? reference) =>
        string.IsNullOrWhiteSpace(reference) ? null : store.Get(Prefix + reference);

    /// <summary>Legt einen Wert ab. Aus den Einstellungen oder aus einem Profil.</summary>
    public void Set(string reference, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        store.Set(Prefix + reference, secret);
    }

    /// <summary>
    /// Entfernt einen Wert — beim Löschen einer Quelle. §9.1 verlangt
    /// dasselbe für ein gelöschtes SIP-Konto: „sonst bleiben Zugangsdaten
    /// verwaist liegen".
    /// </summary>
    public void Remove(string reference)
    {
        if (!string.IsNullOrWhiteSpace(reference))
        {
            store.Remove(Prefix + reference);
        }
    }

    /// <summary>
    /// Welche Verweise einer Quelle <b>nicht</b> hinterlegt sind.
    ///
    /// Die Antwort entscheidet, ob die Quelle überhaupt gefragt wird: eine
    /// Anfrage mit leerem Schlüssel liefert 401 und sieht für den Benutzer aus
    /// wie ein kaputter Server. Stattdessen wird die Quelle übersprungen, mit
    /// einer Meldung, die sagt, was zu tun ist (§15).
    /// </summary>
    public IReadOnlyList<string> FindMissing(IReadOnlyList<string> references) =>
        [.. references.Where(r => !IsConfigured(r))];
}
