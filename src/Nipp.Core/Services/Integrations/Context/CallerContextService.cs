using Microsoft.Extensions.Logging;
using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Integrations.Phone;
using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Services.Integrations.Context;

/// <summary>
/// Woher der Anruferkontext seine Quellen bekommt. Wie bei der Suche eine
/// Abstraktion, weil die Menge aus einer Konfiguration entsteht, die sich zur
/// Laufzeit ändern kann.
/// </summary>
public interface ICallerContextProviderRegistry
{
    IReadOnlyList<ICallerContextProvider> ContextProviders { get; }
}

/// <summary>
/// Holt zu jedem Anruf zusammen, was die angebundenen Systeme wissen (§21.1).
///
/// <b>Diese Klasse ist die einzige Stelle, an der die Integrationsplattform
/// den Anrufpfad berührt.</b> Deshalb hält sie sich an vier Regeln, die
/// jeweils einen konkreten Schaden verhindern:
///
/// <list type="number">
///   <item>
///     <b>Nichts Blockierendes im Ereignis.</b> <see cref="OnCallStateChanged"/>
///     legt Zustand an und startet Aufgaben — es wird nichts erwartet. Ein
///     <c>await</c> hier liefe auf dem Thread, der alle 20 ms
///     <c>Core.Iterate()</c> bedient (§6, §14.1): das Telefon stünde still,
///     solange ein fremder Server nicht antwortet.
///   </item>
///   <item>
///     <b>Die Anrufkennung entscheidet, nicht der Zustandsübergang.</b> Das
///     ist die Lehre aus docs/plans/REVIEW.md §8: welche Zustände das SDK in welcher
///     Reihenfolge meldet, ist für ein- und ausgehende Anrufe verschieden,
///     und jede Liste erlaubter Übergänge hat bisher eine Richtung vergessen.
///     Hier wird gefragt: kenne ich diesen Anruf schon?
///   </item>
///   <item>
///     <b>Jede Quelle ist isoliert.</b> Eigene Zeitgrenze, eigener Fehler,
///     eigener Abbruch. Eine langsame Quelle verzögert keine andere, und
///     keine verzögert die Anzeige (§21.2).
///   </item>
///   <item>
///     <b>Es wird nie geworfen.</b> Was schiefgeht, wird zu einem Zustand mit
///     Meldung. Eine Ausnahme aus einem SDK-Ereignishandler nimmt die ganze
///     Anwendung mit.
///   </item>
/// </list>
///
/// <b>Threading.</b> Wie bei der Suche wird der <c>SynchronizationContext</c>
/// beim Bau erfasst und für die Meldungen benutzt — <c>Nipp.Core</c> bleibt
/// ohne UI-Abhängigkeit (§6).
/// </summary>
public sealed class CallerContextService : ICallContextSnapshots, IDisposable
{
    /// <summary>Was zu einem laufenden Anruf gehört.</summary>
    private sealed class Session(CallHandle handle, PhoneNumberKey number) : IDisposable
    {
        public CallHandle Handle { get; } = handle;

        public PhoneNumberKey Number { get; } = number;

        public CancellationTokenSource Cancellation { get; } = new();

        public ContextSnapshot Snapshot { get; set; } = ContextSnapshot.Empty(handle, number);

        public void Dispose()
        {
            Cancellation.Cancel();
            Cancellation.Dispose();
        }
    }

    private readonly ISipService _sip;
    private readonly ICallerContextProviderRegistry _registry;
    private readonly IntegrationConfigStore _config;
    private readonly SettingsService _settings;
    private readonly CallerContextCache _cache;
    private readonly ILogger<CallerContextService> _logger;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;

    /// <summary>
    /// Die Anrufe, um die es gerade geht.
    ///
    /// <b>Nach Kennung</b>, und das ist die Regel aus docs/plans/REVIEW.md §8: ein Anruf,
    /// dessen Kennung schon hier steht, ist nicht neu — unabhängig davon,
    /// welchen Zustand das SDK gerade meldet und in welcher Reihenfolge.
    /// </summary>
    private readonly Dictionary<CallHandle, Session> _sessions = [];

    private bool _started;
    private bool _disposed;

    public CallerContextService(
        ISipService sip,
        ICallerContextProviderRegistry registry,
        IntegrationConfigStore config,
        SettingsService settings,
        ILogger<CallerContextService> logger,
        TimeProvider? time = null)
    {
        _sip = sip;
        _registry = registry;
        _config = config;
        _settings = settings;
        _logger = logger;
        _cache = new CallerContextCache(time ?? TimeProvider.System);
    }

    /// <summary>
    /// Ein neuer Stand zu einem Anruf. Wird auf dem Thread ausgelöst, auf dem
    /// dieser Dienst gebaut wurde.
    /// </summary>
    public event EventHandler<ContextSnapshot>? ContextChanged;

    /// <summary>
    /// Beginnt zuzuhören.
    ///
    /// <b>Getrennt vom Konstruktor</b>, damit die App bestimmt, wann das
    /// geschieht: erst nach dem ersten Zeichnen, zusammen mit den übrigen
    /// Shell-Diensten. Nichts an den Integrationen darf zwischen dem Start und
    /// dem ersten möglichen Anruf stehen (§21.2).
    /// </summary>
    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        _sip.CallStateChanged += OnCallStateChanged;

        // Wird an der Konfiguration etwas geaendert, ist der Zwischenspeicher
        // veraltet: nach einer Mapping-Korrektur zeigte die Karte sonst fuenf
        // Minuten lang die alte Antwort, und die Fragmente einer soeben
        // abgeschalteten Quelle blieben ebenso lange im Arbeitsspeicher liegen.
        // Die Clear-Methode gab es von Anfang an — sie wurde nur nie gerufen.
        _config.Changed += OnConfigChanged;
    }

    private void OnConfigChanged(object? sender, IntegrationConfig config)
    {
        RunOnUi(() =>
        {
            _cache.Clear();
            CallerContextLog.CacheCleared(_logger);
        });
    }

    /// <summary>Was zu einem Anruf bekannt ist, oder <c>null</c>.</summary>
    public ContextSnapshot? SnapshotFor(CallHandle handle) =>
        _sessions.TryGetValue(handle, out var session) ? session.Snapshot : null;

    /// <summary>
    /// Fragt die Quellen zu einer Nummer, ohne dass ein Anruf läuft — für die
    /// Anrufliste (§22.3).
    ///
    /// <para><b>Warum das eine eigene Methode ist.</b> Der Weg über
    /// <see cref="Begin"/> hängt an einer Anrufkennung, an einer Richtung und
    /// an einer Sitzung, die beim Auflegen aufgeräumt wird. Ein Journaleintrag
    /// hat nichts davon: er hat eine Nummer und einen Klick.</para>
    ///
    /// <para><b>Es wird nichts gespeichert</b> (ADR-027). Der Zwischenspeicher
    /// ist derselbe wie im Gespräch — fünf Minuten im Arbeitsspeicher, nie auf
    /// der Platte —, und wer denselben Eintrag zweimal aufklappt, fragt die
    /// Quelle deshalb nicht zweimal.</para>
    ///
    /// <para><b>Der Aufrufer sorgt dafür, dass nur eine Abfrage läuft.</b> Wer
    /// durch die Liste klickt, löst sonst je Eintrag eine aus; das Abbrechen
    /// geschieht über den übergebenen Token.</para>
    /// </summary>
    /// <returns>
    /// Der vollständige Stand, wenn alle Quellen geantwortet haben — oder was
    /// bis zum Abbruch da war. Nie <c>null</c>: eine leere Antwort ist auch
    /// eine.
    /// </returns>
    public async Task<ContextSnapshot> LookupNumberAsync(
        string? rawNumber,
        CancellationToken cancellationToken = default)
    {
        var normalizer = new NumberNormalizer(_settings.Current.Advanced.CountryPrefix);
        var number = PhoneNumberKey.From(rawNumber, normalizer);

        // Die Kennung ist hier ohne Bedeutung — der Schnappschuss gehört zu
        // keinem Anruf. Sie wird trotzdem gebraucht, weil ContextSnapshot sie
        // führt; eine neue je Abfrage hält die Karte auseinander.
        var snapshot = ContextSnapshot.Empty(CallHandle.New(), number);

        var lookup = _config.Current.CallerLookup;

        if (!lookup.Enabled)
        {
            return snapshot;
        }

        // Richtung „eingehend": im Journal geht es darum, wer die Gegenstelle
        // ist, und die Regel für interne Nummern (§21.4) gilt genauso.
        var providers = _registry.ContextProviders
            .Where(p => p.AppliesTo(number, CallDirection.Incoming))
            .ToList();

        if (providers.Count == 0)
        {
            return snapshot;
        }

        var results = await Task.WhenAll(
            providers.Select(p => AskOnceAsync(p, number, cancellationToken)))
            .ConfigureAwait(false);

        foreach (var fragment in results)
        {
            snapshot = snapshot.With(fragment);
        }

        return snapshot;
    }

    /// <summary>
    /// Eine Quelle einmal fragen, mit Zwischenspeicher und eigener Zeitgrenze —
    /// ohne Sitzung und ohne Ereignis.
    ///
    /// <para>Wirft nie: was schiefgeht, wird zu einem Fragment mit Meldung.
    /// Dieselbe Regel wie im Gespräch, und aus demselben Grund — die Anzeige
    /// soll sagen, was los ist, statt leer zu bleiben.</para>
    /// </summary>
    private async Task<ContextFragment> AskOnceAsync(
        ICallerContextProvider provider,
        PhoneNumberKey number,
        CancellationToken cancellationToken)
    {
        if (_cache.TryGet(provider.SourceId, number) is { } cached)
        {
            return cached with { FromCache = true };
        }

        using var timeout = provider.Timeout > TimeSpan.Zero
            ? new CancellationTokenSource(provider.Timeout)
            : new CancellationTokenSource();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);

        try
        {
            var fragment = await provider.LookupAsync(number, linked.Token).ConfigureAwait(false);

            if (fragment.State is SourceState.Success or SourceState.Empty)
            {
                RunOnUi(() => _cache.Set(provider.SourceId, number, fragment, _config.Current.CallerLookup));
            }

            return fragment;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Der Benutzer ist weitergeklickt. Kein Fehler, nur überholt.
            return ContextFragment.Loading(provider.SourceId, provider.DisplayName, provider.Priority);
        }
        catch (OperationCanceledException)
        {
            CallerContextLog.SourceTimedOut(_logger, provider.SourceId);

            return new ContextFragment(
                provider.SourceId,
                provider.DisplayName,
                SourceState.Timeout,
                new Dictionary<string, ContextValue>(StringComparer.Ordinal),
                $"{provider.DisplayName} antwortet nicht.",
                Priority: provider.Priority);
        }
        catch (Exception ex)
        {
            CallerContextLog.SourceFailed(_logger, provider.SourceId, ex.GetType().Name);

            return new ContextFragment(
                provider.SourceId,
                provider.DisplayName,
                SourceState.Error,
                new Dictionary<string, ContextValue>(StringComparer.Ordinal),
                $"{provider.DisplayName} ist nicht erreichbar.",
                Priority: provider.Priority);
        }
    }

    /// <summary>
    /// Ein Anruf hat seinen Zustand geändert.
    ///
    /// <b>Hier wird nichts erwartet</b> — die Begründung steht am
    /// Klassenkommentar.
    /// </summary>
    private void OnCallStateChanged(object? sender, CallStateEventArgs e)
    {
        try
        {
            if (!e.Call.IsActive)
            {
                Forget(e.Call.Handle);
                return;
            }

            // Die eine Frage, die zählt: kenne ich diesen Anruf schon?
            if (_sessions.ContainsKey(e.Call.Handle))
            {
                return;
            }

            Begin(e.Call);
        }
        catch (Exception ex)
        {
            // Eine Ausnahme in einem SDK-Ereignishandler nimmt die Anwendung
            // mit. Was hier schiefgeht, kostet höchstens den Kontext.
            CallerContextLog.StartFailed(_logger, ex.GetType().Name);
        }
    }

    private void Begin(CallInfo call)
    {
        var lookup = _config.Current.CallerLookup;

        if (!lookup.Enabled)
        {
            return;
        }

        var wanted = call.Direction == CallDirection.Incoming
            ? lookup.LookupIncoming
            : lookup.LookupOutgoing;

        if (!wanted)
        {
            return;
        }

        var normalizer = new NumberNormalizer(_settings.Current.Advanced.CountryPrefix);
        var number = PhoneNumberKey.From(call.RemoteNumber, normalizer);

        var session = new Session(call.Handle, number);
        _sessions[call.Handle] = session;

        var providers = _registry.ContextProviders
            .Where(p => p.AppliesTo(number, call.Direction))
            .ToList();

        if (providers.Count == 0)
        {
            return;
        }

        // Der erste Stand geht sofort hinaus: jede Quelle, die gefragt wird,
        // steht als „wird geladen" darin. Die Karte zeigt damit vom ersten
        // Augenblick an, worauf sie wartet — und die lokalen Kontakte
        // antworten noch in dieser Iteration.
        foreach (var provider in providers)
        {
            session.Snapshot = session.Snapshot.With(
                ContextFragment.Loading(provider.SourceId, provider.DisplayName, provider.Priority));
        }

        Publish(session);

        CallerContextLog.LookupStarted(_logger, providers.Count);

        // Kein await: die Aufgaben laufen weiter, während das Ereignis
        // zurückkehrt und das SDK weiterarbeitet.
        foreach (var provider in providers)
        {
            _ = AskAsync(session, provider);
        }
    }

    /// <summary>
    /// Fragt eine Quelle und veröffentlicht ihr Ergebnis, sobald es da ist.
    ///
    /// Jede Quelle für sich — deshalb je eine Aufgabe und nicht ein
    /// <c>WhenAll</c>: eine langsame darf keine schnelle aufhalten (§21.2).
    /// </summary>
    private async Task AskAsync(Session session, ICallerContextProvider provider)
    {
        var fromCache = _cache.TryGet(provider.SourceId, session.Number);

        if (fromCache is not null)
        {
            Apply(session, fromCache with { FromCache = true });
            return;
        }

        // Eigene Zeitgrenze je Quelle, verbunden mit dem Abbruch der Sitzung.
        // Nur so lässt sich „zu langsam" von „das Gespräch ist vorbei"
        // unterscheiden — und das ist der Unterschied zwischen einer Meldung
        // und Schweigen.
        using var timeout = provider.Timeout > TimeSpan.Zero
            ? new CancellationTokenSource(provider.Timeout)
            : new CancellationTokenSource();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            session.Cancellation.Token,
            timeout.Token);

        ContextFragment fragment;

        try
        {
            fragment = await provider.LookupAsync(session.Number, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested)
        {
            // Das Gespräch ist vorbei. Es gibt nichts zu melden.
            return;
        }
        catch (OperationCanceledException)
        {
            CallerContextLog.SourceTimedOut(_logger, provider.SourceId);

            fragment = new ContextFragment(
                provider.SourceId,
                provider.DisplayName,
                SourceState.Timeout,
                new Dictionary<string, ContextValue>(StringComparer.Ordinal),
                $"{provider.DisplayName} antwortet nicht.",
                Priority: provider.Priority);
        }
        catch (Exception ex)
        {
            CallerContextLog.SourceFailed(_logger, provider.SourceId, ex.GetType().Name);

            fragment = new ContextFragment(
                provider.SourceId,
                provider.DisplayName,
                SourceState.Error,
                new Dictionary<string, ContextValue>(StringComparer.Ordinal),
                $"{provider.DisplayName} ist nicht erreichbar.",
                Priority: provider.Priority);
        }

        // Ab hier läuft alles wieder auf dem UI-Thread — siehe RunOnUi.
        RunOnUi(() =>
        {
            // Nur behalten, was eine Antwort ist. Ein Fehler oder eine
            // Zeitüberschreitung gehört nicht in den Zwischenspeicher — sonst
            // bliebe eine kurze Störung fünf Minuten lang sichtbar.
            if (fragment.State is SourceState.Success or SourceState.Empty)
            {
                _cache.Set(provider.SourceId, session.Number, fragment, _config.Current.CallerLookup);
            }

            Apply(session, fragment);
        });
    }

    /// <summary>
    /// Führt aus, was den gemeinsamen Zustand anfasst — immer auf dem Thread,
    /// auf dem dieser Dienst gebaut wurde.
    ///
    /// <para><b>Warum das nötig ist.</b> Nach <c>ConfigureAwait(false)</c> läuft
    /// die Fortsetzung auf einem Threadpool-Thread. Von dort wurden bisher der
    /// Zwischenspeicher beschrieben und <c>session.Snapshot</c> gelesen und
    /// zurückgeschrieben, während der UI-Thread im selben Moment
    /// <c>TryGet</c> aufrief. Zwei Folgen, beide schlecht: bei zwei gleichzeitig
    /// antwortenden Quellen ging ein Fragment verloren — die Quelle blieb dann
    /// für immer „wird gefragt …". Und ein <c>Dictionary</c>, in das nebenläufig
    /// geschrieben wird, kann in .NET in eine Endlosschleife im Nachschlagen
    /// laufen. Das wäre hier nicht irgendein Thread, sondern der, der alle
    /// 20 ms <c>Core.Iterate()</c> bedient: das Telefon stünde, und niemand
    /// käme auf die Idee, den Anruferkontext dafür verantwortlich zu
    /// machen.</para>
    ///
    /// <para>Statt Sperren also zurück auf einen Thread — dieselbe Wahl wie
    /// bei <c>ContactSearchService</c>, und sie hält den Rest der Klasse
    /// einfach.</para>
    /// </summary>
    private void RunOnUi(Action action)
    {
        if (_ui is null || _ui == SynchronizationContext.Current)
        {
            Guarded(action);
            return;
        }

        _ui.Post(_ => Guarded(action), null);
    }

    /// <summary>
    /// Faengt, was Empfaenger des Kontexts werfen (ADR-053).
    ///
    /// <para><b>Warum das hier zaehlt.</b> An <c>ContextChanged</c> haengen
    /// Toast und ViewModels, und der Inhalt kommt aus einer <b>fremden</b>
    /// Quelle: ein Kartenausdruck aus einer importierten Anbietervorlage, eine
    /// Antwort, die anders aussieht als erwartet. Ohne diesen Faenger koennte
    /// eine fehlerhafte Vorlage nipp beim Anruf beenden statt nur ihre Karte
    /// leer zu lassen — und genau das ist der Unterschied zwischen einer
    /// kaputten Karte und einem kaputten Telefon (§21, Regel 1).</para>
    /// </summary>
    private void Guarded(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            CallerContextLog.ContextDeliveryFailed(_logger, ex.GetType().Name);
        }
    }

    private void Apply(Session session, ContextFragment fragment)
    {
        if (session.Cancellation.IsCancellationRequested)
        {
            return;
        }

        session.Snapshot = session.Snapshot.With(fragment);

        CallerContextLog.SourceAnswered(
            _logger,
            fragment.SourceId,
            fragment.State.ToString(),
            (int)fragment.Elapsed.TotalMilliseconds);

        Publish(session);
    }

    /// <summary>
    /// Vergisst einen beendeten Anruf und bricht ab, was noch läuft.
    ///
    /// <b>Der Schnappschuss bleibt bis hierher stehen</b>, damit die Ansicht
    /// beim Auflegen nicht mitten im Bild leer wird. Was danach kommt, gehört
    /// zum nächsten Anruf.
    /// </summary>
    private void Forget(CallHandle handle)
    {
        if (!_sessions.Remove(handle, out var session))
        {
            return;
        }

        session.Dispose();
    }

    private void Publish(Session session)
    {
        var snapshot = session.Snapshot;

        RunOnUi(() =>
        {
            try
            {
                ContextChanged?.Invoke(this, snapshot);
            }
            catch (Exception ex)
            {
                // Ein Empfänger, der wirft, darf die Anwendung nicht mitnehmen:
                // aus einem geposteten Rückruf heraus fängt sie niemand mehr,
                // und die Regel „Telefonieren hängt von keiner Integration ab"
                // (§21.2) gilt gerade auch für den Weg zurück in die Ansicht.
                CallerContextLog.PublishFailed(_logger, ex.GetType().Name);
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_started)
        {
            _sip.CallStateChanged -= OnCallStateChanged;
            _config.Changed -= OnConfigChanged;
        }

        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }

        _sessions.Clear();
    }
}
