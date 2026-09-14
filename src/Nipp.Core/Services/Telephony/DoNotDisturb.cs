using System.Globalization;

namespace Nipp.Core.Services.Telephony;

/// <summary>
/// «Nicht stören»: der Klingelton schweigt auf Zeit (§10, ADR-055).
///
/// <para><b>Was es tut und was ausdrücklich nicht.</b> Es schaltet den
/// <b>Klingelton</b> stumm, mehr nicht. Anrufe kommen weiterhin an, der Toast
/// erscheint, die Anrufliste füllt sich, und ein Kollege sieht am
/// Besetztlampenfeld unverändert «frei». Es ist kein SIP-Zustand und keine
/// Mitteilung an die Anlage — wer das will, braucht PUBLISH, und das ist eine
/// andere Entscheidung.</para>
///
/// <para><b>Auf Zeit und nicht auf Dauer.</b> Ein Schalter, den man einschaltet
/// und vergisst, ist die schlechteste Form davon: er nimmt Anrufe entgegen, die
/// niemand hört, und niemand merkt es. Deshalb ein Ablauf — und deshalb
/// überlebt der Zustand keinen Neustart. Wer nipp neu startet, ist aus der
/// Besprechung zurück.</para>
///
/// <para>Reine Funktion mit einer Uhr von aussen, damit sie ohne Warten
/// prüfbar ist.</para>
/// </summary>
public sealed record DoNotDisturb
{
    /// <summary>Nicht aktiv.</summary>
    public static DoNotDisturb Aus { get; } = new();

    /// <summary>
    /// Bis wann geschwiegen wird. <c>null</c> heisst: gar nicht.
    /// </summary>
    public DateTimeOffset? Until { get; private init; }

    /// <summary>Ab <paramref name="jetzt"/> für <paramref name="dauer"/>.</summary>
    public static DoNotDisturb Fuer(DateTimeOffset jetzt, TimeSpan dauer) =>
        dauer <= TimeSpan.Zero ? Aus : new DoNotDisturb { Until = jetzt + dauer };

    /// <summary>Ob der Klingelton gerade schweigt.</summary>
    public bool IsActive(DateTimeOffset jetzt) => Until is { } bis && jetzt < bis;

    /// <summary>
    /// Wie es im Menü steht — <c>null</c>, wenn nicht aktiv.
    ///
    /// <para><b>Aufgerundet auf Minuten.</b> «noch 29 Min.» ist für eine
    /// Besprechung genau, und eine Sekundenanzeige im Kontextmenü, die sich
    /// beim Hinsehen ändert, liest sich wie ein Countdown, der etwas
    /// Schlimmes ankündigt.</para>
    /// </summary>
    public string? Describe(DateTimeOffset jetzt)
    {
        if (Until is not { } bis || jetzt >= bis)
        {
            return null;
        }

        var minuten = (int)Math.Ceiling((bis - jetzt).TotalMinutes);

        return minuten <= 1
            ? "noch weniger als eine Minute"
            : $"noch {minuten.ToString(CultureInfo.InvariantCulture)} Minuten";
    }
}
