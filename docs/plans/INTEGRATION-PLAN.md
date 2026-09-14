# nipp — Integrationsplattform: Analyse und Umsetzungsplan

**Bezug:** `NIPP-BUILD.md` (Spezifikation), `IMPLEMENTATION-PLAN.md` (P0–P9), `REVIEW.md`, `docs/decisions.md`.
**Stand der Analyse:** 05.09.2026, Arbeitskopie nach Commit `4a7c273`. **Offene Fragen entschieden am 06.09.2026** (Abschnitt I.3).
**Zweck:** Ein generisches, erweiterbares Integrations-Framework, über das externe Systeme (CRM, ERP, Ticketing, beliebige REST-APIs) für zwei Aufgaben angebunden werden: **Anruferkontext** bei ein- und ausgehenden Anrufen und **Kontaktsuche** über mehrere Quellen. Keine fest verdrahtete CRM-Anbindung.

**Dieses Dokument enthält keinen produktiven Code.** Codebeispiele sind Skizzen, die Absicht zeigen, nicht Implementierung.

> **Formale Voraussetzung.** `CLAUDE.md` sagt: „Kein Feature ohne Auftrag aus NIPP-BUILD.md." Die Integrationsplattform steht dort nicht. Bevor Phase I1 beginnt, braucht es einen Nachtrag in `NIPP-BUILD.md` (vorgeschlagen: **§21 Integrationen**, Rev. 6) und eine **ADR-015**, die dieses Dokument als Auftrag benennt. Beides ist in Phase I0 enthalten.

---

## Inhalt

- [A. Analyse des Ist-Zustands](#a-analyse-des-ist-zustands)
- [B. Zielarchitektur](#b-zielarchitektur)
- [C. Interfaces und Domänenmodelle](#c-interfaces-und-domänenmodelle)
- [D. Konfigurationsmodell](#d-konfigurationsmodell)
- [E. Datenfluss](#e-datenfluss)
- [F. Änderungen im bestehenden Code](#f-änderungen-im-bestehenden-code)
- [G. Umsetzung in Phasen](#g-umsetzung-in-phasen)
- [H. Teststrategie](#h-teststrategie)
- [I. Risiken und offene Punkte](#i-risiken-und-offene-punkte)
- [J. MVP und spätere Ausbaustufen](#j-mvp-und-spätere-ausbaustufen)
- [K. Fortschritt](#k-fortschritt)

---

## A. Analyse des Ist-Zustands

### A.1 Projekte

| Projekt | Rolle | Relevante Abhängigkeiten |
|---|---|---|
| `src/Nipp.App` | WinUI 3, DI-Container, Fenster, Seiten, Toast, Tray | Windows App SDK 2.4, `Microsoft.Extensions.DependencyInjection`, Serilog (File-Sink), H.NotifyIcon |
| `src/Nipp.Core` | Services, ViewModels, Modelle. **Einzige** Stelle mit `using Linphone` unter `Services/Telephony/` | `CommunityToolkit.Mvvm`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`, `System.Security.Cryptography.ProtectedData`, `Microsoft.Data.Sqlite` |
| `src/Nipp.Provisioning` | Kommandozeilenwerkzeug für Profile (`neu`, `pruefen`, `schema`) | keine |
| `tests/Nipp.Core.Tests` | 256 Komponententests | xunit 2.9, NSubstitute 5.3 |
| `tests/Nipp.Architecture.Tests` | 7 Quelltext-Scan-Tests | xunit |

Build-Eigenschaften aus `Directory.Build.props`, die jede neue Klasse einhalten muss: .NET 8, C# 12, `Nullable` ein, **`TreatWarningsAsErrors`**, `AnalysisLevel latest-recommended` (CA1848 erzwingt `[LoggerMessage]`, CA2007 erzwingt `ConfigureAwait`, CA1031 verbietet pauschales `catch` ohne Begründung), x64 only. NuGet-Quelle ist **ausschliesslich nuget.org** (`nuget.config`). Es gibt **keine** JSON-Path-, HTTP-Factory- oder Options-Pakete.

### A.2 Dependency Injection

`App.ConfigureServices()` (`src/Nipp.App/App.xaml.cs:590`) baut eine `ServiceCollection`. **Alles ist Singleton.** Es gibt keine Scopes, keine Factories, kein Options-Pattern. Mehrere Implementierungen eines Interface werden als `IEnumerable<T>` aufgelöst, und **die Registrierungsreihenfolge ist Fachlogik**: `IContactSource` wird als `TeamContactSource`, dann `OutlookContactSource` registriert, und `ContactStore` übernimmt diese Reihenfolge in die Liste.

ViewModels sind Singletons, weil sie im Konstruktor Ereignisse abonnieren; die App erzeugt `ShellViewModel` und `ActiveCallViewModel` **beim Start** (`StartTelephony`), damit sie laufende Anrufe nicht verpassen. Seiten holen ihr ViewModel aus `((App)Application.Current).Services`.

Dienste mit Lebenszyklus werden in `StartShellServices()` gestartet und in `ExitApplication()` einzeln freigegeben. Ein neuer Dienst folgt genau diesem Muster.

### A.3 Bestehendes Call-Handling

**Quelle aller Anrufereignisse** ist `SipService.OnBridgeCallStateChanged` (`SipService.cs:1348`). Sie läuft **auf dem UI-Thread**, weil `Core.Iterate()` von `SipPumpHost` über einen `DispatcherQueueTimer` (20 ms) getaktet wird (§6). Ein eingehender Anruf wird in `Track()` (`SipService.cs:1709`) sofort mit `CallStatus.Incoming` angelegt; das erste Ereignis trägt `Previous = null`.

Nach aussen geht `CallStateEventArgs(CallInfo Call, CallStatus? Previous)` mit zwei geprüften Regeln: `IsNewIncoming` und `HasJustEnded`. Die **Lehre aus REVIEW.md §8** gilt für jeden neuen Empfänger: *nicht auf Zustandsübergänge reagieren, sondern auf die Anrufkennung* (`CallHandle`). Ein neuer Anruf ist der, dessen Handle man noch nicht kennt — unabhängig von Richtung und Meldereihenfolge.

`CallInfo` liefert das, was ein Kontext-Lookup braucht: `RemoteNumber` (**roh**, der `Username` des From-Headers: `0791234567`, `+41…` oder `151`), `RemoteDisplayName`, `Direction`, `AccountIdentity`, `Handle`.

Heutige Empfänger von `CallStateChanged`:

| Empfänger | Was er tut | Thread |
|---|---|---|
| `MainWindow.OnCallStateChanged` | Navigation zur `ActiveCallPage` über `_announcedCalls`, `ShowFromTray()` bei eingehend | UI |
| `ToastService.OnCallStateChanged` | Toast bei `IsNewIncoming` mit `call.DisplayLabel` | UI |
| `ShellViewModel.OnCallStateChanged` | Gesprächsleiste, Verlaufseintrag bei `HasJustEnded`; löst den Namen über `ClipResolver` auf (mit Zwischenspeicher je Gespräch) | UI |
| `ActiveCallViewModel.OnCallStateChanged` | `Calls`-Sammlung, Auswahl über Handle | UI |

Zustandsändernde SDK-Aufrufe aus einem Callback sind verboten (Lehre `AcceptPendingAutoAnswer`). Für die Integration heisst das: der Lookup wird im Callback nur **gestartet**, nichts wird dort erwartet.

**Auto-Answer** (`Advanced.AutoAnswer`) nimmt im nächsten `Pump()` an. Der Kontext-Lookup muss davon unabhängig sein.

### A.4 Bestehende Kontaktarchitektur

```
IContactSource (Kind, IsAvailable, LoadAsync → alle Kontakte)
   ├── TeamContactSource     aus Settings.Contacts.Team, synchron
   └── OutlookContactSource  COM, eigener STA-Thread, 60 s Grenze, LateResultAvailable
ContactStore   Zwischenspeicher (12 h), sequentiell über Quellen, Changed (auf Ladethread), Search() in-memory
ClipResolver   Nummer → Contact (Team vor Outlook), reine Funktion IsSameNumber
ContactRow     UI-Zeile mit Präsenz; SourceLabel/IsTeam aus ContactSourceKind
ShellViewModel TeamContacts / OutlookContacts (zwei ObservableCollections), Suggestions (max 5) aus ContactStore.Search + History
BlfService     Präsenz nur für Team (§14.8)
```

Das Modell ist ein **Snapshot-Modell**: jede Quelle liefert *alle* Kontakte, gesucht wird lokal. Es gibt keine Suche gegen die Quelle, kein Debounce (Vorschläge entstehen synchron in `OnDialedNumberChanged`), keine Cancellation, keine Zustände je Quelle in der Oberfläche.

`Contact` ist ein flacher Record: `Id`, `DisplayName`, `Numbers`, `Source: ContactSourceKind {Team, Outlook}`, `SipAddress`, `Company`. **Kein** `Email`, keine externe Kennung, keine Herkunftsliste. `ContactSourceKind` ist an sieben Stellen fest verdrahtet: `ClipResolver.FindIn` (Reihenfolge), `ContactRow.IsTeam/SourceLabel/HasPresence`, `ContactStore.Sort/RefreshAsync` (`UseOutlook`-Prüfung), `ShellViewModel.RefreshContacts` (Aufteilung in zwei Listen), `ShellPage.RefreshContactSections` (zwei `Expander`, Zeilenhöhen), `ContactLog`.

Das Kontakt-Suchfeld wurde mit ADR-014 **entfernt**; die Vorschlagsliste unter dem Nummernfeld (fünf Treffer, §8.1) ist heute der einzige Suchweg. Eine externe Suche braucht einen Ort in der Oberfläche — offene Frage in [I.3](#i3-offene-fragen-an-dominic).

### A.5 Outlook-COM-Integration

`OutlookContactSource` (397 Zeilen) ist die heikelste Komponente des Repos und **funktioniert**:

- Späte Bindung (`dynamic`), keine Interop-Assembly.
- `CLSIDFromProgID` + `GetActiveObject`: nur ein **laufendes** Outlook wird benutzt (§8.4, ADR-009).
- Pro Ladelauf ein eigener **STA-Thread**; das Ergebnis kommt über `TaskCompletionSource`.
- 60 s Zeitgrenze; ein verspätetes Ergebnis wird aufgehoben (`LateResultAvailable`).
- Höchstens 5000 Kontakte; nur Einträge mit Nummer.
- COM-Verweise werden einzeln freigegeben (`FinalReleaseComObject`).

**Was daraus für die Integration folgt:** Outlook bleibt eine **Snapshot-Quelle**. Eine Suche „pro Tastendruck über COM" ist mit STA-Thread und 20-ms-Iterate-Timer nicht vertretbar und ist auch nicht nötig — der Snapshot liegt im Speicher. Die Migration besteht darin, den Snapshot hinter dieselbe Such-Schnittstelle zu stellen wie einen REST-Provider, **ohne die Klasse selbst anzufassen**.

### A.6 UI-Struktur

- **Fenster** im Smartphone-Format, etwa **400 logische Pixel breit** (`WindowPlacement`). Jede Card-Definition muss in dieser Breite lesbar sein — ein 12-Spalten-Raster ist hier Theorie.
- `MainWindow` hält einen `Frame`; `ShellPage` und `ActiveCallPage` sind `NavigationCacheMode.Required` und verbinden sich in `OnLoaded`/`OnUnloaded` (Lehre in `CLAUDE.md`).
- **Refresh-Muster im Code-Behind:** `ShellPage.Refresh()` und `ActiveCallPage.Refresh()` setzen Sichtbarkeiten und Texte von Hand, ausgelöst durch `ViewModel.PropertyChanged`. REVIEW.md F11 nennt den Umbau auf `x:Bind` als bewusst offen. Ein Card-Renderer darf sich diesem Muster **nicht** anschliessen — er bekommt ein fertiges Modell und baut daraus einen Baum.
- `ActiveCallPage.xaml`: acht `Auto`-Zeilen; der Kopf `CallHeader` (Zeile 2) zeigt `PartyText` (= `call.DisplayLabel`, **nicht** der aufgelöste Name — der steht nur in der Gesprächsleiste der Shell), `StateText`, `DurationText`, Chips für Codec und Verschlüsselung. **Hier gehört die Anruferkarte hin.**
- `SettingsPage`: `Expander`-Gruppen mit `SettingCard` (kennt einen Policy-Pfad und zeigt ein Schloss). Eine neue Gruppe „Integrationen" reiht sich ein.
- `Themes/Tokens.xaml` liefert Abstände, Radien und Pinsel; `XamlResourceTests` prüft alle `StaticResource`-Verweise.
- Benutzertexte stehen **in XAML und C#** (`Strings/de-CH.resw` ist leer, REVIEW.md O12).

### A.7 Konfigurationssystem

- `NippSettings` (`Services/Settings/NippSettings.cs`): typisierter Record, `System.Text.Json`, `JsonStringEnumConverter`, `SchemaVersion = 1` mit `Migrate()`-Haken. Datei `%APPDATA%\nipp\settings.json`.
- `SettingsService`: `Load()` (kaputt → beiseitelegen, Standardwerte), `Save()` (atomar über `.tmp`, dann `Changed`), `SaveViewState()` (still), `Export/TryImport` (ohne Passwörter), `Reset()` (Konten bleiben). **`Changed` hat fünf Empfänger** und reicht bis in den SDK-Core (`ApplySettingsAsync`), in die Registrierung (Autostart, Protokolle), zum Hotkey und zum Besetztlampenfeld — mit einer bekannten offenen SEH-Ausnahme (REVIEW.md F15).
- **Geheimnisse:** `SecretStore` (DPAPI `CurrentUser`, Entropie `nipp.secrets.v1`, JSON-Wörterbuch `Schlüssel → Wert`, atomar). `SettingsService.ExtractSecrets/Redact/AttachSecrets` behandeln **jedes Geheimnisfeld einzeln und bewusst**; der Kommentar an `Redact` verlangt, dass jedes neue Geheimnisfeld dort eingetragen wird.
- `PolicyService`: gesperrte Pfade (`network.sip-port`, `contacts`, …), Oberpfad sperrt Unterpfade. Bedienschutz, keine Sicherheitsgrenze.
- **Provisionierung:** XML (`<nipp-provisioning version="1" profile="…">` mit `<accounts>`, `<team>`, `<locked>`, `<settings><set path value/>`), Parser mit `DtdProcessing.Prohibit`, Abruf über `HttpClient`, https-Zwang mit ausdrücklicher Ausnahme (ADR-012). Werte sind **flache Pfad-Wert-Paare**; ein ganzes Integrationspaket passt dort nicht hinein.
- `DiagnosticsBundle.AddSanitizedSettings`: **Whitelist** — jedes Feld muss bewusst aufgenommen werden. Das Muster ist genau richtig für Integrationskonfiguration mit Endpunkten und Headern.

### A.8 HTTP- und API-Infrastruktur

Es gibt **eine** HTTP-Stelle: `ProvisioningService` (`ProvisioningService.cs:57`) erzeugt `new HttpClient { Timeout = 5 s }` mit User-Agent `nipp/1.0`, ruft `GetStringAsync` und unterscheidet `HttpRequestException` von `TaskCanceledException` (Zeitüberschreitung). Keine Grössenbegrenzung, kein Retry, kein Auth, kein JSON. Es gibt **keine** wiederverwendbare HTTP-Schicht, **keinen** `IHttpClientFactory`, **kein** JSON-Path.

Vorhanden und wiederverwendbar: `System.Text.Json` (in .NET 8 enthalten, mit `JsonNode`), das Sicherheitsmuster des Provisioning-Parsers, das Fehlermeldungsmuster nach §15 („was passiert ist, was zu tun ist").

### A.9 Logging

- Serilog in der App, Stufe umschaltbar (`LogLevelSwitch`), Rolling-File unter `%LOCALAPPDATA%\nipp\logs`, 14 Tage.
- Jede Meldung ist ein **`[LoggerMessage]`-Delegat** in einer `XyzLog`-Klasse je Bereich, mit EventId-Bereich: 1000 App, 2000 Telephony, 2400 Secret, 2500 Settings, 2600–2900 weitere Core-Dienste, **3000 `IntegrationLog` (WindowsIntegration!)**, 3100 Toast, 3300 Contact, 3500 der höchste belegte Bereich.
- **Namenskollision:** `internal static partial class IntegrationLog` existiert bereits in `Services/Windows/WindowsIntegration.cs:264` und protokolliert Autostart und Protokoll-Handler. Die neuen Klassen brauchen andere Namen (Vorschlag: `ConnectorLog`, `CallerContextLog`, `ContactSearchLog`, `CardLog`) oder die alte wird zu `WindowsIntegrationLog` umbenannt (16 Aufrufstellen in zwei Dateien: `WindowsIntegration.cs` und `GlobalHotkeyService.cs`, die sich die `partial`-Klasse teilen).
- Datenschutzregel aus `ContactLog`: **keine Namen, keine Nummern** in Meldungen — gezählt, nicht aufgezählt. Kein Backslash in Vorlagen (CS1009).

### A.10 Nummern

Drei reine Funktionen, alle in `Services/Telephony` bzw. `Contacts`, alle getestet:

| Klasse | Zweck | Für die Integration |
|---|---|---|
| `NumberNormalizer(countryPrefix)` | Wählform: `0…` → `+41…`, `00` → `+`, interne Ziele (≤ 4 Ziffern, `*…`) unverändert | Grundlage für **E.164-Schlüssel**; `IsInternalTarget` entscheidet, ob eine Nummer überhaupt an ein externes System geht |
| `ClipResolver.DigitsOnly/IsSameNumber` | Vergleich über Ziffern von hinten (≥ 7) | Grundlage für die Dedup-Regel „gleiche Nummer" |
| `PhoneNumberFormat.ForDisplay` | Lesbare Anzeige (Schweizer Plan) | `formatPhone` in der Expression Engine |

Das Länderpräfix steht in `Advanced.CountryPrefix` (Standard `+41`). Eine `E.164`-Normalisierung existiert **nicht** als eigener Typ; `Normalize` liefert die Wählform, die für `+41…` mit E.164 zusammenfällt, aber interne Nummern und SIP-Adressen unverändert lässt.

### A.11 Threading-Zusammenfassung

| Was | Thread |
|---|---|
| `Core.Iterate()`, alle SDK-Callbacks, `CallStateChanged` | UI |
| `ContactStore.RefreshAsync`, `Changed` | Ladethread (Threadpool oder STA) — `ShellViewModel.OnUiThread` marshallt über den beim Bau erfassten `SynchronizationContext` |
| Toast-Callbacks | fremder Thread → `DispatcherQueue.TryEnqueue` |
| Hotkey | eigener Nachrichtenthread → `DispatcherQueue` |

Das etablierte Muster in `Nipp.Core`, UI-frei zu bleiben: **`SynchronizationContext.Current` im Konstruktor erfassen und mit `Post` zurückkehren.** Die Integrationsdienste übernehmen genau das.

### A.12 Technische Einschränkungen, die den Entwurf prägen

1. **Fensterbreite 400 px** → Card-Layout mit höchstens zwei Spalten in der Praxis; alles muss umbrechen.
2. **UI-Thread trägt Iterate** → kein Debounce-Timer, der auf dem UI-Thread rechnet; kein Card-Rebuild im Sekundentakt; HTTP nie auf dem UI-Thread.
3. **`SettingsService.Changed`-Kette** → Integrationskonfiguration darf **nicht** über `settings.json`/`Save()` laufen; sonst löst jede Mapping-Änderung eine Neuregistrierung von Präsenz-Abonnements aus.
4. **Warnungen sind Fehler** → jede Ausnahmebehandlung braucht eine begründete Eingrenzung; jede Log-Zeile einen Delegaten.
5. **nuget.org only, AGPL-Frage offen** (`docs/licensing.md`) → neue Pakete nur mit permissiver Lizenz, so wenige wie möglich.
6. **Tests nie auf `%APPDATA%`** (`TestIsolationTests`) → jeder neue Store nimmt den Pfad per Konstruktor.
7. **Provisionierung ist flach** → ein Integrationspaket wird als eigene Datei verteilt, nicht als `<set>`-Zeilen.
8. **Benutzertexte auf Hochdeutsch (ss)**, in Code und XAML.
9. **ARM64-Entwicklungsmaschine** → Performance-Aussagen (Card-Rebuild, Debounce-Gefühl) nur auf x64-Hardware belastbar (ADR-001).

---

## B. Zielarchitektur

### B.1 Grundsatz

Ein Integrationskern in `Nipp.Core` unter `Services/Integrations/`, der von **zwei Orchestratoren** benutzt wird — einem für den Anruferkontext, einem für die Kontaktsuche. Beide teilen sich Konfiguration, HTTP-Client, Authentifizierung, Geheimnisse, Mapping, Expressions, Logging, Zustände und Fehlerbehandlung. Es entstehen **keine zwei Frameworks**.

Der Kern kennt **weder das SDK noch WinUI**. Er kennt `CallInfo`/`CallHandle` (eigene Modelle, §6) und die bestehenden Nummernfunktionen. Ein neuer Architekturtest erzwingt beides (siehe H).

**Kein eigenes Projekt im MVP.** Ein `Nipp.Integrations.csproj` würde die Grenze stärker erzwingen, kostet aber Build-Aufwand auf der ARM64-Maschine (`Linphone.Sdk.targets`, Plattformen) und bringt heute keinen Nutzen. Der Namespace `Nipp.Core.Services.Integrations` plus Quelltext-Scan-Test reicht und lässt sich später ohne Codeänderung herauslösen.

### B.2 Schichtenbild

```
                    Nipp.App (WinUI)
 ┌──────────────────────────────────────────────────────────────────────┐
 │ ActiveCallPage ── CallerCardHost (Controls/Cards/CardView)           │
 │ ShellPage      ── Kontaktsuche: Suchfeld, Ergebnisliste, Quellen-    │
 │                   zustände                                           │
 │ SettingsPage   ── Gruppe „Integrationen" (Liste, Test, Import/Export)│
 └───────────────▲──────────────────────────────▲───────────────────────┘
                 │ CardModel (UI-frei)           │ ContactRow / SourceState
 ┌───────────────┴──────────────────────────────┴───────────────────────┐
 │ Nipp.Core / ViewModels                                               │
 │   CallerCardViewModel        ContactSearch-Teil des ShellViewModel   │
 │   IntegrationSettingsViewModel                                       │
 └───────────────▲──────────────────────────────▲───────────────────────┘
                 │ ContextChanged(handle, snapshot)  │ ResultsChanged(gen, …)
 ┌───────────────┴──────────────────────────────┴───────────────────────┐
 │ Nipp.Core / Services / Integrations                                   │
 │                                                                       │
 │  Context/                          Search/                            │
 │   CallerContextService  ◄─┐         ContactSearchService              │
 │   (Orchestrator, Cache)   │         (Debounce, Generation, Cancel)    │
 │   ICallerContextProvider  │         IContactSearchProvider            │
 │    ├ LocalContactsContext │          ├ LocalSnapshotSearchProvider    │
 │    │  (ClipResolver)      │          │   (Team + Outlook aus          │
 │    └ HttpCallerContext    │          │    ContactStore — unverändert) │
 │                           │          └ HttpContactSearchProvider      │
 │                           │         ContactMerger                     │
 │  Cards/                   │                                           │
 │   CardDefinition (Schema) │  Gemeinsam:                               │
 │   CardLayoutEngine → CardModel   Config/  IntegrationConfigStore,     │
 │                           │               Validator, DataSource-      │
 │  Mapping/                 │               Definition, Registry        │
 │   MappingEngine (JSONPath)│      Http/    IntegrationHttpClient,      │
 │   ContextValue, Fragment  │               Auth, Limits, Health        │
 │  Expressions/             │      Secrets/ IntegrationSecrets          │
 │   Tokenizer, Parser, Eval │               (über SecretStore)          │
 │                           │      Phone/   PhoneNumberKey (E.164)      │
 └───────────────────────────┼───────────────────────────────────────────┘
                             │ CallStateChanged (UI-Thread, nur Start)
 ┌───────────────────────────┴───────────────────────────────────────────┐
 │ Nipp.Core / Services / Telephony — SipService, ISipService (unverändert)│
 └───────────────────────────────────────────────────────────────────────┘
```

### B.3 Verantwortlichkeiten

| Komponente | Verantwortung | Kennt |
|---|---|---|
| `IntegrationConfigStore` | `integrations.json` laden, validieren, speichern (atomar), migrieren, `Changed` | Dateisystem, `SecretStore` |
| `IntegrationRegistry` | Aus Definitionen die aktiven Provider-Instanzen bauen (Definition × Connector-Typ), Health je Quelle | Config, Connectoren |
| `IntegrationHttpClient` | Request aus Vorlage + Parametern bauen, Auth einsetzen, Zeitgrenze, Grössenlimit, Statusklassifikation, Log ohne Nummer/Geheimnis | `HttpMessageHandler`, Secrets |
| `MappingEngine` | Rohes JSON → `ContextFragment` (Namespace mit typisierten Werten) über JSONPath und Expressions | `JsonNode`, `ExpressionEngine` |
| `ExpressionEngine` | Eingeschränkte Ausdrücke auswerten (Referenzen, Vergleiche, `concat`, `formatPhone`, …) | nichts ausserhalb |
| `CallerContextService` | Pro Anruf: Nummer normalisieren, Provider parallel starten, Zustände sammeln, Snapshot inkrementell veröffentlichen, Cache | `ISipService` (nur Ereignis), Provider, `SynchronizationContext` |
| `ContactSearchService` | Debounce, Generation, Cancellation, Provider parallel, Zusammenführen | Provider, `ContactMerger` |
| `ContactMerger` | Treffer aus mehreren Quellen konservativ zusammenführen, Herkunft behalten | `ClipResolver.IsSameNumber` |
| `CardLayoutEngine` | `CardDefinition` + `ContextSnapshot` → `CardModel` (aufgelöste Texte, Sichtbarkeit, Aktionen) | `ExpressionEngine` |
| `CardView` (App) | `CardModel` → WinUI-Baum aus einer festen Komponentenmenge | WinUI, Tokens |
| `CallerCardViewModel` | Welche Karte zu welchem Anruf, Umschalten bei zwei Gesprächen, Aktionen ausführen (Wählen, URL öffnen, Kopieren) | `CallerContextService`, `CardLayoutEngine`, `ISipService` |

---

## C. Interfaces und Domänenmodelle

Nur Abstraktionen mit einem klaren Grund. Was heute genau eine Implementierung hat und keine Testnaht braucht, ist eine Klasse, kein Interface.

### C.1 Nummer als Schlüssel

```csharp
namespace Nipp.Core.Services.Integrations.Phone;

/// Eine Nummer in der Form, in der sie an externe Systeme geht und
/// unter der zwischengespeichert wird. Reine Funktion über NumberNormalizer.
public sealed record PhoneNumberKey(
    string E164,            // "+41791234567", oder leer wenn nicht bildbar
    string Digits,          // "41791234567" — Vergleichsschlüssel
    string National,        // "0791234567"  — für APIs, die kein + verstehen
    bool IsInternal,        // NumberNormalizer.IsInternalTarget
    bool IsAddress)         // SIP-Adresse, kein Lookup
{
    public static PhoneNumberKey From(string raw, NumberNormalizer normalizer);
}
```

Parameter in Request-Vorlagen: `{{number.e164}}`, `{{number.national}}`, `{{number.digits}}`.

### C.2 Datenquelle und Fähigkeiten

```csharp
namespace Nipp.Core.Services.Integrations.Config;

public enum Capability { LookupByPhone, SearchContacts, OpenContact /* später: GetContact, CreateContact, UpdateContact */ }

/// Konfiguration einer Quelle — Daten, kein Verhalten. Wird aus integrations.json gelesen.
public sealed record DataSourceDefinition
{
    public required string Id { get; init; }              // "crm" — Namespace im Kontext, Präfix in Kontakt-Ids
    public required string DisplayName { get; init; }     // "Muster-CRM"
    public required string Type { get; init; }            // "http" (MVP); später "dynamics", …
    public bool Enabled { get; init; } = true;
    public int Priority { get; init; } = 100;             // Reihenfolge bei Konflikten (Name, Dedup)
    public HttpConnection? Http { get; init; }            // nur bei Type == "http"
    public LookupByPhoneCapability? LookupByPhone { get; init; }
    public SearchContactsCapability? SearchContacts { get; init; }
    public OpenContactCapability? OpenContact { get; init; }
}
```

Welche Fähigkeit eine Quelle hat, steht **in der Konfiguration** (der Block ist da oder nicht), nicht im Code. Der Connector prüft, ob er den Block bedienen kann.

### C.3 Connector und Provider

Der Connector ist der **Typ** (HTTP, Outlook-Snapshot, Team), der Provider die **Instanz** (Definition × Connector) mit genau einer Fähigkeit. Die Registry erzeugt Provider aus Definitionen.

```csharp
namespace Nipp.Core.Services.Integrations;

/// Ein Connector-Typ. Genau eine Implementierung je Type-Zeichenkette.
public interface IDataSourceConnector
{
    string Type { get; }
    IReadOnlyList<ValidationIssue> Validate(DataSourceDefinition definition);
    ICallerContextProvider?  CreateContextProvider(DataSourceDefinition definition);
    IContactSearchProvider?  CreateSearchProvider(DataSourceDefinition definition);
    Task<ConnectionTestResult> TestAsync(DataSourceDefinition definition, CancellationToken ct); // Admin-Test
}

/// Liefert zu einer Nummer einen Beitrag unter seinem Namespace.
public interface ICallerContextProvider
{
    string SourceId { get; }
    TimeSpan Timeout { get; }
    bool AppliesTo(PhoneNumberKey number, CallDirection direction);   // z. B. keine internen Nummern
    Task<ContextFragment> LookupAsync(PhoneNumberKey number, CallerContextRequest request, CancellationToken ct);
}

/// Sucht Kontakte. Lokale Provider antworten synchron abgeschlossen.
public interface IContactSearchProvider
{
    string SourceId { get; }
    ContactSearchTraits Traits { get; }   // IsLocal, MinQueryLength, SupportsPaging, Timeout
    Task<ContactSearchPage> SearchAsync(ContactQuery query, CancellationToken ct);
}
```

Implementierungen im MVP:

| Interface | Implementierung | Bemerkung |
|---|---|---|
| `IDataSourceConnector` | `HttpDataSourceConnector` | generischer REST-Connector |
| `IDataSourceConnector` | `LocalContactsConnector` | fest registriert, ohne Definition in der Datei; liefert die zwei lokalen Provider |
| `ICallerContextProvider` | `HttpCallerContextProvider` | Request + Mapping |
| `ICallerContextProvider` | `LocalContactsContextProvider` | `ClipResolver` → Namespace `contacts` (`displayName`, `company`, `source`); antwortet sofort |
| `IContactSearchProvider` | `HttpContactSearchProvider` | Request + Mapping auf `Contact` |
| `IContactSearchProvider` | `LocalSnapshotSearchProvider` | `ContactStore.Search` (Team + Outlook) — die Outlook-Migration ohne Berührung von `OutlookContactSource` |

Kein `IIntegrationCapability`-Interface: die Fähigkeit ist ein Konfigurationsblock plus die passende Provider-Schnittstelle. Kein `IContextMapper`-Interface: `MappingEngine` ist eine Klasse mit einer Implementierung, testbar über reine Eingaben. Kein `ICardRenderer`-Interface: `CardLayoutEngine` (Core) und `CardView` (App) sind je genau eine Klasse; die Naht ist das Datenmodell `CardModel`. `IContactMerger` wird ein Interface, **weil** die Strategie (konservativ/aggressiv) austauschbar sein soll und Tests eine Fassung ohne Merge brauchen.

### C.4 Kontextmodell

```csharp
namespace Nipp.Core.Services.Integrations.Context;

public enum SourceState { Loading, Success, Empty, Error, Timeout, Skipped }

/// Ein typisierter Wert. Kein JsonNode verlässt die Mapping-Schicht.
public abstract record ContextValue;                       // Text, Number, Boolean, Date, List<ContextValue>, Null
                                                            // (als geschlossene Hierarchie, C# 12: sealed records)

/// Beitrag genau einer Quelle.
public sealed record ContextFragment(
    string SourceId,
    SourceState State,
    IReadOnlyDictionary<string, ContextValue> Fields,       // "customerName" → Text("Hans Muster")
    string? Message,                                        // Fehlertext nach §15, ohne Geheimnisse
    TimeSpan Elapsed,
    DateTimeOffset ReceivedAt,
    bool FromCache);

/// Alles, was zu einem Anruf bekannt ist. Unveränderlich; jede Änderung ist eine neue Instanz (wie CallInfo).
public sealed record ContextSnapshot(
    CallHandle Call,
    PhoneNumberKey Number,
    IReadOnlyDictionary<string, ContextFragment> Sources)   // "crm" → Fragment
{
    public ContextValue? Resolve(string path);              // "crm.customerName"
    public bool IsComplete => Sources.Values.All(f => f.State != SourceState.Loading);
}
```

Das entspricht dem gewünschten JSON `{ "crm": {...}, "erp": {...} }` — die Herkunft ist der Schlüssel der ersten Ebene, und die Oberfläche adressiert nur `quelle.feld`, nie einen JSON-Pfad der Quelle.

### C.5 Kontaktmodell (erweitert)

`Contact` bleibt der Typ, den `ContactStore`, `ClipResolver`, `ContactRow` kennen — mit Erweiterungen, die für bestehende Aufrufer Standardwerte haben:

```csharp
public sealed record Contact(
    string Id,                         // Konvention neu: "{sourceId}:{externalId}" — "outlook:<EntryID>", "team:0:151", "crm:4711"
    string DisplayName,
    IReadOnlyList<ContactNumber> Numbers,
    ContactSourceKind Source,          // Team | Outlook | External   (Enum bleibt für die drei Klassen von Herkunft)
    string? SipAddress = null,
    string? Company = null,
    // neu:
    string SourceId = "",              // "outlook", "team", "crm" — feiner als das Enum
    string? Email = null,
    string? ExternalId = null,
    Uri? OpenUri = null,               // nur http/https, vom Connector geprüft
    IReadOnlyList<ContactOrigin>? Origins = null);   // nach dem Zusammenführen: alle Herkünfte

public sealed record ContactOrigin(string SourceId, string ExternalId);
```

Warum das Enum bleibt: `ContactRow.HasPresence`, `ClipResolver`-Reihenfolge und `ContactStore.Sort` unterscheiden **Klassen** von Quellen (Team hat Präsenz, Outlook ist lokal, Extern ist Netz). Ein dritter Wert `External` ist eine kleine, sichere Erweiterung; `SourceId` trägt die Feinheit.

### C.6 Card-Modelle

```csharp
namespace Nipp.Core.Services.Integrations.Cards;

// Definition (Konfiguration, JSON, versioniert)
public sealed record CardDefinition(int SchemaVersion, string Id, string Name, CardKind Kind, IReadOnlyList<CardSection> Sections);
public enum CardKind { IncomingCompact, ActiveExpanded, History }
public sealed record CardSection(string Id, string? Title, string? VisibleWhen, IReadOnlyList<CardRow> Rows, bool Collapsible = false);
public sealed record CardRow(IReadOnlyList<CardColumn> Columns);
public sealed record CardColumn(int Span, IReadOnlyList<CardElement> Elements, int Order = 0);
public abstract record CardElement(string? VisibleWhen);
public sealed record TextElement(string Value, TextStyle Style, int MaxLines = 1, string? VisibleWhen = null) : CardElement(VisibleWhen);
public sealed record FieldElement(string Label, string Value, string? EmptyText = "—", string? VisibleWhen = null) : CardElement(VisibleWhen);
public sealed record BadgeElement(string Text, string Tone /* expr → Neutral|Info|Success|Warning|Danger */, string? VisibleWhen = null) : CardElement(VisibleWhen);
public sealed record DividerElement() : CardElement(null);
public sealed record ButtonElement(string Label, CardAction Action, string? EnabledWhen = null, string? VisibleWhen = null) : CardElement(VisibleWhen);
public sealed record LinkElement(string Label, string Url, string? VisibleWhen = null) : CardElement(VisibleWhen);
public sealed record SourceStatusElement(string SourceId) : CardElement(null);   // „CRM: wird geladen …" / „Zeitüberschreitung"
public abstract record CardAction;
public sealed record OpenUrlAction(string UrlTemplate) : CardAction;    // nur http/https; Host muss zur Quelle passen
public sealed record DialAction(string Number) : CardAction;
public sealed record CopyAction(string Value) : CardAction;

// Aufgelöstes Modell (Ausgabe der CardLayoutEngine, Eingabe der CardView) — keine Ausdrücke mehr, nur Werte
public sealed record CardModel(string DefinitionId, IReadOnlyList<CardSectionModel> Sections);
public sealed record CardSectionModel(string Id, string? Title, bool Visible, IReadOnlyList<CardRowModel> Rows);
public sealed record CardRowModel(IReadOnlyList<CardColumnModel> Columns);
public sealed record CardColumnModel(int Span, IReadOnlyList<CardElementModel> Elements);
public abstract record CardElementModel(string Key, bool Visible);       // Key: stabil je Definitionselement → Renderer kann Instanzen wiederverwenden
public sealed record TextModel(string Key, bool Visible, string Text, TextStyle Style, int MaxLines) : CardElementModel(Key, Visible);
// … FieldModel, BadgeModel(Text, Tone), DividerModel, ButtonModel(Label, Enabled, Action), LinkModel(Label, Uri), SourceStatusModel(SourceId, State, Message)
```

Spans werden auf ein **6er-Raster** bezogen (Summe je Zeile ≤ 6); die `CardView` klappt bei Inhaltsbreite unter 340 px jede Zeile auf eine Spalte um. Ein 12er-Raster ist bei 400 px Fensterbreite nicht bedienbar.

### C.7 Zusammenführen

```csharp
public interface IContactMerger
{
    /// Führt einen neuen Treffersatz in den bestehenden ein; liefert die neue Liste und welche Zeilen sich geändert haben.
    MergeResult Merge(IReadOnlyList<Contact> existing, IReadOnlyList<Contact> incoming, MergePolicy policy);
}
```

---

## D. Konfigurationsmodell

### D.1 Ablage

**Eigene Datei** `%APPDATA%\nipp\integrations.json` mit eigenem `IntegrationConfigStore` — nicht Teil von `settings.json`.

Gründe:
1. `SettingsService.Changed` reicht bis in den SDK-Core, die Registrierung und das Besetztlampenfeld (A.7). Eine Mapping-Korrektur darf das nicht auslösen.
2. Die Datei ist ein **Administrations-Artefakt**: gross, versioniert, wird pro Kunde gepflegt und verteilt. Sie hat einen anderen Lebenszyklus als „Lautstärke 72".
3. Verteilung: das Provisionierungsprofil bekommt **einen** Verweis (`<integrations src="https://…/integrations.json" sha256="…"/>`) statt Hunderten `<set>`-Zeilen.
4. `Export/Import/Reset` der Einstellungen bleiben unberührt; Integrationen haben eigene Schaltflächen.

Der Store übernimmt die Muster von `SettingsService`: atomares Schreiben über `.tmp`, kaputte Datei beiseitelegen (`.kaputt-<Zeit>`), `SchemaVersion` + `Migrate`, Pfad per Konstruktor (Testisolation), `Changed`-Ereignis mit **einem** Empfänger (Registry).

**Geheimnisse** stehen nie in der Datei. Ein Feld wie `apiKey` ist in der Datei ein **Verweis** `{"secretRef": "crm.apiKey"}`; der Wert liegt im bestehenden `SecretStore` unter dem Schlüssel `integration:crm.apiKey`. Der Store validiert beim Laden, welche Verweise **keinen** hinterlegten Wert haben, und meldet das als Zustand `Skipped` mit Text „API-Schlüssel für Muster-CRM fehlt. In den Einstellungen unter Integrationen eintragen." — statt einen Request mit leerem Header abzusetzen.

### D.2 Beispiel `integrations.json`

```jsonc
{
  "schemaVersion": 1,
  "callerLookup": {
    "enabled": true,
    "lookupIncoming": true,
    "lookupOutgoing": true,
    "lookupInternalNumbers": false,     // 151 geht nicht ans CRM
    "cacheSeconds": 300,
    "cacheMaxEntries": 200
  },
  "contactSearch": {
    "debounceMs": 300,
    "minQueryLength": 2,
    "resultLimitPerSource": 25,
    "totalLimit": 100,
    "cacheSeconds": 60,
    "merge": { "enabled": true, "byPhone": true, "byEmail": true, "byNameAndCompany": false }
  },
  "dataSources": [
    {
      "id": "crm",
      "displayName": "Muster-CRM",
      "type": "http",
      "enabled": true,
      "priority": 10,
      "http": {
        "baseUrl": "https://crm.muster.ch/api/v2",
        "timeoutMs": 1500,
        "maxResponseBytes": 1048576,
        "allowInsecureHttp": false,
        "headers": { "Accept": "application/json" },
        "auth": { "type": "apiKey", "in": "header", "name": "X-Api-Key", "secretRef": "crm.apiKey" }
      },
      "lookupByPhone": {
        "request": {
          "method": "GET",
          "path": "/contacts/by-phone",
          "query": { "phone": "{{number.e164}}" }
        },
        "emptyWhen": "status == 404 || isEmpty($.contact)",
        "mapping": {
          "customerName":   { "path": "$.contact.fullName" },
          "company":        { "path": "$.contact.company.name" },
          "accountManager": { "path": "$.contact.owner.name" },
          "customerId":     { "path": "$.contact.id" },
          "vip":            { "path": "$.contact.tags[?(@ == 'VIP')]", "as": "boolean" },
          "label":          { "expr": "concat(customerName, ' · ', company)" }
        }
      },
      "searchContacts": {
        "request": {
          "method": "GET",
          "path": "/contacts",
          "query": { "q": "{{query.text}}", "limit": "{{query.limit}}" }
        },
        "itemsPath": "$.items[*]",
        "mapping": {
          "externalId":  { "path": "$.id" },
          "displayName": { "expr": "concat($.firstName, ' ', $.lastName)" },
          "company":     { "path": "$.company.name" },
          "email":       { "path": "$.email" },
          "phones": [
            { "path": "$.phoneBusiness", "kind": "business" },
            { "path": "$.phoneMobile",   "kind": "mobile" }
          ]
        }
      },
      "openContact": { "urlTemplate": "https://crm.muster.ch/contacts/{{externalId}}" }
    },
    {
      "id": "erp",
      "displayName": "ERP",
      "type": "http",
      "http": {
        "baseUrl": "https://erp.muster.ch",
        "timeoutMs": 2500,
        "auth": { "type": "basic", "usernameSecretRef": "erp.user", "passwordSecretRef": "erp.password" }
      },
      "lookupByPhone": {
        "request": { "method": "POST", "path": "/api/debtor/lookup",
                     "body": { "phone": "{{number.national}}" } },
        "mapping": {
          "customerNumber": { "path": "$.debtor.number" },
          "openOrders":     { "path": "$.orders.open", "as": "number" },
          "revenue":        { "path": "$.statistics.revenue", "as": "number" },
          "customerLabel":  { "expr": "concat(customerNumber, ' - ', $.debtor.name)" }
        }
      }
    }
  ],
  "cards": [
    {
      "schemaVersion": 1,
      "id": "active-default",
      "name": "Gespräch",
      "kind": "activeExpanded",
      "sections": [
        {
          "id": "kopf",
          "rows": [
            { "columns": [ { "span": 6, "elements": [
              { "type": "text", "value": "coalesce(crm.customerName, contacts.displayName, formatPhone(number.e164))", "style": "title" },
              { "type": "text", "value": "coalesce(crm.company, contacts.company)", "style": "subtitle", "visibleWhen": "!isEmpty(coalesce(crm.company, contacts.company))" }
            ] } ] },
            { "columns": [
              { "span": 3, "elements": [ { "type": "field", "label": "Kundennummer", "value": "erp.customerNumber" } ] },
              { "span": 3, "elements": [ { "type": "field", "label": "Offene Aufträge", "value": "erp.openOrders" } ] }
            ] },
            { "columns": [ { "span": 6, "elements": [
              { "type": "field", "label": "Account Manager", "value": "crm.accountManager" },
              { "type": "badge", "text": "'VIP'", "tone": "'warning'", "visibleWhen": "crm.vip == true" }
            ] } ] }
          ]
        },
        {
          "id": "aktionen",
          "rows": [ { "columns": [ { "span": 6, "elements": [
            { "type": "button", "label": "Kontakt im CRM öffnen",
              "action": { "type": "openUrl", "url": "https://crm.muster.ch/contacts/{{crm.customerId}}" },
              "enabledWhen": "!isEmpty(crm.customerId)" },
            { "type": "sourceStatus", "source": "crm" },
            { "type": "sourceStatus", "source": "erp" }
          ] } ] } ]
        }
      ]
    }
  ]
}
```

### D.3 Authentifizierung

| `auth.type` | Felder | Umsetzung |
|---|---|---|
| `none` | — | — |
| `apiKey` | `in: header\|query`, `name`, `secretRef` | Header oder Query-Parameter; **Query-Variante warnt** im Validator (landet in Server-Logs) |
| `bearer` | `secretRef` | `Authorization: Bearer …` |
| `basic` | `usernameSecretRef`, `passwordSecretRef` | `Authorization: Basic …` |
| `oauth2ClientCredentials` | *später*: `tokenUrl`, `clientId`, `clientSecretRef`, `scope` | Token-Cache im Speicher, Ablauf beachten, ein Refresh je Quelle gleichzeitig |

Alle Geheimnisse gehen über `SecretStore` (DPAPI, Benutzerkonto, Rechner). Folge: ein Geheimnis muss **auf jedem Gerät einmal eingetragen** werden, oder das Provisionierungsprofil liefert es über https mit — genau wie heute das SIP-Passwort, mit demselben Hinweis in `docs/provisioning.md`. Der Windows Credential Manager bringt gegenüber DPAPI hier keinen Vorteil (gleicher Schutzumfang, zusätzliche API) und wird nicht eingeführt.

### D.4 Mapping

**Technologie: JSONPath nach RFC 9535** über das Paket **`JsonPath.Net`** (json-everything, MIT, auf `System.Text.Json.Nodes`). Gründe: standardkonform, keine Newtonsoft-Abhängigkeit, Filterausdrücke (`[?(@.type == 'mobile')]`) inklusive, aktiv gepflegt. Die Abhängigkeit bleibt auf `Services/Integrations/Mapping/` beschränkt (Architekturtest). **Rückfall**, falls das Paket nicht gewünscht ist: ein eigener Mini-Pfad-Auswerter für `$.a.b[0].c` und `[*]` ohne Filter — ein Tag Arbeit, weniger Ausdruckskraft.

Ein Mapping-Eintrag ist entweder `path` (JSONPath, optional `as: text|number|boolean|date`, optional `format`) oder `expr` (Expression über bereits gemappte Felder derselben Quelle und rohe Pfade `$.…`). Reihenfolge: erst alle `path`-Einträge, dann `expr`-Einträge in Dateireihenfolge. Ein Fehler in einem Eintrag setzt **dieses Feld** auf `Null` und hängt eine Diagnosezeile an; die Quelle bleibt `Success`.

Mehrfachtreffer eines Pfads: bei `as` skalar → erster Treffer; `as: list` → Liste. `emptyWhen` (Expression über `status` und den Rohkörper) erlaubt, eine 200-Antwort mit leerem Körper als `Empty` zu werten.

### D.5 Expression Engine

Eigene Implementierung (≈ 600 Zeilen mit Tests), **keine** Skriptsprache, **keine** `DynamicExpresso`/Roslyn-Abhängigkeit:

- **Literale:** `'Text'`, `123`, `1.5`, `true`, `false`, `null`
- **Referenzen:** `crm.customerName`, `number.e164`, `query.text`, `status`; innerhalb eines Mappings zusätzlich `$.pfad` (JSONPath auf den Rohkörper)
- **Operatoren:** `== != < <= > >=`, `&& || !`, `+` nur für Zahlen; Textverkettung über `concat`
- **Bedingung:** `if(cond, a, b)`
- **Funktionen (Whitelist):** `concat`, `coalesce`, `isEmpty`, `upper`, `lower`, `trim`, `substring`, `contains`, `startsWith`, `join`, `count`, `formatDate(value, 'dd.MM.yyyy')`, `formatPhone(value)` (→ `PhoneNumberFormat.ForDisplay`), `formatNumber(value, 'N0')`
- **Vorlagen:** `{{ … }}` in Request-Feldern und URL-Vorlagen ist dieselbe Engine; in URLs wird das Ergebnis **URL-kodiert** eingesetzt, Query-Werte über `Uri.EscapeDataString`

Sicherheits- und Wartbarkeitsgrenzen: Tokenizer + rekursiv absteigender Parser + Baumauswerter, alles rein und deterministisch; Grenzen: 512 Token, Tiefe 32, Ergebnistext 4 KB, keine Rekursion in Funktionen, keine Zuweisung, keine Schleife, kein Zugriff auf Typen, Dateien, Umgebung; Parsefehler werden **beim Validieren** mit Position gemeldet, Laufzeitfehler liefern `Null` plus Diagnose. Vergleiche über gemischte Typen (`'3' == 3`) sind `false` — kein implizites Konvertieren, das ist die Quelle der meisten Überraschungen.

### D.6 Timeouts, Grenzen, Wiederholungen

| Wert | Standard | Wo |
|---|---|---|
| Zeitgrenze je Quelle (Lookup) | 1500 ms | `http.timeoutMs` |
| Zeitgrenze je Quelle (Suche) | 3000 ms | `searchContacts.timeoutMs`, sonst `http.timeoutMs` |
| Antwortgrösse | 1 MB | `http.maxResponseBytes` — Stream wird nach dem Limit **abgebrochen**, nicht erst danach geprüft |
| Wiederholung | **keine** beim Lookup (Zeitbudget), **eine** bei Suche nur für `GET` und nur bei Netzfehler/5xx, 200 ms Abstand | `http.retry` |
| Schutzschalter | nach 5 Fehlern in Folge 60 s `Skipped` | Registry/Health, Phase I8 |
| 429 | `Retry-After` beachten, Quelle bis dahin `Skipped` | Phase I8 |
| Verbindungen | ein `SocketsHttpHandler` für alle Quellen, `PooledConnectionLifetime` 5 min | `IntegrationHttpClient` |

### D.7 Caching

| Cache | Schlüssel | TTL | Grösse | Wo |
|---|---|---|---|---|
| Anruferkontext | `(sourceId, number.digits)` | 300 s | 200 | Speicher, nur `Success`/`Empty` |
| Kontaktsuche | `(sourceId, normalisierte Anfrage)` | 60 s | 100 | Speicher |
| Lokale Kontakte | bestehender `ContactStore` | 12 h | — | unverändert |

Kein Cache auf der Platte: personenbezogene Daten fremder Systeme bleiben nur so lange im Speicher, wie die Karte sie plausibel braucht; **Änderung der Konfiguration leert beide Caches.** Ein zweiter Anruf derselben Nummer innerhalb der TTL erscheint aus dem Cache mit Kennzeichnung `fromCache`.

### D.8 Validierung

`IntegrationConfigValidator` liefert `ValidationIssue(Path, Severity, Message, Hint)` mit Texten nach §15. Geprüft wird beim Laden, beim Speichern in den Einstellungen und beim Import:

- Ids eindeutig, `^[a-z][a-z0-9_-]{1,31}$`, nicht `contacts`, `number`, `query`, `status` (reservierte Namespaces)
- `baseUrl` absolut, `https` (oder `allowInsecureHttp` ausdrücklich, wie ADR-012), kein Benutzername in der URL
- `secretRef` vorhanden → sonst Zustand `Skipped` mit Hinweis
- Request-Vorlagen und Ausdrücke parsen; referenzierte Namespaces existieren; `openContact.urlTemplate` gleicher Host wie `baseUrl` oder ausdrücklich `allowedHosts`
- Card: Span-Summe je Zeile ≤ 6; Elementtypen bekannt (unbekannt → Warnung, Renderer zeigt Platzhalter); referenzierte `sourceStatus`-Quellen existieren
- Zeitgrenzen 200–10 000 ms; Antwortgrösse 4 KB–8 MB

---

## E. Datenfluss

### E.1 Eingehender (und ausgehender) Anruf

```
UI-Thread (Iterate)                      Threadpool / I/O                     UI-Thread (Post)
──────────────────                       ────────────────                     ────────────────
SipService.OnBridgeCallStateChanged
  └ CallStateChanged(Previous=null)
      └ CallerContextService.OnCallState
          • Handle unbekannt? → Session anlegen
          • PhoneNumberKey.From(RemoteNumber)
          • Provider filtern (AppliesTo, Enabled, Health)
          • Snapshot v0: alle Loading, contacts sofort
            Success (ClipResolver, synchron, < 1 ms)
          • ContextChanged(v0)  ──────────────────────────────────────────►  CallerCardViewModel
          • _ = RunAsync(session)   (kein await hier!)                        → CardLayoutEngine.Build
                └── je Provider: Task mit eigenem CTS ─►  HttpClient.SendAsync   → CardView zeigt Name/Loading
                    linked(session.Token).CancelAfter(Timeout)   (I/O-Thread)
                                                          MappingEngine → Fragment
                                                          Cache.Set
                                                          _ui.Post(…) ───────────►  Snapshot v1 = v0 with { crm = Success }
                                                                                    ContextChanged(v1) → Card aktualisiert (nur crm-Elemente)
                                                          (ERP nach 1.5 s Timeout)
                                                          _ui.Post(…) ───────────►  Snapshot v2 (erp = Timeout, Text „ERP antwortet nicht (1.5 s)")
CallStateChanged(Ended)
  └ CallerContextService: session.Cancel(); Snapshot bleibt für die Anzeige, Session wird nach Navigation zurück entfernt
```

**Regeln, die der Fluss einhält:**

1. **Sofort sichtbar:** Die Gesprächsansicht erscheint durch `MainWindow` wie heute; die Karte zeigt in derselben Iteration den lokal aufgelösten Namen (oder die formatierte Nummer) und je Quelle „wird geladen". Kein Warten auf irgendein Netz.
2. **Nichts Blockierendes im Callback:** `OnCallState` legt Zustand an und startet Tasks. Der einzige synchrone Provider (`contacts`) arbeitet auf dem bestehenden In-Memory-Index.
3. **Isolation je Quelle:** eigener `CancellationTokenSource` mit `CancelAfter(provider.Timeout)`, eigener `try/catch` um alles (`HttpRequestException`, `TaskCanceledException`, `JsonException`, Mapping-Fehler). Unterscheidung: abgebrochen **und** Session lebt → `Timeout`; Session abgebrochen → kein Ereignis mehr. Eine Ausnahme im Provider wird zu `Error` mit Text, nie zu einem Absturz; der Orchestrator selbst hat einen letzten `catch`, der protokolliert.
4. **Rückkehr auf den UI-Thread** über den im Konstruktor erfassten `SynchronizationContext` (Muster `ShellViewModel.OnUiThread`). Snapshots sind unveränderlich; die Card rechnet nur mit dem letzten.
5. **Richtung:** konfigurierbar (`lookupIncoming`, `lookupOutgoing`). Bei ausgehenden Anrufen ist die Nummer schon normalisiert; bei eingehenden wird der rohe `Username` normalisiert. `IsInternal` → nur lokale Quellen, es sei denn `lookupInternalNumbers`.
6. **Zwei Gespräche:** Sessions sind je `CallHandle`; `CallerCardViewModel` folgt `ActiveCallViewModel.SelectedCall`.
7. **Toast:** unverändert `DisplayLabel` im MVP. Anreicherung (zweite Zeile mit Firma aus dem ersten `Success`) ist mit `AppNotificationManager.Default.Show` unter gleichem `Tag` möglich, aber erst nach Abnahme von T06 (REVIEW.md §8) sinnvoll.
8. **Verlauf:** `ShellViewModel.RecordInHistory` löst weiter über `ClipResolver` auf; **zusätzlich** darf es auf `crm.customerName` zurückfallen, wenn der Snapshot es hat (kleine Änderung, grosser Alltagsnutzen: die Anrufliste zeigt den CRM-Namen).

### E.2 Kontaktsuche

```
UI-Thread                                     Threadpool / I/O                   UI-Thread (Post)
─────────                                     ────────────────                   ────────────────
Suchfeld TextChanged → ShellViewModel.ContactQuery
  └ ContactSearchService.Query(text)
      • text.Trim(); < minQueryLength → Clear, Generation++
      • Debounce: laufenden Delay abbrechen, neuen Task.Delay(300 ms, cts) starten
      (nach 300 ms, Fortsetzung auf _ui)
      • generation = ++_generation; alte Provider-CTS abbrechen
      • lokale Provider synchron: Team/Outlook aus ContactStore.Search → ResultsChanged(gen, "outlook", …) sofort
      • Remote-Provider parallel:  ───────────►  HttpContactSearchProvider.SearchAsync (Timeout 3 s)
                                                 Mapping itemsPath → List<Contact>
                                                 Cache.Set
                                                 _ui.Post ───────────────────────►  if (gen != _generation) return;   // Race-Schutz
                                                                                   Merger.Merge(existing, incoming)
                                                                                   ResultsChanged(gen, "crm", state)
```

**Race-Beispiel aus dem Auftrag:** „Hans" (Gen 1) startet CRM-Request A; „Hansi" (Gen 2) bricht A's CTS ab und startet B. Kommt A trotzdem zurück (Abbruch greift nicht sofort), trägt seine Fortsetzung `gen = 1`, und `1 != 2` verwirft ihn. Die Generation ist ein `int` auf dem UI-Thread, kein Lock nötig.

**Anzeige:** Die Ergebnisliste wird **inkrementell ergänzt**, nicht neu gebaut (Lehre `ContactRow`/`SameContacts`: Bildlauf und Auswahl erhalten). Jede Zeile trägt ein Herkunfts-Badge („CRM", „Outlook", „Team"), zusammengeführte Zeilen mehrere. Über der Liste steht eine schmale Zustandszeile je Remote-Quelle („CRM · 12 Treffer", „ERP · antwortet nicht"). Lokale Treffer stehen zuerst, dann Remote in Prioritätsreihenfolge, innerhalb alphabetisch.

**Ort in der Oberfläche (Empfehlung, offene Frage I.3.1):** Der Kontakte-Tab bekommt ein Suchfeld **zurück**, das nur erscheint, wenn mindestens eine Quelle mit `searchContacts` aktiv ist. Solange es leer ist, zeigt der Tab die heutigen Abschnitte Team/Outlook; mit Text ersetzt die Ergebnisliste beide Abschnitte. Die Vorschlagsliste unter dem Nummernfeld bleibt lokal, synchron und auf fünf begrenzt (§8.1) — dort ist Latenz nicht akzeptabel.

### E.3 Fehlerbilder und ihre Darstellung

| Was passiert | Zustand | Text (Beispiel, §15) |
|---|---|---|
| DNS/Verbindung scheitert | `Error` | „Muster-CRM ist nicht erreichbar. Netzwerk und Adresse prüfen." |
| Zeitgrenze | `Timeout` | „Muster-CRM antwortet nicht (mehr als 1.5 s)." |
| 401/403 | `Error` | „Muster-CRM lehnt die Anmeldung ab (401). API-Schlüssel in den Einstellungen prüfen." |
| 404 oder `emptyWhen` | `Empty` | „Keine Daten im Muster-CRM." (Card zeigt `EmptyText`) |
| 429 | `Skipped` bis Retry-After | „Muster-CRM begrenzt Anfragen. Nächster Versuch in 30 s." |
| Antwort > Limit | `Error` | „Antwort von Muster-CRM ist grösser als 1 MB und wurde verworfen." |
| Kein JSON / Mapping-Fehler | `Error` bzw. `Success` mit Feld `Null` | „Die Antwort von Muster-CRM hat nicht die erwartete Form (Feld customerName)." |
| `secretRef` fehlt | `Skipped` | „API-Schlüssel für Muster-CRM fehlt. In den Einstellungen unter Integrationen eintragen." |
| Quelle deaktiviert | nicht im Snapshot | — |

Ins Protokoll gehen: Quelle, Zustand, HTTP-Status, Dauer, Grösse. **Nie**: Nummer, Suchtext, URL mit Query, Header, Antwortkörper. Auf Stufe Debug zusätzlich der **Pfad** ohne Query und die Anzahl gemappter Felder.

---

## F. Änderungen im bestehenden Code

### F.1 Angepasste Klassen

| Datei | Änderung | Risiko |
|---|---|---|
| `src/Nipp.App/App.xaml.cs` | `ConfigureServices`: Registrierung der Integrationsdienste (Config-Store, Connectoren als `IDataSourceConnector`, Registry, HTTP-Client, Mapping, Expressions, Card-Engine, beide Orchestratoren, `CallerCardViewModel`, `IntegrationSettingsViewModel`). `StartShellServices`: `CallerContextService.Start()`. `ExitApplication`: `Dispose` in bestehender Reihenfolge. | gering — reines Anfügen |
| `src/Nipp.Core/Services/Contacts/Contact.cs` | `ContactSourceKind.External`; neue optionale Felder `SourceId`, `Email`, `ExternalId`, `OpenUri`, `Origins` (siehe C.5). | mittel — alle `switch` über das Enum prüfen (`ContactRow.SourceLabel` hat `_ =>`, `ClipResolver.FindIn` iteriert nach Kind) |
| `src/Nipp.Core/Services/Contacts/ClipResolver.cs` | `Resolve`: Reihenfolge Team → Outlook → External (Snapshot-Kontakte aus externen Quellen gibt es im MVP nicht; die Zeile ist Vorsorge). Sonst unverändert; wird von `LocalContactsContextProvider` genutzt. | gering |
| `src/Nipp.Core/Services/Contacts/ContactStore.cs` | keine Verhaltensänderung. `Search` wird vom `LocalSnapshotSearchProvider` aufgerufen. Optional: `Search` bekommt `limit`. | gering |
| `src/Nipp.Core/Services/Contacts/OutlookContactSource.cs` | **unverändert.** | — |
| `src/Nipp.Core/ViewModels/ShellViewModel.cs` | Neue Eigenschaften: `ContactQuery`, `SearchResults` (ObservableCollection\<ContactRow\>), `SearchSourceStates`, `IsSearching`, `HasRemoteSearch`. `RecordInHistory` und `ResolveParty`: Rückfall auf Kontext-Namen. `OnSettingsChanged` bleibt. Datei ist mit 1074 Zeilen gross — die Suche kommt als **`partial`-Datei** `ShellViewModel.Search.cs`. | mittel — Refresh-Muster in `ShellPage` muss mitziehen |
| `src/Nipp.Core/ViewModels/ContactRow.cs` | `SourceLabel` aus `Contact.SourceId`/`DisplayName` der Quelle; `OriginLabels`; `CanOpen`. `HasPresence` unverändert (nur Team). | gering |
| `src/Nipp.App/Views/ShellPage.xaml(.cs)` | Suchfeld über `ContactsBody`, Ergebnisliste als dritte `ListView` in einer eigenen Zeile (Grid `Auto`/`*`, **nicht** in einem ScrollViewer — Lehre Virtualisierung), Zustandszeile, Kontextmenü „Im CRM öffnen". `RefreshContactSections` schaltet Team/Outlook aus, wenn `ContactQuery` nicht leer ist. | mittel — Layoutlogik mit Zeilenhöhen ist schon heute die empfindlichste Stelle der Seite |
| `src/Nipp.App/Views/ActiveCallPage.xaml(.cs)` | Neue Zeile nach `CallHeader`: `CallerCardHost` (`CardView`). `Refresh()` setzt `CardHost.Model = ViewModel.Card` — oder die Page holt zusätzlich `CallerCardViewModel` aus DI und bindet direkt. `PartyText` zeigt den **aufgelösten** Namen (heute `DisplayLabel`). Abonnements in `OnLoaded`/`OnUnloaded` (Lehre NavigationCacheMode). | mittel |
| `src/Nipp.Core/ViewModels/ActiveCallViewModel.cs` | Kleines Ereignis `SelectedCallChanged` oder Nutzung von `PropertyChanged(SelectedCall)` durch `CallerCardViewModel`. | gering |
| `src/Nipp.App/Views/Settings/SettingsPage.xaml(.cs)` | Neue Gruppe „Integrationen" (Expander) mit eigenem `IntegrationSettingsViewModel`; Policy-Pfad `integrations`. **`SettingsViewModel` (1107 Zeilen) wird nicht erweitert.** | gering |
| `src/Nipp.Core/Diagnostics/DiagnosticsBundle.cs` | Neuer Eintrag `integrationen.json` nach Whitelist (Ids, Typ, Host der `baseUrl`, Zeitgrenzen, Fähigkeiten, `secretRef`-Namen mit „(gesetzt)/(fehlt)", letzter Zustand, letzte Fehlertexte). **Keine** Header, keine Query-Vorlagen mit Werten, keine Card-Inhalte. | gering |
| `src/Nipp.Core/Services/Settings/ProvisioningProfile.cs`, `ProvisioningParser.cs`, `ProvisioningService.cs`, `src/Nipp.Provisioning/Program.cs` | `<integrations src="https://…" sha256="…"/>` (oder Inline-JSON in CDATA) → `IntegrationConfigStore.ImportFromProfileAsync`. Lock-Pfad `integrations`. Nur https, wie ADR-012. | mittel — Provisionierung liegt im Startpfad; Abruf der Integrationsdatei **asynchron nach dem Start**, nie im 5-s-Budget |
| `src/Nipp.Core/Services/Windows/WindowsIntegration.cs` | `IntegrationLog` → `WindowsIntegrationLog` (Namenskollision, A.9). 16 Aufrufstellen, davon 8 in `GlobalHotkeyService.cs` — die Klasse ist `partial` und wird geteilt. | gering, mechanisch |
| `src/Nipp.App/Windows/ToastService.cs` | **MVP unverändert.** Später: zweite Zeile aus Kontext. | — |
| `tests/Nipp.Architecture.Tests` | Neuer `IntegrationBoundaryTests` (siehe H.3); `XamlResourceTests` deckt neue XAML automatisch ab. | — |
| `CLAUDE.md`, `README.md`, `NIPP-BUILD.md` (§21), `docs/decisions.md` (ADR-015), `docs/test-matrix.md` (T40 ff.) | Dokumentation. | — |

### F.2 Neue Klassen und Dateien

```
src/Nipp.Core/Services/Integrations/
  Config/    IntegrationConfig.cs (Records aus D.2), IntegrationConfigStore.cs, IntegrationConfigValidator.cs,
             ValidationIssue.cs, IntegrationRegistry.cs, IntegrationHealth.cs
  Secrets/   IntegrationSecrets.cs               (dünne Hülle über SecretStore mit Präfix "integration:")
  Phone/     PhoneNumberKey.cs
  Http/      IntegrationHttpClient.cs, HttpRequestTemplate.cs, HttpAuthenticator.cs, ResponseLimitStream.cs,
             HttpDataSourceConnector.cs, HttpCallerContextProvider.cs, HttpContactSearchProvider.cs
  Mapping/   MappingEngine.cs, MappingDefinition.cs, ContextValue.cs, JsonPathBinding.cs   (einziger Ort mit `using Json.Path`)
  Expressions/ ExpressionTokenizer.cs, ExpressionParser.cs, ExpressionEvaluator.cs, ExpressionFunctions.cs,
             TemplateRenderer.cs ({{ }})
  Context/   ICallerContextProvider.cs, ContextFragment.cs, ContextSnapshot.cs, CallerContextService.cs,
             CallerContextCache.cs, LocalContactsConnector.cs, LocalContactsContextProvider.cs
  Search/    IContactSearchProvider.cs, ContactQuery.cs, ContactSearchPage.cs, ContactSearchService.cs,
             ContactSearchCache.cs, LocalSnapshotSearchProvider.cs, IContactMerger.cs, ContactMerger.cs
  Cards/     CardDefinition.cs, CardModel.cs, CardLayoutEngine.cs, CardDefinitionValidator.cs, DefaultCards.cs
  IntegrationLogs.cs   (ConnectorLog 3600–3699, CallerContextLog 3700–3799, ContactSearchLog 3800–3899, CardLog 3900–3999)

src/Nipp.Core/ViewModels/
  CallerCardViewModel.cs, IntegrationSettingsViewModel.cs, ShellViewModel.Search.cs, SearchSourceRow.cs

src/Nipp.App/Controls/Cards/
  CardView.cs (ContentControl, baut aus CardModel), CardElementFactory.cs, Cards.xaml (Styles → Generic.xaml)
src/Nipp.App/Views/Settings/
  IntegrationsGroup (Teil der SettingsPage.xaml) — Liste, Ein/Aus, „Verbindung testen", „Antwort anzeigen", Import/Export

tools/
  Nipp.MockApi/   kleines Kestrel-Projekt: /contacts/by-phone, /contacts, /api/debtor/lookup;
                  ?delay=1500&status=500&size=2000000 für Fehlerfälle. Nur für Tests am Gerät; nicht Teil der Solution-Auslieferung.

tests/Nipp.Core.Tests/Services/Integrations/**   (siehe H)
```

### F.3 Ersetzt

Nichts wird ersatzlos entfernt. `ContactStore.Search` bleibt und wird zur lokalen Suchquelle; `ContactSourceKind` bleibt; `ClipResolver` bleibt die schnelle Ebene.

### F.4 Weiterverwendet

`SecretStore`, das Speicher-Muster von `SettingsService`, `NumberNormalizer`/`ClipResolver`/`PhoneNumberFormat`, `PolicyService` + `SettingCard`, `Tokens.xaml`, `[LoggerMessage]`-Konvention, das `SynchronizationContext`-Muster, `ProvisioningParser`-Sicherheitsmuster, `DiagnosticsBundle`-Whitelist, `XamlResourceTests`, `TestIsolationTests`, das Fehlertext-Muster nach §15.

### F.5 Migrationsrisiken

1. **`ContactSourceKind`-Erweiterung**: jedes `switch` prüfen; `ClipResolver.FindIn` bekommt dritten Durchlauf.
2. **`ShellPage`-Layout**: dritte Liste ohne ScrollViewer; am Gerät auf Ruckeln beim Tab-Wechsel prüfen (Lehre `CLAUDE.md`).
3. **Verlaufssemantik**: `DisplayName` aus dem CRM in `history.db` — gewollt, aber dokumentieren (der Name stammt dann aus einem Fremdsystem zum Zeitpunkt des Anrufs).
4. **`SettingsService.Changed`-Kette bleibt unangetastet** — die Integrationskonfiguration darf **nie** über `Save()` laufen.
5. **`OutlookContactSource` bleibt unangetastet** — die Migration ist ein Adapter, kein Umbau.
6. **`IntegrationLog`-Umbenennung**: mechanisch, aber im selben Commit wie die neuen Log-Klassen, sonst Compilerfehler.
7. **Startpfad**: nichts Neues **vor** `StartTelephony`; Integrationskonfiguration wird in `StartShellServices` geladen (Dateizugriff, wenige ms) und der Provisionierungsabruf der Integrationsdatei läuft **nach** dem ersten Zeichnen im Hintergrund (wie `StartContacts`).

---

## G. Umsetzung in Phasen

Die Reihenfolge folgt der Codebasis: **Mapping vor Lookup**, weil ein Lookup ohne Mapping nur Roh-JSON liefert und Mapping/Expressions rein, threadfrei und beidseitig gebraucht sind. **Lokale Suchmigration vor Remote-Suche**, weil sich so die Outlook-Migration ohne Netz auf Verhaltensgleichheit prüfen lässt. Aufwände sind Netto-Entwicklungstage wie in `IMPLEMENTATION-PLAN.md`.

### I0 — Auftrag und Gerüst (0.5–1 T) — **erledigt 06.09.2026**

- **Ziel:** Formale Grundlage und Leitplanken, bevor Code entsteht.
- **Umgesetzt:**
  - `NIPP-BUILD.md` **Rev. 6** mit **§21 Integrationsplattform** (Umfang, acht unverhandelbare Grenzen, Konfiguration, die Entscheidungen aus I.3, Nicht-Umfang) und Eintrag im Änderungsprotokoll §19.
  - `docs/decisions.md`: **ADR-015** (generische Plattform statt fester CRM-Anbindung, mit den acht Entscheidungen), **ADR-016** (`JsonPath.Net` als Paket, Ausdrücke aus eigener Hand), **ADR-017** (eigene Konfigurationsdatei).
  - `docs/licensing.md`: Abschnitt „Lizenzen der übrigen Abhängigkeiten" mit der Regel *permissiv oder gar nicht*; `JsonPath.Net` als MIT eingetragen.
  - `CLAUDE.md`: die neue Schichtgrenze und der Hinweis zur Log-Klasse unter „Grenzen".
  - **Umbenennung** `IntegrationLog` → `WindowsIntegrationLog`: 16 Aufrufstellen in `WindowsIntegration.cs` und `GlobalHotkeyService.cs`, die sich die `partial`-Klasse teilen. Der Name `IntegrationLog` ist damit für die Plattform frei.
  - `tests/Nipp.Architecture.Tests/IntegrationBoundaryTests.cs` mit vier Prüfungen, dazu `RepositoryFiles.cs` als gemeinsame Grundlage für künftige Quelltext-Scans.
  - **Ankerdatei** `Services/Integrations/Phone/PhoneNumberKey.cs` samt 23 Tests. Bewusst aus I1 vorgezogen: ein Architekturtest, der über einem leeren Ordner läuft, bewacht nichts, und ein leerer Ordner lässt sich nicht versionieren. Die Klasse ist rein, ohne Zustand und entscheidet als Einzige, ob eine Nummer überhaupt nach aussen geht.
- **Nachweis:** die **ganze Solution** baut mit 0 Warnungen und 0 Fehlern. **279 Komponententests** (vorher 256) und **11 Architekturtests** (vorher 7) grün. Die drei neuen Grenzprüfungen wurden mit einer absichtlichen Verletzung gegengeprüft und schlagen mit Datei und Zeile an — SDK-Verweis, WinUI-Verweis, JSONPath ausserhalb der Mapping-Schicht und die verbotenen Protokollplatzhalter `{Number}` und `{Url}`.
- **Nichts offen.** Der Solution-Build brauchte einen zweiten Anlauf, weil eine laufende nipp-Instanz `Nipp.Core.dll` sperrte (MSB3027, siehe `CLAUDE.md`); nach dem Beenden lief er durch.

### I1 — Integration Core: Konfiguration, Geheimnisse, HTTP (3–4 T) — **erledigt 06.09.2026**

- **Ziel:** Eine Quelle lässt sich beschreiben, validieren, speichern; eine Anfrage lässt sich sicher absetzen.
- **Umgesetzt:**
  - `Config/IntegrationConfig.cs` — das Schema aus D.2 als Records: Quellen, Verbindung, Anmeldung, Fähigkeiten. Welche Fähigkeit eine Quelle hat, steht in der Datei, nicht im Code.
  - `Config/IntegrationConfigStore.cs` — eigene Datei `integrations.json` (ADR-017), atomares Schreiben, kaputte Datei beiseite, Schemaversion mit Migrationshaken, Pfad über den Konstruktor. `UsableSources` liefert, was eingeschaltet und fehlerfrei ist.
  - `Config/IntegrationConfigValidator.cs` — sammelt **alle** Befunde, mit Pfad und Abhilfe nach §15: Kennungen (auch die reservierten wie `number`), https-Zwang, Zugangsdaten in der Adresse, Zeit- und Grössengrenzen, Anfragevorlagen, Mappings, Öffnen-Adressen.
  - `Secrets/IntegrationSecrets.cs` — dünne Hülle über dem bestehenden `SecretStore` mit dem Präfix `integration:`, damit eine Integrationskonfiguration kein SIP-Passwort überschreiben kann.
  - `Http/HttpRequestTemplate.cs` und `Http/IntegrationHttpClient.cs` — Anfragen aus Vorlagen, Anmeldung aus dem Geheimnisspeicher, Zeitgrenze je Quelle, Grössengrenze **beim Lesen**, verständliche Sätze statt Statuszahlen.
  - `IntegrationLogs.cs` (EventId 3600–3617), Registrierung in `App.xaml.cs`, Laden in `StartShellServices`, Freigabe beim Beenden, Eintrag `integrationen.json` im Diagnosepaket nach Whitelist.
- **Tests:** 33 für Speicher und Prüfung, 19 für den HTTP-Zugang. Darunter der Datenschutztest, der die Protokollausgabe einer ganzen Anfrage mitliest und auf Rufnummer, Schlüssel, Adresse und jede Ziffernfolge ab sieben Stellen prüft.
- **Akzeptanz erreicht:** eine Konfiguration mit einer HTTP-Quelle wird geladen und geprüft; das Diagnosepaket enthält die Quelle ohne Geheimnis, ohne Endpunktpfad und ohne Anfrageparameter.

### I2 — Mapping Engine und Expression Engine (3–4 T) — **erledigt 06.09.2026**

- **Ziel:** Rohes JSON wird zu typisierten Feldern in einem eigenen Namensraum; berechnete Felder und Vorlagen funktionieren.
- **Umgesetzt:**
  - `Context/ContextValue.cs` — geschlossene Werthierarchie (Text, Zahl als `decimal`, Wahrheitswert, Datum, Liste, nichts, Objekt-Marker).
  - `Expressions/*` — Tokenizer, rekursiv absteigender Parser, Auswerter, Funktions-Whitelist, `TemplateRenderer` für `{{ }}`. Grenzen: 512 Zeichen, Tiefe 32, Ergebnistext 4 KB.
  - `Mapping/*` — `JsonPathBinding` als einzige Datei mit `using Json.Path`, `MappingEngine` mit zwei Durchgängen (erst Pfade, dann Ausdrücke) und `MapItems` für Trefferlisten.
- **Tests:** 75 für die Ausdruckssprache, 28 für das Mapping, darunter die CRM- und ERP-Beispiele aus dem Auftrag.
- **Drei Befunde, die erst die Tests gezeigt haben** — sie stehen als Regressionstests im Repo:
  1. **Eine Rufnummer ist keine Zahl.** Der erste Entwurf las Text mit `decimal.TryParse`, und `+41791234567` ist dafür eine gültige Zahl mit Vorzeichen. Eine Rufnummer wurde zu 41'791'234'567: `nummer + 1` ergab ein Ergebnis, `nummer > 5` war wahr, und eine Zahlenformatierung hätte sie gruppiert. Auf einer Karte sieht so ein Fehler plausibel aus. Jetzt gibt es **keine** stillschweigende Umwandlung mehr; wer eine Zahl aus einem Textfeld braucht, schreibt `as: number` ins Mapping.
  2. **Ein Objekt ist nicht dasselbe wie ein fehlendes Feld.** Beide wurden zu „nichts", womit `isEmpty($.contact)` — genau die Regel aus dem Auftrag, mit der eine leere Antwort erkannt wird — **immer** wahr war. Jede Antwort hätte als „nichts gefunden" gegolten. Dafür gibt es jetzt `StructureValue`.
  3. **Ein Datum wird nur in ISO-Form angenommen.** `TryParse` mit der invarianten Kultur nimmt auch `06.09.2026` und liest es als 6. September; in einem anderen Land ist das der 9. Juni. Ein um drei Monate falsches Datum fällt auf einer Karte niemandem auf.
- **Akzeptanz erreicht:** Die Beispiele aus D.2 mappen; ein absichtlich falsches Mapping liefert ein leeres Feld mit Diagnosezeile, nie eine Ausnahme.

### I3 — Anruferkontext (4–5 T) — **erledigt 06.09.2026**

- **Ziel:** Bei einem Anruf erscheint in der Gesprächsansicht sofort der lokale Name; externe Quellen laden nach, mit Zustand je Quelle.
- **Umgesetzt:** `Context/*` mit Schnappschussmodell, lokalem Anbieter über `ClipResolver`, HTTP-Anbieter, Orchestrator und Zwischenspeicher; `CallerCardViewModel`; eine feste Karte in der Gesprächsansicht (die konfigurierbare kommt mit I4).
- **Die vier Regeln im Anrufpfad**, jede gegen einen konkreten Schaden: nichts Blockierendes im Ereignis, die Anrufkennung entscheidet statt des Zustandsübergangs (die Lehre aus REVIEW.md §8), jede Quelle isoliert, und es wird nie geworfen.
- **Tests:** 17, darunter — ein Anruf wird genau einmal nachgeschlagen, obwohl er mehrere Zustände durchläuft; ein ausgehender Anruf mit anderer Zustandsfolge genauso; eine langsame Quelle hält keine schnelle auf; eine werfende Quelle kostet nur sich selbst; interne Nummern und Notrufe gehen nicht nach aussen; ein beendetes Gespräch bricht laufende Abfragen ab.
- **Vorbehalt:** Der Plan sah vor, dass **T06 vorher am Gerät abgenommen ist**. Das ist nicht geschehen — auf ausdrücklichen Wunsch wurde trotzdem gebaut. Der Anrufpfad selbst wurde nicht verändert, es kommt nur ein weiterer Empfänger von `CallStateChanged` dazu. Die Abnahme bleibt offen und gilt jetzt für beides.
- **Betroffen:** `ActiveCallPage` (neue Zeile, Abonnements), `ActiveCallViewModel` (Auswahlereignis), `ShellViewModel.RecordInHistory/ResolveParty` (Rückfall auf Kontextnamen), `App.xaml.cs`.
- **Neu:** `Context/*` (Service, Cache, lokaler Provider, HTTP-Provider), `CallerCardViewModel`, **Übergangsanzeige**: eine feste Mini-Karte (Name, Firma, je Quelle eine Zustandszeile) direkt in XAML — noch ohne Card-Engine.
- **Risiken:** Der einzige Ort, an dem der Integrationscode den Telefoniepfad berührt. Regeln: kein `await` im Handler, Kennung statt Übergang, keine Ausnahme verlässt den Handler. Zeitbudget je Quelle gegen Abnahme T06 (Klingeln darf nicht leiden).
- **Tests:** Orchestrator mit Fake-Providern über `TaskCompletionSource`: Reihenfolge der Snapshots, Timeout einer Quelle beeinflusst andere nicht, Cancel bei `Ended`, Cache-Treffer, `AppliesTo` (intern), Ereignisse landen auf dem erfassten `SynchronizationContext` (Test mit eigenem Kontext), zwei Handles gleichzeitig.
- **Abhängigkeiten:** I1, I2; **T06 am Gerät abgenommen** (REVIEW.md §8), sonst prüft man auf einem Pfad, der selbst noch offen ist.
- **Akzeptanz (Testmatrix T40–T43):** Eingehender Anruf mit MockApi: Name sofort, CRM nach 300 ms, ERP-Zeitüberschreitung nach 1.5 s als Text; MockApi aus → Telefonieren unverändert; zweiter Anruf derselben Nummer aus dem Cache; Protokoll ohne Nummer.

### I4 — Card-Definition, Layout-Engine, Renderer (4–6 T) — **erledigt 06.09.2026**

- **Umgesetzt:** `Cards/CardDefinition.cs` (geschlossene Bausteinmenge, 6er-Raster), `CardModel.cs` (aufgelöst, ohne Ausdrücke), `CardLayoutEngine.cs` (übersetzen beim Laden, auflösen bei jeder Antwort), `DefaultCards.cs`, `Controls/Cards/CardView.cs` als Renderer.
- **Die mitgelieferten Karten sind gewöhnliche Beschreibungen**, kein Sonderfall im Code. Wären sie fest verdrahtet, fiele erst beim ersten Kunden auf, was sich nicht beschreiben lässt — die Tests prüfen sie deshalb wie jede andere Karte.
- **Zwei Entscheidungen:** das Raster hat **sechs** Einheiten, nicht zwölf (bei 400 px Fensterbreite wäre eine Zwölftelspalte dreissig Pixel breit und zeigte nichts). Und der Renderer **entscheidet nichts** — Sichtbarkeit, Werte und Aktionen sind ausgerechnet, bevor er sie bekommt; das ist der Grund, warum sich die Kartenlogik ohne Fenster prüfen lässt.
- **Sicherheit:** Adressen werden zweimal geprüft, in der Engine und beim Ausführen; alles ausser `http` und `https` macht die Schaltfläche unbedienbar. Ein unbekannter Bausteintyp aus einer neueren Kartenversion wird übergangen, nicht geraten.
- **Tests:** 27, darunter die mitgelieferten Karten, Bedingungen, Töne, fremde Schemata in Adressen und die Stabilität der Element-Schlüssel über Aktualisierungen.
- **Offen:** eigene Karten lassen sich noch nicht konfigurieren — die Herkunft der Definition wechselt mit I7, der Code dahinter nicht. Ein visueller Designer bleibt spätere Ausbaustufe (I9).

### I4 — ursprüngliche Planung

- **Ziel:** Die Übergangskarte aus I3 wird durch konfigurierbare Karten ersetzt; zwei Standardkarten (kompakt/erweitert) liegen bei.
- **Betroffen:** `ActiveCallPage` (Host), `CallerCardViewModel`.
- **Neu:** `Cards/*` (Definition, Validator, Engine, `DefaultCards`), `Controls/Cards/CardView` + Styles in `Generic.xaml`, Aktionen (URL nur http/https und Host-Prüfung; Wählen über `ShellViewModel.DialCommand`-Weg; Kopieren wie `ShellPage.CopyToClipboard`).
- **Risiken:** Rebuild-Kosten auf dem UI-Thread — Elemente mit stabilem `Key` wiederverwenden, nur Text/Sichtbarkeit setzen; Schriftgrössen/Abstände aus `Tokens.xaml`; unbekannte Elementtypen aus künftigen Versionen tolerieren.
- **Tests:** Engine: Definition + Snapshot → Modell (Sichtbarkeit, `EmptyText`, Tone-Ausdruck, `enabledWhen`); Validator (Span-Summe, unbekannter Typ → Warnung); Versionsmigration 1 → 2 (Attrappe); `XamlResourceTests` für die neuen Styles. Renderer nur manuell (T44–T46: hell/dunkel, 400 px, zwei Gespräche umschalten).
- **Abhängigkeiten:** I3.
- **Akzeptanz:** Die Karte aus D.2 zeigt Felder aus CRM und ERP gemischt; „Kontakt im CRM öffnen" öffnet den Browser nur mit gültiger `customerId`; eine Karte mit Fehler in der Definition zeigt einen Hinweis statt abzustürzen.

### I5 — Contact-Provider-Framework und lokale Migration (3–4 T) — **erledigt 06.09.2026**

- **Ziel:** Die Suche läuft über den neuen Orchestrator — zunächst nur mit Team und Outlook-Snapshot. Verhalten wie heute, aber mit Debounce, Generation, Abbruch und Suchfeld.
- **Umgesetzt:**
  - **Charakterisierungstests zuerst.** 17 Tests halten das heutige Suchverhalten fest, bevor es umzieht — einschliesslich der zwei bekannten Schwächen: `0791234567` findet `+41791234567` nicht, und Umlaute werden nicht umgeschrieben. Beides ist Ist-Zustand und soll sich in einem Umbau nicht unbemerkt ändern.
  - `Search/IContactSearchProvider.cs` — eine Schnittstelle für Outlook und für ein CRM. Dass die Quellen technisch nichts gemeinsam haben, bleibt dahinter.
  - `Search/LocalSnapshotSearchProvider.cs` — **die Outlook-Migration als Adapter**. `OutlookContactSource` bleibt unangetastet; gelesen wird aus dem `ContactStore`, gerufen wird dessen `Search`, nicht eine neue Fassung davon.
  - `Search/ContactSearchService.cs` — Debounce, Generationen, Abbruch, fortlaufende Ergebnisse; Rückkehr auf den erfassten `SynchronizationContext`.
  - `Contact` trägt Quellenkennung, E-Mail, Fremdkennung, Öffnen-Adresse und Herkünfte; `ContactSourceKind` hat `External`; `ClipResolver` löst in drei Stufen auf.
  - `ShellViewModel.Search.cs` und die Oberfläche: Suchfeld im Kontakte-Tab, Trefferliste mit Herkunfts-Abzeichen, Zustandszeile über der Liste, „Im Fremdsystem öffnen" im Kontextmenü.
- **Tests:** 12 für den Orchestrator, darunter der **Wettlauf aus dem Auftrag** — „Hans" antwortet nach „Hansi" und darf dessen Ergebnis nicht überschreiben. Das Debounce wird über eine Testzeitquelle geprüft, nicht über echte Wartezeit: ein Test, der auf einer ausgelasteten Maschine mal rot und mal grün ist, ist schlimmer als keiner.
- **Zwei Entscheidungen beim Bauen:**
  1. **Das Suchfeld erscheint nur, wenn eine Quelle über das Netz sucht.** Ohne eine solche wäre es ein zweiter Weg, dasselbe Adressbuch zu durchsuchen, das die Vorschlagsliste schon durchsucht — und genau deshalb hat ADR-014 das alte Suchfeld entfernt.
  2. **Umgeschaltet wird an der Eingabe, nicht am Ergebnis.** Sonst verschwände die Trefferliste bei jedem Zwischenstand ohne Treffer und käme gleich wieder.
- **Am Gerät abzunehmen:** T40 bis T47 in der Testmatrix, besonders **T46** — die dritte Liste im Kontakte-Tab darf die Virtualisierung nicht kosten (die Lehre aus `CLAUDE.md`).

### I6 — HTTP-Kontaktsuche und Zusammenführen (3–4 T) — **erledigt 06.09.2026**

- **Ziel:** Fremde Systeme werden parallel zu den lokalen Quellen durchsucht; Treffer zusammengeführt mit sichtbarer Herkunft; „Im Fremdsystem öffnen".
- **Umgesetzt:**
  - `Search/HttpContactSearchProvider.cs` — übersetzt eine fremde Trefferliste in Kontakte, die genauso aussehen wie die aus Outlook. **Die Nummern folgen einer Namenskonvention** statt einem zweiten Schema: alles, was mit `phone` beginnt, wird zu einer Nummer, und der Rest des Namens bestimmt ihre Art. Ein eigenes Schema für Nummern wäre ein zweiter Weg, ein Feld zu beschreiben, mit eigener Prüfung und eigenen Fehlermeldungen.
  - `Search/ContactMerger.cs` — die konservative Strategie: gleiche Rufnummer über `ClipResolver.IsSameNumber`, gleiche E-Mail; **Name und Firma ab Werk aus**. Passt ein Kandidat auf zwei bestehende Einträge, gewinnt der erste — drei Einträge zu einer Zeile zu verschmelzen machte den Fehler nur grösser.
  - `Config/IntegrationRegistry.cs` — baut die Anbieter aus der Konfiguration und **nur neu, wenn sie sich ändert**. Die Abstraktion `ISearchProviderRegistry` gibt es, weil die Menge der Quellen nicht fest ist: eine über den Container eingespritzte Liste stünde beim Start fest und wüsste von einer neu eingerichteten Quelle nichts.
- **Tests:** 17 für das Zusammenführen, 14 für den HTTP-Anbieter. Die Merger-Tests prüfen in beide Richtungen — was zusammengeführt wird und, wichtiger, was getrennt bleibt.
- **Ein Befund beim Bauen:** der bestehende Test über die Fortlaufigkeit der Suche wurde rot, weil er allen Testkontakten dieselbe Nummer gab und der Merger sie nun korrekt zusammenführte. Die Testdaten leiten die Nummer jetzt aus dem Namen ab — ein Test über die Fortlaufigkeit prüfte sonst in Wahrheit das Zusammenführen.
- **Offen aus dieser Phase:** der Zwischenspeicher für Suchergebnisse (`cacheSeconds`) ist konfigurierbar, aber noch nicht wirksam. Er lohnt sich erst, wenn eine echte Quelle angebunden ist und sich zeigt, welche Anfragen sich wiederholen.
- **Am Gerät abzunehmen:** T43 bis T45 in der Testmatrix.

### I6b — Pilot crm (1–2 T) — **teilweise erledigt 06.09.2026**

- **Umgesetzt:** `docs/integrations/crm.json` als Quellendefinition und `docs/integrations/crm.md` mit dem Einrichtungsweg. `SampleConfigurationTests` prüft jede Vorlage im Repo gegen den Validator — eine Vorlage, die abgelehnt wird, sieht aus wie ein funktionierendes Beispiel und kostet eine Stunde Fehlersuche an der falschen Stelle.
- **Nicht erledigt, und das ist der Kern der Phase:** die Feldpfade sind **nicht gegen die echte API verifiziert**. Die Zugangsdaten des Test-Mandanten lagen nicht vor. Die Vorlage ist deshalb ab Werk **abgeschaltet**, und ein Test erzwingt das: eine Quelle gehört erst eingeschaltet, wenn ihre Pfade geprüft sind. Dieselbe Regel wie beim SDK (§0, Regel 1) — nicht raten, was man nicht gesehen hat.
- **Was noch fehlt:** Zugangsdaten für den Test-Mandanten, ein Testabruf über die Einstellungen, Nachziehen der Pfade, dann einschalten.

### I6b — ursprüngliche Planung

- **Ziel:** Der erste echte Connector — ohne neuen Code, als `DataSourceDefinition` gegen den Test-Mandanten des CRMs. Beweist, dass der generische HTTP-Connector ein reales System trägt, und liefert die Vorlage für Kundensysteme.
- **Betroffen:** nichts im Code. Neu: `docs/integrations/crm.json` (Definition ohne Geheimnisse) und `docs/integrations/crm.md` (welche Endpunkte, welche Felder, Auth-Art, Testnummern).
- **Inhalt:** `lookupByPhone` über die Kunden-/Kontakt-API (Kunde → Kontakte), `searchContacts` über die Kontaktliste, `openContact` auf die Kundenseite. Mapping auf `crm.customerName`, `crm.company`, `crm.customerNumber`, `crm.openProjects`.
- **Risiken:** Die API-Form (Paging, Filterparameter, Auth) ist erst am Test-Mandanten zu verifizieren — nicht raten, wie bei der SDK-API (`docs/sdk-api-notes.md`). **Nie gegen den produktiven Mandanten** (§13, `CLAUDE.md`).
- **Tests:** Golden-File mit einer echten, anonymisierten Antwort des Test-Mandanten in den Mapping-Tests; T50 in der Testmatrix.
- **Abhängigkeiten:** I6; Zugangsdaten des Test-Mandanten von Dominic.
- **Akzeptanz:** Ein Anruf von einer Testnummer zeigt Kunde und offene Projekte aus dem CRM; die Suche findet einen CRM-Kontakt neben Outlook.

### I7 — Administration, Verteilung, Diagnose (4–6 T) — **erledigt 06.09.2026**

- **Umgesetzt:** `IntegrationTester` (Testabruf mit Rohantwort **und** gemappten Feldern nebeneinander), `IntegrationProvisioning` (Verteilung über einen Verweis im Profil, nur https), `IntegrationSettingsViewModel` und die Gruppe „Integrationen" in den Einstellungen; `PolicyService`-Pfad `integrations`.
- **Was ohne Texteditor geht:** eine Quelle ein- und ausschalten, Zugangsdaten eintragen, einen Testabruf ausführen und sehen, was ankommt, Konfiguration ein- und ausgeben.
- **Was bewusst als JSON bleibt:** Endpunkte, Mappings und Karten. Ein Formular je Feld wäre eine zweite Beschreibung derselben Sache, mit eigener Prüfung und eigenen Fehlermeldungen, und müsste bei jeder Erweiterung nachgezogen werden. Der visuelle Weg ist I9.
- **Zwei Sicherheitsentscheidungen:** die Integrationsdatei kommt **nur über https**, ohne die Ausnahme, die es bei der Provisionierung selbst gibt (ADR-012) — diese Datei bestimmt, welche fremden Adressen nipp mit Rufnummern beliefert. Und sie wird **nach** dem ersten Zeichnen geholt, nie im Startpfad (§21.2).
- **Tests:** 8 für den Testabruf, 6 für die Verteilung, darunter die Ablehnung unsicherer Adressen ohne Abruf.

### I7 — ursprüngliche Planung

- **Ziel:** Der Ablauf aus dem Auftrag (Quelle anlegen → Verbindung → Auth → Test-Request → Antwort → Felder → Fähigkeit → Karte → Suche → Vorschau → Aktivieren) ist ohne Texteditor bedienbar — in einer **strukturierten** Form, kein Designer.
- **Betroffen:** `SettingsPage` (Gruppe), `ProvisioningParser/Service/Profile`, `Nipp.Provisioning`, `DiagnosticsBundle`, `docs/provisioning.md`.
- **Neu:** `IntegrationSettingsViewModel`: Liste der Quellen (Ein/Aus, Zustand, letzte Antwortzeit), Formular Verbindung + Auth (Geheimnis in `PasswordBox`, geht direkt in `SecretStore`), „Verbindung testen" mit Testnummer → zeigt Status, Dauer, **Antwort (gekürzt, 64 KB)** und die **gemappten Felder** daneben, Import/Export von `integrations.json` (Export ohne Geheimnisse, Hinweis wie `SettingsService.Export`), Validierungsliste mit Pfad und Abhilfe. Mapping und Karte werden im MVP **als JSON-Text bearbeitet** (mehrzeiliges Feld mit Validierung beim Übernehmen) — der visuelle Designer ist I9.
- **Risiken:** Umfang der Seite im 400-px-Fenster; Provisionierungsabruf nie im Startbudget.
- **Tests:** ViewModel-Tests für Validierungsfluss; Parser-Test für `<integrations>`; Diagnosepaket-Test (kein `secretRef`-Wert, kein Header).
- **Abhängigkeiten:** I1–I6.
- **Akzeptanz:** Eine Quelle lässt sich vollständig in der Oberfläche einrichten und testen; ein Profil mit `<integrations src>` verteilt die Datei; das Diagnosepaket erklärt einen fehlenden Schlüssel.

### I8 — Härtung und Abnahme (2–3 T) — **teilweise erledigt 06.09.2026**

- **Umgesetzt:** `IntegrationHealth` — Schutzschalter nach fünf Fehlern in Folge, sofortige Pause bei `429` mit Beachtung von `Retry-After` (gedeckelt auf fünf Minuten), Rücksetzung bei jeder Antwort. Eingebaut in den HTTP-Zugang.
- **Warum fünf und nicht einer:** ein einzelner Fehler ist ein Zucken im Netz. Eine Quelle danach für eine Minute abzuschalten wäre schlimmer als das Problem.
- **Warum ein 401 nicht pausiert:** das ist eine Antwort, nicht ein Ausfall — der Server steht, die Zugangsdaten stimmen nicht. Ihn dafür zu pausieren würde die Ursache verdecken.
- **Nicht erledigt:** die Abnahme am Gerät (T40 bis T47) und die Messung von Kaltstart und Kartenaufbau auf echter x64-Hardware. Beides braucht ein Gerät und ist nicht am Schreibtisch zu erledigen (ADR-001).

### I8 — ursprüngliche Planung

- **Ziel:** Alltagstauglichkeit.
- **Neu/Betroffen:** Schutzschalter und 429 in `IntegrationHealth`, Retry nur für Suche, Cache-Feinschliff, Datenschutz-Durchsicht aller Log-Zeilen (Scan-Test H.3), Testmatrix T40–T52 am **x64-Gerät**, Messung Kaltstart unverändert (< 3 s, AP7.8), Card-Rebuild-Dauer im Debug-Protokoll.
- **Akzeptanz:** Alle T40–T52 bestanden; Kaltstart und Klingeln unverändert; Protokoll-Scan findet keine personenbezogenen Platzhalter.

**Summe MVP (I0–I8 mit I6b): 25–36 Tage netto.** I2 ∥ I1 und I5 ∥ I3/I4 sind parallelisierbar; realistisch mit Abnahmen am Gerät **7–9 Wochen**.

### Später (nicht MVP)

> **Stand 07.09.2026 nachmittags:** **I9 und I12 sind vorgezogen und gebaut**
> — auf Auftrag (§21.6), nicht als Abweichung. Der Anlass war ein Befund:
> `IntegrationConfig` hatte kein `cards`, obwohl D.2 die Datei genau so
> beschreibt; eine eigene Karte war nicht schwer einzurichten, sondern
> unmoeglich. Umsetzung und Phasen: `EINRICHTUNG-PLAN.md`.
>
> Was von I9 offen blieb: **Ziehen** aus der Palette (T101). Eingefuegt wird
> ueber Doppelklick und Knopf; ein halb funktionierendes Ziehen waere
> schlimmer als keines.

| Phase | Inhalt | Architektur ist vorbereitet durch |
|---|---|---|
| **I9 Card-Designer** ✅ **07.09.2026** (§21.6, ADR-032) | Strukturierter Editor: Palette links (Felder je Quelle aus dem letzten Test-Request bzw. Mapping-Definition), Sektionen/Zeilen/Spalten, Drag & Drop, Eigenschaften, Vorschau mit Testdaten (gespeicherte Antwort aus I7), Undo. Eigene Seite oder eigenes Fenster (400 px reichen nicht). | `CardDefinition` ist ein reines Datenmodell mit Validator; `CardLayoutEngine` rendert jede Definition, auch halb fertige; `Key`-stabile Modelle |
| **I10 Abhängige Quellen** | `dependsOn: ["crm"]` und Parameter `{{crm.customerId}}` in der Request-Vorlage; Orchestrator startet eine Quelle, sobald ihre Abhängigkeiten `Success/Empty` sind; Zyklusprüfung im Validator; Zeitbudget kumuliert. | `CallerContextRequest` trägt den aktuellen Snapshot; `TemplateRenderer` löst Namespaces auf; Provider laufen schon als einzelne Tasks |
| **I11 OAuth2** | Client Credentials; später Device Code für Benutzer-Kontext. Token im Speicher, Refresh serialisiert je Quelle. | `HttpAuthenticator` als Strategie je `auth.type` |
| **I12 Spezifische Connectoren** ✅ **07.09.2026** als Katalog (§21.6, ADR-033) | Dynamics, HubSpot, Salesforce zunächst als **Vorlagen** (`DataSourceDefinition`-Presets) ohne neuen Code; echter Connector-Typ nur, wenn Auth oder Paging es verlangen. | `IDataSourceConnector.Type` |
| **I13 Weitere Fähigkeiten** | `GetContact`, `CreateContact`, `UpdateContact` (Formulare aus Karten-Elementen `Input`), Toast-Anreicherung, History-Card, Präsenz aus Ticketing. | Capability-Blöcke, `CardKind.History` |
| **I14 Graph für Outlook** | Falls je gewünscht (ADR-009 offen): ein weiterer `IContactSearchProvider`, ohne die Kontakt-UI anzufassen. | Provider-Modell |

---

## H. Teststrategie

### H.1 Komponententests (`tests/Nipp.Core.Tests/Services/Integrations/**`)

| Bereich | Was geprüft wird |
|---|---|
| `PhoneNumberKey` | E.164 aus `079…`, `+41…`, `0041…`, `151` (intern), `sip:…` (Adresse), leer |
| Config-Store | Roundtrip, kaputte Datei beiseite, Migration, Pfad-Isolation (kein `%APPDATA%`) |
| Validator | Jede Regel aus D.8; Fehlertexte enthalten Pfad und Abhilfe |
| HTTP-Client | Fake-`HttpMessageHandler`: Auth-Header je Typ, Query-Kodierung, Zeitgrenze → `Timeout`, Antwort > Limit → `Error` ohne vollständiges Lesen, 401/404/429/500-Klassifikation, kein Retry bei Lookup, ein Retry bei Suche-GET; **Log-Redaction** über einen Test-`ILogger`, der alle Meldungen sammelt und per Regex auf Ziffernfolgen ≥ 7, `Authorization`, `X-Api-Key` prüft |
| Expression Engine | Tabellen für Operatoren, Typmischungen, `null`, Funktionen, Grenzen, Fehlerpositionen, Ablehnung unbekannter Bezeichner |
| Mapping Engine | Golden-Files (Eingabe-JSON + Definition → erwartetes Fragment als JSON), Mehrfachtreffer, `as`-Konvertierungen, `emptyWhen` |
| `CallerContextService` | Fake-Provider mit `TaskCompletionSource`: Reihenfolge der Snapshots, isolierter Timeout, Cancel bei Ende, Cache, `AppliesTo`, zwei Anrufe, Ereignisse auf erfasstem `SynchronizationContext` |
| `ContactSearchService` | Debounce mit `TimeProvider`-Attrappe, Mindestlänge, **Generation-Race** (E.2), lokale Treffer vor Remote, Zustände je Quelle |
| `ContactMerger` | Zusammenführen über Nummer/E-Mail, Nicht-Zusammenführen bei Name+Firma, Herkünfte, Prioritätsregel für Anzeigename |
| `CardLayoutEngine` | Definition + Snapshot → Modell; Sichtbarkeit; `EmptyText`; Aktions-URL nur http/https und erlaubter Host; unbekannter Elementtyp → Platzhalter |
| ViewModels | `CallerCardViewModel` folgt `SelectedCall`; `IntegrationSettingsViewModel` zeigt Validierungsfehler |

### H.2 Integrations- und Gerätetests

- **`tools/Nipp.MockApi`**: Kestrel-Minimal-API mit den Endpunkten aus D.2, steuerbar über Query-Parameter (`delay`, `status`, `size`, `malformed`). Startet lokal auf `http://localhost:5199` — für den Test muss `allowInsecureHttp` gesetzt sein, was gleichzeitig die Warnung im Validator prüft.
- **Testmatrix** `docs/test-matrix.md`, neu **T40–T52**: Karte bei eingehendem Anruf (sofort/nachladend), Zeitüberschreitung, Quelle aus, zweiter Anruf aus Cache, zwei Gespräche umschalten, hell/dunkel, Aktion öffnen, Suche lokal/Remote/Race, Import/Export, Provisionierung `<integrations>`, Diagnosepaket, Kaltstart unverändert.
- **Kein Test gegen echte Kundensysteme** — nur MockApi und, sobald vorhanden, ein Test-Mandant des CRMs (offene Frage I.3.6).

### H.3 Architekturtests (neu)

`IntegrationBoundaryTests` als Quelltext-Scan wie `SdkBoundaryTests`:

1. `Services/Integrations/**` enthält weder `Linphone` noch `Microsoft.UI` noch `Nipp.Core.Services.Telephony.SipService` (nur `ISipService` und `Model/`).
2. `using Json.Path` ausschliesslich unter `Services/Integrations/Mapping/`.
3. **Datenschutz-Scan:** In `IntegrationLogs.cs` darf kein `[LoggerMessage]`-Platzhalter `Number`, `Phone`, `Query`, `Name`, `Url`, `Header`, `Body` heissen. Grob, aber wirksam gegen den häufigsten Fehler.
4. `XamlResourceTests` bleibt und deckt `Cards.xaml` ab.

### H.4 Migrationstests Outlook

- Vor I5: **Charakterisierungstests** für `ContactStore.Search` (Name, Firma, Ziffern mit Trennzeichen, Gross/klein) in `ContactStoreTests`.
- Nach I5: dieselben Fälle über `LocalSnapshotSearchProvider` → identische Treffermengen.
- `OutlookContactSource` wird **nicht** verändert; ihre Tests bleiben die manuellen (T-Outlook in der Matrix: Outlook läuft / läuft nicht / offener Dialog).

---

## I. Risiken und offene Punkte

### I.1 Technische Risiken

| Risiko | Bewertung | Gegenmassnahme |
|---|---|---|
| **COM und Threading** | gering im MVP | Outlook bleibt Snapshot; kein COM-Aufruf je Tastendruck; `OutlookContactSource` unangetastet |
| **UI-Thread mit Iterate** | mittel | kein `await` im Callback; HTTP auf I/O-Threads; Debounce über `Task.Delay` + `Post`, nicht über einen UI-Timer; Card-Rebuild mit stabilen Keys; Messung in I8 |
| **Langsame oder tote APIs** | mittel | Zeitgrenze je Quelle, kein Gesamtwarten, Schutzschalter, 429-Behandlung; Telefonie hängt an nichts davon |
| **Authentifizierung** | mittel | MVP ohne OAuth2; Geheimnisse an Gerät gebunden → Einrichtung pro Gerät oder Profil über https; Token-Ablauf erst mit I11 |
| **Datenschutz** | **hoch** — die Nummer verlässt den Arbeitsplatz | `lookupInternalNumbers` aus; Caches nur im Speicher, kurze TTL; Logs ohne Nummer/Text; Diagnosepaket nach Whitelist; Hinweis in `docs/`: der Kunde ist Verantwortlicher der Verarbeitung; Abschaltbar je Quelle |
| **Fehlerhafte Mappings** | mittel | Validator, Test-Request mit Feldvorschau, `Null` + Diagnose statt Ausnahme, unbekannte Typen tolerieren |
| **Konfigurationsmigration** | gering | `schemaVersion` in Datei und Karte, `Migrate`-Haken ab Tag 1, kaputte Datei beiseite |
| **API-Qualität fremder Systeme** | mittel | Paging nur erste Seite, `emptyWhen`, Grössenlimit, `as`-Konvertierungen tolerant („3" → 3 nur bei `as: number`) |
| **Dedup False Positives** | mittel | konservative MVP-Regel, Herkünfte sichtbar, Merge abschaltbar; Name+Firma nur als spätere Option |
| **Card-Versionskompatibilität** | gering | Renderer kennt Version; unbekannte Elemente → Platzhalter; Designer schreibt immer aktuelle Version; Beispielkarten im Repo als Regressionsdaten |
| **Fensterbreite** | mittel | 6er-Raster, Umbruch unter 340 px, Vorschau in I7 zeigt echte Breite |
| **Startpfad** | gering | nichts vor `StartTelephony`; Profilabruf der Integrationsdatei nach dem ersten Zeichnen |
| **Paketabhängigkeit `JsonPath.Net`** | gering | MIT; kapselt in einem Ordner; Rückfall Mini-Auswerter |
| **ARM64-Entwicklung** | — | Latenz- und Ruckelaussagen nur am x64-Gerät (ADR-001) |

### I.2 Bewusst nicht gebaut

Skript-Engines, freies HTML/XAML, Cache auf Platte, Schreiben in Fremdsysteme (MVP), OAuth2 (MVP), Windows Credential Manager (kein Mehrwert über DPAPI), Graph, eigenes Projekt für den Integrationskern (MVP).

### I.3 Entscheidungen von Dominic (06.09.2026)

Alle acht Fragen sind entschieden; die Entscheidungen gelten für die Umsetzung und gehören in ADR-015.

| # | Frage | Entscheidung | Folge im Plan |
|---|---|---|---|
| 1 | Ort der Kontaktsuche | **Suchfeld im Kontakte-Tab**, nur sichtbar mit mindestens einer aktiven Remote-Suchquelle. Vorschläge unter dem Nummernfeld bleiben lokal, sofort, fünf Treffer (§8.1). | E.2, F.1 `ShellPage`, Phase I5 |
| 2 | Ausgehende Anrufe nachschlagen | **Ja**, `lookupOutgoing` Standard ein. | E.1 Regel 5, D.2 |
| 3 | Interne Nummern an externe Systeme | **Nein**, `lookupInternalNumbers` Standard aus. | `ICallerContextProvider.AppliesTo`, D.2 |
| 4 | Ablage der Konfiguration | **Eigene Datei `integrations.json`** mit eigenem Store (ADR-017). | D.1, F.2 |
| 5 | JSONPath | **`JsonPath.Net`** (MIT), beschränkt auf `Services/Integrations/Mapping/` (ADR-016). | D.4, H.3 |
| 6 | Erstes echtes Zielsystem | **das CRM** mit Test-Mandant, nie produktiv (§13). | neue Phase **I6b** |
| 7 | Toast anreichern | **Nicht im MVP.** Bleibt bei Name/Nummer, bis T06 abgenommen ist; später I13. | E.1 Regel 7 |
| 8 | CRM-Name in der Anrufliste | **Ja.** `RecordInHistory` fällt auf den Kontextnamen zurück; Hinweis in `docs/` und im Diagnosepaket-Text, dass Fremddaten lokal liegen. | E.1 Regel 8, F.1 `ShellViewModel` |

---

## J. MVP und spätere Ausbaustufen

### MVP (I0–I8) — produktiv nutzbar

- Generischer **HTTP-/REST-Connector** mit `none/apiKey/bearer/basic`, Zeitgrenze, Grössenlimit, https-Zwang.
- **Anruferkontext** für ein- und ausgehende Anrufe: lokaler Name sofort, externe Quellen parallel, Zustand je Quelle, Cache 5 min.
- **Mapping** über JSONPath mit typisierten Werten; **berechnete Felder** und Vorlagen über eine eingeschränkte Expression Engine.
- **Konfigurierbare Karte** aus einer festen Komponentenmenge (Text, Feld, Badge, Trenner, Schaltfläche, Link, Quellenzustand) mit Sektionen/Zeilen/Spalten/Span; zwei Kartenarten (kompakt, erweitert); Aktionen `openUrl`, `dial`, `copy`; JSON-Bearbeitung mit Validierung.
- **Kontaktsuche** über Team, Outlook-Snapshot und HTTP-Quellen mit Debounce, Generation, Cancellation, Zuständen und konservativem Zusammenführen mit sichtbarer Herkunft; „Kontakt öffnen".
- **Administration** in den Einstellungen (Liste, Verbindung, Auth, Test mit Antwort- und Feldvorschau, Import/Export), **Verteilung** über das Provisionierungsprofil, **Diagnosepaket** ohne Geheimnisse, **Policy-Sperre** `integrations`.
- Alles hinter Architekturtests, Komponententests und der Testmatrix T40–T52.

### Spätere Ausbaustufen

Visueller **Card-Designer** (I9), **abhängige Quellen** (I10), **OAuth2** (I11), **spezifische Connectoren** als Vorlagen (I12), **weitere Fähigkeiten** und Toast/Verlauf (I13), Graph (I14). Jede davon setzt auf Datenmodelle und Nahtstellen, die im MVP schon existieren — keine verlangt einen Umbau des Kerns.

---

---

## K. Fortschritt

### 06.09.2026 — I0 abgeschlossen

Auftrag, Leitplanken und die erste Ankerdatei stehen; Einzelheiten oben bei Phase I0. Kurz:

| Prüfung | Ergebnis |
|---|---|
| `build.ps1 build Nipp.sln -c Debug` | 0 Warnungen, 0 Fehler |
| `Nipp.Core.Tests` | **279 bestanden** (vorher 256) |
| `Nipp.Architecture.Tests` | **11 bestanden** (vorher 7) |
| Gegenprobe der neuen Grenzen | alle drei schlagen bei absichtlicher Verletzung an |
| `dotnet format` auf den neuen Dateien | sauber (IDE1006 trifft den Bestand gleichermassen und bleibt) |

**Was als Nächstes gebraucht wird:** die Zugangsdaten des CRM-Test-Mandanten für I6b. Sie blockieren I1 bis I6 nicht.

### 06.09.2026 — I1 und I2 abgeschlossen

Der Integrationskern steht: eine Quelle lässt sich beschreiben, prüfen und speichern, eine Anfrage sicher absetzen, und eine fremde Antwort auf eigene Felder abbilden. Einzelheiten oben bei den Phasen.

| Prüfung | Ergebnis |
|---|---|
| `build.ps1 build Nipp.sln -c Debug` | 0 Warnungen, 0 Fehler |
| `Nipp.Core.Tests` | **435 bestanden** (vorher 279) |
| `Nipp.Architecture.Tests` | **11 bestanden** |
| `dotnet format` auf den neuen Dateien | sauber |

**Neue Abhängigkeit:** `JsonPath.Net` 3.0.2 mit `Json.More.Net` 3.0.1, beide MIT, eingetragen in `docs/licensing.md`. Die API wurde vor der Verwendung aus der Paketdokumentation verifiziert, nicht geraten — dieselbe Regel wie beim SDK.

**Was noch fehlt, bevor I3 beginnen kann:** nichts aus diesen beiden Phasen. I3 setzt die Abnahme von T06 am Gerät voraus (REVIEW.md §8.3), weil es den Anrufpfad berührt.

### 06.09.2026 — I5 abgeschlossen

Die Kontaktsuche läuft über das gemeinsame Framework. Ohne eingerichtete externe Quelle ändert sich für den Benutzer nichts: das Suchfeld erscheint dann gar nicht, und Team und Outlook stehen wie bisher.

| Prüfung | Ergebnis |
|---|---|
| `build.ps1 build Nipp.sln -c Debug` | 0 Warnungen, 0 Fehler |
| `Nipp.Core.Tests` | **464 bestanden** (vorher 435) |
| `Nipp.Architecture.Tests` | 11 bestanden |
| Neue Testfälle in der Matrix | T40 bis T47 |

**Neue Testabhängigkeit:** `Microsoft.Extensions.TimeProvider.Testing` 8.10.0, auf der 8er-Linie gehalten wie alle anderen Pakete.

### 06.09.2026 — I6 abgeschlossen

Externe Quellen werden parallel zu Team und Outlook durchsucht, Doppelte werden konservativ zusammengeführt, und die Herkunft steht an jeder Zeile.

| Prüfung | Ergebnis |
|---|---|
| `build.ps1 build Nipp.sln -c Debug` | 0 Warnungen, 0 Fehler |
| `Nipp.Core.Tests` | **495 bestanden** (vorher 464) |
| `Nipp.Architecture.Tests` | 11 bestanden |

**Damit ist die Kontaktsuche des MVP vollständig** — vom Suchfeld über Debounce, Generationen und Abbruch bis zu fremden Quellen und dem Zusammenführen. Was fehlt, ist die Einrichtung in der Oberfläche (I7): heute wird eine Quelle durch Bearbeiten von `integrations.json` angelegt.

### 06.09.2026 — I3, I4, I7, I6b und I8 abgeschlossen

Alle Phasen des MVP sind gebaut. Was bleibt, braucht ein Gerät oder Zugangsdaten.

| Prüfung | Ergebnis |
|---|---|
| `build.ps1 build Nipp.sln -c Debug` | 0 Warnungen, 0 Fehler |
| `Nipp.Core.Tests` | **568 bestanden** (zu Beginn des Tages 256) |
| `Nipp.Architecture.Tests` | 11 bestanden |

### 06.09.2026 — die APIs geprüft, ein Modellfehler gefunden

CRM und Gesprächsjournal waren von hier aus erreichbar. Statt zu raten liess sich prüfen — mit drei Ergebnissen:

1. **Die Kontaktsuche des CRMs findet keine Telefonnummern.** Das blockiert den Anruferkontext dort. Belegt mit vier Anfragen.
2. **Das Gesprächsjournal hat die Daten, aber keinen REST-Zugang** — und `include_recent_calls` liefert eine leere Liste, obwohl das Profil sechs Anrufe zählt.
3. **Die Beispielkonfiguration in Abschnitt D.2 passte nicht zum implementierten Modell.** Das Mapping erwartete eine Zwischenebene `fields`, die im Plan nicht steht. Aufgefallen ist es, weil die Vorlagen gegen die geforderten Antwortformen getestet werden — vor dem Modell hätte niemand den Unterschied bemerkt, und ein Team hätte nach einer Vorlage gebaut, die nipp nicht lesen kann. Das Modell folgt jetzt der lesbareren Form ohne Zwischenebene.

Beide Auftragstexte stehen in `docs/integrations/api-anpassungen.md`, jeweils mit der Antwortform, die nipp erwartet. `ExpectedResponseTests` prüft, dass die Vorlagen genau diese Form verarbeiten.

**Was jetzt offen ist, und zwar nur das:**

1. **T06 am Gerät** — die Abnahme eingehender Anrufe aus REVIEW.md §8. Sie war Voraussetzung für I3 und wurde auf ausdrücklichen Wunsch übersprungen; jetzt gilt sie für den Anrufpfad und für den Anruferkontext zusammen.
2. **T40 bis T47** — die Abnahme der Integrationen am Gerät.
3. **Die zwei API-Aufträge** aus `docs/integrations/api-anpassungen.md`: die Nummernsuche im CRM, der REST-Endpunkt beim Gesprächsjournal, beides über https.
4. **Messungen auf echter x64-Hardware** — Kaltstart und Kartenaufbau (ADR-001).

*Nächster Schritt: die Abnahme am Gerät. Ohne sie ist alles hier gebaut, aber nichts bestätigt.*
