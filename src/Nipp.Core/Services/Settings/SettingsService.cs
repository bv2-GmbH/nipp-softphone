using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Nipp.Core.Diagnostics;
using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Telephony;

namespace Nipp.Core.Services.Settings;

/// <summary>
/// Lädt und speichert die Einstellungen (AP5.2, §9, §10).
///
/// §10: Konfiguration unter <c>%APPDATA%\nipp</c>. Passwörter <b>nicht</b>
/// hier, sondern im <see cref="SecretStore"/> — diese Datei ist Klartext und
/// darf jederzeit in einem Diagnosepaket landen (§9.6).
/// </summary>
public sealed class SettingsService
{
    private readonly SecretStore _secrets;
    private readonly ILogger<SettingsService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private NippSettings _current = new();

    /// <summary>
    /// Der Text, der zuletzt in die Datei geschrieben wurde — <c>null</c>, bis
    /// einmal geschrieben wurde.
    ///
    /// <para>Die Gegenprobe für «hat sich überhaupt etwas geändert?». Siehe den
    /// Absatz in <see cref="Write"/>; er trägt den Befund vom 13.09.2026.</para>
    /// </summary>
    private string? _lastWritten;

    /// <param name="path">
    /// Wo die Datei liegt. <c>null</c> heisst <see cref="DefaultPath"/>.
    ///
    /// <b>Der Parameter existiert für die Tests</b>, und das ist keine
    /// Bequemlichkeit: solange der Pfad fest verdrahtet war, arbeiteten sie auf
    /// der Konfiguration des angemeldeten Benutzers. Ein gewöhnliches
    /// <c>dotnet test</c> hat dabei ein eingerichtetes SIP-Konto samt Passwort
    /// gelöscht — der Test „eine kaputte Datei kostet nicht den Start" schreibt
    /// schliesslich eine kaputte Datei, und er schrieb sie dorthin, wo die
    /// echte lag.
    /// </param>
    public SettingsService(SecretStore secrets, ILogger<SettingsService> logger, string? path = null)
    {
        _secrets = secrets;
        _logger = logger;
        SettingsPath = path ?? DefaultPath;
    }

    /// <summary>§10: Konfiguration unter %APPDATA%\nipp.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "nipp",
        "settings.json");

    /// <summary>Die Datei, mit der diese Instanz arbeitet.</summary>
    public string SettingsPath { get; }

    /// <summary>Die aktuellen Einstellungen. Nach <see cref="Load"/> gefüllt.</summary>
    public NippSettings Current => _current;

    /// <summary>Wird nach jedem Speichern ausgelöst, damit Dienste nachziehen (AP5.5).</summary>
    public event EventHandler<NippSettings>? Changed;

    /// <summary>
    /// Lädt die Einstellungen. Fehlt die Datei oder ist sie beschädigt, gelten
    /// die Standardwerte aus §9 — <b>der Start darf daran nicht scheitern</b>.
    /// Dasselbe verlangt §11 für das Provisioning.
    /// </summary>
    public NippSettings Load()
    {
        var path = SettingsPath;

        if (!File.Exists(path))
        {
            SettingsLog.UsingDefaults(_logger, path);
            _current = new NippSettings();
            return _current;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<NippSettings>(File.ReadAllText(path), JsonOptions);

            if (loaded is null)
            {
                SettingsLog.EmptyFile(_logger, path);
                _current = new NippSettings();
                return _current;
            }

            _current = Migrate(loaded);
            _current = NormalizeGroups(_current);
            _current = AttachSecrets(_current);

            SettingsLog.Loaded(_logger, path, _current.Accounts.Count);
            return _current;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Eine beschädigte Datei darf den Start nicht verhindern. Sie wird
            // beiseitegelegt statt überschrieben: vielleicht steckt eine
            // mühsam eingerichtete Konfiguration darin, die jemand retten will.
            SettingsLog.Unreadable(_logger, path, ex.Message);
            TryPreserveBroken(path);

            _current = new NippSettings();
            return _current;
        }
    }

    /// <summary>
    /// Speichert. Passwörter gehen in den <see cref="SecretStore"/> und werden
    /// aus der Klartextdatei entfernt — §10: „niemals Klartext".
    /// </summary>
    public void Save(NippSettings settings) => Write(settings, notify: true);

    /// <summary>
    /// Speichert, <b>ohne</b> die geänderten Pfade als Benutzeränderung zu
    /// vermerken (ADR-054).
    ///
    /// <para>Der Weg der Provisionierung. Sie schreibt, was im Profil steht —
    /// das ist per Definition keine Entscheidung des Benutzers, und wer das
    /// vermerkte, machte aus jedem Profilwert sofort einen unantastbaren
    /// Benutzerwert. Die Regel «der Benutzer gewinnt» liefe dann beim zweiten
    /// Start ins Leere.</para>
    /// </summary>
    public void SaveFromProfile(NippSettings settings) =>
        Write(settings, notify: true, markUserChanges: false);

    /// <summary>
    /// Speichert einen reinen <b>Anzeigezustand</b> — Wähltastatur ein- oder
    /// ausgeblendet, Fensterlage — ohne <see cref="Changed"/> auszulösen.
    ///
    /// <b>Warum es diesen zweiten Weg gibt.</b> An <see cref="Changed"/> hängen
    /// fünf Empfänger: die Einstellungen gehen auf den laufenden Core, der
    /// Autostart und vier Protokoll-Handler werden in die Registrierung
    /// geschrieben, das systemweite Tastenkürzel wird neu angemeldet und die
    /// Präsenz-Abonnements werden erneuert. Jeder Klick auf die Umschaltung der
    /// Wähltastatur löste diese ganze Kette aus — für eine Einstellung, die
    /// nichts als die Oberfläche betrifft.
    ///
    /// <b>Auch die Reihenfolge der Team-Nebenstellen geht diesen Weg.</b> Sie
    /// ist kein Anzeigezustand im engen Sinn, sondern eine Entscheidung des
    /// Benutzers — aber die Empfänger von <see cref="Changed"/> haben mit ihr
    /// nichts zu tun: die Menge der beobachteten Adressen bleibt dieselbe, die
    /// Registrierung auch. Das Besetztlampenfeld würde trotzdem sämtliche
    /// Abonnements erneuern, und genau dort steht eine offene SEH-Ausnahme
    /// (docs/plans/REVIEW.md F15). Eine umsortierte Liste ist kein Grund, ins SDK zu
    /// greifen. Den Zwischenspeicher der Kontakte zieht der Auslöser selbst
    /// nach — <see cref="Contacts.ContactStore.ReorderTeam"/>.
    ///
    /// <b>Die Geheimnisse bleiben dabei unberührt.</b> Sie stehen ohnehin schon
    /// im <see cref="SecretStore"/>; sie bei jedem Klick erneut abzulegen, hätte
    /// nur die Gelegenheiten vermehrt, die DPAPI-Datei in einem ungünstigen
    /// Moment zu erwischen.
    /// </summary>
    public void SaveViewState(NippSettings settings) => Write(settings, notify: false);

    /// <summary>
    /// Schreibt die Einstellungen an einen frei gewählten Ort — <b>ohne
    /// Passwörter</b>.
    ///
    /// Sie liegen im <see cref="SecretStore"/> unter DPAPI und sind an
    /// Benutzerkonto und Rechner gebunden; mitexportiert wären sie anderswo
    /// wertlos und hier ein Klartext-Schlüsselbund (§10: „niemals Klartext").
    /// Nach dem Import werden sie einmal neu eingetragen — steht so auch in der
    /// Datei, damit niemand rätselt, warum sich nichts anmeldet.
    /// </summary>
    public void Export(string path)
    {
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var redacted = JsonSerializer.SerializeToNode(Redact(_current), JsonOptions)!.AsObject();

        // Der Hinweis steht vorn, wo man ihn liest, und beginnt mit einem
        // Unterstrich — beim Import faellt er als unbekanntes Feld heraus.
        // Neu aufgebaut statt eingefuegt: JsonObject kennt in .NET 8 kein
        // Insert an einer Position, und die Reihenfolge ist hier der Zweck.
        var node = new JsonObject
        {
            ["_hinweis"] = "Passwörter sind nicht enthalten. Sie werden nach dem "
                + "Import einmal neu eingetragen.",
        };

        foreach (var (key, value) in redacted.ToList())
        {
            redacted.Remove(key);
            node[key] = value;
        }

        var temporary = path + ".tmp";
        File.WriteAllText(temporary, node.ToJsonString(JsonOptions));
        File.Move(temporary, path, overwrite: true);

        SettingsLog.Exported(_logger, path);
    }

    /// <summary>
    /// Liest eine Exportdatei und übernimmt sie.
    ///
    /// <b>Eine kaputte oder fremde Datei ändert nichts.</b> Sie meldet, was
    /// fehlt, statt die laufende Konfiguration halb zu überschreiben — dieselbe
    /// Haltung wie beim Start (<see cref="Load"/>).
    ///
    /// Passwörter kommen aus dem <b>vorhandenen</b> SecretStore: wer dieselben
    /// Konten schon eingerichtet hatte, muss nichts neu eintippen.
    /// </summary>
    public bool TryImport(string path, out string? error)
    {
        try
        {
            var loaded = JsonSerializer.Deserialize<NippSettings>(File.ReadAllText(path), JsonOptions);

            if (loaded is null)
            {
                error = $"Die Datei {Path.GetFileName(path)} ist leer.";
                return false;
            }

            var imported = AttachSecrets(Migrate(loaded));

            // <b>Vollstaendig pruefen, nicht nur die Codecs.</b> Bisher stand
            // hier allein die Codec-Regel, weil Save daran wirft. Alle uebrigen
            // Pruefungen — Port, Aufbewahrung, Portbereich, Laenderpraefix,
            // Provisioning-Adresse — lagen im ViewModel und galten damit nur
            // fuer Eingaben von Hand. Eine Datei mit SipPort 0 wurde
            // gespeichert und wirkte beim naechsten Start.
            if (SettingsValidator.FirstIssue(imported) is { } issue)
            {
                error = $"Die Datei {Path.GetFileName(path)} enthaelt eine unbrauchbare "
                    + $"Einstellung: {issue}";
                SettingsLog.ImportFailed(_logger, path, issue);
                return false;
            }

            Save(imported);

            SettingsLog.Imported(_logger, path, imported.Accounts.Count);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            error = $"Die Datei {Path.GetFileName(path)} liess sich nicht lesen: {ex.Message}";
            SettingsLog.ImportFailed(_logger, path, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Setzt auf die Standardwerte aus §9 zurück.
    ///
    /// <b>Konten und Passwörter bleiben.</b> Nach einem Zurücksetzen soll nipp
    /// weiter telefonieren können — wer seine Darstellung, seine Codecs oder
    /// sein Tastenkürzel verstellt hat, will nicht auch noch seine
    /// Zugangsdaten neu eintragen. Die Anrufliste und die Aufnahmen bleiben
    /// ebenfalls; sie stehen nicht in dieser Datei.
    ///
    /// <b>Und drei Dinge mehr, die vorher stillschweigend mitgingen:</b> die
    /// Team-Nebenstellen, die Provisioning-Adresse samt ihrer
    /// http-Erlaubnis, und welches Konto das Standardkonto ist. Keines davon
    /// ist eine „verstellte Einstellung" — es ist Konfiguration, teils von Hand
    /// gepflegt, teils die Voraussetzung dafür, dass der nächste Start
    /// überhaupt wieder ein Profil holt. Die Oberfläche versprach dabei nur,
    /// dass die Konten bleiben; wer zurücksetzte, verlor sein Team und bekam
    /// beim nächsten Start kein Profil mehr.
    /// </summary>
    public void Reset()
    {
        var defaults = new NippSettings();

        Save(defaults with
        {
            Accounts = _current.Accounts,
            // Auch die Gruppen. Ohne sie verlöre ein Zurücksetzen die Ordnung,
            // während die Einträge bleiben — und alles landete stumm in „Team".
            Contacts = defaults.Contacts with
            {
                Team = _current.Contacts.Team,
                Groups = _current.Contacts.Groups,
            },
            Advanced = defaults.Advanced with
            {
                ProvisioningUri = _current.Advanced.ProvisioningUri,
                AllowInsecureProvisioning = _current.Advanced.AllowInsecureProvisioning,
                DefaultAccountIdentity = _current.Advanced.DefaultAccountIdentity,
            },
        });

        SettingsLog.Reset(_logger);
    }

    /// <summary>
    /// Vergleicht alt gegen neu über den <see cref="ProvisioningCatalog"/> und
    /// merkt sich jeden Pfad, dessen Wert sich geändert hat (ADR-054).
    ///
    /// <para><b>Warum über den Katalog und nicht über die Eigenschaften.</b>
    /// Ein Profil spricht in Pfaden; die Markierung muss in derselben Sprache
    /// stehen, sonst braucht es eine Übersetzungstabelle — und damit eine
    /// zweite Wahrheit darüber, welche Eigenschaft zu welchem Pfad gehört.</para>
    ///
    /// <para><b>Listen werden mitgeführt.</b> Konten, Nebenstellen und Gruppen
    /// haben kein <c>Read</c>; sie werden über die Anzahl und den Inhalt
    /// verglichen. Ein zweites, von Hand angelegtes Konto war bis zum
    /// 13.09.2026 nach jedem Start weg — das Profil ersetzte die Liste
    /// vollständig.</para>
    /// </summary>
    private static NippSettings MarkUserChanges(NippSettings alt, NippSettings neu)
    {
        List<string>? geaendert = null;

        foreach (var eintrag in ProvisioningCatalog.Entries)
        {
            if (!string.Equals(eintrag.Read(alt), eintrag.Read(neu), StringComparison.Ordinal))
            {
                (geaendert ??= []).Add(eintrag.Path);
            }
        }

        if (!Gleich(alt.Accounts, neu.Accounts))
        {
            (geaendert ??= []).Add(ProvisioningCatalog.AccountsPath);
        }

        if (!Gleich(alt.Contacts.Team, neu.Contacts.Team))
        {
            (geaendert ??= []).Add(ProvisioningCatalog.TeamPath);
        }

        if (!Gleich(alt.Contacts.Groups, neu.Contacts.Groups))
        {
            (geaendert ??= []).Add(ProvisioningCatalog.GroupsPath);
        }

        // <b>Die Markierungen des bisherigen Standes sind die Wahrheit</b>
        // (13.09.2026).
        //
        // Hier stand <c>new List&lt;string&gt;(neu.UserOverrides)</c>, und bei
        // <c>geaendert is null</c> ging <c>neu</c> unverändert zurück. Beides
        // nimmt die Liste, die der <em>Aufrufer</em> mitbringt — und wer ein
        // <c>NippSettings</c> speichert, das er einen Augenblick früher aus
        // <c>Current</c> abgeleitet hat, bringt eine veraltete mit.
        // <b>Damit löscht ein zweites Speichern desselben Objekts, was der
        // Benutzer angefasst hat</b>, und beim nächsten Start gewinnt das
        // Profil wieder — genau der Fall, den ADR-054 beseitigt hat.
        //
        // <c>UserOverrides</c> ist eine Historie und kein Feld, das ein
        // Aufrufer setzt: sie wächst hier und wird nur von einer Sperre
        // gekürzt, und die geht über <c>SaveFromProfile</c> an dieser Funktion
        // vorbei.
        var alle = new List<string>(alt.UserOverrides);

        foreach (var pfad in neu.UserOverrides)
        {
            if (!alle.Contains(pfad, StringComparer.OrdinalIgnoreCase))
            {
                alle.Add(pfad);
            }
        }

        foreach (var pfad in geaendert ?? [])
        {
            if (!alle.Contains(pfad, StringComparer.OrdinalIgnoreCase))
            {
                alle.Add(pfad);
            }
        }

        return neu with { UserOverrides = alle };
    }

    /// <summary>Inhaltsgleichheit zweier Listen, ueber ihre Werte.</summary>
    private static bool Gleich<T>(IReadOnlyList<T> alt, IReadOnlyList<T> neu)
    {
        if (alt.Count != neu.Count)
        {
            return false;
        }

        for (var i = 0; i < alt.Count; i++)
        {
            if (!Equals(alt[i], neu[i]))
            {
                return false;
            }
        }

        return true;
    }

    private void Write(NippSettings settings, bool notify, bool markUserChanges = true)
    {
        // Die letzte Verteidigungslinie: was hier durchgeht, gilt.
        //
        // Vorher stand hier nur die Codec-Regel aus §9.5. Sie stand hier
        // richtigerweise und nicht nur im UI, damit auch ein
        // Provisioning-Profil sie nicht umgehen kann — nur galt das eben
        // ausschliesslich für sie. Alles andere (Port, Portbereich,
        // Aufbewahrung, Länderpräfix) konnte ein Profil oder eine eingelesene
        // Datei ungeprüft mitbringen. Der Validator prüft jetzt alles, an einer
        // Stelle, für alle drei Wege.
        if (SettingsValidator.FirstIssue(settings) is { } issue)
        {
            throw new InvalidOperationException(issue);
        }

        // ADR-054: hier — und nur hier — wird vermerkt, was der Benutzer
        // selbst eingestellt hat. Jeder Aufrufer von Save bekommt das
        // geschenkt, ohne daran zu denken; eine Erlaubnisliste, an die sich
        // jemand erinnern muss, ist die Bauart, die ADR-045 fuer das Speichern
        // schon einmal verworfen hat.
        //
        // Der Provisionierungsdienst geht ueber SaveFromProfile und vermerkt
        // damit nichts — sonst gaelte sein eigener Wert sofort als
        // Benutzeraenderung, und die Regel liefe leer.
        if (markUserChanges)
        {
            settings = MarkUserChanges(_current, settings);
        }

        var path = SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Passwörter herausnehmen, bevor irgendetwas geschrieben wird.
        //
        // Beim stillen Speichern werden sie nur geschwärzt, nicht neu abgelegt:
        // notify=false heisst „reiner Ansichtszustand" (Wähltastatur auf oder
        // zu, ein Abschnitt geklappt, die Fensterlage). Vorher schrieb jeder
        // solche Klick die DPAPI-Datei neu — im Protokoll stand dann
        // „Zugangsdaten abgelegt (3 Eintraege)", weil jemand einen Expander
        // bewegt hatte. Ein Absturz mitten in diesem Schreiben träfe die
        // Passwörter, und die Ursache wäre ein Klick gewesen, der mit ihnen
        // nichts zu tun hat.
        var forDisk = notify ? ExtractSecrets(settings) : Redact(settings);

        var inhalt = JsonSerializer.Serialize(forDisk, JsonOptions);

        // <b>Ein Speichern, das nichts ändert, ist kein Speichern</b>
        // (13.09.2026).
        //
        // <b>Der Befund.</b> An <see cref="Changed"/> hängen vier Empfänger,
        // und einer davon speicherte wieder. Damit lief eine Kette ohne Ende:
        // schreiben, melden, anwenden, schreiben — <b>alle neun
        // Millisekunden</b>, mit demselben Inhalt. In sechs Minuten wurden
        // daraus 248 MB Protokoll, jede Runde übertrug die Einstellungen auf
        // den laufenden Core, und das SDK registrierte jedes Mal neu.
        //
        // <b>Wer die Kette auslöst, ist dabei die falsche Frage.</b> Sie
        // konnte entstehen, weil niemand geprüft hat, ob es überhaupt etwas zu
        // schreiben gibt — und diese Prüfung gehört an die eine Stelle, durch
        // die jeder Schreibweg läuft, nicht in vier Empfänger, die sich daran
        // erinnern müssten.
        //
        // <b>Verglichen wird der Text, der auf die Platte ginge</b>, und nicht
        // der Wert: <c>NippSettings</c> ist ein <c>record</c>, aber seine
        // Listen vergleichen sich über die Referenz — zwei gleiche Teamlisten
        // gälten als verschieden, und die Prüfung liefe ins Leere. Den Text
        // haben wir ohnehin gerade gebildet.
        if (string.Equals(_lastWritten, inhalt, StringComparison.Ordinal))
        {
            SettingsLog.Unchanged(_logger);
            return;
        }

        var temporary = path + ".tmp";

        // W2.2 (Befund B21): ein Schreibfehler ist kein Absturz.
        //
        // <b>Seit ADR-045 wird bei JEDER Feldänderung geschrieben.</b> Hält
        // ein Virenscanner oder ein Synchronisierungsdienst die Datei kurz,
        // warf diese Zeile — und die Aufrufer fingen nicht alle: «Konto
        // entfernen» lief aus einem <c>async void</c> und nahm damit den
        // Prozess mit.
        try
        {
            File.WriteAllText(temporary, inhalt);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SettingsLog.WriteFailed(_logger, LogMasking.Path(path), ex.Message);

            throw new InvalidOperationException(
                "Die Einstellungen liessen sich nicht speichern. Meist hält ein anderes "
                    + "Programm die Datei kurz — noch einmal versuchen. Die bisherigen "
                    + "Einstellungen gelten weiter.");
        }

        _current = settings;
        _lastWritten = inhalt;
        SettingsLog.Saved(_logger, path);

        if (notify)
        {
            Changed?.Invoke(this, settings);
        }
    }

    /// <summary>
    /// Legt die Passwörter in den SecretStore und gibt die Fassung ohne sie
    /// zurück.
    ///
    /// <para><b>Räumt dabei auf.</b> §9.1 verlangt, dass die Zugangsdaten eines
    /// gelöschten Kontos mitgehen — „sonst bleiben Zugangsdaten verwaist
    /// liegen". Die Einzelpfade (ein Konto entfernen, eines umbenennen) taten
    /// das schon; die beiden Pfade, die die <b>ganze Liste</b> ersetzen, nicht:
    /// ein Provisioning-Profil und das Einlesen einer Sicherungsdatei. Wer
    /// zweimal ein Profil mit wechselnden Konten bekam, sammelte in der
    /// DPAPI-Ablage Passwörter zu Konten, die es nicht mehr gibt.</para>
    /// </summary>
    private NippSettings ExtractSecrets(NippSettings settings)
    {
        var kept = new HashSet<string>(
            settings.Accounts.Select(a => a.Identity),
            StringComparer.OrdinalIgnoreCase);

        foreach (var gone in _current.Accounts.Where(a => !kept.Contains(a.Identity)))
        {
            _secrets.Remove(gone.Identity);
            SettingsLog.SecretRemoved(_logger, gone.Identity);
        }

        foreach (var account in settings.Accounts)
        {
            if (!string.IsNullOrEmpty(account.Password))
            {
                _secrets.Set(account.Identity, account.Password);
            }
        }

        return Redact(settings);
    }

    /// <summary>
    /// Entfernt die Geheimnisse aus einer Fassung — <b>ohne Nebenwirkung</b>.
    ///
    /// Getrennt von <see cref="ExtractSecrets"/>, weil der Export dieselbe
    /// Schwärzung braucht, aber nichts ablegen darf: eine Datei zu schreiben,
    /// die nebenbei den SecretStore beschreibt, wäre eine Überraschung.
    ///
    /// <b>Wenn hier je ein Feld dazukommt, das ein Geheimnis trägt</b>, gehört
    /// es in diese Methode. TURN-Zugangsdaten (§9.3) sind der nächste Fall —
    /// heute ist kein Passwort dafür im Modell.
    /// </summary>
    private static NippSettings Redact(NippSettings settings) =>
        settings with
        {
            Accounts = [.. settings.Accounts.Select(static a => a with { Password = string.Empty })],
        };

    /// <summary>Holt die Passwörter beim Laden aus dem SecretStore zurück.</summary>
    private NippSettings AttachSecrets(NippSettings settings)
    {
        var accounts = settings.Accounts
            .Select(a => a with { Password = _secrets.Get(a.Identity) ?? string.Empty })
            .ToList();

        return settings with { Accounts = accounts };
    }

    /// <summary>
    /// Hebt eine unlesbare Datei auf, statt sie zu überschreiben. Ein Benutzer,
    /// der eine Stunde in seiner Konfiguration verbracht hat, soll sie
    /// wiederfinden können.
    /// </summary>
    private void TryPreserveBroken(string path)
    {
        try
        {
            var target = $"{path}.kaputt-{DateTimeOffset.Now:yyyyMMdd-HHmmss}";
            File.Move(path, target, overwrite: false);
            SettingsLog.Preserved(_logger, target);
        }
        catch (IOException)
        {
            // Wenn nicht einmal das geht, bleibt es bei den Standardwerten.
        }
    }

    /// <summary>
    /// Migration älterer Schemaversionen. Noch nichts zu tun — die Stelle
    /// existiert, damit die erste echte Änderung nicht die Ablagestruktur
    /// mitverändern muss.
    /// </summary>
    /// <summary>
    /// Stellt die Gruppen-Invariante her, die das Umsortieren trägt (ADR-041):
    /// <c>Contacts.Team</c> blockweise nach <c>Contacts.Groups</c>.
    ///
    /// <para><b>Und schreibt dabei nicht.</b> Die normalisierte Fassung steht
    /// in <c>_current</c> und landet beim nächsten regulären <c>Save</c> auf
    /// der Platte. Ein Schreibvorgang im Ladepfad wäre eine Änderung an einer
    /// Datei, die niemand angefasst hat — und beim ersten Start nach einem
    /// Update ausgerechnet an der, die noch die alte Form trägt.</para>
    ///
    /// <para>Eine <c>settings.json</c> von vor dieser Änderung kennt weder
    /// <c>groups</c> noch <c>group</c>: dann entsteht genau eine Gruppe, und
    /// alle Nebenstellen stehen darin — in unveränderter Reihenfolge.</para>
    /// </summary>
    private static NippSettings NormalizeGroups(NippSettings settings)
    {
        var (gruppen, team) = TeamGroups.Normalize(
            settings.Contacts.Groups,
            settings.Contacts.Team);

        return settings with
        {
            Contacts = settings.Contacts with { Groups = gruppen, Team = team },
        };
    }

    private NippSettings Migrate(NippSettings loaded)
    {
        if (loaded.SchemaVersion == new NippSettings().SchemaVersion)
        {
            return loaded;
        }

        SettingsLog.Migrating(_logger, loaded.SchemaVersion, new NippSettings().SchemaVersion);
        return loaded with { SchemaVersion = new NippSettings().SchemaVersion };
    }
}

internal static partial class SettingsLog
{
    [LoggerMessage(EventId = 2500, Level = LogLevel.Information,
        Message = "Einstellungen geladen aus {Path} ({Accounts} Konten)")]
    public static partial void Loaded(ILogger logger, string path, int accounts);

    [LoggerMessage(EventId = 2501, Level = LogLevel.Information,
        Message = "Keine Einstellungen unter {Path} — es gelten die Standardwerte aus Paragraph 9")]
    public static partial void UsingDefaults(ILogger logger, string path);

    [LoggerMessage(EventId = 2502, Level = LogLevel.Information,
        Message = "Einstellungen gespeichert nach {Path}")]
    public static partial void Saved(ILogger logger, string path);

    [LoggerMessage(EventId = 2513, Level = LogLevel.Debug,
        Message = "Nichts zu speichern — die Einstellungen sind unveraendert")]
    public static partial void Unchanged(ILogger logger);

    [LoggerMessage(EventId = 2503, Level = LogLevel.Warning,
        Message = "Einstellungen in {Path} sind leer")]
    public static partial void EmptyFile(ILogger logger, string path);

    [LoggerMessage(EventId = 2504, Level = LogLevel.Error,
        Message = "Einstellungen in {Path} sind nicht lesbar: {Reason}. Es gelten die Standardwerte.")]
    public static partial void Unreadable(ILogger logger, string path, string reason);

    [LoggerMessage(EventId = 2505, Level = LogLevel.Information,
        Message = "Beschaedigte Einstellungen aufgehoben als {Path}")]
    public static partial void Preserved(ILogger logger, string path);

    [LoggerMessage(EventId = 2506, Level = LogLevel.Information,
        Message = "Einstellungen werden von Schema {From} auf {To} gehoben")]
    public static partial void Migrating(ILogger logger, int from, int to);

    [LoggerMessage(EventId = 2507, Level = LogLevel.Information,
        Message = "Einstellungen ausgegeben nach {Path} (ohne Passwoerter)")]
    public static partial void Exported(ILogger logger, string path);

    [LoggerMessage(EventId = 2508, Level = LogLevel.Information,
        Message = "Einstellungen uebernommen aus {Path} ({Accounts} Konten)")]
    public static partial void Imported(ILogger logger, string path, int accounts);

    [LoggerMessage(EventId = 2509, Level = LogLevel.Warning,
        Message = "Einstellungen aus {Path} liessen sich nicht uebernehmen: {Reason}")]
    public static partial void ImportFailed(ILogger logger, string path, string reason);

    [LoggerMessage(EventId = 2510, Level = LogLevel.Information,
        Message = "Einstellungen auf die Standardwerte zurueckgesetzt (Konten, Team-Nebenstellen "
            + "und Provisioning-Adresse bleiben)")]
    public static partial void Reset(ILogger logger);

    [LoggerMessage(EventId = 2511, Level = LogLevel.Information,
        Message = "Zugangsdaten von {Identity} entfernt: das Konto ist nicht mehr eingerichtet")]
    public static partial void SecretRemoved(ILogger logger, string identity);

    // W2.2 (B21): das Schreiben ist gescheitert. Der Benutzer bekommt einen
    // Satz, der sagt was zu tun ist; hier steht, woran es lag.
    [LoggerMessage(EventId = 2512, Level = LogLevel.Warning,
        Message = "Einstellungen liessen sich nicht nach {Path} schreiben: {Reason}. "
            + "Der bisherige Stand gilt weiter.")]
    public static partial void WriteFailed(ILogger logger, string path, string reason);
}
