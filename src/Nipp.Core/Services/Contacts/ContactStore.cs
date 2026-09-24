using Microsoft.Extensions.Logging;
using Nipp.Core.Services.Settings;

namespace Nipp.Core.Services.Contacts;

/// <summary>
/// Alle Kontakte an einer Stelle, mit Zwischenspeicher (AP6.3, AP6.6).
///
/// Der Zwischenspeicher ist kein Feinschliff, sondern der Grund, warum die
/// Oberfläche nicht stehen bleibt: Outlook über COM zu lesen dauert bei einem
/// grossen Adressbuch mehrere Sekunden. Deshalb gilt hier:
/// <list type="bullet">
///   <item><see cref="Contacts"/> antwortet sofort mit dem, was da ist.</item>
///   <item><see cref="RefreshAsync"/> lädt im Hintergrund nach und meldet sich, wenn fertig.</item>
///   <item>Team-Nebenstellen sind immer sofort da — sie kommen aus den Einstellungen.</item>
/// </list>
/// </summary>
public sealed class ContactStore : IDisposable
{
    private readonly IReadOnlyList<IContactSource> _sources;

    /// <summary>
    /// Warum keine Outlook-Kontakte da sind, als fertiger Satz — oder
    /// <c>null</c>, wenn es keinen Grund gibt.
    ///
    /// <para>§8.4: „Outlook muss laufen (sonst verstaendliche Anzeige statt
    /// leerer Liste)". Der Zustand endete bisher im Protokoll; die Oberflaeche
    /// zeigte nur «Outlook (0)», und das sieht aus wie ein Defekt.</para>
    /// </summary>
    public string? OutlookHint { get; private set; }
    private readonly SettingsService _settings;
    private readonly ILogger<ContactStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IReadOnlyList<Contact> _contacts = [];
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;
    private bool _disposed;

    public ContactStore(
        IEnumerable<IContactSource> sources,
        SettingsService settings,
        ILogger<ContactStore> logger)
    {
        _sources = [.. sources];
        _settings = settings;
        _logger = logger;

        // W2.2 (Befund B22): den UI-Kontext HIER einfangen, am Ereignis und
        // nicht beim Abonnenten.
        //
        // <b>Der Befund.</b> Changed und LoadingChanged feuern nach einem
        // «await … ConfigureAwait(false)», also vom Threadpool. ShellViewModel
        // marshallte selbst — richtig, aber die Regel stand nirgends, und
        // jeder neue Abonnent musste sie kennen. In WinUI ist ein
        // OnPropertyChanged vom falschen Thread kein Fehlverhalten, sondern
        // ein Absturz; genau so ist der UpdateService am 08.09.2026
        // aufgefallen, und dort war es dieselbe Ursache.
        //
        // Der Konstruktor laeuft beim Aufbau der Dienste auf dem UI-Thread.
        _ui = SynchronizationContext.Current;

        // Wird eine Quelle nach ihrer Zeitgrenze doch noch fertig, holen wir
        // das Ergebnis ab — sonst bliebe die Liste leer, obwohl alles da ist.
        foreach (var late in _sources.OfType<OutlookContactSource>())
        {
            late.LateResultAvailable += (_, _) => _ = RefreshAfterLateResultAsync();
        }
    }

    /// <summary>
    /// Laedt neu, nachdem eine Quelle verspaetet geliefert hat. Der Aufruf
    /// kostet nichts: die Daten liegen in der Quelle bereit.
    /// </summary>
    private async Task RefreshAfterLateResultAsync()
    {
        try
        {
            await RefreshAsync(force: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ContactLog.OutlookFailed(_logger, ex.Message);
        }
    }

    /// <summary>Wird ausgelöst, wenn sich die Liste geändert hat — auf dem Ladethread, nicht im UI.</summary>
    public event EventHandler<IReadOnlyList<Contact>>? Changed;

    /// <summary>
    /// Der Team-Block ist neu aus den Einstellungen gebaut worden.
    ///
    /// <para><b>Wofür es das gibt</b> (24.09.2026, gemessen). Wer eine
    /// SIP-Adresse einer Nebenstelle änderte, hatte danach eine Lampe, die
    /// nichts meldete: im Protokoll stand «11 Nebenstellen abonniert (0 neu)»
    /// statt zwölf, und erst die <b>nächste</b> Änderung holte das Abo nach.
    /// <see cref="BlfService"/> und <see cref="ShellViewModel"/> hingen beide
    /// an <c>SettingsService.Changed</c> — der Dienst lief zuerst und zählte
    /// einen Speicher, den erst das ViewModel über <see cref="ReloadTeam"/>
    /// aktualisierte. <b>Was ein Dienst zu sehen bekam, entschied damit die
    /// Erzeugungsreihenfolge im Container</b>, und die steht nirgends.</para>
    ///
    /// <para>Dieses Ereignis dreht das um: es meldet nicht, <i>dass jemand
    /// gespeichert hat</i>, sondern <b>dass die Daten stehen</b>. Wer darauf
    /// hört, liest nie einen halben Stand.</para>
    ///
    /// <para><b>Es ersetzt <see cref="Changed"/> nicht</b>: das meldet einen
    /// ganzen Ladelauf samt Outlook und externen Quellen und kommt vom
    /// Ladethread. Dieses hier kostet nichts und feuert dort, wo
    /// <see cref="ReloadTeam"/> gerufen wird — in der Anwendung auf dem
    /// UI-Thread, und das ist genau der, den das SDK für die Abos will (§6).</para>
    /// </summary>
    public event EventHandler? TeamReloaded;

    /// <summary>
    /// Meldet Beginn und Ende eines Ladelaufs, damit die Oberfläche einen
    /// Fortschritt zeigen kann. Outlook über COM zu lesen dauert Sekunden; ein
    /// stehender Text ohne Bewegung sieht in dieser Zeit nach hängender App aus.
    /// </summary>
    public event EventHandler<bool>? LoadingChanged;

    /// <summary>Ob gerade geladen wird.</summary>
    public bool IsLoading { get; private set; }

    /// <summary>Was gerade bekannt ist. Nie <c>null</c>, notfalls leer.</summary>
    public IReadOnlyList<Contact> Contacts => _contacts;

    /// <summary>Wann zuletzt geladen wurde — für die Anzeige „Stand von …".</summary>
    public DateTimeOffset LoadedAt => _loadedAt;

    /// <summary>Ob der Zwischenspeicher abgelaufen ist (AP6.3: zwölf Stunden).</summary>
    public bool IsStale =>
        DateTimeOffset.UtcNow - _loadedAt
            > TimeSpan.FromHours(Math.Max(1, _settings.Current.Contacts.OutlookCacheHours));

    /// <summary>
    /// Lädt neu, wenn nötig. <paramref name="force"/> übergeht den
    /// Zwischenspeicher — das tut die Schaltfläche „Aktualisieren".
    /// </summary>
    /// <summary>Der UI-Kontext, auf dem die Ereignisse ankommen sollen (W2.2).</summary>
    private readonly SynchronizationContext? _ui;

    /// <summary>
    /// Meldet auf dem UI-Thread — und faengt, was ein Empfaenger wirft
    /// (W2.2, ADR-053).
    ///
    /// <para><b>Post und nicht Send:</b> der Ladelauf soll nicht darauf
    /// warten, dass die Oberflaeche fertig gezeichnet hat. Ohne
    /// SynchronizationContext — in Tests — wird sofort gerufen.</para>
    /// </summary>
    private void Melde(Action was)
    {
        if (_ui is null || _ui == SynchronizationContext.Current)
        {
            Gefangen(was);
            return;
        }

        _ui.Post(_ => Gefangen(was), null);
    }

    private void Gefangen(Action was)
    {
        try
        {
            was();
        }
        catch (Exception ex)
        {
            ContactLog.NotifyFailed(_logger, ex.GetType().Name);
        }
    }

    public async Task RefreshAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if (!force && !IsStale && _contacts.Count > 0)
        {
            return;
        }

        // Nur ein Ladelauf gleichzeitig: zwei parallele COM-Durchläufe durch
        // Outlook sind langsamer als einer und können sich gegenseitig
        // blockieren.
        if (!await _gate.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            IsLoading = true;
            Melde(() => LoadingChanged?.Invoke(this, true));

            var useOutlook = _settings.Current.Contacts.UseOutlook;
            var collected = new List<Contact>();

            foreach (var source in _sources)
            {
                if (source.Kind == ContactSourceKind.Outlook && !useOutlook)
                {
                    continue;
                }

                // Die IsAvailable-Pruefung stand hier und uebersprang die
                // Quelle stillschweigend. Damit kam die Outlook-Quelle nie
                // dazu, ihren Grund zu nennen — und §8.4 verlangt genau den
                // („sonst verstaendliche Anzeige statt leerer Liste"). Sie
                // prueft es selbst und kehrt dann sofort zurueck; das kostet
                // nichts.
                var loaded = await source.LoadAsync(cancellationToken).ConfigureAwait(false);
                collected.AddRange(loaded);
            }

            _contacts = Sort(collected);
            _loadedAt = DateTimeOffset.UtcNow;
            OutlookHint = _sources.OfType<OutlookContactSource>().FirstOrDefault()?.StatusHint;

            ContactLog.StoreRefreshed(_logger, _contacts.Count);
            Melde(() => Changed?.Invoke(this, _contacts));
        }
        finally
        {
            IsLoading = false;
            Melde(() => LoadingChanged?.Invoke(this, false));

            _gate.Release();
        }
    }

    /// <summary>
    /// Sucht über Namen, Firma und Nummern. Bei den Nummern werden Trennzeichen
    /// ignoriert — wer „0445128430" eintippt, findet auch „044 512 84 30".
    /// </summary>
    public IReadOnlyList<Contact> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return _contacts;
        }

        var needle = query.Trim();
        var digits = ClipResolver.DigitsOnly(needle);

        return [.. _contacts.Where(contact =>
            contact.DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || (contact.Company?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false)
            || (digits.Length > 0 && contact.Numbers.Any(
                n => ClipResolver.DigitsOnly(n.Number).Contains(digits, StringComparison.Ordinal))))];
    }

    /// <summary>
    /// Tauscht den Team-Block aus, nachdem der Benutzer in der Liste verschoben
    /// hat (§8.4, ADR-042).
    ///
    /// <para><b>Kein Ladelauf und kein <see cref="Changed"/>.</b> Die Oberfläche
    /// hat die neue Ordnung längst vor sich — die Liste hat sie selbst
    /// hergestellt. Ein Neuaufbau mitten im Ziehen nähme ihr die Zeilen unter
    /// den Fingern weg. Hier wird nur nachgezogen, damit die nächste
    /// Aktualisierung nicht den alten Stand zurückbringt.</para>
    ///
    /// <para><b>Ganze Kontakte statt einer Reihenfolge von Kennungen</b>, und
    /// das ist seit den Gruppen kein Feinschliff: <c>Contact.Group</c> ist
    /// unveränderlich. Nach einem Gruppenwechsel <i>muss</i> der Kontakt neu
    /// entstehen — sonst liest die Gruppensicht weiter die alte Gruppe und
    /// schiebt die Zeile zurück, wo sie hergekommen ist.</para>
    ///
    /// <para>Outlook und die externen Quellen bleiben unberührt.</para>
    /// </summary>
    public void ReplaceTeam(IReadOnlyList<Contact> team)
    {
        ArgumentNullException.ThrowIfNull(team);

        _contacts = [.. team, .. _contacts.Where(static c => c.Source != ContactSourceKind.Team)];
    }

    /// <summary>
    /// Baut den Team-Block aus den Einstellungen neu — <b>still, ohne
    /// <see cref="Changed"/></b>.
    ///
    /// <para><b>Wofür das gebraucht wird:</b> eine Gruppe anlegen, umbenennen
    /// oder entfernen ändert keine einzige Nummer, und trotzdem muss die Liste
    /// es zeigen. Bis zum 12.09.2026 half dagegen nur ein Neustart — der
    /// Neuaufbau hing an einer Kontaktänderung, die es bei einer leeren Gruppe
    /// nie gibt.</para>
    ///
    /// <para><b>Kein COM, kein Netz, kein SDK.</b> Die Nebenstellen stehen in
    /// den Einstellungen; das hier ist ein Lauf über eine Liste im Speicher und
    /// darf deshalb an einem Ereignis hängen, das ohnehin schon läuft.</para>
    /// </summary>
    public void ReloadTeam()
    {
        var contacts = _settings.Current.Contacts;

        ReplaceTeam(TeamContactSource.Build(contacts.Team, contacts.Groups));

        // Erst die Daten, dann die Meldung — in dieser Reihenfolge, sonst
        // wäre nichts gewonnen. Über Melde, weil ein Empfänger (das
        // Besetztlampenfeld) ins SDK ruft und das den UI-Thread will.
        Melde(() => TeamReloaded?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>
    /// Team zuerst, dann Outlook. §8.4 verlangt die Quellen getrennt sichtbar —
    /// die Sortierung liefert die Gruppen schon in der richtigen Reihenfolge.
    ///
    /// <b>Team wird nicht alphabetisch sortiert.</b> Seine Reihenfolge steht in
    /// den Einstellungen und ist eine Entscheidung des Benutzers: wer seine drei
    /// wichtigsten Nebenstellen nach oben zieht, will sie oben haben und nicht
    /// unter „A". <see cref="TeamContactSource"/> liefert sie in genau dieser
    /// Reihenfolge; hier bleibt sie nur erhalten — <c>Where</c> ist stabil, das
    /// ist die ganze Zusicherung, auf der das ruht. Outlook bleibt alphabetisch,
    /// dort gibt es nichts zu entscheiden, nur etwas zu finden.
    ///
    /// Käme je eine zweite Team-Quelle dazu, hinge die Reihenfolge innerhalb des
    /// Blocks an der Reihenfolge der Quellen. Dann braucht es hier eine
    /// Rangkarte über <see cref="TeamContactSource.IdOf"/>. Heute gibt es genau
    /// eine, und Vorratsbau wäre nur eine weitere Stelle zum Irren.
    /// </summary>
    private static IReadOnlyList<Contact> Sort(IReadOnlyList<Contact> contacts) =>
    [
        .. contacts.Where(static c => c.Source == ContactSourceKind.Team),
        .. contacts.Where(static c => c.Source != ContactSourceKind.Team)
            .OrderBy(static c => c.Source)
            .ThenBy(static c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase),
    ];

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }
}
