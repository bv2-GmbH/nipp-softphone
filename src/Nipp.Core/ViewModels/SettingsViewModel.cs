using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;
using Nipp.Core.Services.Updates;
using Nipp.Core.Services.Windows;

namespace Nipp.Core.ViewModels;

/// <summary>
/// Die Einstellungen (AP5.4, §9, §20).
///
/// <para><b>Jede Änderung wirkt sofort</b> (ADR-045). Hier stand bis zum
/// 12.09.2026 das Gegenteil — „arbeitet auf einer Kopie, Änderungen wirken
/// erst beim Speichern", mit der Begründung, ein Fehlgriff solle folgenlos
/// bleiben. Die Begründung war nachvollziehbar und das Ergebnis trotzdem
/// falsch: <b>ein Konto wurde beim Klick auf «Hinzufügen» sofort geschrieben,
/// eine Nebenstelle nicht.</b> Wer eine Nebenstelle anlegte, sah sie in der
/// Liste stehen — das ist die Rückmeldung, die man erwartet, und sie war hier
/// keine Zusage. Ein Klick auf den Rückweg, und die Eingabe war weg, ohne
/// Meldung.</para>
///
/// <para>Zwei Modelle nebeneinander sind unlernbar: die eine Erfahrung
/// widerlegt die andere. Und der Schaden lag nicht beim Verklicken, sondern
/// beim Vergessen des Knopfes.</para>
///
/// <para><b>Wann geschrieben wird.</b> Schalter, Auswahllisten, Regler und
/// Sammlungen wirken mit der Änderung — dort ist ein Fehlgriff sichtbar und
/// mit einem Klick zurückgenommen. Freitext- und Zahlenfelder wirken erst
/// beim Verlassen des Feldes (<see cref="ApplyEdits"/>): sonst hätte jeder
/// Tastendruck eine halbfertige Eingabe geprüft und beanstandet.</para>
///
/// <para>Geschrieben wird nur, was <see cref="Validate"/> durchlässt. Schlägt
/// die Prüfung fehl, steht die Beanstandung in <see cref="Error"/> und die
/// Datei bleibt, wie sie war.</para>
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settings;
    private readonly SecretStore _secrets;
    private readonly PolicyService _policy;
    private readonly ISipService _sip;
    private readonly GlobalHotkeyService _hotkeys;
    private readonly UpdateService _updates;
    private readonly ILogger<SettingsViewModel> _logger;
    private bool _disposed;

    /// <summary>
    /// Solange gesetzt, schreibt keine Änderung (ADR-045).
    ///
    /// <para>Gilt beim Füllen der Felder aus der Datei und beim Zurücksetzen:
    /// dort ändern sich vierzig Eigenschaften hintereinander, und jede davon
    /// würde sonst eine Datei schreiben, die sie gerade erst gelesen hat.</para>
    /// </summary>
    private bool _suspendApply;

    /// <summary>
    /// Eigenschaften, die nichts einstellen — Formularfelder, Meldungen,
    /// Zustände der Oberfläche.
    ///
    /// <para><b>Eine Sperrliste und keine Erlaubnisliste</b>, und das mit
    /// Absicht: wer hier eine Eigenschaft vergisst, bekommt eine überflüssige
    /// Schreiboperation mit demselben Inhalt. Bei einer Erlaubnisliste wäre
    /// das Vergessen ein stiller Datenverlust — also genau der Fehler, den
    /// ADR-045 abstellt. Die Liste irrt in die harmlose Richtung.</para>
    /// </summary>
    private static readonly HashSet<string> NurAnzeige = new(StringComparer.Ordinal)
    {
        nameof(NewUsername), nameof(NewDomain), nameof(NewPassword), nameof(NewDisplayName),
        nameof(NewTransport), nameof(NewAuthUserId), nameof(NewOutboundProxy),
        nameof(NewExpiresSeconds), nameof(IsRegistering),
        nameof(NewTeamName), nameof(NewTeamNumber), nameof(NewTeamSip), nameof(NewTeamMobile),
        nameof(NewTeamGroup), nameof(SelectedGroup), nameof(GroupName),
        nameof(EditingTeamIndex), nameof(EditingIdentity),
        nameof(IsCalibrating), nameof(HotkeyStatus),
        nameof(Message), nameof(Error), nameof(RestartHint),
    };

    /// <summary>
    /// Felder, die erst beim Verlassen wirken — Freitext und Zahlen.
    ///
    /// <para>Sie hängen an einer Eingabe, die zwischendurch unfertig ist: ein
    /// halb getippter SIP-Port ist „0", eine halb getippte Adresse keine. Wer
    /// hier mit jedem Tastendruck prüfte, beanstandete das Tippen selbst. Die
    /// Oberfläche ruft <see cref="ApplyEdits"/>, sobald das Feld den Fokus
    /// verliert.</para>
    /// </summary>
    /// <summary>
    /// Eigenschaften <b>ohne oeffentlichen Setter</b> — berechnete Werte wie
    /// <c>CanAddAccount</c> oder <c>AccountFormIssue</c>.
    ///
    /// <para><b>Warum das keine dritte Liste ist.</b> Die beiden Listen darueber
    /// sind Entscheidungen, die jemand treffen und pflegen muss. Dies hier ist
    /// keine: eine Eigenschaft ohne Setter <b>kann</b> keinen neuen Wert tragen,
    /// also gibt es an ihr nichts zu speichern. Deshalb wird sie aus dem Typ
    /// gelesen und nicht von Hand gefuehrt — vergessen kann man sie damit
    /// nicht.</para>
    ///
    /// <para><b>Der Befund dahinter (A1-22).</b> <c>RefreshAccounts</c> meldet
    /// nach jedem Kontoereignis drei solche Werte. Am 22.09.2026 gemessen:
    /// waehrend die Anmeldung wackelte, schrieb nipp die Einstellungen
    /// <b>727-mal</b> und die verschluesselte Datei mit den Zugangsdaten
    /// <b>787-mal</b> — achtzehn Mal je Runde, sechs Runden je Wackler. Der
    /// Vergleich aus ADR-060 fing das Schreiben auf die Platte ab, aber erst
    /// <b>nach</b> dem Ablegen der Geheimnisse; und ein Aufruf, den es nicht
    /// geben muesste, ist auch mit Bremse einer.</para>
    /// </summary>
    private static readonly HashSet<string> OhneSetter = new(
        typeof(SettingsViewModel)
            .GetProperties(System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance)
            .Where(p => p.GetSetMethod(nonPublic: false) is null)
            .Select(p => p.Name),
        StringComparer.Ordinal);

    private static readonly HashSet<string> ErstBeimVerlassen = new(StringComparer.Ordinal)
    {
        nameof(SipPort), nameof(KeepAliveSeconds), nameof(StunServer),
        nameof(CountryPrefix), nameof(RecordingDirectory), nameof(HistoryRetentionDays),
        nameof(GlobalHotkey), nameof(MuteHotkey), nameof(ProvisioningUri),
    };

    // --- Konten (§9.1, §20.2) ---

    public ObservableCollection<AccountStatus> Accounts { get; } = [];

    /// <summary>
    /// Die Team-Nebenstellen (§8.4). Lassen sich hier pflegen, damit die
    /// Kontaktliste auch ohne Provisionierung brauchbar ist — ein Profil
    /// ueberschreibt sie beim naechsten Start.
    /// </summary>
    public ObservableCollection<TeamExtension> Team { get; } = [];

    /// <summary>
    /// Die Gruppen, in denen die Nebenstellen stehen (ADR-041) — in ihrer
    /// Reihenfolge. Die erste ist die Standardgruppe.
    /// </summary>
    public ObservableCollection<string> Groups { get; } = [];

    [ObservableProperty]
    private string _newUsername = string.Empty;

    [ObservableProperty]
    private string _newDomain = string.Empty;

    [ObservableProperty]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    private string _newDisplayName = string.Empty;

    /// <summary>
    /// §9.2 gibt <b>TLS</b> als Standardtransport vor. Bis zum 05.09.2026 stand
    /// hier UDP, ohne dass eine ADR das begründete — und weil die
    /// Zertifikatsprüfung nirgends angebunden war, fiel es nicht auf.
    /// </summary>
    [ObservableProperty]
    private SipTransport _newTransport = SipTransport.Tls;

    /// <summary>§9.1: Authentifizierungs-ID; leer heisst „= Benutzername".</summary>
    [ObservableProperty]
    private string _newAuthUserId = string.Empty;

    /// <summary>§9.1: Outbound-Proxy, optional.</summary>
    [ObservableProperty]
    private string _newOutboundProxy = string.Empty;

    /// <summary>§9.1: Registrierungsdauer in Sekunden, Standard 600.</summary>
    [ObservableProperty]
    private int _newExpiresSeconds = 600;

    /// <summary>Ob gerade auf die Antwort der Anlage gewartet wird.</summary>
    [ObservableProperty]
    private bool _isRegistering;

    // --- Netzwerk (§9.2) ---

    [ObservableProperty]
    private int _sipPort;

    [ObservableProperty]
    private bool _verifyServerCertificate;

    [ObservableProperty]
    private int _keepAliveSeconds;

    [ObservableProperty]
    private bool _automaticGainControl;

    /// <summary>§9.6: Adresse, von der ein Provisionierungsprofil geholt wird (§11).</summary>
    [ObservableProperty]
    private string _provisioningUri = string.Empty;

    /// <summary>ADR-012: unverschlüsselte Provisionierung ausdrücklich erlauben.</summary>
    [ObservableProperty]
    private bool _allowInsecureProvisioning;

    // --- Kontakte und Besetztlampenfeld (§8.4) ---

    [ObservableProperty]
    private bool _useOutlook = true;

    [ObservableProperty]
    private bool _enableBlf = true;

    [ObservableProperty]
    private string _newTeamName = string.Empty;

    [ObservableProperty]
    private string _newTeamNumber = string.Empty;

    [ObservableProperty]
    private string _newTeamSip = string.Empty;

    /// <summary>Die Handynummer der Nebenstelle im Formular — freiwillig.</summary>
    [ObservableProperty]
    private string _newTeamMobile = string.Empty;

    /// <summary>
    /// Die Gruppe im Formular. Leer heisst Standardgruppe; ein Name, den es
    /// noch nicht gibt, legt sie an — das Feld ist eine editierbare Auswahl.
    /// </summary>
    [ObservableProperty]
    private string _newTeamGroup = string.Empty;

    /// <summary>Die im Gruppenbereich ausgewählte Gruppe, oder <c>null</c>.</summary>
    [ObservableProperty]
    private string? _selectedGroup;

    /// <summary>Der Name für „Gruppe hinzufügen" und „Umbenennen".</summary>
    [ObservableProperty]
    private string _groupName = string.Empty;

    // --- Erscheinungsbild und Fenster (§20.4, §20.5) ---

    [ObservableProperty]
    private AppTheme _theme;

    [ObservableProperty]
    private bool _alwaysOnTop;

    // --- Audio (§9.4) ---

    public ObservableCollection<AudioDeviceInfo> InputDevices { get; } = [];
    public ObservableCollection<AudioDeviceInfo> OutputDevices { get; } = [];

    [ObservableProperty]
    private AudioDeviceInfo? _selectedInput;

    [ObservableProperty]
    private AudioDeviceInfo? _selectedOutput;

    [ObservableProperty]
    private AudioDeviceInfo? _selectedRinger;

    /// <summary>
    /// Die Klingeltöne zur Wahl (§9.4).
    ///
    /// <para><b>Warum es das jetzt gibt.</b> <c>AudioSettings.RingtonePath</c>
    /// steht seit P5 im Modell, hatte aber keine Oberfläche und wurde von
    /// <c>Compose()</c> nicht einmal geschrieben — änderbar war der Klingelton
    /// nur von Hand in <c>settings.json</c>. Aus dem Alltag kam am 07.09.2026
    /// „der Klingelton ist etwas nervend", und genau da fällt eine Einstellung
    /// auf, die es nur auf dem Papier gibt.</para>
    /// </summary>
    public ObservableCollection<RingtoneChoice> Ringtones { get; } = [];

    [ObservableProperty]
    private RingtoneChoice? _selectedRingtone;

    [ObservableProperty]
    private int _playbackVolume;

    [ObservableProperty]
    private int _microphoneLevel;

    [ObservableProperty]
    private bool _echoCancellation;

    [ObservableProperty]
    private bool _noiseSuppression;

    [ObservableProperty]
    private int? _echoCalibrationMs;

    [ObservableProperty]
    private bool _isCalibrating;

    // --- Netzwerk und Medien (§9.2, §9.3) ---

    [ObservableProperty]
    private bool _encryptionMandatory;

    [ObservableProperty]
    private string _stunServer = string.Empty;

    [ObservableProperty]
    private bool _enableIce;

    // --- Codecs (§9.5) ---

    public ObservableCollection<CodecChoice> Codecs { get; } = [];

    [ObservableProperty]
    private DtmfMode _dtmf;

    // --- Erweitert (§9.6) ---

    [ObservableProperty]
    private string _countryPrefix = "+41";

    [ObservableProperty]
    private string _recordingDirectory = string.Empty;

    [ObservableProperty]
    private int _historyRetentionDays;

    [ObservableProperty]
    private string _globalHotkey = "Ctrl+Shift+A";

    /// <summary>
    /// Das systemweite Kürzel fürs Stummschalten (C7, ADR-050). Leer heisst
    /// «keines» — nipp beansprucht es nicht ungefragt.
    /// </summary>
    [ObservableProperty]
    private string _muteHotkey = string.Empty;

    /// <summary>
    /// AP7.6: „Konflikte abfangen und melden". Ein Kuerzel, das eine andere
    /// Anwendung schon hat, laesst sich nicht registrieren — und das darf nicht
    /// nur im Protokoll stehen, wo es niemand sucht.
    /// </summary>
    [ObservableProperty]
    private string _hotkeyStatus = string.Empty;

    /// <summary>Dasselbe für das Kürzel zum Stummschalten (C7).</summary>
    [ObservableProperty]
    private string _muteHotkeyStatus = string.Empty;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _startMinimized;

    [ObservableProperty]
    private bool _autoAnswer;

    [ObservableProperty]
    private LogVerbosity _logging;

    // --- Über nipp (§9, §16.2) ---

    /// <summary>„nipp 0.1.0 (unpackaged)".</summary>
    public static string VersionLine => AppInfo.VersionLine;

    /// <summary>„© 2026 bv2 GmbH".</summary>
    public static string Copyright => AppInfo.Copyright;

    /// <summary>Die Mailadresse, als Text — den <c>mailto:</c> baut die Ansicht.</summary>
    public static string ContactMail => AppInfo.ContactMail;

    /// <summary>Die Website.</summary>
    public static string Website => AppInfo.Website;

    // --- Rückmeldung ---

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private string? _error;

    /// <summary>
    /// Was erst nach einem Neustart gilt (§12, M4). Leer heisst: alles wirkt
    /// sofort.
    /// </summary>
    [ObservableProperty]
    private string _restartHint = string.Empty;

    public SettingsViewModel(
        SettingsService settings,
        SecretStore secrets,
        PolicyService policy,
        ISipService sip,
        GlobalHotkeyService hotkeys,
        UpdateService updates,
        ILogger<SettingsViewModel> logger)
    {
        _settings = settings;
        _secrets = secrets;
        _policy = policy;
        _sip = sip;
        _hotkeys = hotkeys;
        _updates = updates;
        _logger = logger;

        _sip.AccountsChanged += OnAccountsChanged;
        _updates.Changed += OnUpdateStateChanged;

        // Die Sammlungen melden keine PropertyChanged (ADR-045). Nebenstellen
        // und Gruppen aendern sich ueber Befehle, die Codec-Reihenfolge ueber
        // Move — und ob ein Codec eingeschaltet ist, meldet die Zeile selbst.
        Team.CollectionChanged += OnSettingCollectionChanged;
        Groups.CollectionChanged += OnSettingCollectionChanged;
        Codecs.CollectionChanged += OnCodecsChanged;

        Load();
    }

    /// <summary>
    /// Jede Aenderung an einer Einstellung schreibt sie (ADR-045).
    ///
    /// <para>Ausgenommen ist, was nichts einstellt (<see cref="NurAnzeige"/>
    /// und <see cref="OhneSetter"/>) und was erst beim Verlassen des Feldes
    /// wirkt (<see cref="ErstBeimVerlassen"/>).</para>
    /// </summary>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName is not { Length: > 0 } name
            || NurAnzeige.Contains(name)
            || OhneSetter.Contains(name)
            || ErstBeimVerlassen.Contains(name))
        {
            return;
        }

        Apply();
    }

    private void OnSettingCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Apply();

    private void OnCodecsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var alt in e.OldItems?.OfType<CodecChoice>() ?? [])
        {
            alt.PropertyChanged -= OnCodecChanged;
        }

        foreach (var neu in e.NewItems?.OfType<CodecChoice>() ?? [])
        {
            neu.PropertyChanged += OnCodecChanged;
        }

        Apply();
    }

    private void OnCodecChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Apply();

    /// <summary>
    /// Uebernimmt, was in den Freitext- und Zahlenfeldern steht (ADR-045).
    ///
    /// <para>Die Oberflaeche ruft das, sobald ein solches Feld den Fokus
    /// verliert. Oeffentlich, weil nur sie weiss, wann eine Eingabe fertig
    /// ist.</para>
    /// </summary>
    public void ApplyEdits() => Apply();

    /// <summary>
    /// Ob „Hinzufügen" beziehungsweise „Übernehmen" möglich ist.
    ///
    /// <para>Die Obergrenze aus §20.2 gilt nur für <b>neue</b> Konten. Vorher
    /// zählte sie auch beim Bearbeiten mit: bei zehn Konten füllte ein Klick
    /// auf den Stift das Formular, aber „Übernehmen" blieb grau — es liess sich
    /// dann kein einziges Konto mehr ändern, ohne vorher eines zu löschen.</para>
    /// </summary>
    public bool CanAddAccount => AccountFormIssue is null;

    /// <summary>
    /// Warum „Hinzufügen" grau ist — oder <c>null</c>, wenn es das nicht ist
    /// (ADR-045).
    ///
    /// <para><b>Ein toter Knopf ohne Grund ist von einem Fehler nicht zu
    /// unterscheiden.</b> Drei der vier Felder im Formular sind Pflicht, und
    /// beschriftet war nur das optionale — „Anzeigename (optional)". Die
    /// einzige Rückmeldung war ein grauer Knopf, der nicht sagte, welches
    /// Feld fehlt.</para>
    ///
    /// <para>Genannt wird das <b>erste</b> fehlende Feld, in der Reihenfolge
    /// des Formulars. Eine Aufzählung aller drei beim leeren Formular wäre
    /// eine Beanstandung, bevor jemand etwas getan hat.</para>
    /// </summary>
    public string? AccountFormIssue
    {
        get
        {
            if (Accounts.Count >= 10 && EditingIdentity is null)
            {
                return "Es sind zehn Konten eingerichtet — mehr verwaltet nipp nicht. "
                    + "Erst eines entfernen.";
            }

            if (string.IsNullOrWhiteSpace(NewUsername))
            {
                return "Es fehlt noch: Benutzername.";
            }

            if (string.IsNullOrWhiteSpace(NewDomain))
            {
                return "Es fehlt noch: Domain.";
            }

            if (string.IsNullOrWhiteSpace(NewPassword))
            {
                return "Es fehlt noch: Passwort.";
            }

            // Ein Tippfehler in der Domain kostete zwölf Sekunden Wartezeit:
            // das Konto wurde angelegt, registriert, und erst die Zeitgrenze
            // meldete, dass niemand geantwortet hat. Die Form lässt sich hier
            // prüfen, die Erreichbarkeit nicht — das bleibt dem Versuch.
            if (SettingsValidator.DescribeDomainIssue(NewDomain) is { } domain)
            {
                return domain;
            }

            return null;
        }
    }


    /// <summary>Wie viele Konten noch möglich sind — für den Hinweis im UI.</summary>
    public string AccountCapacity => $"{Accounts.Count} von 10 Konten";

    /// <summary>
    /// §9.4 und ADR-006: ob die Echounterdrückung im laufenden Gespräch
    /// tatsächlich arbeitet. Bei 8-kHz-Codecs tut sie es nicht.
    /// </summary>
    public bool IsEchoEffective => _sip.IsEchoCancellationEffective;

    public void Load() => Batch(LoadCore, schreiben: false);

    /// <summary>
    /// Fuellt die Felder aus der Datei.
    ///
    /// <para>Getrennt von <see cref="Load"/>, weil hier vierzig
    /// Eigenschaften hintereinander gesetzt werden und keine davon eine
    /// Datei schreiben darf, die sie gerade gelesen hat (ADR-045).</para>
    /// </summary>
    private void LoadCore()
    {
        var s = _settings.Current;

        Theme = s.Advanced.Theme;
        AlwaysOnTop = s.Advanced.AlwaysOnTop;

        PlaybackVolume = s.Audio.PlaybackVolume;
        MicrophoneLevel = s.Audio.MicrophoneLevel;
        EchoCancellation = s.Audio.EchoCancellation;
        NoiseSuppression = s.Audio.NoiseSuppression;
        EchoCalibrationMs = s.Audio.EchoCalibrationMs;

        EncryptionMandatory = s.NatMedia.EncryptionMandatory;
        StunServer = s.NatMedia.StunServer ?? string.Empty;
        EnableIce = s.NatMedia.EnableIce;

        SipPort = s.Network.SipPort;
        VerifyServerCertificate = s.Network.VerifyServerCertificate;
        KeepAliveSeconds = s.Network.KeepAliveSeconds;
        AutomaticGainControl = s.Audio.AutomaticGainControl;

        ProvisioningUri = s.Advanced.ProvisioningUri ?? string.Empty;
        AllowInsecureProvisioning = s.Advanced.AllowInsecureProvisioning;

        Dtmf = s.Codecs.Dtmf;

        CountryPrefix = s.Advanced.CountryPrefix;
        RecordingDirectory = s.Advanced.RecordingDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "nipp",
                "recordings");
        HistoryRetentionDays = s.Advanced.HistoryRetentionDays;
        GlobalHotkey = s.Advanced.GlobalHotkey;
        MuteHotkey = s.Advanced.MuteHotkey;
        HotkeyStatus = DescribeHotkey(
            _hotkeys.ActiveHotkey(HotkeyRole.AnnehmenAuflegen),
            s.Advanced.GlobalHotkey);
        MuteHotkeyStatus = DescribeHotkey(
            _hotkeys.ActiveHotkey(HotkeyRole.Stumm),
            s.Advanced.MuteHotkey);
        StartWithWindows = s.Advanced.StartWithWindows;
        StartMinimized = s.Advanced.StartMinimized;
        AutoAnswer = s.Advanced.AutoAnswer;
        Logging = s.Advanced.Logging;

        UseOutlook = s.Contacts.UseOutlook;
        EnableBlf = s.Contacts.EnableBlf;

        CheckUpdatesOnStart = s.Update.CheckOnStart;
        UpdateChannel = s.Update.Channel;

        Groups.Clear();
        foreach (var gruppe in TeamGroups.Collect(s.Contacts.Groups, s.Contacts.Team))
        {
            Groups.Add(gruppe);
        }

        Team.Clear();
        foreach (var member in s.Contacts.Team)
        {
            Team.Add(member);
        }

        RefreshDevices();
        RefreshCodecs(s.Codecs);
        RefreshAccounts(_sip.Accounts);
    }

    /// <summary>
    /// Welche Nebenstelle gerade bearbeitet wird, als <b>Platz in der Liste</b> —
    /// oder -1 für eine neue.
    ///
    /// Der Index und nicht der Eintrag selbst: <see cref="TeamExtension"/> ist
    /// ein Record mit Wertgleichheit, zwei gleichnamige Nebenstellen mit
    /// derselben Nummer wären nicht auseinanderzuhalten, und das Übernehmen
    /// träfe immer die erste.
    /// </summary>
    [ObservableProperty]
    private int _editingTeamIndex = -1;

    /// <summary>Beschriftung der Schaltfläche unter dem Team-Formular.</summary>
    public string TeamActionLabel => EditingTeamIndex < 0 ? "Hinzufügen" : "Übernehmen";

    /// <summary>Ob gerade eine bestehende Nebenstelle bearbeitet wird.</summary>
    public bool IsEditingTeamMember => EditingTeamIndex >= 0;

    partial void OnEditingTeamIndexChanged(int value)
    {
        OnPropertyChanged(nameof(TeamActionLabel));
        OnPropertyChanged(nameof(IsEditingTeamMember));
    }

    /// <summary>§8.4: eine Nebenstelle aufnehmen oder eine bestehende ändern.</summary>
    [RelayCommand(CanExecute = nameof(CanAddTeamMember))]
    private void AddTeamMember()
    {
        var gruppe = NewTeamGroup.Trim();

        // Eine getippte Gruppe, die es noch nicht gibt, entsteht hier — sonst
        // müsste man sie oben anlegen, um sie unten wählen zu können.
        if (gruppe.Length > 0 && !Groups.Any(g => string.Equals(g, gruppe, StringComparison.OrdinalIgnoreCase)))
        {
            Groups.Add(gruppe);
        }

        var member = new TeamExtension(
            DisplayName: NewTeamName.Trim(),
            Extension: NewTeamNumber.Trim(),
            SipAddress: string.IsNullOrWhiteSpace(NewTeamSip) ? null : NewTeamSip.Trim(),
            Mobile: string.IsNullOrWhiteSpace(NewTeamMobile) ? null : NewTeamMobile.Trim(),
            Group: gruppe.Length == 0 ? null : gruppe);

        if (EditingTeamIndex >= 0 && EditingTeamIndex < Team.Count)
        {
            // An Ort und Stelle ersetzen: eine geänderte Nebenstelle soll nicht
            // ans Ende der Liste rutschen — die Reihenfolge ist gewollt (§8.4).
            Team[EditingTeamIndex] = member;
        }
        else
        {
            Team.Add(member);
        }

        CancelEditTeamMember();
    }

    /// <summary>Übernimmt eine bestehende Nebenstelle ins Formular.</summary>
    [RelayCommand]
    private void EditTeamMember(TeamExtension? member)
    {
        if (member is null)
        {
            return;
        }

        var index = Team.IndexOf(member);

        if (index < 0)
        {
            return;
        }

        EditingTeamIndex = index;
        NewTeamName = member.DisplayName;
        NewTeamNumber = member.Extension;
        NewTeamSip = member.SipAddress ?? string.Empty;
        NewTeamMobile = member.Mobile ?? string.Empty;
        NewTeamGroup = member.Group ?? string.Empty;
    }

    /// <summary>Bricht das Bearbeiten ab und räumt das Formular.</summary>
    [RelayCommand]
    private void CancelEditTeamMember()
    {
        EditingTeamIndex = -1;
        NewTeamName = string.Empty;
        NewTeamNumber = string.Empty;
        NewTeamSip = string.Empty;
        NewTeamMobile = string.Empty;
        NewTeamGroup = string.Empty;
    }

    // --- Gruppen (ADR-041) ---

    /// <summary>
    /// Legt eine Gruppe an — auch eine leere.
    ///
    /// <b>Genau dafür gibt es die eigene Liste.</b> Würden die Gruppen aus den
    /// Einträgen abgeleitet, müsste man „Support" erst befüllen, um sie zu
    /// haben.
    /// </summary>
    [RelayCommand]
    private void AddGroup()
    {
        var name = GroupName.Trim();

        if (name.Length == 0
            || Groups.Any(g => string.Equals(g, name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Groups.Add(name);
        GroupName = string.Empty;
    }

    /// <summary>
    /// Benennt die ausgewählte Gruppe um. Die Einträge wandern mit — sie tragen
    /// den Namen und keinen Verweis.
    /// </summary>
    [RelayCommand]
    private void RenameGroup()
    {
        if (SelectedGroup is not { Length: > 0 } alt || GroupName.Trim() is not { Length: > 0 } neu)
        {
            return;
        }

        var (gruppen, mitglieder) = TeamGroups.Rename([.. Groups], [.. Team], alt, neu);

        Ersetzen(gruppen, mitglieder);

        SelectedGroup = neu;
        GroupName = string.Empty;
    }

    /// <summary>
    /// Entfernt die ausgewählte Gruppe.
    ///
    /// <b>Ohne einen Kontakt zu verlieren</b> — die Einträge wechseln in die
    /// Standardgruppe, und genau das sagt auch die Rückfrage davor. Die letzte
    /// Gruppe bleibt stehen.
    /// </summary>
    [RelayCommand]
    private void RemoveGroup()
    {
        if (SelectedGroup is not { Length: > 0 } name || Groups.Count <= 1)
        {
            return;
        }

        var (gruppen, mitglieder) = TeamGroups.Remove([.. Groups], [.. Team], name);

        Ersetzen(gruppen, mitglieder);

        SelectedGroup = null;
    }

    /// <summary>
    /// Wohin die Einträge einer entfernten Gruppe wandern — für die Rückfrage.
    /// </summary>
    public string DefaultGroupName =>
        Groups.Count > 0 ? Groups[0] : TeamGroups.DefaultName;

    private void Ersetzen(IReadOnlyList<string> gruppen, IReadOnlyList<TeamExtension> mitglieder)
    {
        // Ein Formular, das auf eine Zeile zeigt, die es gleich nicht mehr an
        // dieser Stelle gibt, schriebe beim Übernehmen in die falsche.
        CancelEditTeamMember();

        Groups.Clear();
        foreach (var gruppe in gruppen)
        {
            Groups.Add(gruppe);
        }

        Team.Clear();
        foreach (var mitglied in mitglieder)
        {
            Team.Add(mitglied);
        }

        OnPropertyChanged(nameof(DefaultGroupName));
    }

    public bool CanAddTeamMember =>
        !string.IsNullOrWhiteSpace(NewTeamName) && !string.IsNullOrWhiteSpace(NewTeamNumber);

    [RelayCommand]
    private void RemoveTeamMember(TeamExtension? member)
    {
        if (member is null)
        {
            return;
        }

        var index = Team.IndexOf(member);

        if (index < 0)
        {
            return;
        }

        Team.RemoveAt(index);

        // Ein Formular, das auf eine entfernte Zeile zeigt, schriebe beim
        // Übernehmen in die falsche.
        if (EditingTeamIndex == index)
        {
            CancelEditTeamMember();
        }
        else if (EditingTeamIndex > index)
        {
            EditingTeamIndex--;
        }
    }

    /// <summary>
    /// Meldet ein Kuerzel gleich an, statt bis zum Speichern zu warten. Ob es
    /// frei ist, weiss nur Windows — und die Antwort gehoert sofort neben das
    /// Eingabefeld.
    /// </summary>
    /// <summary>
    /// Warum ein Kürzel nicht angenommen wurde (W1.7, Befund B20).
    ///
    /// <para><b>Zwei Fälle, die gleich aussahen.</b> Bis zum 13.09.2026 hiess
    /// es immer «von einer anderen Anwendung belegt» — auch dann, wenn der
    /// Dienst selbst gar nicht lief und deshalb <em>gar kein</em> Kürzel
    /// wirkte. Die Meldung schickte die Suche zum Task-Manager, während die
    /// Ursache im eigenen Prozess lag. Dasselbe Muster wie die Protokollzeile
    /// zum Rufton: eine Ursache behauptet, die niemand gemessen hat.</para>
    /// </summary>
    private string HotkeyAbgelehnt(string wanted) =>
        _hotkeys.IstBereit
            ? $"«{wanted}» ist bereits von einer anderen Anwendung belegt. Ein anderes wählen."
            : "Systemweite Tastenkürzel stehen in dieser Sitzung nicht zur Verfügung. "
                + "nipp einmal neu starten; hilft das nicht, steht der Grund im Protokoll.";

    [RelayCommand]
    private void TryHotkey()
    {
        var wanted = GlobalHotkey.Trim();

        HotkeyStatus = _hotkeys.Apply(HotkeyRole.AnnehmenAuflegen, wanted)
            ? $"{wanted} ist aktiv."
            : GlobalHotkeyService.TryParse(wanted, out _, out _)
                ? HotkeyAbgelehnt(wanted)
                : $"«{wanted}» lässt sich nicht deuten. Erwartet wird etwas wie Ctrl+Shift+A — "
                    + "mindestens ein Modifikator und eine Taste.";
    }

    /// <summary>
    /// Dasselbe für das Kürzel zum Stummschalten (C7).
    ///
    /// <para>Eigener Befehl und nicht derselbe mit einem Parameter: die beiden
    /// Felder stehen nebeneinander, jedes mit eigenem Knopf, und ein Parameter
    /// im XAML wäre die Stelle, an der man sich vertut.</para>
    /// </summary>
    [RelayCommand]
    private void TryMuteHotkey()
    {
        var wanted = MuteHotkey.Trim();

        if (wanted.Length > 0
            && string.Equals(wanted, GlobalHotkey.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            MuteHotkeyStatus = "Dasselbe Kürzel wie für Annehmen und Auflegen. "
                + "Eine Taste kann nur eine Bedeutung haben.";
            return;
        }

        MuteHotkeyStatus = _hotkeys.Apply(HotkeyRole.Stumm, wanted)
            ? $"{wanted} schaltet stumm."
            : wanted.Length == 0
                ? "Kein Tastenkürzel fürs Stummschalten."
                : GlobalHotkeyService.TryParse(wanted, out _, out _)
                    ? HotkeyAbgelehnt(wanted)
                    : $"«{wanted}» lässt sich nicht deuten. Erwartet wird etwas wie Ctrl+Shift+M — "
                        + "mindestens ein Modifikator und eine Taste.";
    }

    private static string DescribeHotkey(string? active, string configured) =>
        string.Equals(active, configured, StringComparison.OrdinalIgnoreCase)
            ? $"{configured} ist aktiv."
            : string.IsNullOrWhiteSpace(configured)
                ? "Kein Tastenkürzel eingerichtet."
                : $"{configured} ist nicht aktiv — vermutlich hat es eine andere Anwendung.";

    partial void OnNewTeamNameChanged(string value) => AddTeamMemberCommand.NotifyCanExecuteChanged();

    partial void OnNewTeamNumberChanged(string value) => AddTeamMemberCommand.NotifyCanExecuteChanged();

    public void RefreshDevices()
    {
        var devices = _sip.GetAudioDevices();
        var s = _settings.Current;

        InputDevices.Clear();
        OutputDevices.Clear();

        foreach (var device in devices.Where(d => d.CanRecord))
        {
            InputDevices.Add(device);
        }

        foreach (var device in devices.Where(d => d.CanPlay))
        {
            OutputDevices.Add(device);
        }

        SelectedInput = InputDevices.FirstOrDefault(d => d.Id == s.Audio.InputDeviceId);
        SelectedOutput = OutputDevices.FirstOrDefault(d => d.Id == s.Audio.OutputDeviceId);
        SelectedRinger = OutputDevices.FirstOrDefault(d => d.Id == s.Audio.RingerDeviceId);

        RefreshRingtones(s.Audio.RingtonePath);
    }

    /// <summary>
    /// Baut die Klingeltonliste und wählt darin, was gespeichert ist.
    ///
    /// Eine eigene Datei kommt als zusätzlicher Eintrag dazu — sonst stünde
    /// die Liste auf einem der mitgelieferten Klänge, während in Wirklichkeit
    /// ein anderer gilt.
    /// </summary>
    private void RefreshRingtones(string? chosen)
    {
        Ringtones.Clear();

        foreach (var klang in NippSounds.BundledRingtones)
        {
            Ringtones.Add(new RingtoneChoice(klang.RelativePath, klang.DisplayName));
        }

        if (string.IsNullOrWhiteSpace(chosen))
        {
            SelectedRingtone = Ringtones[0];
            return;
        }

        var bekannt = Ringtones.FirstOrDefault(
            r => string.Equals(r.Path, chosen, StringComparison.OrdinalIgnoreCase));

        if (bekannt is not null)
        {
            SelectedRingtone = bekannt;
            return;
        }

        var eigene = new RingtoneChoice(chosen, Path.GetFileName(chosen), IsCustom: true);
        Ringtones.Add(eigene);
        SelectedRingtone = eigene;
    }

    /// <summary>
    /// Nimmt eine vom Benutzer gewählte Datei als Klingelton auf (§9.4).
    /// Der Dateidialog selbst gehört in die Ansicht — hier landet nur das
    /// Ergebnis.
    /// </summary>
    public void UseCustomRingtone(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        // Eine zweite eigene Datei ersetzt die erste: zwei Einträge, die beide
        // „irgendeine Datei des Benutzers" heissen, sind nicht auseinanderzuhalten.
        foreach (var alte in Ringtones.Where(static r => r.IsCustom).ToList())
        {
            Ringtones.Remove(alte);
        }

        var eigene = new RingtoneChoice(path, Path.GetFileName(path), IsCustom: true);
        Ringtones.Add(eigene);
        SelectedRingtone = eigene;
    }

    /// <summary>
    /// Spielt den gewählten Klingelton zur Probe — auf dem Klingelgerät (§9.4).
    ///
    /// <para>Ohne das wäre die Auswahl ein Blindflug: die Namen sagen nichts
    /// über den Klang, und der einzige andere Weg wäre, sich anrufen zu
    /// lassen.</para>
    /// </summary>
    [RelayCommand]
    private void PlayRingtoneSample()
    {
        if (SelectedRingtone is not { } wahl)
        {
            return;
        }

        // Über NippSounds und nicht mit dem rohen Pfad: ein mitgelieferter
        // Klang steht relativ in den Einstellungen, und ein fehlender fällt
        // auf denselben Ersatz zurück, den auch das Klingeln nimmt.
        if (NippSounds.Ringtone(wahl.Path) is { } datei)
        {
            _sip.PlaySoundPreview(datei);
        }
    }

    private void RefreshCodecs(CodecSettings codecs)
    {
        Codecs.Clear();

        foreach (var name in codecs.Order)
        {
            Codecs.Add(new CodecChoice(name, codecs.Enabled.Contains(name, StringComparer.OrdinalIgnoreCase)));
        }
    }

    private void OnAccountsChanged(object? sender, IReadOnlyList<AccountStatus> accounts) =>
        RefreshAccounts(accounts);

    private void RefreshAccounts(IReadOnlyList<AccountStatus> accounts)
    {
        Accounts.Clear();
        foreach (var account in accounts)
        {
            Accounts.Add(account);
        }

        OnPropertyChanged(nameof(CanAddAccount));
        OnPropertyChanged(nameof(AccountFormIssue));
        OnPropertyChanged(nameof(AccountCapacity));
        AddAccountCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Die Identität des Kontos, das gerade bearbeitet wird — oder <c>null</c>,
    /// wenn ein neues angelegt wird.
    ///
    /// Sie ist mehr als ein Anzeigezustand: ändert der Benutzer Benutzername
    /// oder Domain, heisst das Konto danach anders. Dann muss das alte
    /// abgemeldet und sein Passwort entfernt werden, sonst bleibt eine Karteileiche
    /// mit Fehlerlampe stehen und ein Geheimnis ohne Konto liegen.
    /// </summary>
    [ObservableProperty]
    private string? _editingIdentity;

    /// <summary>Beschriftung der Schaltfläche — sie sagt, was gleich passiert.</summary>
    public string AccountActionLabel => EditingIdentity is null ? "Hinzufügen" : "Übernehmen";

    /// <summary>Ob gerade ein bestehendes Konto bearbeitet wird.</summary>
    public bool IsEditingAccount => EditingIdentity is not null;

    partial void OnEditingIdentityChanged(string? value)
    {
        OnPropertyChanged(nameof(AccountActionLabel));
        OnPropertyChanged(nameof(IsEditingAccount));

        // CanAddAccount hängt seit der Korrektur der Zehnergrenze ebenfalls am
        // Bearbeitungszustand und muss deshalb mitgemeldet werden.
        OnPropertyChanged(nameof(CanAddAccount));
        OnPropertyChanged(nameof(AccountFormIssue));
        AddAccountCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Übernimmt ein bestehendes Konto ins Formular (§9.1).
    ///
    /// Das Passwort kommt mit: <c>SettingsService.Load</c> holt es beim Laden aus
    /// dem SecretStore zurück. Wer nur den Anzeigenamen korrigieren will, soll
    /// nicht sein Passwort neu eintippen müssen.
    /// </summary>
    [RelayCommand]
    private void EditAccount(AccountStatus? account)
    {
        if (account is null)
        {
            return;
        }

        var stored = _settings.Current.Accounts.FirstOrDefault(a => a.Identity == account.Identity);

        if (stored is null)
        {
            // Kann vorkommen, wenn das Konto aus einem Profil stammt und
            // zwischenzeitlich ersetzt wurde.
            Error = $"Zu {account.ShortLabel} sind keine gespeicherten Angaben da. "
                + "Das Konto lässt sich entfernen und neu anlegen.";
            return;
        }

        Error = null;
        Message = null;

        EditingIdentity = stored.Identity;
        NewUsername = stored.Username;
        NewDomain = stored.Domain;
        NewPassword = stored.Password;
        NewDisplayName = stored.DisplayName ?? string.Empty;
        NewTransport = stored.Transport;
        NewAuthUserId = stored.AuthUserId ?? string.Empty;
        NewOutboundProxy = stored.OutboundProxy ?? string.Empty;
        NewExpiresSeconds = stored.ExpiresSeconds;
    }

    /// <summary>Bricht das Bearbeiten ab und räumt das Formular.</summary>
    [RelayCommand]
    private void CancelEditAccount()
    {
        EditingIdentity = null;
        ClearAccountForm();
        Error = null;
        Message = null;
    }

    private void ClearAccountForm()
    {
        NewUsername = string.Empty;
        NewDomain = string.Empty;
        NewPassword = string.Empty;
        NewDisplayName = string.Empty;
        NewAuthUserId = string.Empty;
        NewOutboundProxy = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanAddAccount))]
    private async Task AddAccountAsync()
    {
        Error = null;

        var account = new SipAccountSettings
        {
            Username = NewUsername.Trim(),
            Domain = NewDomain.Trim(),
            Password = NewPassword,
            AuthUserId = string.IsNullOrWhiteSpace(NewAuthUserId) ? null : NewAuthUserId.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(NewDisplayName) ? null : NewDisplayName.Trim(),
            Transport = NewTransport,
            OutboundProxy = string.IsNullOrWhiteSpace(NewOutboundProxy) ? null : NewOutboundProxy.Trim(),
            ExpiresSeconds = NewExpiresSeconds,
        };

        // Beim Bearbeiten kann sich die Identität geändert haben — Benutzername
        // oder Domain sind anders. Das alte Konto muss dann weg, samt seinem
        // Passwort: sonst bleibt eine Karteileiche mit Fehlerlampe stehen, und
        // im SecretStore liegt ein Geheimnis ohne Konto.
        var renamedFrom = EditingIdentity is { } editing && editing != account.Identity
            ? editing
            : null;

        try
        {
            if (renamedFrom is not null)
            {
                await _sip.RemoveAccountAsync(renamedFrom).ConfigureAwait(true);
                _secrets.Remove(renamedFrom);
            }

            await _sip.RegisterAccountAsync(account).ConfigureAwait(true);

            // Speichern, sobald das Konto eingerichtet ist — nicht erst nach
            // der Antwort der Anlage. Ein Konto, das gerade nicht durchkommt,
            // soll einen Neustart überleben und es dann erneut versuchen.
            var s = _settings.Current;
            var accounts = s.Accounts
                .Where(a => a.Identity != account.Identity && a.Identity != renamedFrom)
                .ToList();
            accounts.Add(account);
            _settings.Save(s with { Accounts = accounts });

            IsRegistering = true;
            Message = $"Konto eingerichtet. Anmeldung bei {account.Domain} läuft …";

            var result = await WaitForRegistrationAsync(account.Identity).ConfigureAwait(true);

            IsRegistering = false;
            Message = null;

            if (result is null)
            {
                Error = $"{account.Domain} hat auf die Anmeldung nicht geantwortet. "
                    + "Netzwerkverbindung, Domain und Transport prüfen — bei TLS ausserdem den "
                    + "Port. Das Konto bleibt eingerichtet und versucht es weiter.";
                return;
            }

            if (result.Status == RegistrationStatus.Failed)
            {
                // Die Eingaben bleiben stehen: ein Konto derselben Identität
                // ersetzt beim nächsten „Hinzufügen" das bestehende — so lässt
                // sich ein falsches Passwort korrigieren, ohne das Konto
                // vorher zu löschen.
                Error = result.Message ?? "Die Anmeldung ist fehlgeschlagen.";
                return;
            }

            EditingIdentity = null;
            ClearAccountForm();

            Message = $"Konto {result.ShortLabel} ist angemeldet.";
        }
        catch (InvalidOperationException ex)
        {
            IsRegistering = false;
            Error = ex.Message;
        }
    }

    /// <summary>
    /// Wartet auf die Antwort der Anlage — höchstens zwölf Sekunden.
    ///
    /// <b>Warum das nötig ist.</b> Vorher meldete die Seite „Konto
    /// hinzugefügt.", sobald der REGISTER hinaus war. Bei falschem Passwort
    /// stand diese Erfolgsmeldung dann neben einem Konto mit roter Lampe, und
    /// der Kommentar im Code behauptete, es werde erst nach erfolgreicher
    /// Anmeldung gespeichert.
    /// </summary>
    private async Task<AccountStatus?> WaitForRegistrationAsync(string identity)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(12);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var account = _sip.Accounts.FirstOrDefault(a =>
                string.Equals(a.Identity, identity, StringComparison.OrdinalIgnoreCase));

            if (account?.Status is RegistrationStatus.Registered or RegistrationStatus.Failed)
            {
                return account;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(true);
        }

        return null;
    }

    [RelayCommand]
    private async Task RemoveAccountAsync(AccountStatus? account)
    {
        if (account is null)
        {
            return;
        }

        await _sip.RemoveAccountAsync(account.Identity).ConfigureAwait(true);

        var s = _settings.Current;

        // W2.2 (B21): ein Schreibfehler darf «Konto entfernen» nicht zum
        // Absturz machen — der Befehl läuft aus einem async void.
        try
        {
            _settings.Save(s with
            {
                Accounts = s.Accounts.Where(a => a.Identity != account.Identity).ToList(),
            });
        }
        catch (InvalidOperationException ex)
        {
            Error = ex.Message;
            return;
        }

        // §9.1: „beim Löschen eines Kontos geht die AuthInfo mit — sonst bleiben
        // Zugangsdaten verwaist liegen." Die Rückfrage verspricht es dem
        // Benutzer ausdrücklich; getan wurde es nicht.
        _secrets.Remove(account.Identity);

        // Wer das Konto entfernt, das er gerade bearbeitet, soll kein Formular
        // behalten, das auf nichts mehr zeigt.
        if (EditingIdentity == account.Identity)
        {
            EditingIdentity = null;
            ClearAccountForm();
        }

        Message = $"Konto {account.ShortLabel} entfernt.";
    }

    /// <summary>
    /// Gibt die Einstellungen als Datei aus (ohne Passwörter).
    ///
    /// <para>Vorher einmal übernehmen: in einem Freitextfeld kann etwas stehen,
    /// das noch nicht gewirkt hat, weil der Fokus noch darin liegt (ADR-045).
    /// Die Datei soll das wiedergeben, was auf dem Bildschirm steht.</para>
    /// </summary>
    public bool TryExport(string path)
    {
        Error = null;
        Message = null;

        Apply();

        if (Error is { Length: > 0 })
        {
            return false;
        }

        try
        {
            _settings.Export(path);
            Message = $"Einstellungen ausgegeben nach {path}. Passwörter sind nicht enthalten.";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = $"Die Datei liess sich nicht schreiben: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Liest eine Ausgabedatei ein und übernimmt sie.
    ///
    /// Danach <see cref="Load"/>, damit die Seite zeigt, was jetzt gilt — und
    /// der Neustart-Hinweis, wenn die Datei etwas enthält, das erst nach einem
    /// Neustart greift (§9.2: Port und Transport — die Zertifikatsprüfung
    /// wirkt seit ADR-053/B13 sofort).
    /// </summary>
    public bool TryImport(string path)
    {
        Error = null;
        Message = null;

        var before = _settings.Current;

        if (!_settings.TryImport(path, out var problem))
        {
            Error = problem;
            return false;
        }

        var restart = SettingsApplier.RequiresRestart(before, _settings.Current);

        Load();

        RestartHint = restart.Count > 0
            ? $"Diese Änderungen gelten erst nach einem Neustart von nipp: {string.Join(", ", restart)}."
            : string.Empty;

        Message = "Einstellungen übernommen.";
        return true;
    }

    /// <summary>
    /// Setzt auf die Standardwerte aus §9 zurück. Konten und Passwörter
    /// bleiben — danach lässt sich weiter telefonieren.
    /// </summary>
    public void ResetAll()
    {
        Error = null;
        Message = null;

        var before = _settings.Current;
        _settings.Reset();

        var restart = SettingsApplier.RequiresRestart(before, _settings.Current);

        Load();

        RestartHint = restart.Count > 0
            ? $"Diese Änderungen gelten erst nach einem Neustart von nipp: {string.Join(", ", restart)}."
            : string.Empty;

        Message = "Alle Einstellungen sind zurückgesetzt. Die Konten sind geblieben.";
    }

    /// <summary>
    /// Ob ein Provisionierungsprofil Felder festlegt (§17). Dann gewinnt beim
    /// nächsten Abruf ohnehin das Profil — das gehört vor einem Einlesen oder
    /// Zurücksetzen gesagt, sonst sieht der Benutzer seine Eingabe später
    /// grundlos verschwinden.
    /// </summary>
    public bool HasProvisioningLocks => _policy.HasAnyLock;

    /// <summary>Echo-Kalibrierung (§9.4, AP5.7).</summary>
    [RelayCommand]
    private async Task CalibrateEchoAsync()
    {
        Error = null;
        Message = null;
        IsCalibrating = true;

        try
        {
            var result = await _sip.CalibrateEchoAsync().ConfigureAwait(true);

            if (result is { } ms)
            {
                EchoCalibrationMs = ms;
                Message = $"Kalibrierung abgeschlossen: {ms} ms Verzögerung.";
            }
            else
            {
                Error = "Die Kalibrierung lieferte kein Ergebnis. "
                    + "Mikrofon und Lautsprecher prüfen und erneut versuchen.";
            }
        }
        catch (InvalidOperationException ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsCalibrating = false;
        }
    }

    /// <summary>
    /// Schreibt die Einstellungen, wenn sie brauchbar sind (ADR-045).
    ///
    /// <para><b>Still im Erfolgsfall.</b> Hier stand bis zum 12.09.2026
    /// „Einstellungen gespeichert." — richtig, solange es einen Knopf gab, den
    /// man drückt. Wenn jede Änderung wirkt, wäre die Meldung ein Dauerzustand
    /// und damit Rauschen. Was der Benutzer wissen muss, steht ohnehin da: der
    /// Schalter ist umgelegt.</para>
    ///
    /// <para><b>Nicht still im Fehlerfall.</b> Schlägt die Prüfung fehl, steht
    /// die Beanstandung in <see cref="Error"/> und die Datei bleibt
    /// unverändert — §15.</para>
    /// </summary>
    private void Apply()
    {
        if (_suspendApply)
        {
            return;
        }

        Error = null;

        // §15: Eingaben prüfen, bevor sie wirken — und mit einer Meldung, die
        // sagt, was zu tun ist. Ein unbrauchbarer Aufnahmeordner fiel vorher
        // erst beim Wählen auf, und dort fängt die Ausnahme niemand.
        if (Validate() is { } problem)
        {
            Error = problem;
            return;
        }

        var s = _settings.Current;
        var updated = Compose();

        try
        {
            var restart = SettingsApplier.RequiresRestart(s, updated);
            _settings.Save(updated);

            // Der Neustarthinweis bleibt stehen, sobald er einmal gilt: er
            // nennt genau die Werte, die nicht im laufenden Betrieb
            // greifen (SIP-Port und Transport).
            if (restart.Count > 0)
            {
                RestartHint = "Diese Änderungen gelten erst nach einem Neustart von nipp: "
                    + string.Join(", ", restart) + ".";
            }
        }
        catch (InvalidOperationException ex)
        {
            // Der SettingsValidator hat in Write das letzte Wort; was hier
            // ankommt, hat die Prüfung oben nicht erfasst.
            Error = ex.Message;
        }
    }

    /// <summary>
    /// Führt eine Änderung aus, ohne dass jeder Zwischenschritt schreibt —
    /// und schreibt danach einmal.
    ///
    /// <para>Für alles, was mehrere Eigenschaften auf einmal setzt: Laden,
    /// Zurücksetzen, Einlesen. Ohne das schriebe jede der vierzig Zuweisungen
    /// in <see cref="Load"/> eine Datei, die sie gerade gelesen hat.</para>
    /// </summary>
    private void Batch(Action aenderung, bool schreiben)
    {
        var vorher = _suspendApply;

        _suspendApply = true;

        try
        {
            aenderung();
        }
        finally
        {
            _suspendApply = vorher;
        }

        if (schreiben)
        {
            Apply();
        }
    }

    /// <summary>
    /// Baut aus den Eingabefeldern die Einstellungen zusammen.
    ///
    /// <para>Getrennt vom Speichern, damit <see cref="Validate"/> genau das
    /// prüfen kann, was gleich gespeichert wird — vorher prüfte es einzelne
    /// Felder und liess dabei aus, was nur im Zusammenspiel auffällt.</para>
    /// </summary>
    private NippSettings Compose()
    {
        var s = _settings.Current;

        var enabled = Codecs.Where(c => c.IsEnabled).Select(c => c.Name).ToList();
        var order = Codecs.Select(c => c.Name).ToList();

        var (gruppen, mitglieder) = TeamGroups.Normalize([.. Groups], [.. Team]);

        return s with
        {
            Audio = s.Audio with
            {
                InputDeviceId = SelectedInput?.Id,
                OutputDeviceId = SelectedOutput?.Id,
                RingerDeviceId = SelectedRinger?.Id,

                // Bis zum 07.09.2026 stand diese Zeile hier nicht. Der Wert
                // ueberlebte nur, weil "s.Audio with" ihn mitnimmt — setzen
                // konnte ihn niemand.
                RingtonePath = SelectedRingtone?.Path,
                PlaybackVolume = PlaybackVolume,
                MicrophoneLevel = MicrophoneLevel,
                EchoCancellation = EchoCancellation,
                NoiseSuppression = NoiseSuppression,
                AutomaticGainControl = AutomaticGainControl,
                EchoCalibrationMs = EchoCalibrationMs,
            },
            Network = s.Network with
            {
                SipPort = SipPort,
                VerifyServerCertificate = VerifyServerCertificate,
                KeepAliveSeconds = KeepAliveSeconds,
            },
            NatMedia = s.NatMedia with
            {
                EncryptionMandatory = EncryptionMandatory,
                StunServer = string.IsNullOrWhiteSpace(StunServer) ? null : StunServer.Trim(),
                EnableIce = EnableIce,
            },
            Codecs = s.Codecs with
            {
                Enabled = enabled,
                Order = order,
                Dtmf = Dtmf,
            },
            Contacts = s.Contacts with
            {
                UseOutlook = UseOutlook,
                EnableBlf = EnableBlf,

                // Normalisiert, und das ist keine Kosmetik: die gespeicherte
                // Liste muss blockweise nach Gruppen sortiert sein, sonst
                // schreibt ein einziger Ziehvorgang in der Kontaktliste eine
                // Reihenfolge zurück, die niemand hergestellt hat (ADR-041).
                Groups = gruppen,
                Team = mitglieder,
            },
            Update = s.Update with
            {
                CheckOnStart = CheckUpdatesOnStart,
                Channel = UpdateChannel,
            },
            Advanced = s.Advanced with
            {
                Theme = Theme,
                AlwaysOnTop = AlwaysOnTop,
                CountryPrefix = CountryPrefix.Trim(),
                RecordingDirectory = RecordingDirectory.Trim(),
                HistoryRetentionDays = HistoryRetentionDays,
                GlobalHotkey = GlobalHotkey.Trim(),
                MuteHotkey = MuteHotkey.Trim(),
                StartWithWindows = StartWithWindows,
                StartMinimized = StartMinimized,
                AutoAnswer = AutoAnswer,
                Logging = Logging,
                ProvisioningUri = string.IsNullOrWhiteSpace(ProvisioningUri)
                    ? null
                    : ProvisioningUri.Trim(),
                AllowInsecureProvisioning = AllowInsecureProvisioning,
            },
        };
    }

    /// <summary>
    /// Prüft die Eingaben vollständig und liefert die erste Beanstandung als
    /// fertigen Satz (§15), oder <c>null</c>, wenn alles brauchbar ist.
    /// </summary>
    private string? Validate()
    {
        // Was aus den Werten selbst folgt, prüft der gemeinsame
        // SettingsValidator — dieselbe Regel gilt dann auch beim Einlesen einer
        // Datei und beim Provisionieren, wo sie vorher fehlte.
        if (SettingsValidator.FirstIssue(Compose()) is { } issue)
        {
            return issue;
        }

        // Was nur hier prüfbar ist, weil es an der Eingabe hängt und nicht am
        // gespeicherten Wert: das Tastenkürzel muss sich deuten lassen, und der
        // Aufnahmeordner muss beschreibbar sein.
        if (!string.IsNullOrWhiteSpace(GlobalHotkey)
            && !GlobalHotkeyService.TryParse(GlobalHotkey, out _, out _))
        {
            return $"Das Tastenkürzel «{GlobalHotkey}» lässt sich nicht deuten. Erwartet wird "
                + "etwas wie Ctrl+Shift+A — mindestens ein Modifikator und eine Taste.";
        }

        if (!string.IsNullOrWhiteSpace(MuteHotkey)
            && !GlobalHotkeyService.TryParse(MuteHotkey, out _, out _))
        {
            return $"Das Tastenkürzel «{MuteHotkey}» für das Stummschalten lässt sich nicht "
                + "deuten. Erwartet wird etwas wie Ctrl+Shift+M — mindestens ein Modifikator "
                + "und eine Taste.";
        }

        if (!string.IsNullOrWhiteSpace(MuteHotkey)
            && string.Equals(MuteHotkey.Trim(), GlobalHotkey.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return "Beide Tastenkürzel sind gleich. Eine Taste kann nur eine Bedeutung haben — "
                + "für das Stummschalten ein anderes wählen, etwa Ctrl+Shift+M.";
        }

        return ValidateRecordingDirectory();
    }

    /// <summary>
    /// Der Aufnahmeordner muss anlegbar <b>und</b> beschreibbar sein. Geprüft
    /// wird das hier, nicht erst beim ersten Anruf: dort legt der
    /// Telefoniedienst das Verzeichnis an, und eine Ausnahme daraus hätte den
    /// Klick auf „Anrufen" zum Absturz gemacht.
    /// </summary>
    private string? ValidateRecordingDirectory()
    {
        var directory = RecordingDirectory.Trim();

        if (directory.Length == 0)
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(directory);

            var probe = Path.Combine(directory, $"nipp-schreibtest-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);

            return null;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return $"Der Aufnahmeordner «{directory}» lässt sich nicht verwenden: {ex.Message} "
                + "Einen anderen Pfad wählen oder das Feld leeren — dann gilt der Standardordner.";
        }
    }

    [RelayCommand]
    private void MoveCodecUp(CodecChoice? codec)
    {
        if (codec is null)
        {
            return;
        }

        var index = Codecs.IndexOf(codec);
        if (index > 0)
        {
            Codecs.Move(index, index - 1);
        }
    }

    [RelayCommand]
    private void MoveCodecDown(CodecChoice? codec)
    {
        if (codec is null)
        {
            return;
        }

        var index = Codecs.IndexOf(codec);
        if (index >= 0 && index < Codecs.Count - 1)
        {
            Codecs.Move(index, index + 1);
        }
    }

    partial void OnNewUsernameChanged(string value) => NotifyAddState();
    partial void OnNewDomainChanged(string value) => NotifyAddState();
    partial void OnNewPasswordChanged(string value) => NotifyAddState();

    private void NotifyAddState()
    {
        OnPropertyChanged(nameof(CanAddAccount));
        OnPropertyChanged(nameof(AccountFormIssue));
        AddAccountCommand.NotifyCanExecuteChanged();
    }

    // --- Aktualisierung (§9.6, ADR-039) ---

    /// <summary>Ob beim Start nach Updates gesucht wird.</summary>
    [ObservableProperty]
    private bool _checkUpdatesOnStart = true;

    /// <summary>
    /// Der Kanal. <b>Wirkt sofort und nicht erst beim Speichern</b> — anders
    /// als alles andere auf dieser Seite.
    ///
    /// <para>Der Grund: die Prüfung, die direkt danach läuft, muss den neuen
    /// Kanal benutzen. Wer auf „beta" stellt und „Jetzt prüfen" drückt, würde
    /// sonst wahrheitswidrig „kein Update" lesen und den Fehler beim Feed
    /// suchen.</para>
    /// </summary>
    [ObservableProperty]
    private UpdateChannel _updateChannel = UpdateChannel.Stable;

    /// <summary>Die Kanäle für die Auswahlliste.</summary>
    public IReadOnlyList<UpdateChannel> UpdateChannelChoices { get; } =
        [UpdateChannel.Stable, UpdateChannel.Beta];

    /// <summary>Wo die Prüfung steht, in einem Satz.</summary>
    public string UpdateStatus => _updates.State switch
    {
        UpdateState.Checking => "Wird geprüft …",
        UpdateState.UpToDate => "nipp ist auf dem neuesten Stand.",
        UpdateState.Available => $"Version {_updates.Available?.Version} steht bereit.",
        UpdateState.Downloading => $"Wird geladen … {_updates.DownloadPercent} %",
        UpdateState.Ready =>
            $"Version {_updates.Available?.Version} ist geladen und wird beim Neustart angewandt.",
        UpdateState.Failed => "Die Update-Prüfung war nicht möglich. Der nächste Start versucht es wieder.",
        _ => "Noch nicht geprüft.",
    };

    /// <summary>Ob der Knopf „Jetzt laden" sichtbar ist.</summary>
    public bool CanDownloadUpdate => _updates.State == UpdateState.Available;

    /// <summary>Ob der Knopf „Neu starten" gedrückt werden darf.</summary>
    public bool CanApplyUpdate => _updates.CanApply;

    /// <summary>
    /// Warum gerade nicht neu gestartet werden kann — leer, wenn es geht.
    /// Ein abgeblendeter Knopf ohne Grund liest sich wie ein Fehler.
    /// </summary>
    public string UpdateBlockedReason => _updates.BlockedReason ?? string.Empty;

    /// <summary>Ob ein geladenes Update auf den Neustart wartet.</summary>
    public bool IsUpdateReady => _updates.State == UpdateState.Ready;

    /// <summary>Ob gerade geprüft oder geladen wird.</summary>
    public bool IsUpdateBusy =>
        _updates.State is UpdateState.Checking or UpdateState.Downloading;

    /// <summary>§9.6: „Update prüfen" als Aktion.</summary>
    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        // Auf Knopfdruck wird auch dann gefragt, wenn die Prüfung beim Start
        // abgeschaltet ist: "aus" heisst "nicht von selbst".
        await _updates.CheckAsync(
            new UpdateSettings { CheckOnStart = CheckUpdatesOnStart, Channel = UpdateChannel },
            onStart: false);
    }

    /// <summary>Das gefundene Update herunterladen. Nie von selbst (ADR-039).</summary>
    [RelayCommand]
    private async Task DownloadUpdateAsync() => await _updates.DownloadAsync();

    /// <summary>Anwenden und neu starten. Kehrt im Erfolgsfall nicht zurück.</summary>
    [RelayCommand]
    private void ApplyUpdate()
    {
        if (!_updates.ApplyAndRestart())
        {
            // Der Dienst prüft selbst noch einmal auf ein laufendes Gespräch.
            // Kommt er zurück, ist genau das dazwischengekommen.
            OnUpdateStateChanged(this, EventArgs.Empty);
        }
    }

    private void OnUpdateStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(UpdateStatus));
        OnPropertyChanged(nameof(CanDownloadUpdate));
        OnPropertyChanged(nameof(CanApplyUpdate));
        OnPropertyChanged(nameof(UpdateBlockedReason));
        OnPropertyChanged(nameof(IsUpdateReady));
        OnPropertyChanged(nameof(IsUpdateBusy));
        DownloadUpdateCommand.NotifyCanExecuteChanged();
        ApplyUpdateCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sip.AccountsChanged -= OnAccountsChanged;
        _updates.Changed -= OnUpdateStateChanged;
    }
}

/// <summary>
/// Ein Klingelton zur Auswahl (§9.4).
/// </summary>
/// <param name="Path">
/// Wie es in <c>settings.json</c> landet: relativ für mitgelieferte Klänge,
/// absolut für eine eigene Datei. Warum, steht bei <see cref="BundledSound"/>.
/// </param>
/// <param name="DisplayName">Was in der Liste steht.</param>
/// <param name="IsCustom">Ob es die Datei des Benutzers ist.</param>
public sealed record RingtoneChoice(string Path, string DisplayName, bool IsCustom = false)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// Ein Codec in der Tabelle aus §9.5. Veränderlich, weil die Reihenfolge per
/// Hoch/Runter geändert wird und das Häkchen umschaltbar ist.
/// </summary>
public sealed partial class CodecChoice(string name, bool isEnabled) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    private bool _isEnabled = isEnabled;
}
