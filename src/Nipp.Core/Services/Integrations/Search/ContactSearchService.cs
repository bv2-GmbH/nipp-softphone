using Microsoft.Extensions.Logging;
using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Integrations.Config;

namespace Nipp.Core.Services.Integrations.Search;

/// <summary>
/// Ein Zwischenstand der Suche, wie ihn die Oberfläche bekommt.
/// </summary>
/// <param name="Generation">
/// Zu welcher Eingabe dieses Ergebnis gehört. Die Oberfläche verwirft alles,
/// was nicht zur laufenden Generation passt — siehe
/// <see cref="ContactSearchService"/>.
/// </param>
/// <param name="Query">Der Suchtext, zu dem das gehört.</param>
/// <param name="Contacts">Alle bisher bekannten Treffer, in Anzeigereihenfolge.</param>
/// <param name="Sources">Der Zustand jeder befragten Quelle.</param>
/// <param name="IsComplete">Ob keine Quelle mehr aussteht.</param>
public sealed record ContactSearchSnapshot(
    long Generation,
    string Query,
    IReadOnlyList<Contact> Contacts,
    IReadOnlyList<ContactSourceState> Sources,
    bool IsComplete);

/// <summary>Was eine Quelle gerade tut oder getan hat.</summary>
/// <param name="SourceId">Kennung der Quelle.</param>
/// <param name="DisplayName">Wie sie in der Oberfläche heisst.</param>
/// <param name="State">Ihr Zustand.</param>
/// <param name="Count">Wie viele Treffer sie beigesteuert hat.</param>
/// <param name="Message">Was zu sagen ist, oder <c>null</c>.</param>
public sealed record ContactSourceState(
    string SourceId,
    string DisplayName,
    SearchState State,
    int Count,
    string? Message);

/// <summary>
/// Sucht Kontakte über alle Quellen gleichzeitig (§21.1).
///
/// <b>Vier Dinge macht diese Klasse, und jedes davon löst ein Problem, das
/// sonst beim Kunden auftritt:</b>
///
/// <list type="number">
///   <item>
///     <b>Debounce.</b> Ohne ihn stellte jeder Tastendruck eine Anfrage an
///     jedes fremde System. „Hans Muster" wären elf Anfragen je Quelle.
///   </item>
///   <item>
///     <b>Generationen.</b> Der Fall aus dem Auftrag: Suche „Hans" startet
///     Anfrage A, Suche „Hansi" startet B, und A kommt <b>nach</b> B zurück.
///     Ohne Generationszähler überschriebe das alte Ergebnis das neue, und
///     in der Liste stünden Treffer zu einem Text, der nicht mehr im Feld
///     steht. Jede Fortsetzung prüft ihre Generation und verwirft sich
///     selbst, wenn sie überholt ist.
///   </item>
///   <item>
///     <b>Abbruch.</b> Überholte Anfragen werden abgebrochen, statt zu Ende
///     zu laufen — sie kosten sonst Bandbreite und Last beim Kunden.
///   </item>
///   <item>
///     <b>Fortlaufende Ergebnisse.</b> Lokale Treffer erscheinen sofort,
///     fremde kommen dazu, sobald sie da sind. Eine langsame Quelle hält
///     keine schnelle auf (§21.2).
///   </item>
/// </list>
///
/// <b>Threading.</b> Die Klasse merkt sich beim Bau den
/// <c>SynchronizationContext</c> — den UI-Thread — und meldet ihre Ergebnisse
/// dorthin zurück. Dasselbe Muster wie in <c>ShellViewModel</c>, und aus
/// demselben Grund: eine <c>ObservableCollection</c> von einem anderen Thread
/// zu ändern wirft eine COMException ohne Meldung. <c>Nipp.Core</c> bleibt
/// dabei ohne UI-Abhängigkeit (§6).
/// </summary>
public sealed class ContactSearchService : IDisposable
{
    /// <summary>
    /// Woher die Quellen kommen.
    ///
    /// <b>Bei jeder Suche neu gefragt</b>, nicht einmal beim Start
    /// eingesammelt: eine Quelle kann über die Einstellungen oder ein
    /// Provisionierungsprofil dazukommen, und ein Neustart als Bedingung wäre
    /// für einen Administrator, der gerade eine Anbindung einrichtet, die
    /// falsche Antwort.
    /// </summary>
    private readonly ISearchProviderRegistry _registry;

    private readonly IContactMerger _merger;
    private readonly IntegrationConfigStore _config;
    private readonly ILogger<ContactSearchService> _logger;
    private readonly TimeProvider _time;

    /// <summary>
    /// Der Thread, dem die Ergebnisse gehören. Siehe Klassenkommentar.
    /// </summary>
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;

    /// <summary>
    /// Welche Eingabe gerade gilt. Wird nur auf dem UI-Thread verändert —
    /// deshalb braucht es kein Schloss.
    /// </summary>
    private long _generation;

    private CancellationTokenSource? _running;
    private bool _disposed;

    public ContactSearchService(
        ISearchProviderRegistry registry,
        IContactMerger merger,
        IntegrationConfigStore config,
        ILogger<ContactSearchService> logger,
        TimeProvider? time = null)
    {
        _registry = registry;
        _merger = merger;
        _config = config;
        _logger = logger;

        // Über den Konstruktor steuerbar, damit ein Test das Debounce prüfen
        // kann, ohne 300 ms zu warten — und ohne dass die Prüfung von der
        // Auslastung der Maschine abhängt.
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Ein neuer Zwischenstand liegt vor. Wird auf dem Thread ausgelöst, auf
    /// dem dieser Dienst gebaut wurde.
    /// </summary>
    public event EventHandler<ContactSearchSnapshot>? ResultsChanged;

    /// <summary>Die Generation, die gerade gilt.</summary>
    public long CurrentGeneration => _generation;

    /// <summary>Ob es überhaupt eine Quelle gibt, die über das Netz sucht.</summary>
    public bool HasRemoteSources =>
        _registry.SearchProviders.Any(static p => !p.Traits.IsLocal);

    /// <summary>
    /// Die Namen der Quellen, die über das Netz suchen — für den Platzhalter
    /// des Suchfelds.
    ///
    /// <para>Der Platzhalter hiess „Kontakte suchen" und sagte damit nicht, was
    /// dieses Feld kann und die Vorschlagsliste über dem Nummernfeld nicht:
    /// fremde Systeme fragen. Zwei Felder mit verschiedener Reichweite, und
    /// nichts erklärte den Unterschied (ADR-025).</para>
    ///
    /// <para>Aus den eingerichteten Quellen gebaut und nicht fest eingetragen:
    /// welche es gibt, entscheidet die Konfiguration je Kunde (§21.3).</para>
    /// </summary>
    public IReadOnlyList<string> RemoteSourceNames =>
        [.. _registry.SearchProviders
            .Where(static p => !p.Traits.IsLocal)
            .Select(static p => p.DisplayName)
            .Where(static n => !string.IsNullOrWhiteSpace(n))];

    /// <summary>
    /// Startet eine Suche. Kehrt sofort zurück; die Ergebnisse kommen über
    /// <see cref="ResultsChanged"/>.
    ///
    /// Ein Aufruf macht jede vorherige Suche ungültig — auch eine, die noch
    /// läuft.
    /// </summary>
    public void Search(string? text)
    {
        var query = (text ?? string.Empty).Trim();
        var settings = _config.Current.ContactSearch;

        // Die alte Suche zuerst ungültig machen, dann abbrechen: zwischen
        // beidem darf kein Ergebnis durchrutschen.
        var generation = ++_generation;

        _running?.Cancel();
        _running?.Dispose();
        _running = null;

        if (query.Length < Math.Max(1, settings.MinQueryLength))
        {
            // Zu kurz ist kein Fehler, sondern der Anfang jeder Eingabe. Die
            // Liste wird geleert, damit nicht die Treffer von vorhin
            // stehenbleiben.
            Publish(new ContactSearchSnapshot(generation, query, [], [], IsComplete: true));
            return;
        }

        var source = new CancellationTokenSource();
        _running = source;

        _ = RunAsync(generation, query, settings, source.Token);
    }

    /// <summary>Bricht eine laufende Suche ab und leert die Anzeige.</summary>
    public void Clear() => Search(null);

    private async Task RunAsync(
        long generation,
        string query,
        ContactSearchSettings settings,
        CancellationToken cancellationToken)
    {
        var results = new List<Contact>();
        var states = new Dictionary<string, ContactSourceState>(StringComparer.Ordinal);

        try
        {
            // Die Quellen einmal je Suche holen — sie können sich zwischen
            // zwei Suchen geändert haben, aber nicht mitten in einer.
            var providers = _registry.SearchProviders;

            var local = providers.Where(static p => p.Traits.IsLocal).ToList();
            var remote = providers.Where(static p => !p.Traits.IsLocal).ToList();

            // Lokale Quellen sofort: ihre Antwort liegt im Arbeitsspeicher,
            // und auf sie zu warten wäre Verzögerung ohne Gegenwert.
            foreach (var provider in local)
            {
                var page = await AskAsync(provider, query, settings, cancellationToken)
                    .ConfigureAwait(false);

                Take(results, states, provider, page, settings);
            }

            if (remote.Count == 0)
            {
                Publish(Snapshot(generation, query, results, states, complete: true));
                return;
            }

            // Erst die lokalen Treffer zeigen, dann warten. Wer tippt, sieht
            // sofort etwas.
            foreach (var provider in remote)
            {
                states[provider.SourceId] = new ContactSourceState(
                    provider.SourceId,
                    provider.DisplayName,
                    SearchState.Loading,
                    Count: 0,
                    Message: null);
            }

            Publish(Snapshot(generation, query, results, states, complete: false));

            // Jetzt das Debounce — und zwar nur für die Quellen, die es
            // braucht. Ein abgebrochenes Warten ist der Normalfall beim
            // Tippen und kein Fehler.
            await Task.Delay(
                TimeSpan.FromMilliseconds(Math.Max(0, settings.DebounceMs)),
                _time,
                cancellationToken).ConfigureAwait(false);

            var running = remote
                .Select(provider => AskAsync(provider, query, settings, cancellationToken)
                    .ContinueWith(
                        task => (Provider: provider, Page: task.Result),
                        cancellationToken,
                        TaskContinuationOptions.OnlyOnRanToCompletion,
                        TaskScheduler.Default))
                .ToList();

            // Jede Quelle wird angezeigt, sobald sie da ist — nicht erst,
            // wenn alle fertig sind (§21.2: eine langsame Quelle hält keine
            // schnelle auf).
            while (running.Count > 0)
            {
                var finished = await Task.WhenAny(running).ConfigureAwait(false);
                running.Remove(finished);

                var (provider, page) = await finished.ConfigureAwait(false);

                Take(results, states, provider, page, settings);
                Publish(Snapshot(generation, query, results, states, complete: running.Count == 0));
            }
        }
        catch (OperationCanceledException)
        {
            // Überholt: eine neue Eingabe hat diese Suche abgelöst. Es gibt
            // nichts zu melden — die neue Generation zeigt ihre eigenen
            // Ergebnisse.
        }
        catch (Exception ex)
        {
            // Eine Suche darf nichts kosten ausser sich selbst.
            ContactSearchLog.SearchFailed(_logger, ex.GetType().Name);
        }
    }

    /// <summary>
    /// Fragt eine Quelle. Wirft nur bei Abbruch — alles andere wird zu einer
    /// Seite mit Zustand und Meldung.
    /// </summary>
    private async Task<ContactSearchPage> AskAsync(
        IContactSearchProvider provider,
        string query,
        ContactSearchSettings settings,
        CancellationToken cancellationToken)
    {
        if (query.Length < provider.Traits.MinQueryLength)
        {
            return new ContactSearchPage(provider.SourceId, SearchState.Skipped, []);
        }

        // Eigene Zeitgrenze je Quelle, verbunden mit dem Abbruch von aussen —
        // dasselbe Muster wie im HTTP-Zugang, und aus demselben Grund: nur so
        // lässt sich „zu langsam" von „überholt" unterscheiden.
        //
        // <b>Sie laeuft gegen die des HTTP-Zugangs</b>, denn
        // HttpContactSearchProvider reicht dasselbe Traits.Timeout eine Ebene
        // tiefer weiter — zwei Zeitgrenzen aus derselben Zahl, und diese hier
        // gewinnt, weil sie frueher startet. Unten sah das aus wie ein Abbruch
        // von aussen, und genau dieser Fall schweigt (Befund A1-8).
        //
        // <b>Aufgeloest wird das unten und nicht hier:</b> der HTTP-Zugang
        // prueft jetzt zuerst seine eigene Zeitgrenze. Ein Aufschlag an dieser
        // Stelle waere der naheliegende, aber falsche Griff gewesen — er
        // verlaengert die Wartezeit fuer jeden Provider, der seine Zeitgrenze
        // NICHT selbst durchsetzt, und fuer die ist diese hier die einzige.
        using var timeout = provider.Traits.Timeout is { } limit
            ? new CancellationTokenSource(limit)
            : new CancellationTokenSource();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);

        try
        {
            return await provider.SearchAsync(
                new ContactQuery(query, Math.Max(1, settings.ResultLimitPerSource)),
                linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            ContactSearchLog.SourceTimedOut(_logger, provider.SourceId);

            return new ContactSearchPage(
                provider.SourceId,
                SearchState.Timeout,
                [],
                $"{provider.DisplayName} antwortet nicht.");
        }
        catch (Exception ex)
        {
            ContactSearchLog.SourceFailed(_logger, provider.SourceId, ex.GetType().Name);

            return new ContactSearchPage(
                provider.SourceId,
                SearchState.Error,
                [],
                $"{provider.DisplayName} ist nicht erreichbar.");
        }
    }

    /// <summary>
    /// Nimmt die Treffer einer Quelle auf und führt zusammen, was
    /// nachweislich dieselbe Person ist (§21.4).
    ///
    /// <b>Bestehende Einträge behalten ihren Platz</b>, neue kommen hinten
    /// dazu. Die Reihenfolge bleibt damit stabil, während weitere Quellen
    /// antworten — eine Liste, die sich unter dem Zeiger umsortiert, ruft die
    /// falsche Person an.
    ///
    /// <b>Gezählt wird, was die Quelle geliefert hat</b>, nicht was neu
    /// dazukam: „CRM · 12 Treffer" ist die Aussage über das CRM, auch wenn
    /// zehn davon mit Outlook-Einträgen verschmolzen sind.
    /// </summary>
    private void Take(
        List<Contact> results,
        Dictionary<string, ContactSourceState> states,
        IContactSearchProvider provider,
        ContactSearchPage page,
        ContactSearchSettings settings)
    {
        if (page.Contacts.Count > 0)
        {
            var merged = _merger.Merge(results, page.Contacts, settings.Merge);

            results.Clear();
            results.AddRange(merged.Take(Math.Max(1, settings.TotalLimit)));
        }

        states[provider.SourceId] = new ContactSourceState(
            provider.SourceId,
            provider.DisplayName,
            page.State,
            page.Contacts.Count,
            page.Message);
    }

    private static ContactSearchSnapshot Snapshot(
        long generation,
        string query,
        List<Contact> results,
        Dictionary<string, ContactSourceState> states,
        bool complete) =>
        new(generation, query, [.. results], [.. states.Values], complete);

    /// <summary>
    /// Meldet einen Zwischenstand — auf dem Thread, dem die Sammlungen
    /// gehören, und nur wenn er noch gilt.
    ///
    /// <b>Die Generation wird zweimal geprüft</b>: hier und beim Empfänger.
    /// Zwischen dem Abschicken und dem Ausführen auf dem UI-Thread kann eine
    /// neue Eingabe liegen.
    /// </summary>
    private void Publish(ContactSearchSnapshot snapshot)
    {
        if (snapshot.Generation != _generation)
        {
            return;
        }

        if (_ui is null || _ui == SynchronizationContext.Current)
        {
            Raise(snapshot);
            return;
        }

        _ui.Post(_ => Raise(snapshot), null);
    }

    private void Raise(ContactSearchSnapshot snapshot)
    {
        if (snapshot.Generation == _generation)
        {
            ResultsChanged?.Invoke(this, snapshot);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _running?.Cancel();
        _running?.Dispose();
        _running = null;
    }
}
