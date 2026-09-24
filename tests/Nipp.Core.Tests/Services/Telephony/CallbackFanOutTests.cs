using Microsoft.Extensions.Logging;
using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Tests.Services.Telephony;

/// <summary>
/// Was geschieht, wenn <b>einer von mehreren</b> Abonnenten wirft (ADR-053,
/// W2.1 Etappe B8).
///
/// <para><b>Der Fall aus der Geschichte dieses Projekts:</b> eine
/// <c>XamlParseException</c> aus dem Konstruktor der Gesprächsansicht, geworfen
/// im Zustands-Callback. An <c>CallStateChanged</c> hängen acht Abonnenten,
/// darunter die Seitennavigation <b>und</b> der Schreibzugriff auf die
/// Anrufliste.</para>
///
/// <para><b>Was <c>ExceptionBoundaryTests</c> prüft, ist, dass der Wächter
/// dasteht</b> — nicht, was er trägt. Diese Datei misst das Zweite, und sie
/// misst es an der Wirklichkeit von .NET-Ereignissen statt an einer
/// Erwartung.</para>
/// </summary>
public class CallbackFanOutTests
{
    private sealed class Sammler : ILogger
    {
        public List<string> Zeilen { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Zeilen.Add($"{logLevel}: {formatter(state, exception)}");
    }

    private event EventHandler<int>? Ereignis;

    /// <summary>
    /// <b>Der Prozess überlebt</b> — das ist die Zusage des Wächters, und sie
    /// ist die wichtigste: eine Ausnahme aus einem Callback kommt nie in den
    /// nativen Rahmen zurück.
    /// </summary>
    [Fact]
    public void Ein_werfender_Abonnent_nimmt_den_Rahmen_nicht_mit()
    {
        var log = new Sammler();
        Ereignis += (_, _) => throw new InvalidOperationException("Navigation gescheitert");

        var durchgelaufen = CallbackGuard.Run(log, "OnCallStateChanged", () => Ereignis?.Invoke(this, 1));

        Assert.False(durchgelaufen);
        Assert.Contains(log.Zeilen, static z => z.StartsWith("Warning", StringComparison.Ordinal));
    }

    /// <summary>
    /// <b>Abonnenten vor dem werfenden laufen</b> — die Kette wird der Reihe
    /// nach abgearbeitet.
    /// </summary>
    [Fact]
    public void Wer_vorher_drankommt_bekommt_sein_Ereignis()
    {
        var log = new Sammler();
        var gesehen = new List<string>();

        Ereignis += (_, _) => gesehen.Add("erster");
        Ereignis += (_, _) => throw new InvalidOperationException("Navigation gescheitert");

        CallbackGuard.Run(log, "OnCallStateChanged", () => Ereignis?.Invoke(this, 1));

        Assert.Equal(["erster"], gesehen);
    }

    /// <summary>
    /// <b>Und wer nach ihm drankäme, bekommt nichts</b> — gemessen, nicht
    /// vermutet.
    ///
    /// <para><b>Das ist die unangenehme Hälfte der Zusage</b>, und sie steht
    /// hier, damit niemand mehr davon ausgeht, der Wächter halte die Kette
    /// zusammen: ein .NET-Multicast-Delegat bricht beim ersten Fehler ab. Der
    /// Wächter rettet den Prozess, nicht das Ereignis.</para>
    ///
    /// <para><b>Was das praktisch heisst:</b> wirft die Seitennavigation,
    /// erfährt ein danach angemeldeter Abonnent — etwa die Anrufliste — von
    /// diesem Anruf nichts. Die Reihenfolge der Anmeldungen entscheidet
    /// darüber, und sie steht an keiner Stelle geschrieben.</para>
    ///
    /// <para>Ob das so bleibt, ist eine Entscheidung und keine Fehlersuche:
    /// je Abonnent zu fangen hiesse, dass ein kaputter Empfänger die anderen
    /// nicht mehr kostet — aber auch, dass sein Fehler weniger auffällt.</para>
    /// </summary>
    [Fact]
    public void Wer_nachher_drankaeme_bekommt_nichts()
    {
        var log = new Sammler();
        var gesehen = new List<string>();

        Ereignis += (_, _) => throw new InvalidOperationException("Navigation gescheitert");
        Ereignis += (_, _) => gesehen.Add("danach");

        CallbackGuard.Run(log, "OnCallStateChanged", () => Ereignis?.Invoke(this, 1));

        // Gemessen am 24.09.2026: die Kette bricht ab.
        Assert.Empty(gesehen);
    }

    /// <summary>
    /// <b>Das nächste Ereignis kommt trotzdem an</b> — auch beim
    /// Abonnenten hinter dem werfenden. Der Wächter fängt für die Dauer
    /// <i>eines</i> Callbacks und merkt sich nichts.
    /// </summary>
    [Fact]
    public void Das_naechste_Ereignis_laeuft_wieder()
    {
        var log = new Sammler();
        var gesehen = new List<int>();
        var werfen = true;

        Ereignis += (_, _) =>
        {
            if (werfen)
            {
                throw new InvalidOperationException("einmalig");
            }
        };

        Ereignis += (_, wert) => gesehen.Add(wert);

        CallbackGuard.Run(log, "OnCallStateChanged", () => Ereignis?.Invoke(this, 1));
        werfen = false;
        CallbackGuard.Run(log, "OnCallStateChanged", () => Ereignis?.Invoke(this, 2));

        Assert.Equal([2], gesehen);
    }

    /// <summary>
    /// <b>Nie still</b> (W1.7): die gefangene Ausnahme steht mit Callback-Namen
    /// im Protokoll. Eine verschluckte Ausnahme ohne Zeile ist genau die
    /// Lücke, die dieses Projekt zweimal bezahlt hat.
    /// </summary>
    [Fact]
    public void Die_gefangene_Ausnahme_steht_im_Protokoll()
    {
        var log = new Sammler();
        Ereignis += (_, _) => throw new InvalidOperationException("Navigation gescheitert");

        CallbackGuard.Run(log, "OnCallStateChanged", () => Ereignis?.Invoke(this, 1));

        var zeile = Assert.Single(log.Zeilen);
        Assert.Contains("OnCallStateChanged", zeile, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", zeile, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ohne Abonnenten passiert nichts — und das ist kein Fehlschlag. Beim
    /// Start hängt noch niemand an den Ereignissen.
    /// </summary>
    [Fact]
    public void Ohne_Abonnenten_laeuft_es_durch()
    {
        var log = new Sammler();

        Assert.True(CallbackGuard.Run(log, "OnCallStateChanged", () => Ereignis?.Invoke(this, 1)));
        Assert.Empty(log.Zeilen);
    }
}
