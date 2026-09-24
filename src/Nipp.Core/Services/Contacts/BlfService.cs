using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Services.Contacts;

/// <summary>
/// Das Besetztlampenfeld (§8.4, AP6.5).
///
/// Hält den zuletzt gemeldeten Präsenzzustand je Nebenstelle und sorgt dafür,
/// dass nur Team-Nebenstellen abonniert werden (§14.8). Der eigentliche
/// SUBSCRIBE liegt hinter <see cref="ISipService.WatchPresenceAsync"/> — hier
/// steht keine SDK-Zeile (§6).
///
/// <b>Warum die Auswahl hier getroffen wird und nicht im Telefoniedienst:</b>
/// der kennt nur Adressen, nicht ihre Herkunft. Ob eine Adresse zum Team
/// gehört oder aus einem Outlook-Adressbuch mit tausend Einträgen stammt,
/// weiss nur diese Schicht — und genau daran hängt, ob die Anlage die Last
/// verkraftet.
/// </summary>
public sealed class BlfService : IDisposable
{
    private readonly ISipService _sip;
    private readonly ContactStore _contacts;
    private readonly SettingsService _settings;
    private readonly ILogger<BlfService> _logger;

    private readonly ConcurrentDictionary<string, PresenceStatus> _states =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _disposed;

    public BlfService(
        ISipService sip,
        ContactStore contacts,
        SettingsService settings,
        ILogger<BlfService> logger)
    {
        _sip = sip;
        _contacts = contacts;
        _settings = settings;
        _logger = logger;

        _sip.PresenceChanged += OnPresenceChanged;

        // <b>Gehört wird, dass die Daten stehen — nicht, dass jemand
        // gespeichert hat</b> (24.09.2026). Bis dahin hing hier ein Abo auf
        // <c>SettingsService.Changed</c>, und das war einen Takt zu früh: an
        // demselben Ereignis hängt auch das ShellViewModel, und erst dessen
        // <c>ReloadTeam()</c> bringt den Speicher auf den neuen Stand. Wer
        // zuerst lief, entschied die Erzeugungsreihenfolge im Container.
        // Gemessen: nach einer geänderten SIP-Adresse stand «11 Nebenstellen
        // abonniert (0 neu)» statt zwölf, und erst die nächste Änderung holte
        // das Abo nach. Die Lampe blieb bis dahin still — sie zeigte
        // «unbekannt» und log damit nicht, sie sagte nur nichts.
        //
        // <c>TeamReloaded</c> feuert nach dem Umbau der Liste und auf dem
        // Thread, auf dem <c>ReloadTeam</c> gerufen wurde — in der Anwendung
        // der UI-Thread, und den will das SDK für die Abos (§6).
        _contacts.TeamReloaded += OnTeamReloaded;

        // Bewusst weiterhin KEIN Abo auf ContactStore.Changed: das meldet
        // einen ganzen Ladelauf und kommt vom Ladethread. Wer die Kontakte
        // lädt, synchronisiert danach ohnehin selbst — App.StartContacts über
        // den Dispatcher, ShellViewModel.ReloadContactsAsync schon auf dem
        // UI-Thread. Beides zusammen ergäbe zwei gleiche Zeilen im Protokoll
        // für denselben Vorgang.
    }

    /// <summary>Ein beobachteter Zustand hat sich geändert.</summary>
    public event EventHandler<PresenceEventArgs>? PresenceChanged;

    /// <summary>
    /// Der bekannte Zustand einer Nebenstelle. <see cref="PresenceStatus.Unknown"/>,
    /// solange die Anlage nichts gemeldet hat — das ist ein ehrlicher Zustand
    /// und wird als solcher angezeigt, nicht als „frei".
    /// </summary>
    public PresenceStatus StatusOf(string? sipAddress)
    {
        if (string.IsNullOrWhiteSpace(sipAddress))
        {
            return PresenceStatus.Unknown;
        }

        return _states.TryGetValue(SipUri.Normalize(sipAddress), out var status)
            ? status
            : PresenceStatus.Unknown;
    }

    /// <summary>
    /// Setzt die Abonnements neu auf — nach dem Start, nach einer
    /// Kontaktänderung und nach jedem Wechsel der Einstellungen.
    /// </summary>
    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.Current.Contacts.EnableBlf)
        {
            _states.Clear();
            await _sip.WatchPresenceAsync([], cancellationToken).ConfigureAwait(false);
            return;
        }

        // §14.8, die eine Zeile, auf die es ankommt: ausschliesslich Team.
        var addresses = _contacts.Contacts
            .Where(static c => c.Source == ContactSourceKind.Team)
            .Select(static c => c.SipAddress)
            .Where(static a => !string.IsNullOrWhiteSpace(a))
            .Select(static a => a!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Zustände von Nebenstellen vergessen, die nicht mehr beobachtet
        // werden — sonst zeigt die Liste einen Wert, den niemand mehr erneuert.
        foreach (var known in _states.Keys)
        {
            if (!addresses.Any(a => SipUri.Same(a, known)))
            {
                _states.TryRemove(known, out _);
            }
        }

        await _sip.WatchPresenceAsync(addresses, cancellationToken).ConfigureAwait(false);

        ContactLog.BlfSubscribed(_logger, addresses.Count);
    }

    private void OnPresenceChanged(object? sender, PresenceEventArgs e)
    {
        // Normalisiert ablegen: was das SDK meldet und was in den
        // Einstellungen steht, muss auf denselben Schluessel treffen — sonst
        // kommt der Zustand an und die Lampe bleibt trotzdem grau.
        _states[SipUri.Normalize(e.SipAddress)] = e.Presence;
        ContactLog.BlfPresence(_logger, e.Presence.ToString());

        PresenceChanged?.Invoke(this, e);
    }

    private void OnTeamReloaded(object? sender, EventArgs e) =>
        _ = SynchronizeSafelyAsync();

    /// <summary>
    /// Wie <see cref="SynchronizeAsync"/>, aber für Ereignishandler: ein
    /// <c>async void</c> mit einer Ausnahme darin reisst den Prozess ab.
    /// </summary>
    private async Task SynchronizeSafelyAsync()
    {
        try
        {
            await SynchronizeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ContactLog.BlfFailed(_logger, ex.Message);
        }
    }

    /// <summary>
    /// Text zum Zustand. §8.4: die Anzeige erfolgt farblich <b>und</b> als
    /// Text — nie nur über Farbe. Das ist Barrierefreiheit, keine Stilfrage,
    /// und deshalb steht der Text hier und nicht in einem Konverter, den man
    /// vergessen kann.
    /// </summary>
    public static string Describe(PresenceStatus status) => status switch
    {
        PresenceStatus.Available => "frei",
        PresenceStatus.Ringing => "klingelt",
        PresenceStatus.OnCall => "im Gespräch",
        PresenceStatus.Away => "abwesend",
        PresenceStatus.Offline => "offline",
        _ => "unbekannt",
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _sip.PresenceChanged -= OnPresenceChanged;
        _contacts.TeamReloaded -= OnTeamReloaded;
    }
}
