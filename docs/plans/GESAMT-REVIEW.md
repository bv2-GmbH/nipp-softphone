# Gesamtprüfung von nipp — Befunde und Plan

**Stand 06.09.2026 abends**, Build `6e4e2c1`. Geprüft wurde die ganze Anwendung:
Code aller Schichten, die laufende Oberfläche mit Bildschirmfotos, Tests, Build,
Protokoll der laufenden Instanz, und die Spezifikation gegen die Umsetzung.

Dieses Dokument löst `ALLTAG-PLAN.md` nicht ab — die sechs Befunde von dort
gelten weiter und werden hier eingeordnet. Es ist der **Gesamtplan**, aus dem
sich die nächsten Wochen ableiten.

---

## Umsetzungsstand (06.09.2026, nachts)

**Der Plan aus §6 ist umgesetzt**, mit Ausnahme dessen, was ausdrücklich als
Entscheidung oder als von aussen abhängig markiert war. Fünf Commits auf dem
Zweig `review-umsetzung`.

| Paket | Zustand |
|---|---|
| 1 — vor der Geräteabnahme | **erledigt.** G1–G5, V1, L4, L1/O7 |
| 2 — Sicherheit und Datenschutz | **erledigt.** G6–G9, D1–D3, A1, L7 |
| 3 — Robustheit Telefonie | **erledigt.** T1–T10 ausser T11 (MWI je Konto), A2, A5–A7, K8, V8 |
| 4 — Einstellungen | **erledigt.** V1–V9, `SettingsValidator`, L2, O3 |
| 5 — Kontakte und Oberfläche | **erledigt.** K1–K7, I1, L3, O1–O2, O4–O6, O8–O10 |
| 6 — Tests und Doku | **erledigt.** `PrivacyLogTests`, `SettingsValidatorTests`, `LogMaskingTests`, ADR-018 bis ADR-022, fünf Doku-Korrekturen |

**Zahlen:** 644 Komponententests (von 603), 15 Architekturtests (von 14), Build
ohne Warnungen.

**Am Gerät nachgeprüft**, nicht nur am Code:
- **G2, der Blocker.** Fenster ins Infobereich-Symbol geschlossen, über eine
  zweite Instanz zurückgeholt, danach das Erscheinungsbild gewechselt — es
  wurde angewendet. Vor der Korrektur wäre das Fenster taub gewesen.
- **K1.** Der Outlook-Abschnitt nennt jetzt den Grund und erkennt das neue
  Outlook.
- **O4.** Filter „Verpasst" ohne Treffer sagt „Keine passenden Anrufe."
- **O5.** Der fünfte Filter ist da.
- **L1.** „Einstellungen" steht in der Leiste und wird nicht abgeschnitten.
- **L3.** Listenzeilen heissen im Bedienhilfe-Baum „AV, 152" statt
  „Nipp.Core.ViewModels.ContactRow".

**Bewusst nicht umgesetzt:**

| Punkt | Grund |
|---|---|
| **A3** — `RedirectActivationToAsync` blockierend | Der Umbau auf das offizielle Muster (Hilfsthread vor `Application.Start`) gefährdet den Start. Ein hängender Start wäre schlimmer als der heutige Zustand. Bleibt als Risiko notiert. |
| **T11** — MWI je Konto | Braucht `MwiServerAddress` und ein eigenes SUBSCRIBE; das ist ein Umbau der Mailbox-Anbindung, kein Fehler an einer Zeile. Gehört in einen eigenen Durchgang mit Geräteabnahme. |
| **L9** — Wähltastatur auf allen Tabs | Produktentscheidung, §6.1 Punkt 3. Nicht meine. |
| **L6** — Wählfeld und CRM | Produktentscheidung, §6.1 Punkt 4. ADR-014 hat sich dagegen entschieden. |
| **§9-Felder in der Oberfläche** | Jetzt entschieden: **ADR-019**. Zwei Ausnahmen dort benannt. |
| **I2** — JSONPath vorübersetzen | Wirkt nur auf die Laufzeit, nicht auf die Richtigkeit. Sauber machbar, aber ein eigener Durchgang. |
| **O11–O13** — Vorlagen zusammenführen, Abstandsraster, Antwortfeld | Wartbarkeit ohne Verhaltensänderung. Gehört in einen Aufräumdurchgang, nicht zwischen Fehlerkorrekturen. |
| **L8** — verwaiste `call-history.db` | Eine leere Datei vom 04.09. unter `%APPDATA%`. Löschen ist Handarbeit am Gerät des Benutzers, nicht Code. |
| **L11** — Wähltastatur nach Neustart | Einmal beobachtet, nicht reproduziert. Als Testfall festgehalten. |

**Was das für T06 heisst.** Der wahrscheinliche Grund, warum der eingehende
Anruf nie abgenommen werden konnte, ist behoben und die Wirkung ist am Gerät
belegt — aber ein eingehender Anruf wurde damit nicht geführt. **T06 bleibt
offen.** Es ist weiterhin der wichtigste Testfall, und er braucht jetzt
ausdrücklich ein **verstecktes** Fenster: am offenen zeigt sich der Fehler
nicht, den es zu prüfen gilt.

---

## 0. Wie geprüft wurde

| Was | Wie | Ergebnis |
|---|---|---|
| Komponententests | `build.ps1 test tests\Nipp.Core.Tests` | **603 grün** |
| Architekturtests | `build.ps1 test tests\Nipp.Architecture.Tests` | **14 grün** |
| App-Build | unpackaged `-t:Rebuild`, Warnungen sind Fehler | **0 Warnungen, 0 Fehler** (§7) |
| Laufende Anwendung | 16 Bildschirmfotos aller Ansichten und Einstellungsabschnitte, UI-Automation-Baum, `docs/review/2026-09-06/` | Befunde unter L |
| Protokoll | `nipp-20260906_001.log` der laufenden Instanz (7'693 Zeilen) | keine eigenen Fehler; nur SDK-Warnungen zum Audiopuffer (bekannt, ADR-006) |
| Code | Sieben getrennte Durchgänge: Telefonie-Kern, App-Schicht, ViewModels und Einstellungen, Integrationsplattform, Kontakte/Verlauf/Windows, XAML und Bedienbarkeit, Spezifikation gegen Umsetzung | ~90 Befunde, unten verdichtet |

Jeder Codebefund trägt Datei und Zeile. Mit **★** markierte Befunde habe ich
zusätzlich selbst am Code oder an der laufenden Anwendung nachgeprüft.

**Nicht geprüft**, weil ohne Gegenstelle oder ohne Eingriff in die Einstellungen
nicht möglich: ein echter Anruf in beide Richtungen (T04–T11), das helle Thema,
Windows-Textskalierung und Kontrastmodus, Toast bei geschlossenem Fenster,
tel:-Link. Diese Punkte bleiben Geräteabnahme.

---

## 1. Ergebnis auf einen Blick

nipp ist **architektonisch solide und ungewöhnlich gut begründet** — die
Grenzen halten (SDK nur in `Telephony/`, Integrationen ohne SDK und WinUI),
Geheimnisse bleiben aus Klartext, Export, Log und Diagnosepaket heraus, alle
neunzehn Oberflächenbefunde vom 05.09. sind nachweisbar umgesetzt, und fast
jeder Kommentar erklärt einen realen Befund.

Die ernsten Fehler haben **ein gemeinsames Muster**: eine Regel steht an
einer Stelle richtig und an einer zweiten nicht mehr. Genau das, was CLAUDE.md
schon als Lehre führt („eine Zustandsregel gehört an das Ereignis, nicht in die
Empfänger"). Beispiele: `Refreshing` in der Bridge richtig, in `SipService`
noch als Fehler; https-Pflicht im Validator, nicht im HTTP-Client;
Normalisierung beim Wählen, nicht beim Weiterleiten.

| Bereich | Zustand | Was vor dem Piloten stehen muss |
|---|---|---|
| Telefonie-Kern | gut, 3 Fehler | G1, G3, G5 |
| App-Schicht | gut, **1 Blocker** | **G2** — erklärt vermutlich T06 |
| ViewModels / Einstellungen | gut, Testlücke | G4, V1–V4 |
| Integrationsplattform | gut, 3 Fehler | I1–I3 |
| Kontakte / Verlauf / Windows | gut, Datenschutz offen | D1, D2, K1 |
| Oberfläche | pilotfähig | L1–L4, O1–O3 |
| Spezifikation / Doku | ~4/5 umgesetzt | S1–S4, 5 falsche Angaben |
| Testmatrix | **6 von 58 bestanden** | Geräteabnahme |

---

## 2. Blocker und Fehler (G — gravierend)

### G1 ★ · Ein Kopfhörer-Wechsel setzt Netzwerk, Codecs und Verschlüsselung zurück

`src/Nipp.Core/Services/Telephony/SipService.cs:1596`

```csharp
_applier.Apply(core, new NippSettings { Audio = audio });
```

`SettingsApplier.Apply` (Zeile 25–33) ruft **immer** `ApplyNetwork`,
`ApplyNatMedia`, `ApplyAudio`, `ApplyCodecs`. Ein frisches `NippSettings`
liefert für alles ausser `Audio` die Werkseinstellungen. Wer Codec-Reihenfolge,
STUN, RTP-Portbereich, „Verschlüsselung erzwingen" oder die Zertifikatprüfung
verstellt hat, verliert das beim Ein- oder Ausstecken eines Geräts — still,
bis zur nächsten Einstellungsänderung. Das Protokoll meldet „Einstellungen auf
den laufenden Core übertragen" und sieht richtig aus.

**Vorgehen:** die zuletzt angewendeten `NippSettings` vollständig merken
(statt nur `_audioSettings`) oder `ApplyAudioOnly` einführen. Test: nach
Gerätewechsel steht `core.AudioPayloadTypes` unverändert. **~2 h.**

### G2 ★ · Nach dem ersten Schliessen ins Tray ist das Fenster taub

`src/Nipp.App/MainWindow.xaml.cs:262–270` gegen `App.xaml.cs:551–560`

`Window.Closed` feuert in WinUI 3 bei jedem Klick auf das Fensterkreuz, **bevor**
`Handled` ausgewertet wird — alle Handler laufen. `MainWindow` hat zuerst
abonniert und meldet ohne Rücksicht auf `_exiting` alles ab:

```csharp
private void OnClosed(object sender, WindowEventArgs args)
{
    RememberPlacement();
    _sip.CallStateChanged -= OnCallStateChanged;   // <- auch beim Verstecken
    _settings.Changed -= OnSettingsChanged;
    _theme.EffectiveThemeChanged -= OnEffectiveThemeChanged;
    Closed -= OnClosed;
}
```

Erst danach setzt `App.OnWindowClosed` `args.Handled = true` und versteckt das
Fenster. Folge: **nach dem ersten Verstecken navigiert `MainWindow` bei einem
eingehenden Anruf nie mehr zur Gesprächsansicht und holt das Fenster nicht
mehr nach vorn** — genau der Weg, den `_announcedCalls` und `ShowFromTray`
absichern sollten. Ausgehende Anrufe sind nicht betroffen (`ShellPage` navigiert
selbst, Zeile 173). Thema und „immer im Vordergrund" werden ebenfalls nicht mehr
nachgezogen.

Das passt zu dem, was CLAUDE.md als offen führt: T06 wurde bisher nur am
offenen Fenster geprüft. Im Alltag lebt nipp im Infobereich.

**Vorgehen:** Abmeldung in ein `Shutdown()` verlegen, das `ExitApplication`
ruft; `OnClosed` nur noch `RememberPlacement()`. **~1 h.** Danach **T06 mit
verstecktem Fenster** — das ist die eigentliche Abnahme.

### G3 · `Refreshing` gilt im zweiten Exemplar der Regel weiter als Fehler

`SipService.cs:1329–1345`, `ReadDefaultAccountStatus()`

```csharp
return account.State.ToString() switch {
    "Ok" => Registered, "Progress" => InProgress, "Cleared" => Unregistered,
    "None" => None, _ => RegistrationStatus.Failed };
```

`Refreshing` fehlt. Die Bridge (`SipEventBridge.cs:106–108`) hat den Befund F2
aus dem letzten Review behoben — diese Kopie nicht, und Zeile 1284
überschreibt das Ergebnis der Bridge: `RegistrationStatus =
ReadDefaultAccountStatus() ?? e.Status`. Bei jeder Erneuerung wird die LED
kurz rot und das Log meldet `Failed`.

**Vorgehen:** `MapRegistrationStatus` in der Bridge `internal static`, hier
aufrufen, Kopie streichen. **~30 min.**

### G4 · Blindes Weiterleiten wählt die Rohform

`ActiveCallViewModel.cs:369`, `SipService.cs:669`

`TransferAsync(call.Handle, TransferTarget.Trim(), …)` — kein
`NumberNormalizer`. Aus „079 123 45 67" wird `sip:079 123 45 67@pbx.example.ch`.
Die einzige Stelle, an der eine Nummer eingetippt und nicht normalisiert wird.

**Vorgehen:** Normalisierung im Dienst (dann gilt sie für jeden Aufrufer).
Test „Weiterleiten normalisiert wie Wählen". **~1 h.**

### G5 · Scheitert der zweite Anruf, bleibt der erste auf Halten

`SipService.cs:621→641` — `PlaceCallAsync` pausiert erst alle verbundenen
Gespräche, dann `InviteAddressWithParams(...) ?? throw`. Liefert das SDK
`null`, fliegt die Ausnahme ohne `Resume()`.

**Vorgehen:** Pausieren erst nach erfolgreichem Invite, oder im Fehlerfall
zurückholen. **~1 h.**

### G6 · Testabruf sendet Token über `http://`

`IntegrationHttpClient.cs:167` prüft nur `Uri.TryCreate`; die https-Regel
lebt nur im Validator (`IntegrationConfigValidator.cs:226`). Der Testknopf
in den Einstellungen (`IntegrationSettingsViewModel.cs:159, 246`) nimmt
**alle** Quellen, auch ungültige, und schickt Bearer oder API-Key im Klartext.

**Vorgehen:** Schemaprüfung in `SendAsync` selbst — Verteidigung in der Tiefe.
**~1 h.**

### G7 · Anruferkontext: ungeschützte Dictionaries zwischen Threadpool und UI-Thread

`CallerContextService.cs:236–310`, `CallerContextCache.cs:26`,
`IntegrationHealth.cs:276`

Nach `ConfigureAwait(false)` schreibt `AskAsync` vom Threadpool in
`_cache` und in `session.Snapshot`; `Begin` liest gleichzeitig auf dem
UI-Thread. Zwei gleichzeitig antwortende Quellen verlieren ein Fragment (eine
Quelle bleibt für immer „wird gefragt …"). Ein `Dictionary` unter
gleichzeitigem Schreiben kann in .NET in einer Endlosschleife in `FindValue`
enden — **auf dem Thread, der alle 20 ms `Core.Iterate()` bedient.** Das
Ausfallbild wäre kein Absturz, sondern ein hängendes Telefon.

**Vorgehen:** Cache-Schreiben und `Apply` per `_ui.Post` auf den UI-Thread
(wie `ContactSearchService`), `IntegrationHealth` mit `lock`. **~3 h.**

### G8 · Zwischenspeicher und Schutzschalter werden nie geleert

`CallerContextCache.Clear()` und `IntegrationHealth.Clear()` haben keinen
Aufrufer. Nach einer Mapping-Korrektur zeigt die Karte fünf Minuten die alte
Antwort; nach Abschalten einer Quelle bleiben deren personenbezogene Fragmente
bis zum Ablauf im Speicher.

**Vorgehen:** `CallerContextService` abonniert `_config.Changed` → `Clear()`;
`IntegrationRegistry.Rebuild()` setzt die Gesundheit zurück. **~1 h.**

### G9 · Redirects tragen API-Key auf fremde Hosts

`IntegrationHttpClient.cs:114–120` — `SocketsHttpHandler` folgt bis zu 50
Weiterleitungen. .NET entfernt dabei nur `Authorization`; die
`apiKey`-Kopfzeile und alle konfigurierten `Headers` gehen mit. Ein 302 vom
Proxy reicht.

**Vorgehen:** `AllowAutoRedirect = false`, 3xx als Fehler „leitet um —
Basisadresse prüfen". **~30 min.**

### G10 · Unpackaged geht die tel:-Nummer der zweiten Instanz verloren

`App.xaml.cs:160–252` — unpackaged liefert die Aktivierung als `Launch` mit der
URI in `Arguments`; `ExtractCallTarget` kennt nur `ProtocolActivation`, und
`allowCommandLine: false` verwirft den Rest. Fenster kommt nach vorn, gewählt
wird nichts. Packaged ist nicht betroffen — aber ADR-008 nennt beide
Betriebsarten als unterstützt. **~1 h.**

---

## 3. Risiken

### Datenschutz (D)

**D1 · Rufnummern im Protokoll** — `TelephonyLog.cs` (EventIds 2030–2037,
2088: `{Destination}`, `{Number}`, Aufnahmepfad mit Nummer), `CallHistoryStore.cs:105`
(`{Number}` auf Debug), `TelephonyLog.cs:194` (Nebenstellen). Die Regel „keine
Rufnummer im Protokoll" ist in `Integrations/` durch einen Architekturtest
erzwungen — für den Telefonie-Kern gilt sie nicht, und dasselbe Log geht an den
Support. **Entscheidung nötig** (siehe §6): maskieren (`…8430`) oder §21.2
ausdrücklich auf Integrationen beschränken. Danach der Architekturtest für alle
`[LoggerMessage]`-Vorlagen. **~3 h.**

**D2 · Diagnosepaket kopiert Logs ungefiltert** — `DiagnosticsBundle.cs:166`.
Damit gilt die Whitelist nur für die JSON-Dateien. Dazu steht die
Provisioning-URI wörtlich im Paket (`:203`; ein Token darin wäre sichtbar) und
der Konfigurationspfad mit Benutzername. Der Test sucht nur bekannte
Geheimnisse, kein Muster. **~3 h.**

**D3 · Feste Kopfzeilen als Nebenweg für Geheimnisse** —
`IntegrationConfig.cs:196` `Headers` wird ungeprüft gesendet;
`"Authorization": "Token abc"` in `integrations.json` funktioniert und wird
exportiert. Validator: Fehler bei `Authorization`, `Cookie`,
`Proxy-Authorization`. **~1 h.**

### Telefonie (T)

- **T1** `SipService.cs:1548–1597` — Gerätewechsel-Callback ruft `Apply`,
  `RetargetRunningCalls` und Datei-E/A (`RootCertificates.Ensure`, alle 30 Tage
  Zertifikatsexport) **direkt im SDK-Callback**. Regel aus CLAUDE.md: Flag
  setzen, im nächsten `Pump()` reagieren. **~2 h.**
- **T2** `SipService.cs:1369` — dritter Anruf: `Decline` im Callback, ohne
  Ereignis für die Oberfläche (§8.2 verlangt eine „klare Meldung"). **~2 h.**
- **T3** `SipService.cs:690–706, 1499` — Annehmen bei laufendem Gespräch
  pausiert das laufende nicht (beim Wählen wird es ausdrücklich getan, mit
  derselben Begründung). **~1 h.**
- **T4** `SipService.cs:409 vs. 489` — Zugangsdaten werden mit dem
  Benutzernamen gesucht, aber mit der Auth-ID angelegt; weicht die Auth-ID ab,
  bleiben sie beim Löschen verwaist (§9.1 verbietet das). **~1 h.**
- **T5** `SipService.cs:1286–1293` — Registrierungsfehler eines Nebenkontos
  nennt die Domäne des zuletzt registrierten Kontos. **~1 h.**
- **T6** `SipService.cs:610` — Kontowahl über `core.DefaultAccount` statt
  `CallParams.Account`; danach misst `ReadDefaultAccountStatus` das falsche
  Konto. **~1 h.**
- **T7** `SipService.cs:1660–1673` — `Resuming` wird zu `Dialing` („wird
  aufgebaut" nach dem Makeln); `Referred`, `Updating` ebenso. **~1 h.**
- **T8** `SipService.cs:799–804` — begleitete Übergabe akzeptiert ein noch
  klingelndes Gespräch als Ziel; der Wrapper verschluckt den Fehlercode. **~1 h.**
- **T9** `SipService.cs:1221` — `AudioStats` im `Pump` ohne Fangnetz.
- **T10** `SettingsApplier.cs:52` — Keep-Alive-Intervall wird verworfen (nur
  an/aus); `DetectNetworkChanges` (`NippSettings.cs:101`) wird nirgends gelesen;
  `RequiresRestart` meldet Neustart für die Zertifikatprüfung, die live
  angewendet wird.
- **T11** MWI (`SipEventBridge.cs:132–144`, `ShellViewModel.cs:861`) — kennt kein
  Konto (mit zwei Konten überschreibt das letzte NOTIFY), und
  `MwiServerAddress` wird nie gesetzt — kein SUBSCRIBE, nur unaufgeforderte
  NOTIFYs. **~3 h.**

### App-Schicht (A)

- **A1** `App.xaml.cs:347` → `IntegrationConfigStore.Changed` feuert **vom
  Threadpool** in `IntegrationSettingsViewModel.Reload()` (ObservableCollection)
  und `CallerCardViewModel`. Heute nur ruhig, weil das Timing passt. Store
  soll `Changed` über den `SynchronizationContext` posten. **~1 h.**
- **A2** `ActiveCallPage.xaml.cs:112–122, 213–227`, `SettingsPage.xaml.cs:298–326,
  709–731`, `TrayIconHost.cs:98` — `async void` um `ExecuteAsync` ohne
  Fangnetz. Endet der Anruf zwischen Klick und Ausführung, beendet eine
  `InvalidOperationException` die App. Gemeinsames `RunGuarded`. **~2 h.**
- **A3** `App.xaml.cs:174` — `RedirectActivationToAsync` synchron auf dem
  UI-Thread erwartet; das offizielle Muster ist ein Hilfsthread vor
  `Application.Start`. **~2 h.**
- **A4** `ToastService.cs:250–291` — fängt nur `InvalidOperationException`;
  `Register()` nach dem Abonnieren; Doc-Kommentar verspricht Aktivierung bei
  nicht laufender App, die `OnLaunched` nicht behandelt.
- **A5** `App.xaml.cs:565–595` — handverlesene Dispose-Liste; `SipService`,
  `ConnectivityMonitor`, `ThemeService` fehlen. Einmal `(Services as
  IDisposable)?.Dispose()`. Dazu `AppLog.cs:67/84` doppelte EventId 1052,
  Shutdown-Fehler wird als „Telefonie liess sich nicht starten" protokolliert.
- **A6** `App.xaml.cs:284–313` — `Settings.Changed` wird nur abonniert, wenn das
  SDK lädt; Protokollstufe, Hotkey, Autostart wirken sonst erst nach Neustart.
- **A7** `ActiveCallPage.xaml.cs:399`, `ShellPage.xaml.cs:834`,
  `DisplayConverters.cs:31/76/148` — werfender Ressourcen-Indexer mit
  String-Literalen; `XamlResourceTests` sieht das nicht. `TryGetValue` wie in
  `CardView`.

### ViewModels und Einstellungen (V)

- **V1** `SettingsViewModel.cs:250–254` — mit zehn Konten lässt sich keines
  mehr bearbeiten (`CanAddAccount` gilt auch beim Bearbeiten). **~30 min.**
- **V2** `SettingsService.cs:628` `Reset()` — setzt auch Team-Nebenstellen,
  Provisioning-Adresse, `AllowInsecureProvisioning`, Fensterlage zurück; die
  Meldung verspricht nur „Konten bleiben". **~1 h.**
- **V3** `SettingsService.cs:580–615` `TryImport` — nur Codecs werden geprüft;
  `SipPort: 0`, `HistoryRetentionDays: -5` werden gespeichert.
  `ProvisioningService.cs:274–319` — gleiche Lücke, und `Save` kann **nach**
  `Policy.Apply` und `Secrets.Set` werfen: halb angewendetes Profil,
  `LastError` bleibt `null`. Gemeinsamer `SettingsValidator`. **~4 h.**
- **V4** `ProvisioningService.cs:293–303`, `SettingsService.cs:603` — ersetzte
  Konten hinterlassen verwaiste Geheimnisse (Einzelpfade räumen auf, die
  Massenpfade nicht). **~1 h.**
- **V5** `SettingsViewModel.cs:293–297, 931` — `RecordingDirectory` verliert
  `null`; der Benutzerpfad landet in `settings.json` und in jeder Exportdatei.
- **V6** `NumberNormalizer.cs:138–157` — „+41 (0) 79 …" wird zu `+41079…`.
- **V7** `ShellViewModel.cs:934–938` — Mailboxnummer aus den Einstellungen
  erscheint erst nach Abschnittswechsel.
- **V8** `ActiveCallViewModel.cs:290–404` — Zustandsregeln doppelt (Property und
  Rumpf, `IsEnabled` von Hand in der Seite); `TransferBlind` ohne `CanExecute`.
- **V9** `ProvisioningService.cs:460–463` — unbrauchbarer Wert wird als
  „unbekannte Einstellung" gemeldet.

### Integrationen (I)

- **I1** `ContactMerger.cs:532, 551–553` — Zusammenführen über eine gemeinsame
  Nummer: zwei Personen derselben Firma mit der Zentrale als Geschäftsnummer
  werden **eine** Zeile, die zweite Person verschwindet. Kein Test dafür.
  **~3 h.**
- **I2** `ExpressionParser.cs:580–582`, `MappingEngine.cs:327–330` — `$.pfad` in
  Ausdrücken wird bei **jeder** Auswertung neu übersetzt, im Anrufpfad auf dem
  UI-Thread; Syntaxfehler erscheinen erst zur Laufzeit statt beim Validieren.
- **I3** `IntegrationSettingsViewModel.cs:258` — Rohantwort des Testabrufs
  (bis 8 KB Kundendaten) bleibt im Singleton, bis der nächste Test läuft.
- **I4** `IntegrationLogs.cs:76–78, 139–141` — zwei tote Protokollmeldungen.

### Kontakte, Verlauf, Windows (K)

- **K1** `OutlookContactSource.cs:204–213` — Outlook-Zustand endet im Log
  (ALLTAG-PLAN 2a, bestätigt): `IsRunning` wird nirgends verwendet, `olk.exe`
  wird nicht erkannt. **~2 h**, Vorgehen steht in ALLTAG-PLAN.md.
- **K2** `OutlookContactSource.cs:145–177`, `ContactStore.cs:97–103` — nach der
  Zeitgrenze läuft der STA-Thread weiter; „Aktualisieren" startet einen
  **zweiten** gegen dasselbe hängende Outlook. Kein `CancellationToken`
  durchgereicht. **~2 h.**
- **K3** `ClipResolver.cs:364–386` — Suffixvergleich ab 7 Ziffern: `044 512 84
  30` trifft `+49 30 445128430`. Beide Seiten nach E.164 normalisieren, Suffix
  nur als Rückfall. **~2 h.**
- **K4** `ContactStore.cs:82–95` — `OutlookCacheHours` hat nach dem Start keine
  Wirkung (kein Timer).
- **K5** `CallHistoryStore.cs:49–74, 186–191` — kein `PRAGMA user_version`
  (kein Migrationspfad), Aufbewahrung `0` = unbegrenzt, `LIKE` ohne `ESCAPE`.
- **K6** `WindowsIntegration.cs:159–257` — unpackaged keine Einzelinstanz bei
  tel:-Klick; `AreProtocolHandlersRegistered` ignoriert `UserChoice` und ist
  wie `IsAutostartEnabled` toter Code; `callto://…` liefert `//…`.
- **K7** `GlobalHotkeyService.cs:337–339` — WndProc-Delegat nach `Join(2 s)`
  freigegeben, auch wenn der Thread noch lebt.
- **K8** `App.xaml.cs:303` — `ConnectivityMonitor` fehlt in der Dispose-Liste;
  ein laufender Debounce nach `ShutdownAsync` läuft in `RequireCore()`.

---

## 4. Oberfläche und Bedienung

### 4.1 An der laufenden Anwendung gesehen (L) ★

Bildschirmfotos: `docs/review/2026-09-06/01-start.png` bis `16-opt-Integrat.png`.
Fenster 459 × 733 logische Pixel bei 150 % (gemerkte Lage, nicht Standard).

**L1 · „Optionen" unten, „Einstellungen" oben.** Der Tab heisst „Optionen"
(`ShellPage.xaml:1035`), die Seite „Einstellungen" (`SettingsPage.xaml:51`),
alle Hinweise im Code sagen „in den Einstellungen". Zwei Namen für eine Seite.
Foto 07.

**L2 · Audio-Auswahl ist leer, wenn der Windows-Standard gilt.** Mikrofon,
Lautsprecher und Klingelgerät zeigen ein leeres Feld (Foto 10). Ursache:
`SelectedInput = InputDevices.FirstOrDefault(d => d.Id == …)` wird `null`
ohne gespeicherte Gerätekennung, und die `ComboBox` hat keinen
`PlaceholderText` (`SettingsPage.xaml:245–262`). Der Benutzer kann „Standard"
nicht von „nichts gefunden" unterscheiden. Vorschlag: `PlaceholderText="Windows-
Standard"` oder ein erster Eintrag „Windows-Standard (Gerätename)".

**L3 · Listenzeilen ohne Namen für Hilfstechnik.** Der UI-Automation-Baum
zeigt jede Kontaktzeile als `Nipp.Core.ViewModels.ContactRow` — ein
Bildschirmleser liest den Klassennamen vor. `AutomationProperties.Name` an
den `ListViewItem`-Vorlagen fehlt (Team, Outlook, Suche, Anrufliste).

**L4 · Umschriebene Umlaute in sichtbaren Texten.** „Serverzertifikat
pruefen" / „Prueft das Zertifikat…" (`SettingsPage.xaml:333–334`, Foto 11),
„Konto fuer ausgehende Anrufe" (`ShellPage.xaml:251`), „Erklaert, warum
Begleitet uebergeben…" (`ActiveCallPage.xaml:432`), „Kopieren nicht moeglich"
(`ActiveCallPage.xaml.cs:208`, `ShellPage.xaml.cs:366`), „liess sich nicht
uebernehmen" (`SettingsPage.xaml.cs:391`), „waehlen / laesst / Tastenkuerzel"
(`SettingsViewModel.cs:444–453`), „Passwoerter" in der **Exportdatei**
(`SettingsService.cs:166`). Die Regel lautet ss statt ß — nicht ue statt ü.
Ein Test über alle Benutzertexte (`ae|oe|ue` nach Konsonant) hält das fest.

**L5 · Suchtreffer ohne sichtbaren Bezug.** Suche „ruoss" liefert auch
„Bernd Clemens", „Franz Pazeller", „marco järmann" (Foto 04). Gegen die
CRM-API geprüft: die Treffer stimmen — alle gehören zum Kunden „RUOSS,
CLEMENS & PARTNER AG", der als **zweiter** Kunde am Kontakt hängt. nipp zeigt
aber nur `primary_customer_name`, und der heisst bei diesen Kontakten wie die
Person selbst („Bernd Clemens · Bernd Clemens"). Zwei Dinge: der Untertitel
wiederholt den Namen, und der Grund des Treffers ist unsichtbar. Vorschlag im
Mapping: `customer_names` bevorzugen, wenn es vom Namen abweicht; sonst
Untertitel leer lassen. Dazu ein Auftrag an CRM: „Ruoss Toni" und „Toni
Ruoss" sind Dubletten in den Daten.

**L6 · Wählfeld kennt CRM nicht.** „044" im Nummernfeld zeigt keine
Vorschläge (Foto 03), weil die Vorschlagsliste nur Team, Outlook und Verlauf
kennt (ADR-014) — für „15" liefert sie Team und Verlauf korrekt (Foto 17). Das Suchfeld darunter findet dieselbe Nummer. Zwei Felder,
zwei Reichweiten — nachvollziehbar begründet, aber nicht selbsterklärend. Kein
Fehler; eine Frage für §20.

**L7 · Jeder Ansichtswechsel schreibt `settings.json` und `secrets.dat` neu.**
Das Ein-/Ausblenden der Wähltastatur und jeder Klappzustand
(`OnIsTeamExpandedChanged`, `OnIsOutlookExpandedChanged`) gehen über
`SaveViewState` → `Write`, und das Protokoll zeigt jedes Mal „Zugangsdaten
abgelegt (3 Einträge)". Beobachtet: „15" ins Nummernfeld tippen und wieder
löschen ergab **drei** Schreibvorgänge innerhalb von acht Sekunden
(18:49:06, :07, :14) — die Vorschlagsliste klappt die Abschnitte mit, und der
Wächter in `SaveExpansion` sieht echte Änderungen. Ein UI-Zustand sollte die
DPAPI-Datei nicht anfassen; ein Absturz mitten im Schreiben träfe die
Passwörter. Vorschlag: `Write(notify: false)` lässt den `SecretStore` aus, und
Klappzustände, die von der Vorschlagsliste ausgelöst werden, werden nicht
gespeichert.

**L11 · Wähltastatur-Zustand nach Neustart — einmal abweichend beobachtet.**
Vor dem Beenden war die Tastatur offen (Fotos 03–06, Speicherung 18:38:46);
nach dem Neustart aus dem frischen Build war sie zu, und `settings.json` steht
auf `false`. Nicht reproduziert, Ursache offen. Als Testfall festhalten
(Tastatur öffnen, beenden, starten), zusammen mit L7 klären.

**L8 · Zwei Verlaufsdatenbanken.** `%LOCALAPPDATA%\nipp\history.db` ist die
echte (`CallHistoryStore.cs:47`); `%APPDATA%\nipp\call-history.db` ist eine
leere Datei vom 04.09. ALLTAG-PLAN.md nennt die falsche. Aufräumen und Doku
korrigieren.

**L9 · Wähltastatur bleibt auf allen Tabs offen** (Fotos 05, 06). In der
Anrufliste und der Mailbox nimmt sie ein Drittel des Fensters, ohne dort
gebraucht zu werden. Vorschlag: nur im Kontakte-Tab zeigen, oder beim
Tabwechsel einklappen.

**L10 · Datenqualität der Nebenstellen:** „Hotine SR" (Tippfehler in der
Anlage, nicht in nipp). Erwähnt, damit niemand im Code sucht.

### 4.2 Aus dem XAML (O)

Alle Befunde F1–F5, K1–K5, D1–D7 vom 05.09. sind umgesetzt (Belege im
Prüfbericht); K6 bewusst nur teilweise. Neu:

- **O1** Feste `Height` an Knöpfen mit Symbol **und** Beschriftung
  (`ShellPage.xaml:1012`, `ActiveCallPage.xaml:316, 69, 308`, `Keypad.xaml:20`).
  Bei Windows-Textskalierung 150 % wird die Beschriftung abgeschnitten.
  `Height` → `MinHeight`. **~1 h**, dann am Gerät bei 150 % Text nachmessen.
- **O2** `ActiveCallPage.xaml:55–62` — „Gespräch läuft weiter" steht statisch
  in jedem Zustand, auch beim Klingeln. Aus `Describe(call.Status)` speisen.
- **O3** `SettingsPage.xaml:559–567, 300–308` — Hotkey-Meldung und
  Kalibrierungstext in einem horizontalen `StackPanel`: `Wrap` wirkt nie, der
  Text wird rechts abgeschnitten — ausgerechnet „… ist von einer anderen
  Anwendung belegt". `Grid` mit `Auto`/`*`.
- **O4** `ShellPage.xaml.cs:717` — Leerzustand prüft die **gefilterte** Liste:
  Filter „Verpasst" ohne Treffer sagt „Noch keine Anrufe."
- **O5** `ShellPage.xaml:504–545` — Filter „Aufgenommen" (§8.3) fehlt in der
  Oberfläche, `CallHistoryFilter.Recorded` existiert. `FontSize="11"` an den
  RadioButtons unter Caption-Grösse.
- **O6** `ThemeService.cs:138` — Kontrastmodus wird zur Laufzeit von den
  Hell/Dunkel-Farben überstimmt; `HighContrast`-Wörterbuch aus `Tokens.xaml`
  ist wirkungslos. Bei `AccessibilitySettings.HighContrast` auslassen.
- **O7** Zwei Vokabulare für den Kontozustand: „angemeldet" oben
  (`ShellViewModel.cs:720`), „Registriert" in den Einstellungen
  (`DisplayConverters.cs:226`, Foto 08). „Weiterleiten" und „Übergabe"
  gemischt (`ActiveCallPage.xaml:391–440`).
- **O8** `SettingsPage.xaml:46, 415–420` — Zurück-Knopf und Codec-Pfeile ohne
  `AutomationProperties.Name` (U21 aus REVIEW.md, weiter offen).
- **O9** `ShellPage.xaml:319–345` — `NumberBox` ohne rechtes Padding, langer
  Text läuft unter die beiden Symbole.
- **O10** `ActiveCallPage.xaml:64–79` — Auflegen verliert beim Hover das Rot
  (Standardvorlage überschreibt `Background`).
- **O11** Drei fast identische Zeilenvorlagen (Team, Outlook, Suche,
  `ShellPage.xaml:22–224`) entgegen dem eigenen Kommentar in Zeile 15–18;
  `CornerRadius="14"` hart an `NippListAvatarSize`=28 gekoppelt.
- **O12** Zwei Abstandsraster nebeneinander: Tokens (3/6/10/14) und hart
  codiert 4/8 (`Spacing="8"` 20×, `ColumnSpacing="8"` 12×). `NippGapSection`
  ungenutzt. `SettingsPage.xaml:40, 55, 825` mit eigenen Rändern statt
  `NippPagePadding`.
- **O13** `SettingsPage.xaml:796–804` — Antwortfeld `NoWrap` ohne horizontales
  Scrollen: lange JSON-Zeile bei 350 px unerreichbar.
- **O14** Escape als Auflegen fehlt (U14). „Ctrl+Shift+A" als Platzhalter
  neben „Strg+Umschalt" in §9.6.

Dunkles Thema: **kein Befund** — keine harte Farbe im XAML, alle Pinsel über
Tokens.

---

## 5. Spezifikation, Tests, Dokumentation (S)

### 5.1 Abweichungen ohne ADR

| Spez. | Anforderung | Zustand |
|---|---|---|
| §10 | Infobereich-Menü mit **„Präsenz setzen"** | fehlt, kein ADR (REVIEW U23 nennt es) |
| §8.4 | BLF-Zustand **„klingelt"** | kommt über `presence` nie an; docs/blf-pruefung.md sagt selbst „vorher gehört eine ADR geschrieben" |
| §9.6 | „Version / Update prüfen" | fehlt, hängt an §16.4 — kein ADR |
| §9.1 | Schalter „Konto aktiviert" | fehlt im Modell |
| §8.1 | Kurzwahl aus der Provisionierung | fehlt |
| §8.4 | Outlook-Hinweis statt leerer Liste | fehlt (K1) |
| §9.2–9.6 | IPv6, RTP-Portbereich, DSCP, Medienverschlüsselung als Auswahl, TURN, adaptive Bitrate, Klingelton-Datei, Codec-Tabelle mit Rate/Bitrate, Protokoll-Schalter | **nur im Modell**, nicht in der Oberfläche — rund ein Dutzend §9-Felder sind allein per Provisioning oder JSON erreichbar |
| §11 | Schloss und Tooltip **je Feld** | `SettingCard` nur an vier Stellen; sonst Sperre auf Gruppenebene |
| §9.4 | Echo-Kalibrierung „eigener Dialog mit Fortschritt" | Schaltfläche und Text, kein Fortschritt |
| §6 | `StartRecordingAsync(call, path)`, `RegistrationState`, `Core.AddListener`, Iterate im `SipService` | anders gebaut, nur inline begründet — ein Sammel-ADR „SDK-bedingte Abweichungen von §6" |
| — | Schalter „Kontakte aus Outlook", „Besetztlampenfeld", Fensterlage merken, `sips:`-Handler | gebaut ohne Auftrag und ohne ADR |

**ADR-018** (Outlook: COM gegen Graph, Befund neues Outlook) ist seit dem 06.09.2026 abends geschrieben und entschieden: **COM bleibt, Graph zurückgestellt.**

### 5.2 Testmatrix

**6 von 58 Fällen bestanden** (T01, T05, T07, T08, T09, T18 — alle am
04.09.). Ausdrücklich offen: T06, T20, T33, T38. Nie geprüft: T02–T04, T10,
T11, T13–T17, T19, T21–T32, T35–T37 und **alle** Integrationsfälle T40–T59.
T60–T65 aus ALLTAG-PLAN.md stehen noch nicht in der Matrix.
`docs/test-matrix.md:111` meldet den eingehenden Anruf als bestanden, Zeile 23
setzt T06 auf offen — Zeile 111 gehört korrigiert.

Ohne Testfall: Live-Vorschläge (§8.1), Qualitätspanel (§8.2), Verlaufsfilter
und Suche (§8.3), Outlook laden (§8.4), „Annehmen und halten", Auto-Annahme,
Klingelgerät (§8.6), Erscheinungsbild, zehn Konten (§20), Infobereich-Menü
(§10), Export/Import/Reset (ADR-014).

Nicht-funktionale Ziele aus §2: **keiner** gemessen. `docs/performance.md`
(AP7.8) existiert nicht.

### 5.3 Komponententests — Lücken

603 Tests sind grün, aber die Verteilung ist schief:

- **`ShellViewModel` (1'131 Zeilen): kein Test.** `SettingsViewModel` (1'107):
  kein Test. `CallerCardViewModel`, `IntegrationSettingsViewModel`: keine.
- `SecretStore`: kein eigener Test. `ProvisioningService.ApplyValues` und
  `FetchRemoteProfileAsync`: ungetestet. `Nipp.Provisioning/Program.cs`: kein
  Test — `KnownPaths` ist eine Kopie der `switch`-Liste in
  `ProvisioningService.cs:386–457`; ein Vergleichstest hält sie zusammen.
- `ContactMerger`: der Zentrale-Fall (I1) fehlt. `ClipResolver`: fremde
  Landesnummer (K3) fehlt. `NumberNormalizer`: Klammer-Null, `#31#…`,
  `*21*…#`, „0" allein.

Die grössten ViewModels sind reine, SDK-freie Logik — sie liessen sich ohne
Gerät prüfen und sind heute die grösste ungedeckte Fläche.

### 5.4 Dokumentation gegen Realität

| Behauptung | Befund |
|---|---|
| „603 Tests, 14 Architekturtests" | **stimmt** ★ |
| Tray-Symbol, zweite Rufnummer, Auflegen oben behoben | **stimmt**, im Code belegt |
| CLAUDE.md: „`Strings/de-CH.resw` ist angelegt, aber leer" | es gibt keine `.resw`, nur ein leeres Verzeichnis |
| README: `%APPDATA%\nipp\secrets.dat` | liegt unter `%LOCALAPPDATA%` (`SecretStore.cs:37`) |
| IMPLEMENTATION-PLAN Kopf „Stand 04.09., Phase P5, Rev. 4" | veraltet; AP6.3 „Graph zuerst" und AP8.2 „`Core.ProvisioningUri`" sind mit **falschem Text** abgehakt |
| INTEGRATION-PLAN „§21 steht noch nicht" | veraltet |
| „Vorlagen bleiben abgeschaltet" | Quellen ja; `callerLookup.enabled` steht in beiden JSON auf `true`, der Test prüft nur die Quellen |
| ALLTAG-PLAN: `call-history.db` | heisst `history.db` (L8) |

Lokalisierungsschuld: rund **1'000 Strings** in ~30 Dateien (548 in XAML, ~450
in C#), keine `.resw`, kein `x:Uid`. §15 unerfüllt; ein ADR zum Aufschub fehlt.

---

## 6. Plan

Reihenfolge nach Wirkung im Alltag und Abhängigkeit. Aufwände sind
Schätzungen für eine Person.

### Paket 1 — Vor der nächsten Geräteabnahme (≈ 1 Tag)

Alles, was T06 und den Alltag direkt betrifft, und alles unter zwei Stunden.

| # | Befund | Aufwand |
|---|---|---|
| 1 | **G2** Fenster taub nach Schliessen ins Tray | 1 h |
| 2 | **G1** Gerätewechsel setzt Einstellungen zurück | 2 h |
| 3 | **G3** `Refreshing` als Fehler (Kopie) | 30 min |
| 4 | **G4** Weiterleiten normalisieren | 1 h |
| 5 | **G5** Rollback beim zweiten Anruf | 1 h |
| 6 | **V1** Zehn Konten, keines bearbeitbar | 30 min |
| 7 | **L4** Umlaute in Benutzertexten, mit Test | 1 h |
| 8 | **L1 / O7** ein Wort: „Einstellungen", „angemeldet" | 1 h |

Danach **T06 mit verstecktem Fenster**, T07–T09 erneut (G4, G5 berühren sie),
T14 (G1).

### Paket 2 — Sicherheit und Datenschutz (≈ 2 Tage)

| # | Befund | Aufwand |
|---|---|---|
| 9 | **G6** https-Prüfung im HTTP-Client | 1 h |
| 10 | **G9** Redirects aus | 30 min |
| 11 | **G7** Anruferkontext threadsicher | 3 h |
| 12 | **G8** Cache und Schutzschalter leeren | 1 h |
| 13 | **D3** Kopfzeilen-Validator | 1 h |
| 14 | **D1** Rufnummern im Log — **nach Entscheidung** (§6.1) | 3 h |
| 15 | **D2** Diagnosepaket filtern | 3 h |
| 16 | **A1** `Changed` des Stores auf den UI-Thread | 1 h |
| 17 | **L7** `SaveViewState` ohne Geheimnisse | 30 min |

Danach T32 (Diagnosepaket), T51, T52, T57.

### Paket 3 — Robustheit Telefonie (≈ 2 Tage)

T1–T8, T11 (MWI je Konto), A2 (`RunGuarded`), A5–A7, K8, V8. Dazu ein
Sammel-ADR für §6 und die Nachträge zu §9.2 (T10). Vorher überlegen, ob
`SipService` (1'944 Zeilen) die Konto- und die Präsenzverwaltung abgibt —
beide sind natürliche Abspaltungen, und jeder der Befunde T4–T6 liegt genau
dort.

Danach T02, T03, T10, T12, T13, T17.

### Paket 4 — Einstellungen und Provisionierung (≈ 2 Tage)

V2–V7, V9, `SettingsValidator` als eine Quelle für Speichern, Import und
Profil. Dazu **L2** (Audio-Platzhalter), **O3** (Hotkey-Meldung), und die
fehlenden §9-Felder aus §5.1 — hier gehört **entschieden**, welche davon in
die Oberfläche kommen und welche per ADR „nur Provisioning" bleiben.

Danach T29, T30, T31, T19, T20.

### Paket 5 — Kontakte und Oberfläche (≈ 2 Tage)

K1 (Outlook-Hinweis, ALLTAG 2a), K2, K3, K4, I1, L3, L5, L9, O1, O2, O4, O5,
O6, O8–O10. Dann die Alltag-Funktionen 3, 4 aus ALLTAG-PLAN.md, sobald der
Auftrag in §8.2/§20 steht.

Danach T16, T27, T28, T40–T47, T46 besonders (Virtualisierung).

### Paket 6 — Tests und Dokumentation (laufend, ≈ 2 Tage gebündelt)

- Tests für `ShellViewModel` und `SettingsViewModel` — mindestens `CanExecute`
  je Zustand, Vorschlagsliste, Kontowechsel, Validierung.
- Architekturtest: kein `{Number}`/`{Destination}` in `[LoggerMessage]`
  (nach D1); Test auf `ae|oe|ue` in Benutzertexten (L4); `KnownPaths`-Vergleich.
- Doku: IMPLEMENTATION-PLAN Kopf und AP6.3/AP8.2, README `secrets.dat`,
  CLAUDE.md `.resw`, test-matrix Zeile 111, ALLTAG-PLAN `history.db`,
  INTEGRATION-PLAN Vorbehalt. T60–T65 in die Matrix.
- ADR-018 (Outlook), ADR „Präsenz im Infobereich", ADR „BLF klingelt", ADR
  „Lokalisierung zurückgestellt", Sammel-ADR §6.

### Nicht in diesem Plan, weil von aussen abhängig

Zertifikat (AP9.2), Messungen auf x64 (AP7.8), Update-Prüfung (§16.4),
Testmatrix auf frischem Win 10/11 (AP9.5) und Lizenz (AP9.6). Alle fünf stehen
in CLAUDE.md; hier nur, damit die Liste vollständig ist. Der Graph-Zugang
(ALLTAG 2b) gehört nicht mehr dazu — er ist entschieden und zurückgestellt
(ADR-018).

---

## 6.1 Entscheidungen, die vor dem Code fallen müssen

1. **Rufnummern im Protokoll (D1).** Maskieren (`…8430`) — dann ist der
   SIP-Trace auf Debug der einzige Ort mit Nummern, und der ist dokumentiert.
   Oder §21.2 ausdrücklich auf Integrationen beschränken. **Empfehlung:
   maskieren.** Ein Log, das an den Support geht, braucht die Nummer nicht;
   das Handle reicht, um einen Anruf zu verfolgen.
2. **§9-Felder ohne Oberfläche.** Welche der rund zwölf Felder kommen in die
   Einstellungen, welche bleiben Provisioning? **Empfehlung:** Medienverschlüsselung
   als Auswahl und Klingelton in die Oberfläche; IPv6, DSCP, RTP-Portbereich,
   TURN, adaptive Bitrate per ADR „nur Provisioning" — ein Endbenutzer stellt
   das nicht ein.
3. **Wähltastatur auf allen Tabs (L9)** — bleiben oder einklappen?
4. **Wählfeld und CRM (L6)** — soll die Vorschlagsliste im Nummernfeld
   auch fremde Quellen fragen? Kostet je Tastendruck einen Netzabruf;
   ADR-014 hat sich dagegen entschieden. **Empfehlung: so lassen**, aber im
   Suchfeld-Platzhalter sagen „Kontakte in CRM und Outlook suchen".
5. Die vier Punkte aus ALLTAG-PLAN.md (2b, 4, 5, Aufträge für 3/4/5) gelten
   weiter.

---

## 7. Build-Ergebnis

Nach dem Beenden der laufenden Instanz, unpackaged mit `-t:Rebuild`:

    Nipp.Core -> …\Nipp.Core.dll
    Nipp.App  -> …\Nipp.App.dll
    Der Buildvorgang wurde erfolgreich ausgeführt.
        0 Warnung(en)
        0 Fehler

Die Angabe „Build ohne Warnungen" in CLAUDE.md stimmt ★. nipp wurde danach aus
dem frischen Build wieder gestartet und hat sich registriert.
