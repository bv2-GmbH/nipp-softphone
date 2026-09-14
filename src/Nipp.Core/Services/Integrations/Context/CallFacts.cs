using System.Globalization;

namespace Nipp.Core.Services.Integrations.Context;

/// <summary>
/// Was über den Anruf selbst bekannt ist — der Namensraum <c>call</c> auf einer
/// Karte (§21.6).
///
/// <para><b>Ergebnis und Richtung stehen hier als fertiger Text</b>, nicht als
/// <c>CallOutcome</c> und <c>CallDirection</c>. Der Integrationskern soll sich
/// ohne Codeänderung in ein eigenes Projekt herauslösen lassen (ADR-015); ein
/// Verweis auf die Anrufliste zöge sie mit. Die Wörter stehen deshalb einmal in
/// <c>CallOutcomeText</c>, dort, wo die Anrufliste ohnehin lebt, und kommen von
/// aussen herein.</para>
///
/// <para><b>Und die Zeit als <see cref="DateTimeOffset"/>, nicht als Text.</b>
/// Wer <c>call.startedAt</c> auf eine Karte legt, bekommt einen Datumswert, den
/// die Anzeige in der Sprache des Benutzers formatiert. <c>call.date</c> und
/// <c>call.time</c> gibt es zusätzlich, weil auf einer 400 Pixel breiten Karte
/// oft nur die Uhrzeit gebraucht wird.</para>
/// </summary>
/// <param name="StartedAt">Beginn des Anrufs.</param>
/// <param name="Duration">Gesprächsdauer, oder <c>null</c>, wenn nie verbunden.</param>
/// <param name="Outcome">Das Ergebnis in Worten — „verpasst", „angenommen".</param>
/// <param name="Direction">„eingehend" oder „ausgehend".</param>
public sealed record CallFacts(
    DateTimeOffset StartedAt,
    TimeSpan? Duration,
    string Outcome,
    string Direction)
{
    /// <summary>Das Datum in der Sprache des Benutzers.</summary>
    public string DateText =>
        StartedAt.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);

    /// <summary>Die Uhrzeit, kurz.</summary>
    public string TimeText =>
        StartedAt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);

    /// <summary>
    /// Die Dauer als <c>m:ss</c>, ab einer Stunde als <c>h:mm:ss</c>. Leer,
    /// wenn kein Gespräch zustande kam — dann verschwindet die Zeile.
    /// </summary>
    public string DurationText => ViewModels.RelativeTime.Duration(Duration);
}
