using System.Net;

namespace Nipp.Core.Services.Integrations.Http;

/// <summary>
/// Merkt sich, wie es einer Quelle geht, und lässt sie in Ruhe, wenn sie
/// nicht kann (§21.2).
///
/// <b>Zwei Fälle, und beide treten beim Kunden auf, nicht im Test.</b>
///
/// Ein Server, der weg ist, bleibt es meist eine Weile. Ihn bei jedem Anruf
/// erneut zu fragen kostet je Anruf die volle Zeitgrenze — die Karte wartet
/// dann sichtbar auf etwas, das absehbar nicht kommt. Nach einigen Fehlern in
/// Folge wird die Quelle deshalb für eine Minute übersprungen.
///
/// Ein Server, der <c>429</c> sagt, sagt damit ausdrücklich: frag später. Wer
/// trotzdem weiterfragt, verlängert die Sperre — bei manchen Anbietern
/// stundenlang.
///
/// <b>Ein Erfolg setzt beides sofort zurück.</b> Der Schutzschalter ist eine
/// Vorsichtsmassnahme, keine Strafe.
/// </summary>
public sealed class IntegrationHealth(TimeProvider? time = null)
{
    /// <summary>
    /// Nach wie vielen Fehlern in Folge eine Quelle pausiert wird.
    ///
    /// Fünf und nicht einer: ein einzelner Fehler ist ein Zucken im Netz, und
    /// eine Quelle nach dem ersten Aussetzer für eine Minute abzuschalten
    /// wäre schlimmer als das Problem.
    /// </summary>
    public const int FailuresBeforeBreak = 5;

    /// <summary>Wie lange eine Quelle nach dem Schutzschalter übersprungen wird.</summary>
    public static readonly TimeSpan BreakDuration = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Obergrenze für eine Pause, die der Server selbst nennt.
    ///
    /// Ein <c>Retry-After</c> von einer Stunde ist möglich und für ein
    /// Softphone unbrauchbar: nach einer Stunde ist die Sitzung eine andere.
    /// Es wird gedeckelt und beim nächsten Mal erneut versucht.
    /// </summary>
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(5);

    private readonly record struct State(int Failures, DateTimeOffset? SkipUntil);

    private readonly Dictionary<string, State> _states = new(StringComparer.Ordinal);
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>
    /// Schuetzt <see cref="_states"/>.
    ///
    /// <para>Anders als der Anruferkontext, der sich auf einen Thread
    /// zurueckzieht, wird diese Klasse aus jeder laufenden Anfrage heraus
    /// beschrieben — also aus beliebigen Threadpool-Threads, nach jedem
    /// <c>ConfigureAwait(false)</c>, und fuer alle Quellen gleichzeitig. Ein
    /// <c>Dictionary</c> unter nebenlaeufigem Schreiben kann in .NET in eine
    /// Endlosschleife im Nachschlagen laufen; hier ist eine Sperre der
    /// einfachere Weg, weil jede Operation kurz ist und nichts erwartet.</para>
    ///
    /// <para>Ein <c>object</c> und nicht der <c>Lock</c>-Typ: den gibt es erst
    /// ab .NET 9, und nipp steht auf .NET 8 (§4).</para>
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    /// Ob diese Quelle gerade gefragt werden darf.
    /// </summary>
    public bool IsAvailable(string sourceId)
    {
        lock (_gate)
        {
            if (!_states.TryGetValue(sourceId, out var state) || state.SkipUntil is not { } until)
            {
                return true;
            }

            if (until > _time.GetUtcNow())
            {
                return false;
            }

            // Die Pause ist vorbei. Der Fehlerzähler bleibt stehen: geht es gleich
            // wieder schief, soll die nächste Pause nicht erst nach fünf weiteren
            // Versuchen kommen.
            _states[sourceId] = state with { SkipUntil = null };
            return true;
        }
    }

    /// <summary>
    /// Wie lange diese Quelle noch übersprungen wird — für die Meldung an den
    /// Benutzer (§15: sagen, was ist und wann es weitergeht).
    /// </summary>
    public TimeSpan? RemainingBreak(string sourceId)
    {
        lock (_gate)
        {
            if (!_states.TryGetValue(sourceId, out var state) || state.SkipUntil is not { } until)
            {
                return null;
            }

            var rest = until - _time.GetUtcNow();

            return rest > TimeSpan.Zero ? rest : null;
        }
    }

    /// <summary>Die Quelle hat geantwortet. Alles zurücksetzen.</summary>
    public void ReportSuccess(string sourceId)
    {
        lock (_gate)
        {
            _states.Remove(sourceId);
        }
    }

    /// <summary>
    /// Die Quelle hat nicht geantwortet — Netz, Zeitgrenze oder Serverfehler.
    /// </summary>
    public void ReportFailure(string sourceId)
    {
        lock (_gate)
        {
            var state = _states.GetValueOrDefault(sourceId);
            var failures = state.Failures + 1;

            _states[sourceId] = failures >= FailuresBeforeBreak
                ? new State(failures, _time.GetUtcNow().Add(BreakDuration))
                : state with { Failures = failures };
        }
    }

    /// <summary>
    /// Der Server hat ausdrücklich um eine Pause gebeten (<c>429</c>).
    ///
    /// <paramref name="retryAfter"/> ist, was er genannt hat; ohne Angabe gilt
    /// die übliche Dauer.
    /// </summary>
    public void ReportRateLimited(string sourceId, TimeSpan? retryAfter)
    {
        var pause = retryAfter is { } wish && wish > TimeSpan.Zero
            ? (wish < MaxRetryAfter ? wish : MaxRetryAfter)
            : BreakDuration;

        lock (_gate)
        {
            var state = _states.GetValueOrDefault(sourceId);

            _states[sourceId] = new State(state.Failures + 1, _time.GetUtcNow().Add(pause));
        }
    }

    /// <summary>
    /// Liest <c>Retry-After</c> aus einer Antwort — als Sekundenzahl oder als
    /// Zeitpunkt, beides ist nach RFC 9110 erlaubt.
    /// </summary>
    public static TimeSpan? RetryAfterOf(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.StatusCode != HttpStatusCode.TooManyRequests)
        {
            return null;
        }

        var header = response.Headers.RetryAfter;

        if (header?.Delta is { } delta)
        {
            return delta;
        }

        return header?.Date is { } date ? date - DateTimeOffset.UtcNow : null;
    }

    /// <summary>Vergisst alles — bei einer Änderung der Konfiguration.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _states.Clear();
        }
    }
}
