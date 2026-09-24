using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Services.Telephony;

/// <summary>
/// Was ein Registrierungsereignis ändert — reine Auskunft, ohne Nebenwirkung.
/// </summary>
/// <param name="Identity">
/// Das zugeordnete Konto, oder <c>null</c>, wenn nipp keines dazu hat.
/// </param>
/// <param name="Settings">Die Einstellungen dieses Kontos, wenn bekannt.</param>
/// <param name="Message">
/// Die Meldung, die angezeigt gehört — bei einem Fehlschlag die erklärte
/// (§15), sonst die des SDK.
/// </param>
/// <param name="AccountsChanged">
/// Ob sich an der Kontoliste wirklich etwas geändert hat. <b>Die Gegenprobe
/// aus ADR-060:</b> ein <c>AccountsChanged</c> ist kein Hinweis, sondern ein
/// Auftrag — daran hängen vier Empfänger, und einer davon hat an einem Tag
/// mit wackelnder Anmeldung 727-mal die Einstellungen geschrieben.
/// </param>
/// <param name="IsStale">
/// Ob das Ereignis zu einem Konto gehört, das nipp nicht (mehr) kennt. Das
/// SDK stellt beim Start gespeicherte Konten aus <c>linphonerc</c> wieder her;
/// die scheitern noch einmal, nachdem sie längst ersetzt sind.
/// </param>
public sealed record RegistrationOutcome(
    string? Identity,
    SipAccountSettings? Settings,
    string? Message,
    bool AccountsChanged,
    bool IsStale);

/// <summary>
/// Die Konten und ihre Zustände (§20.2) — an einer Stelle und ohne SDK
/// (W2.1, Etappe B2).
///
/// <para><b>Was vorher war.</b> Drei Wörterbücher lagen in
/// <c>SipService</c> nebeneinander — Einstellungen, Zustände, Meldungen —,
/// und jede Stelle, die eines anfasste, musste an die beiden anderen denken.
/// Der Zustand eines Kontos war damit nur an einem laufenden SDK zu prüfen.</para>
///
/// <para><b>Was hier bewusst nicht steht:</b> der Zustand des
/// <i>Standardkontos</i>. Den liest <c>SipService</c> weiterhin am Kern des
/// SDK, und das hat einen Grund, der einen Tag gekostet hat: das SDK stellt
/// beim Start Konten aus <c>linphonerc</c> wieder her, und deren Abmeldung
/// trifft je nach Laufzeit <b>nach</b> der Anmeldung des neuen Kontos ein.
/// Wer dem letzten Ereignis glaubt, zeigt «abgemeldet», während nipp
/// registriert ist.</para>
/// </summary>
public sealed class AccountRegistry
{
    private readonly Dictionary<string, SipAccountSettings> _settings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RegistrationStatus> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _messages = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Wie viele Konten eingerichtet sind.</summary>
    public int Count => _settings.Count;

    /// <summary>Das Konto, über das ausgehend telefoniert wird.</summary>
    public string? DefaultIdentity { get; set; }

    /// <summary>Die eingerichteten Kennungen, in der Reihenfolge ihres Eintragens.</summary>
    public IReadOnlyCollection<string> Identities => _settings.Keys;

    public bool Contains(string identity) => _settings.ContainsKey(identity);

    public bool TryGet(string identity, out SipAccountSettings settings) =>
        _settings.TryGetValue(identity, out settings!);

    /// <summary>
    /// Ein Konto eintragen oder ersetzen. Sein Zustand beginnt bei
    /// <see cref="RegistrationStatus.InProgress"/> — <b>nicht</b> bei
    /// <c>None</c>: zwischen «noch nichts gehört» und «meldet sich gerade an»
    /// unterscheidet die Oberfläche, und beim Eintragen gilt das zweite.
    /// </summary>
    public void Set(SipAccountSettings account)
    {
        ArgumentNullException.ThrowIfNull(account);

        _settings[account.Identity] = account;
        _states[account.Identity] = RegistrationStatus.InProgress;
        _messages[account.Identity] = null;

        DefaultIdentity ??= account.Identity;
    }

    /// <summary>
    /// Ein Konto entfernen — mit allem, was daran hängt.
    ///
    /// <para><b>Und das Standardkonto rückt nach</b>, wenn es das entfernte
    /// war: ein Standardkonto, das es nicht mehr gibt, ist kein Standard,
    /// sondern eine leere Kontoauswahl.</para>
    /// </summary>
    public void Remove(string identity)
    {
        _settings.Remove(identity);
        _states.Remove(identity);
        _messages.Remove(identity);

        if (string.Equals(DefaultIdentity, identity, StringComparison.OrdinalIgnoreCase))
        {
            DefaultIdentity = _settings.Keys.FirstOrDefault();
        }
    }

    public void Clear()
    {
        _settings.Clear();
        _states.Clear();
        _messages.Clear();
        DefaultIdentity = null;
    }

    /// <summary>Der Zustand eines Kontos, oder <c>None</c>.</summary>
    public RegistrationStatus StatusOf(string identity) =>
        _states.TryGetValue(identity, out var status) ? status : RegistrationStatus.None;

    /// <summary>Die Kontoliste, wie die Oberfläche sie zeigt (§20.2).</summary>
    public IReadOnlyList<AccountStatus> Snapshot() =>
    [
        .. _settings.Values.Select(a => new AccountStatus(
            Identity: a.Identity,
            DisplayName: string.IsNullOrWhiteSpace(a.DisplayName) ? a.Username : a.DisplayName,
            Status: StatusOf(a.Identity),
            Message: _messages.TryGetValue(a.Identity, out var message) ? message : null,
            IsDefault: string.Equals(a.Identity, DefaultIdentity, StringComparison.OrdinalIgnoreCase))),
    ];

    /// <summary>
    /// Aus einer Anzeige-Identität wie <c>"nipp Testgeraet" &lt;sip:151@…&gt;</c>
    /// die eingetragene Kennung — oder <c>null</c>, wenn keine passt.
    ///
    /// <para>Das SDK liefert die Identität mit Anzeigenamen, die Schlüssel
    /// hier sind ohne.</para>
    /// </summary>
    public string? Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var start = raw.IndexOf('<', StringComparison.Ordinal);
        var end = raw.IndexOf('>', StringComparison.Ordinal);

        var candidate = start >= 0 && end > start
            ? raw[(start + 1)..end]
            : raw;

        return _settings.Keys.FirstOrDefault(k =>
            string.Equals(k, candidate, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ein Registrierungsereignis verbuchen und sagen, was daraus folgt.
    ///
    /// <para><b>Gemeldet wird nur eine echte Änderung</b> (ADR-060). Vorher
    /// löste jedes Ereignis ein <c>AccountsChanged</c> aus — auch die
    /// Erneuerung alle zehn Minuten, die nichts ändert. Das ist kein Hinweis,
    /// sondern ein Auftrag: vier Empfänger hängen daran.</para>
    /// </summary>
    /// <param name="rawIdentity">Die Identität, wie das SDK sie meldet.</param>
    /// <param name="status">Der gemeldete Zustand.</param>
    /// <param name="message">Der Text des SDK.</param>
    /// <param name="explain">
    /// Wie aus einem Fehlschlag eine Meldung wird, die sagt, was zu tun ist
    /// (§15) — als Funktion, damit diese Klasse den Fehlerkatalog nicht
    /// kennen muss.
    /// </param>
    public RegistrationOutcome Record(
        string rawIdentity,
        RegistrationStatus status,
        string? message,
        Func<string, SipAccountSettings, string> explain)
    {
        ArgumentNullException.ThrowIfNull(explain);

        var identity = Normalize(rawIdentity);

        if (identity is null || !_settings.TryGetValue(identity, out var settings))
        {
            // <b>Ein Konto, das nipp nicht kennt, ist kein Alarm.</b> Das SDK
            // stellt beim Start gespeicherte Konten aus linphonerc wieder her;
            // die werden gleich darauf ersetzt, scheitern dabei aber noch
            // einmal — für ein Konto, das es eine Sekunde später nicht mehr
            // gibt. Am Gerät sah man eine Lampe, die kurz rot wurde, und eine
            // Fehlermeldung zu einem Konto, das in der Oberfläche gar nicht
            // steht.
            return new RegistrationOutcome(
                Identity: null,
                Settings: null,
                Message: message,
                AccountsChanged: false,
                IsStale: status == RegistrationStatus.Failed);
        }

        var erklaert = status == RegistrationStatus.Failed
            ? explain(message ?? string.Empty, settings)
            : message;

        var vorherStatus = StatusOf(identity);
        var vorherMeldung = _messages.TryGetValue(identity, out var m) ? m : null;

        var geaendert = vorherStatus != status
            || !string.Equals(vorherMeldung, erklaert, StringComparison.Ordinal);

        _states[identity] = status;
        _messages[identity] = erklaert;

        return new RegistrationOutcome(
            Identity: identity,
            Settings: settings,
            Message: erklaert,
            AccountsChanged: geaendert,
            IsStale: false);
    }
}
