using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.ViewModels;

/// <summary>
/// Wie der Zustand eines Kontos heisst — genau eine Stelle (ADR-044).
///
/// <para><b>Der Befund.</b> Vier Wörter für eine Sache: die Hauptansicht sagte
/// «angemeldet», die Einstellungen «Angemeldet», das Infobereich-Symbol
/// «registriert», und die Fehlermeldungen sprachen von «Registrierung …
/// fehlgeschlagen». Wer eine Fehlermeldung an den Support weitergab, suchte
/// danach vergeblich nach dem Wort «Registrierung» in der Oberfläche.</para>
///
/// <para><b>«Anmeldung», nicht «Registrierung».</b> Das ist das Wort, das ein
/// Benutzer benutzt, und es stand schon an der prominentesten Stelle.
/// «Registrierung» und «REGISTER» bleiben im Protokoll, wo sie hingehören —
/// dort sucht man sie mit dem SIP-Trace daneben.</para>
/// </summary>
public static class AccountStateCatalog
{
    /// <summary>
    /// Der Zustand als Wort, satzbeginnend gross. Ohne Zusatz — wer erklären
    /// will, warum nicht telefoniert werden kann, hängt <see cref="Consequence"/>
    /// an.
    /// </summary>
    public static string Of(RegistrationStatus status) => status switch
    {
        RegistrationStatus.Registered => "Angemeldet",
        RegistrationStatus.InProgress => "Wird angemeldet …",
        RegistrationStatus.Unregistered => "Abgemeldet",
        RegistrationStatus.Failed => "Fehlgeschlagen",
        _ => "Nicht angemeldet",
    };

    /// <summary>
    /// Was der Zustand für den Benutzer bedeutet — leer, wenn alles in Ordnung
    /// ist.
    ///
    /// <para>Getrennt von <see cref="Of"/>, weil die Einstellungsseite den
    /// Zustand neben einer Lampe in einer knappen Spalte zeigt und dort kein
    /// Platz für den Nachsatz ist; die Hauptansicht hat eine ganze Zeile
    /// dafür.</para>
    /// </summary>
    public static string Consequence(RegistrationStatus status) => status switch
    {
        RegistrationStatus.Registered or RegistrationStatus.InProgress => string.Empty,
        _ => "Anrufe sind nicht möglich",
    };

    /// <summary>Zustand und Folge in einer Zeile, wie die Hauptansicht sie zeigt.</summary>
    public static string Line(RegistrationStatus status) =>
        Consequence(status) is { Length: > 0 } folge
            ? $"{Of(status)} — {folge}"
            : Of(status);
}
