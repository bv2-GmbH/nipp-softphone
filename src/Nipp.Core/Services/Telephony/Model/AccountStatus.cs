namespace Nipp.Core.Services.Telephony.Model;

/// <summary>
/// Zustand eines SIP-Kontos für die Anzeige (§20.2).
///
/// §20.1 verlangt eine Status-LED je Konto in der Kontoauswahl — dieser Typ
/// ist ihre Datenquelle.
/// </summary>
/// <param name="Identity">SIP-Adresse, eindeutig innerhalb der Konten.</param>
/// <param name="DisplayName">Was der Benutzer sieht; fällt auf den Benutzernamen zurück.</param>
/// <param name="Status">Registrierungszustand.</param>
/// <param name="Message">Begleittext, im Fehlerfall die erklärte Meldung (§15).</param>
/// <param name="IsDefault">Ob dieses Konto für ausgehende Anrufe verwendet wird (§9.1).</param>
public sealed record AccountStatus(
    string Identity,
    string DisplayName,
    RegistrationStatus Status,
    string? Message,
    bool IsDefault)
{
    /// <summary>Ob über dieses Konto telefoniert werden kann.</summary>
    public bool IsUsable => Status == RegistrationStatus.Registered;

    /// <summary>Kurzform für die Auswahl: „151 · bv2" oder die Identität.</summary>
    public string ShortLabel =>
        string.IsNullOrWhiteSpace(DisplayName) ? Identity : DisplayName;
}
