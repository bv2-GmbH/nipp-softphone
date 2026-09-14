# Review nipp — Optik, Usability, Funktion und Codequalität

**Stand:** 05.09.2026, Commit `4a7c273` (HEAD), Arbeitsbaum sauber.
**Reviewer-Rolle:** Senior Software Engineer / UI-UX (WinUI 3, Fluent, VoIP).
**Vorgehen:** Spezifikation (NIPP-BUILD.md Rev. 5), README, IMPLEMENTATION-PLAN und ADRs gelesen; alle Quelldateien in `src/` und `tests/` durchgesehen; Solution gebaut; Tests ausgeführt; Screenshots aus `docs/review/` und das Laufzeitlog vom 05.09.2026 ausgewertet. **Kein Code geändert.**

## 0. Prüfgrundlage

| Prüfung | Ergebnis |
|---|---|
| `dotnet build Nipp.sln -c Debug` (x64-Host) | Alle Projekte kompilieren **ohne Compiler-Warnung**. Der Solution-Build scheitert nur am Kopieren nach `src\Nipp.App\bin\…`, weil die **laufende nipp-Instanz (PID 36516)** `Nipp.Core.dll` sperrt (MSB3027). Nipp.App wurde deshalb mit eigenem `OutDir` gebaut: **0 Warnungen, 0 Fehler**. |
| `dotnet test` Nipp.Core.Tests | **213 bestanden**, 0 Fehler, 589 ms |
| `dotnet test` Nipp.Architecture.Tests | **7 bestanden**, 0 Fehler |
| `build.ps1` | `.\build.ps1 build … -p:Foo=Bar` bricht in PowerShell 7 ab: *parameter name 'p' is ambiguous* (siehe F18). Der in README und CLAUDE.md dokumentierte unpackaged-Befehl funktioniert so nicht. |
| Laufzeitlog `nipp-20260905_001.log` | 14'382 Zeilen, 80 WRN, 37 ERR, 0 FTL. Auffällig: Klingelton-Datei nicht gefunden (3×), 21× verwaiste NOTIFY/481 nach Neustart, 3× SEH-Ausnahme im Besetztlampenfeld, 3× Überlauf der Ereignisschleife (max. 156 ms). |
| Linphone-Konfiguration `linphonerc` | `passwd=` **0**, `ha1=` **1** — das SDK legt nur den HA1 ab, kein Klartextpasswort. §10 ist damit erfüllt (nur gezählt, nicht gelesen). |

---

## 1. Gesamturteil

nipp ist ein **fortgeschrittener, ungewöhnlich gut dokumentierter Prototyp kurz vor interner Pilotreife**, aber noch nicht releasefähig: die Kernfunktionen sind gebaut und gegen die Test-PBX nachgewiesen, doch der Anruf-Workflow hat im Alltag sofort spürbare Sackgassen. Die **grösste Stärke** ist die konsequent erzwungene Schichtgrenze zum Linphone SDK (ein Listener, eigene Modelle, Architekturtest) zusammen mit einer Fehler- und Dokumentationskultur, die jede Abweichung begründet und jeden Fallstrick festhält; so etwas sieht man in Softphone-Projekten selten. Das **grösste Risiko** liegt nicht in der Architektur, sondern in ungetesteten Laufzeitpfaden der Oberfläche und der Windows-Integration: aus der Gesprächsansicht führt kein Weg zurück zur Wähltastatur (zweiter Anruf und begleitete Übergabe sind damit per UI unerreichbar), ein `tel:`-Klick bei laufender Instanz wählt nicht, und der lokale Klingelton wird im falschen Ordner gesucht. Dazu kommen drei Verstösse gegen die eigene Threading-Regel und ein Provisionierungsfehler, der Passwörter aus dem Speicher wirft. Die Testbasis (220 grüne Tests) ist solide für reine Funktionen, deckt aber **kein einziges ViewModel** ab, obwohl `ISipService` genau dafür gebaut wurde.

---

## 2. Befunde

Schweregrade: **Blocker** (verhindert Kernaufgabe oder verletzt Sicherheit/Recht), **Hoch** (fällt im Alltag sofort auf oder gefährdet Stabilität), **Mittel** (spürbar, umgehbar), **Niedrig** (Politur). Aufwand: S < ½ Tag, M 1–2 Tage, L > 2 Tage.

### 2.1 Optik (Fluent Design / Windows 11)

Das Gesamtbild stimmt: Mica-Hintergrund, Card-Brushes, Fluent-Icons, Accent-Buttons, Dark/Light über `ThemeResource`, ein eigenes HighContrast-Wörterbuch, 4-px-Raster in `Tokens.xaml`. Die Screenshots zeigen eine ruhige, konsistente Oberfläche. Die Abweichungen sind punktuell.

| # | Schwere | Fundstelle | Beobachtung | Empfehlung | Aufwand |
|---|---|---|---|---|---|
| O1 | **Hoch** | `src/Nipp.App/Views/ShellPage.xaml:476-565` | Die Umschaltleiste besteht aus vier `Button`. Es gibt **keinen Selektionszustand** — im Screenshot `docs/review/2026-09-05-nachher/main.png` sehen alle vier gleich aus, obwohl „Kontakte" aktiv ist. Der Benutzer erkennt den aktiven Bereich nur am Inhalt. | `SelectorBar` (WinUI, seit WASDK 1.5) oder `ToggleButton`-Gruppe mit `IsChecked` aus `ViewModel.Section`; Accent-Unterstrich wie in Fluent-Tab-Leisten. | S |
| O2 | **Mittel** | `ShellPage.xaml:501,524,547,561`; `ActiveCallPage.xaml:197,210,223` | Beschriftungen mit `FontSize="9"`. Die Fluent-Typo-Ramp beginnt bei Caption = 12. Bei 100 % Skalierung sind 9 px kaum lesbar, im Kontrastmodus schlecht. Grund laut Kommentar: „Einstellungen" passte nicht in 100 px. | `CaptionTextBlockStyle` (12) verwenden; Leiste auf 52–56 px; kurze Labels („Kontakte / Anrufe / Mailbox / Mehr") oder nur Icon mit Tooltip. | S |
| O3 | **Mittel** | `src/Nipp.App/Themes/Tokens.xaml:83-93`; `Converters/DisplayConverters.cs:31,76,175`; `Views/ActiveCallPage.xaml.cs:191-192` | Die Status-/Präsenz-Pinsel liegen **ausserhalb** der `ThemeDictionaries` und binden ihre Farbe per `ThemeResource`; die Konverter holen sie über `Application.Current.Resources[key]`. Auf App-Ebene richtet sich `ThemeResource` nach `Application.RequestedTheme`, nicht nach `RootGrid.RequestedTheme`, das `ThemeService` setzt (`MainWindow.xaml.cs:43`). Wählt der Benutzer „Dunkel" bei hellem Windows (oder umgekehrt), bleiben LEDs und Chips in den Farben des Systemthemas — `#107C10` auf dunklem Grund liegt bei etwa 2,5:1 Kontrast. | Pinsel je Theme in die `ThemeDictionaries` legen; Konverter durch `{ThemeResource …}`-Zuweisung im XAML (z. B. sichtbarkeitsgesteuerte Ellipsen oder ein `ActualTheme`-bewusster Lookup über `FrameworkElement.Resources`) ersetzen. Am Gerät verifizieren (siehe offene Frage Q2). | M |
| O4 | **Mittel** | `ShellPage.xaml:41-60` | Die Kontozeile zeigt den Registrierungszustand **nur als farbige LED**. Das verletzt die im selben Projekt festgeschriebene Regel „nie nur Farbe" (§8.4, Tokens.xaml Kommentar Z. 20-23). Der Text steht erst in den Einstellungen. | `AccountStateTextConverter` auch hier als Caption rechts vom Namen (er existiert bereits). | S |
| O5 | **Mittel** | `ShellPage.xaml:103-131` (28×28), `:292-299` (30×30); `Views/Settings/SettingsPage.xaml:315-320` (Codec-Pfeile, Standardgrösse ohne MinWidth) | Icon-Schaltflächen unter der Fluent-Mindestgrösse von 32×32 (Maus) bzw. 40×40 (Touch). Das × und der Tastatur-Toggle im Eingabefeld sind mit der Maus knapp, mit dem Finger nicht treffbar. | `MinWidth/MinHeight="32"` mit transparenter Trefferfläche; die beiden Symbole im Feld auf 32 anheben (das Feld ist 40 hoch). | S |
| O6 | **Mittel** | `ShellPage.xaml:446-466` | `ErrorBar` und `HintBar` liegen beide in Grid.Row 3 mit `VerticalAlignment="Bottom"` und überdecken die Liste; treten beide auf, überlagern sie sich. | Eigene Auto-Zeile zwischen Inhalt und Umschaltleiste, oder beide in einen `StackPanel`. | S |
| O7 | **Mittel** | `src/Nipp.App/Windows/WindowPlacement.cs:86-87` | Mindestgrösse wird nur beim Start geklemmt. Der Benutzer kann das Fenster auf 200 px ziehen, dann brechen Tastatur und Leiste um. `OverlappedPresenter.PreferredMinimumWidth/Height` (WASDK ≥ 1.7) wird nicht gesetzt. | In `MainWindow.ApplyWindowChrome` Presenter-Minimum 320×420 logisch × Skalierung setzen. | S |
| O8 | **Niedrig** | `ActiveCallPage.xaml:74,307` | `FontFamily="Consolas"` hart codiert — bricht die Segoe-UI-Variable-Ramp; Consolas fehlt in reduzierten Windows-Installationen nicht, ist aber kein Fluent-Font. | Segoe UI Variable mit `Typography.NumeralAlignment="Tabular"` für laufende Zahlen. | S |
| O9 | **Niedrig** | `MainWindow.xaml:37-39`; `Icons/nipp-light.png` | Titelleisten-Icon ist immer `AppIcon.ico` (helle Fassung mit cremefarbenem Quadrat). Auf dunklem Mica erscheint ein heller „Sticker" (sichtbar in allen Screenshots). Das Kaffee-Motiv selbst (Tasse von oben mit WLAN-Bögen) ist **dezent und stimmig**; im UI gibt es sonst kein Branding ausser der Fusszeile — richtig so. | Titelleisten-Icon wie das Tray-Icon themeabhängig wählen (`ThemeService.IsDark`), Hintergrund transparent. | S |
| O10 | **Niedrig** | `src/Nipp.App/Controls/Keypad.xaml:18-27` | Tasten 56×44 sind für Maus und Finger gut; die Buchstabenzeile mit 8 px liegt jedoch wieder unter der Ramp (dekorativ, daher vertretbar). | 9–10 px als bewusste Ausnahme dokumentieren oder `Typography`-Kapitälchen. | S |
| O11 | **Niedrig** | `MainWindow.xaml.cs` (kein Presenter-Wechsel) | Kein **CompactOverlay**. Für ein Fenster im Smartphone-Format wäre „Bild-im-Bild" im Gespräch der natürliche Fluent-Weg; „Immer im Vordergrund" ersetzt es nur teilweise. | `AppWindow.SetPresenter(AppWindowPresenterKind.CompactOverlay)` als Schalter in der Gesprächsansicht. | S |
| O12 | **Niedrig** | `src/Nipp.App/Strings/de-CH/` (leer) | §15 verlangt Benutzertexte in `Strings/de-CH.resw`. Es gibt **keine** `.resw`; alle Texte stehen hart in XAML und C# (Konverter, ViewModels, ToastService). Für Optik heute folgenlos, für jede zweite Sprache oder Textkorrektur ein Durchsuchen von 30 Dateien. | Schrittweise `x:Uid` einführen, zuerst für die Shell und den Toast. | L |

### 2.2 Usability

Kernaufgaben, Klickzahlen aus dem Code abgeleitet (Fenster offen, registriert):

| Aufgabe | Weg | Klicks/Tasten | Bewertung |
|---|---|---|---|
| Anruf tätigen | Nummer tippen, Enter | n + 1 | gut; Vorschau der normalisierten Nummer und Vorschläge sind stark |
| Anruf annehmen | Toast-Knopf oder „Annehmen" | 1 | gut; **kein Klingelton lokal** (U3) |
| Halten / Stumm | Toggle | 1 | gut |
| Weiterleiten blind | Ziel tippen, „Blind" | n + 1 | gut, Begriff „Blind" ist Fachjargon |
| Weiterleiten begleitet | Zweiten Anruf aufbauen … | **unmöglich** | Sackgasse (U1) |
| Auflegen | „Auflegen" | 1 | gut; kein Escape/Tastenkürzel |
| Rückruf aus Anrufliste | Tab „Anrufe", Doppelklick | 2 | gut; kein Kontextmenü, kein Einzelklick-Weg |
| Kontakt anrufen | Suchen, Doppelklick | n + 1 | gut |

| # | Schwere | Fundstelle | Beobachtung | Empfehlung | Aufwand |
|---|---|---|---|---|---|
| U1 | **Blocker** | `src/Nipp.App/MainWindow.xaml.cs:121-139`; `Views/ActiveCallPage.xaml` (keine Navigation zurück); `ActiveCallPage.xaml:274-279` | Sobald ein Gespräch läuft, navigiert das Fenster in `ActiveCallPage` und zurück erst, wenn **kein** Gespräch mehr läuft. Die Seite hat keinen Weg zur Wähltastatur oder zu den Kontakten. Folge: **zweiter Anruf, Makeln und begleitete Übergabe sind per UI unerreichbar**, obwohl im Dienst gebaut und in der Testmatrix als bestanden geführt (T08/T09 wurden vor dem Umbau auf §20.1 getestet). Der Hinweistext „links auf Wählen" beschreibt das alte NavigationView-Layout. | Zurück-Pfeil in `ActiveCallPage` (wie in `SettingsPage`), Navigation nur bei **neuem eingehenden** Anruf erzwingen, in der Shell eine schmale Gesprächsleiste („Gespräch mit … · 02:14 · zurück") statt Vollwechsel; Hinweistext anpassen. | M |
| U2 | **Blocker** | `src/Nipp.App/App.xaml.cs:140-171` | `OnRedirectedActivation` ignoriert die übergebenen `AppActivationArguments` und liest stattdessen `Environment.GetCommandLineArgs()` der **laufenden** Instanz. Da nipp im Infobereich lebt, ist „läuft schon" der Normalfall: ein `tel:`-Klick aus Outlook oder dem CRM **wählt nicht** — und wenn die erste Instanz selbst per `tel:` gestartet wurde, wählt jede Weiterleitung die **alte** Nummer erneut. Testmatrix T21/T22 sind offen, was dazu passt. | `args.Data as IProtocolActivatedEventArgs` auswerten (`Uri`), für den Erststart `AppInstance.GetCurrent().GetActivatedEventArgs()`; Kommandozeile nur als unpackaged-Rückfall. | S |
| U3 | **Hoch** | `src/Nipp.Core/Services/Telephony/SdkLoadProbe.cs:301-302`; Log 05.09. 18:35/18:42/18:48 | `RingResourcesDir` zeigt auf `share\sounds`, die Klingeltöne liegen unter `share\sounds\linphone\rings\` (dort ist `notes_of_the_optimistic.mkv` vorhanden). Das SDK meldet im Log *Default local ringtone file … does not exist*. **Eingehende Anrufe klingeln lokal nicht**; der Toast-Ton ist der einzige Hinweis, und der fehlt unpackaged. | `RingResourcesDir = share\sounds\linphone\rings`; bei `RingtonePath == null` explizit `core.Ring` auf `oldphone-mono.wav` setzen (§9.4 „mitgelieferte WAV"). Testfall T06 um „klingelt hörbar" ergänzen. | S |
| U4 | **Hoch** | `src/Nipp.App/Windows/ToastService.cs:259-276` | Der Toast ist eine gewöhnliche Benachrichtigung ohne `SetScenario(AppNotificationScenario.IncomingCall)`: er verschwindet nach wenigen Sekunden, spielt keinen Dauerton, wird vom **Fokus-Assistenten** unterdrückt und erscheint nicht über Vollbild-Apps. Für ein Softphone ist genau das der kritische Moment. | `AppNotificationBuilder.SetScenario(IncomingCall)`, `SetAudioEvent(Looping…)` oder Audio stumm (SDK klingelt), `ExpiresOnReboot` behalten. | S |
| U5 | **Hoch** | `src/Nipp.Core/ViewModels/ShellViewModel.cs:148-156`; `ShellPage.xaml:41-60` | Die Shell abonniert `RegistrationChanged` nicht. Ein Registrierungsfehler (falsches Passwort, Anlage weg, Netzwerkverlust) zeigt sich in der Hauptansicht nur als rote 8-px-LED; die gut formulierte Meldung aus `SipErrorCatalog` sieht man erst in den Einstellungen. Bei Netzwerkverlust gibt es keinerlei Banner. | Zustandstext neben dem Konto (O4) plus `InfoBar` bei `Failed`/`Unregistered` mit der Katalogmeldung; „Offline — Registrierung wird erneuert" während `InProgress` nach Netzwerkwechsel. | S |
| U6 | **Hoch** | `src/Nipp.Core/ViewModels/ActiveCallViewModel.cs:95,220-231,247-258,302-308`; `ActiveCallPage.xaml.cs:84-148` | `LastError` wird bei gescheiterter Weiterleitung und Aufnahme gesetzt, aber **nirgends angezeigt**. Der Benutzer drückt „Blind", nichts passiert, keine Meldung. | `InfoBar` in `ActiveCallPage` an `LastError` binden (wie `ErrorBar` in der Shell). | S |
| U7 | **Hoch** | `Views/Settings/SettingsPage.xaml:132-148`; `SettingsViewModel.cs:52` | Kontoformular bietet nur Benutzer, Domain, Passwort, Anzeigename, Transport. **Fehlend gegenüber §9.1/§9.2:** Authentifizierungs-ID, Outbound-Proxy, Registrierungsdauer, Voicemail-Adresse (ohne sie ist der Mailbox-Tab tot), SIP-Port, Zertifikatsprüfung, Keep-Alive, RTP-Portbereich. Ein Supporter kann diese Felder nur über ein Provisioning-Profil setzen. Zudem ist die **Transport-Vorgabe UDP**, die Spezifikation sagt TLS — ohne ADR. | Kontoformular als „Erweitert"-Expander ergänzen; Netzwerk-Gruppe um Port, Zertifikatsprüfung, Keep-Alive; TLS als Vorgabe oder ADR mit Begründung (Test-Trunk?). | M |
| U8 | **Hoch** | `App.xaml.cs:84-96, 176-238, 247-272`; `Services/Settings/ProvisioningService.cs:37,162` | Der Start blockiert den UI-Thread synchron: Provisioning-HTTP bis **5 s**, `InitializeAsync`, `ApplySettingsAsync`, `RegisterAccountAsync` ×n, alles `GetAwaiter().GetResult()` vor `new MainWindow()`. Bei nicht erreichbarem Provisioning-Server erscheint 5 s lang gar nichts; das Ziel Kaltstart < 3 s ist so nicht haltbar. | Fenster zuerst mit Zustand „wird gestartet", Provisioning und Registrierung als `async` mit `InfoBar`; `ApplyProvisioning` mit eigenem `CancellationToken`. | M |
| U9 | **Mittel** | `SettingsViewModel.cs:360-396` | „Konto hinzugefügt." erscheint sofort nach `RegisterAccountAsync`, das nur den REGISTER auslöst. Bei falschem Passwort steht eine Erfolgsmeldung neben einem Konto mit Fehler-LED. Der Kommentar „Erst nach erfolgreicher Anmeldung speichern" trifft nicht zu. | Auf `RegistrationChanged` für diese Identität warten (Timeout 10 s), dann „angemeldet" oder Katalogmeldung; solange „Anmeldung läuft…". | S |
| U10 | **Mittel** | `SettingsViewModel.cs:398-415`; `SettingsPage.xaml:116-122` | Konto entfernen ohne Rückfrage, sofort wirksam und gespeichert — obwohl die Seite sonst auf einer Kopie arbeitet („wer sich verklickt, kann die Seite verlassen", Z. 15-18). Ein Fehlklick auf den Papierkorb kostet das Konto samt Passwort. | `ContentDialog` „Konto … entfernen?"; alternativ Entfernen erst mit „Speichern" wirksam. | S |
| U11 | **Mittel** | `ToastService.cs:265-270, 328-334` | Toast-Knopf „Mailbox" legt nur auf; ob die Anlage umleitet, „entscheidet sie selbst". Die Beschriftung verspricht etwas, das der Knopf nicht tut. | `Call.RedirectTo(voicemailAddress)` (im Wrapper vorhanden, Z. 24522) mit der Voicemail-Adresse des Kontos; ohne Adresse den Knopf weglassen. | S |
| U12 | **Mittel** | `SettingsPage.xaml:438`; `NippSettings.cs:280` | „Anrufe automatisch annehmen" ist in Modell, UI und Diagnose vorhanden, wird aber **nirgends ausgewertet** (grep: keine Verwendung in Services). Ein Schalter ohne Wirkung. | In `SipService.OnBridgeCallStateChanged` bei `IncomingReceived` annehmen und Hinweiston spielen (§8.6), oder Schalter entfernen. | M |
| U13 | **Mittel** | `App.xaml.cs:436-446` | Das systemweite Kürzel holt nipp nur nach vorn. §9.6 nennt „Globaler Hotkey Annehmen/Auflegen", Testmatrix T26 „nimmt an und legt auf". Kein ADR zur Abweichung. | Bei klingelndem Anruf annehmen, bei laufendem Gespräch nach vorn (Auflegen per Hotkey ist riskant), sonst nach vorn; ADR schreiben. | S |
| U14 | **Mittel** | `Views/ActiveCallPage.xaml` (keine `KeyboardAccelerator`); `ShellPage.xaml.cs:87-108`; `Controls/Keypad.xaml` | Tastaturbedienung endet nach Enter/Escape im Nummernfeld: kein Escape = Auflegen, keine Kürzel für Stumm/Halten, Enter im Weiterleiten-Feld tut nichts; die zwölf Tastatur-Buttons sind Tab-Stopps und liegen in der Tab-Reihenfolge vor der Liste. | `KeyboardAccelerator` (Esc, Strg+M, Strg+H, Strg+T), `IsTabStop="False"` für Keypad-Tasten (Ziffern kommen per Tastatur ohnehin ins Feld), Enter im Ziel-Feld = Blind. | S |
| U15 | **Mittel** | `ShellViewModel.cs:402-412`; `ShellPage.xaml:229-267`; `Services/History/CallHistoryStore.cs:113-170` | Anrufliste ohne Filter (Alle/Verpasst/…), ohne Suche, ohne Kontextmenü (Kopieren, Kontakt anlegen), ohne Löschen. `Query(filter, search)` ist implementiert, wird aber nur mit `limit` gerufen. §20.3 reduziert die **Spalten**, nicht die Funktionen aus §8.3. | Kleine Filterleiste (Alle · Verpasst) über der Liste, `MenuFlyout` mit „Nummer kopieren" und „Als Kontakt in Team übernehmen", Suche über das vorhandene Nummernfeld. | M |
| U16 | **Mittel** | `ShellViewModel.cs:458-489` (Z. 488 `RecordingPath: null`) | Der Aufnahmepfad wird nie in die Anrufliste geschrieben. `CallHistoryEntry.HasRecording` und der Filter „Aufgenommen" sind damit tot; der Benutzer findet Aufnahmen nur im Ordner. | `CallInfo` um `RecordingPath` ergänzen (der Dienst kennt ihn ab `StartRecording`) und übernehmen. | S |
| U17 | **Mittel** | `ShellViewModel.cs:463-469` | Ausgehende Anrufe enden nur als `Answered`, `NoAnswer` oder `Failed`; „besetzt" und „abgelehnt" (486/603) werden nie erzeugt, obwohl `SipErrorCatalog` sie erkennt. Die Liste zeigt dann „fehlgeschlagen" für ein besetztes Ziel. | Ergebnis aus `StatusMessage`/SDK-Reason ableiten (`Reason.Busy`, `Reason.Declined`) und im Dienst als eigenes Feld liefern. | S |
| U18 | **Mittel** | `ShellPage.xaml:166-212` | Vorschlagsliste hat `SelectionMode="None"` und reagiert nur auf `Tapped` — mit der Tastatur (Pfeil runter, Enter) nicht erreichbar; genau dort wird aber getippt. | `SelectionMode="Single"`, Pfeil-runter aus dem Nummernfeld fokussiert die Liste, Enter übernimmt. | S |
| U19 | **Mittel** | `Services/Settings/SettingsService.cs:95`; `Views/Settings/SettingsPage.xaml.cs:277-298`; `SipService.cs:651-667` mit `ShellViewModel.cs:183-195` | Fehlende Validierung mit Absturzfolge: Aufnahmeordner wird beim **Wählen** angelegt (`Directory.CreateDirectory`), ein ungültiger Pfad wirft `IOException`/`UnauthorizedAccessException`, die `DialAsync` nicht fängt. `OnDiagnosticsClick` fängt nur `IOException` (async void → Absturz bei Zugriffsverweigerung). Länderpräfix und Hotkey werden beim Speichern nicht geprüft. | Beim Speichern validieren (Pfad anlegen/prüfen, Präfix über `NumberNormalizer`, Hotkey über `TryParse`), im Dienst Ausnahme in `InvalidOperationException` mit Klartext wandeln. | S |
| U20 | **Niedrig** | `ShellPage.xaml:552-563` | Sichtbares Label „Optionen", `AutomationProperties.Name="Einstellungen"` — Label-in-Name (WCAG 2.5.3) verletzt; Sprachsteuerung „Optionen klicken" findet nichts. | Gleicher Text für beides. | S |
| U21 | **Niedrig** | `ShellPage.xaml:292-299`; `SettingsPage.xaml:46-48, 116-122, 315-320, 385-391`; `ActiveCallPage.xaml:241-243` | Icon-Buttons (Neu einlesen, Zurück, Entfernen, Codec hoch/runter) und das Ziel-Textfeld ohne `AutomationProperties.Name`; `ToolTip` ersetzt den UIA-Namen nicht. Screenreader liest „Schaltfläche". | `AutomationProperties.Name` ergänzen; ein `XamlResourceTests`-ähnlicher Scan „jeder Button mit FontIcon-Inhalt hat einen Namen" hält es dauerhaft. | S |
| U22 | **Niedrig** | `ActiveCallPage.xaml:255-266` | „Blind" / „Begleitet" sind Telefonie-Jargon; die Erklärung steht nur im Tooltip, der mit Touch nicht erreichbar ist. | „Sofort übergeben" / „Erst ankündigen"; Erklärung als Caption unter den Knöpfen. | S |
| U23 | **Niedrig** | `src/Nipp.App/Windows/TrayIconHost.cs:482-492` | Kontextmenü ohne „Präsenz setzen" (§10). Kein ADR. | Entweder Menüpunkt (SDK: `Core.PresenceModel`) oder ADR „nicht gebaut, weil …". | S |

### 2.3 Funktion und Codequalität

Architektur: MVVM ist sauber, Abhängigkeiten laufen App → Core → SDK, DI durchgängig, `ISipService` ist mockbar. Die Code-Behinds sind allerdings **dick** (`ShellPage.xaml.cs` 322 Zeilen, ein `Refresh()` für alles) — eher „View-Controller" als MVVM; x:Bind auf Sichtbarkeiten würde vieles ersetzen.

| # | Schwere | Fundstelle | Beobachtung | Empfehlung | Aufwand |
|---|---|---|---|---|---|
| F1 | **Hoch** | `src/Nipp.Core/Services/Windows/ConnectivityMonitor.cs:64-71, 83, 116-129` | Threading-Verstoss gegen §6: `ApplyAsync` läuft nach `Task.Delay(...).ConfigureAwait(false)` auf einem **Threadpool-Thread**, beim Suspend (`immediate: true`) direkt auf dem SystemEvents-Thread, und ruft dort `Core.NetworkReachable` — parallel zu `Iterate()` auf dem UI-Thread. Der eigene Kommentar (Z. 66-67: „hier nichts am SDK anfassen") verspricht das Gegenteil. Native Race, sporadisch. | In `Start()` (läuft auf dem UI-Thread) `SynchronizationContext.Current` merken und `Post` — dasselbe Muster wie `ShellViewModel.OnUiThread`. | S |
| F2 | **Hoch** | `src/Nipp.App/Windows/WindowPlacement.cs:80-97, 190-208` | Der Arbeitsbereich wird vom Bildschirm ermittelt, auf dem das **noch unplatzierte** Fenster liegt (Primär). Eine gemerkte Lage auf einem zweiten Monitor (x ≥ Breite des Primären) fällt in `IsVisibleOn` durch und wird verworfen; zusätzlich klemmt `Clamp` die Grösse auf den Primärbildschirm. Wer nipp auf dem Zweitmonitor parkt, findet es bei **jedem Start** wieder rechts oben auf dem Hauptbildschirm. | `DisplayArea.GetFromRect(storedRect, DisplayAreaFallback.Nearest)` für Arbeitsbereich und Skalierung des Zielbildschirms. | S |
| F3 | **Hoch** | `src/Nipp.Core/Services/Telephony/SipService.cs:611, 690` | Die Domain für Kurzwahlen und Weiterleitungsziele kommt aus `_account` — dem **zuletzt registrierten** Konto, nicht dem gewählten (`accountIdentity`). Bei zwei Konten auf verschiedenen Anlagen (§20.2) wählt nipp `151@falsche-domain`. Zeile 593 ändert ausserdem `core.DefaultAccount` dauerhaft als Nebenwirkung eines Anrufs. | Domain aus `_accountSettings[identity]`; `DefaultAccount` nur über die Property setzen. | S |
| F4 | **Hoch** | `SipService.cs:724-733` | Stumm ist `core.MicEnabled` (global). Bei zwei Gesprächen stummt „Stumm" beide, das `CallInfo.IsMuted` des zweiten bleibt falsch; nach Makeln ist der Zustand inkonsistent. | `Call.MicrophoneMuted` je Gespräch (Wrapper Z. 23232). | S |
| F5 | **Hoch** | `Services/Settings/NippSettings.cs:92`; `Services/Telephony/SettingsApplier.cs:35-42` (kein Aufruf); Wrapper `Core.RootCa` (Z. 40613), `VerifyServerCertificates` (Z. 47103) | „Serverzertifikat prüfen" wird nirgends auf den Core übertragen; `Core.RootCa` wird nie gesetzt, obwohl §14.6 und `docs/sdk-api-notes.md:96` festhalten, dass das SDK eine CA-**Datei** braucht. TLS (Spec-Standardtransport) ist damit ungeprüft — Testmatrix T03 ist offen, und die UI-Vorgabe UDP (U7) kaschiert es. | Beim Start Windows-Root-CAs (`StoreName.Root`, `StoreLocation.LocalMachine`) als PEM nach `%LOCALAPPDATA%\nipp\rootca.pem` exportieren, `core.RootCa` setzen, `VerifyServerCertificates(settings.Network.VerifyServerCertificate)`; T03 durchführen. | M |
| F6 | **Hoch** | `Services/Settings/ProvisioningService.cs:214-252`; `SettingsService.cs:112-138` (Z. 134) | Ein Profil mit Konten **ohne** Passwort (der in `docs/provisioning.md` empfohlene Weg) erzeugt `SipAccountSettings { Password = "" }`; `Save()` setzt `_current` auf genau diese Fassung. `App.OnLaunched` registriert anschliessend mit **leerem Passwort**. Das DPAPI-Passwort aus einer früheren Sitzung wird nicht wieder angehängt; erst der nächste `Load()` täte das — aber `ApplyProvisioning` läuft bei jedem Start davor. Mit Profil ohne Passwort registriert nipp nie. T29 ist offen. | In `Save()` `_current = AttachSecrets(settings)` bzw. in `ToAccount` das vorhandene Geheimnis aus `SecretStore` übernehmen; Test dafür (F12). | S |
| F7 | **Mittel** | `SipService.cs:1492-1523` | `ShutdownAsync` ruft 25× `Iterate()` ohne Pause (≈ 1 ms). Der abschliessende REGISTER mit `expires=0` und das Abbestellen der BLF-Abonnements kommen kaum heraus. Das Log vom 05.09. zeigt beim nächsten Start **21× „Receiving NOTIFY … no known dialog" / 481** — die Anlage hält die alten Abonnements. | `_presenceList.SubscriptionsEnabled = false`, dann bis `RegistrationState.Cleared` iterieren mit `Thread.Sleep(20)`, max. 1 s. | S |
| F8 | **Mittel** | `App.xaml.cs:214, 281-306`; `MainWindow.xaml.cs:111-115`; `ShellViewModel.cs:226-239, 563`; `Services/Contacts/BlfService.cs:316` | `SettingsService.Changed` hat fünf Abonnenten, die synchron auf dem UI-Thread arbeiten: Codecs neu setzen, Registry schreiben (Autostart + vier Protokolle), Hotkey neu registrieren, BLF neu synchronisieren. **Jeder Klick auf den Tastatur-Toggle** speichert die ganze Datei (Z. 226-239) und löst diese Kette aus. | UI-Zustand (Dialpad, Fensterlage) getrennt persistieren oder `Changed` mit Diff (`SettingsApplier.RequiresRestart`-Muster) und nur betroffene Empfänger. | M |
| F9 | **Mittel** | `App.xaml.cs:424-429`; `Services/Windows/WindowsIntegration.cs:38-66, 73-102` | HKCU-Autostart und `HKCU\Software\Classes\tel|sip|sips|callto` werden **immer** geschrieben, auch packaged — neben `StartupTask` und Protokoll-Extensions im Manifest. Der Run-Eintrag zeigt dann auf die EXE im WindowsApps-Ordner; Windows bietet zwei „nipp" zur Auswahl. `RegisterProtocolHandlers(false)` löscht `Software\Classes\tel` **komplett**, auch wenn ein anderes Programm den Schlüssel besass. | Paketidentität prüfen (`Package.Current` in try/catch); HKCU nur unpackaged; beim Entfernen nur eigene Werte löschen (Command enthält `Nipp.App`). | S |
| F10 | **Mittel** | `Views/ShellPage.xaml.cs:39`; `MainWindow.xaml.cs:133-137`; `SettingsPage.xaml.cs:308` | Jede Navigation erzeugt eine **neue** `ShellPage`/`ActiveCallPage`; das `CollectionChanged`-Lambda (Z. 39) am Singleton-ViewModel wird nie abgemeldet und hält jede alte Seite am Leben; `Frame.BackStack` wächst je Anruf um zwei Einträge. Über 8 h / 50 Anrufe (§2) ein messbares Leck. | `NavigationCacheMode.Required` mit je einer Instanz, `BackStack.Clear()` nach Rückkehr in die Shell, Lambda in `Unloaded` abmelden. | S |
| F11 | **Mittel** | `ShellPage.xaml.cs:66, 181-315`; `ActiveCallPage.xaml.cs:57, 84-148` | `Refresh()` bei **jedem** `PropertyChanged` — jeder Tastendruck, jede Präsenzmeldung (bis zu zehn je Sekunde) baut sämtliche Sichtbarkeiten, Texte und Badges neu und setzt `AccountBox.SelectedItem`, was wiederum `SelectionChanged` auslöst. Funktioniert, ist aber MVVM-untypisch und die Quelle künftiger Flacker- und Reentranz-Fehler. | Sichtbarkeiten per `x:Bind` mit `BoolToVisibilityConverter` (vorhanden), `Refresh` nach `PropertyName` verzweigen. | M |
| F12 | **Mittel** | `tests/Nipp.Core.Tests/**` (NSubstitute referenziert, `Substitute.For` **nirgends** verwendet) | 213 Tests decken reine Funktionen ab (Normalisierung, Formatierung, Katalog, Parser, Store). **Ungetestet:** `ShellViewModel`, `ActiveCallViewModel`, `SettingsViewModel`, `ContactStore`, `BlfService`, `ConnectivityMonitor`, `WindowPlacement`, `ProvisioningService.ApplyProfile`. Kritischste fehlende Tests: (1) Zwei-Gespräche-Zustandsmaschine in `ActiveCallViewModel` (Swap, Attended, Ende des gewählten Gesprächs), (2) `RecordInHistory`-Ergebnisableitung, (3) Provisioning → Passwörter bleiben erhalten (F6), (4) Debounce und Thread des `ConnectivityMonitor` (F1), (5) `WindowPlacement` mit Zweitmonitor (F2), (6) `AccountsChanged` erhält die Auswahl. | Mit `Substitute.For<ISipService>()` ViewModel-Tests; `WindowPlacement`-Rechenkern in reine Funktion mit `RectInt32`-Parametern ziehen. | M |
| F13 | **Mittel** | `Services/Settings/ProvisioningService.cs:152-158, 340-341` | Provisioning über **http** wird zugelassen (nur Log-Warnung). Ein Profil darf Konten, Passwörter **und die Provisioning-URI selbst** setzen — wer im LAN antwortet, lenkt Anrufe und künftige Profile dauerhaft um. | https erzwingen (Ausnahme nur mit Einstellung „unsicher erlauben" + ADR); `advanced.provisioning-uri` nicht aus dem Profil übernehmen oder nur bei gleicher Host-Domain. | S |
| F14 | **Mittel** | `Services/Contacts/OutlookContactSource.cs:91, 196` | `IsAvailable` prüft nur die ProgID; `Activator.CreateInstance` **startet Outlook**, wenn es nicht läuft (unsichtbarer Prozess, mögliche MAPI-Profilabfrage, 32 s Kaltstart laut Kommentar). §8.4 verlangt: „Outlook muss laufen (sonst verständliche Anzeige)". | `Marshal.GetActiveObject("Outlook.Application")` (laufende Instanz) versuchen; ohne Treffer Platzhalter „Outlook ist nicht geöffnet" statt Start. | S |
| F15 | **Mittel** | `SipService.cs:840-960`; Log 05.09.: 3× *Besetztlampenfeld nicht moeglich: External component has thrown an exception* | Trotz Umbau auf „Liste einmal anlegen" wirft `WatchPresenceAsync` im letzten Lauf dreimal eine SEH-Ausnahme. Welche Zeile, ist aus dem Log nicht ersichtlich. Das Besetztlampenfeld ist damit nicht stabil. | Ausnahme mit Stacktrace loggen (`TelephonyLog.PresenceWatchFailed(_logger, ex)`), Reproduktion: Team-Änderung in den Einstellungen bei laufender Liste. | S |
| F16 | **Mittel** | `Services/Settings/NippSettings.cs:95, 101, 109-112, 123-125, 195`; Log: 18× *belle_sip_socket_set_dscp(): not implemented* | Einstellungen ohne Wirkung: `KeepAliveSeconds` (nur als bool), `DetectNetworkChanges` (Monitor ignoriert es), `SipDscp/AudioDscp` (belle-sip setzt DSCP unter Windows nicht), `AutomaticGainControl` (nie übertragen), `TurnServer/TurnUsername` ohne Passwortfeld (TURN kann nicht funktionieren). | Entweder umsetzen oder aus Modell, Diagnose und `nippprov schema` entfernen; DSCP als „Windows: nur über QoS-Richtlinie" dokumentieren. | S |
| F17 | **Mittel** | `ViewModels/DialerViewModel.cs` (320 Zeilen); `App.xaml:24` + `DisplayConverters.cs:132-150` (`TimeOfDayConverter`); `ShellViewModel.cs:209-216` (`BackspaceCommand`); `Services/Contacts/Contact.cs:406-407` (`SubLabel` doppelt zu `ContactRow.SubLabel`); `SecretStore.cs:353` (`ComputeHa1`) | Toter Code: `DialerViewModel` wird nirgends registriert oder verwendet („fällt in P4 weg"), `TimeOfDayConverter` ist registriert, aber unbenutzt, `BackspaceCommand` hat keinen Aufrufer, `ComputeHa1` ist implementiert und nie gerufen. | Entfernen; wer es später braucht, findet es in Git. | S |
| F18 | **Mittel** | `build.ps1:24-27`; README „Bauen und starten", CLAUDE.md „Bauen auf dieser Maschine" | `[Parameter(ValueFromRemainingArguments)]` fängt `-p:WindowsPackageType=None` nicht: PowerShell 7 bindet `-p` und meldet *parameter name 'p' is ambiguous*. Der dokumentierte unpackaged-Befehl scheitert (in dieser Sitzung reproduziert). | Aufrufsyntax `.\build.ps1 --% build …` dokumentieren oder Argumente als `-Args @('build','-c','Debug')`; in README und CLAUDE.md nachziehen. | S |
| F19 | **Niedrig** | `App.xaml.cs:576-582` | `OnUnhandledException` protokolliert, setzt `e.Handled` aber nicht: jede XAML-Ausnahme beendet das Softphone. Für ein Werkzeug mit 8-h-Ziel eine bewusste Entscheidung wert. | Für bekannte, nicht-fatale Fälle `Handled = true` + Meldung; Rest abstürzen lassen (Log läuft). Als ADR festhalten. | S |
| F20 | **Niedrig** | `SipService.cs:1088-1110`; `Model/CallModels.cs:473-477` | „Jitter" zeigt `JitterBufferSizeMs` (Puffergrösse), nicht den Jitter; MOS (§8.2) fehlt, obwohl `Call.CurrentQuality/AverageQuality` (0–5) im Wrapper stehen. | `CurrentQuality` als MOS-Näherung anzeigen, Beschriftung „Jitter-Puffer". | S |
| F21 | **Niedrig** | `SipService.cs:307-347` vs. `NippSettings.cs`/`SettingsApplier.cs` | `ApplyBaselineConfiguration` wiederholt Vorgaben (Ports, DSCP, Codecs, Echo), die `SettingsApplier` Sekunden später erneut setzt — zwei Wahrheiten für dieselben Zahlen. | Baseline auf das reduzieren, was der Applier nicht kennt (Video-Policy), Rest über `ApplySettingsAsync(new NippSettings())`. | S |
| F22 | **Niedrig** | `tests/Nipp.Architecture.Tests/SdkBoundaryTests.cs:90-107` | Der Scan erkennt `using Linphone` und `global::Linphone.`, aber nicht voll qualifizierte Verweise wie `Linphone.Core x` ohne `using`. | Regex `\bLinphone\.[A-Z]` ausserhalb von Kommentaren und Strings ergänzen. | S |
| F23 | **Niedrig** | `src/Nipp.App/Telephony/SipPumpHost.cs:83-97` | Überlaufmessung mit `Environment.TickCount64` (Auflösung ≈ 15,6 ms) — ein 20-ms-Takt lässt sich damit nicht sinnvoll messen. | `Stopwatch.GetTimestamp()`. | S |
| F24 | **Niedrig** | `ShellViewModel.cs:487` | `AccountIdentity` im Verlauf ist das **gewählte** Konto, nicht das des Anrufs (eingehend auf Konto B während A gewählt ist). | Konto in `CallInfo` führen. | S |
| F25 | **Niedrig** | `Services/Telephony/SdkLogBridge.cs:428-430`; `DiagnosticsBundle.cs:183-186` | Bei „Debug" landen SIP-Nachrichten im Klartext (Rufnummern, Display-Namen) im Log; das Diagnosepaket nimmt die letzten sieben Tage mit. Kein Hinweis beim Erstellen. | Hinweistext im Erfolgsdialog („enthält Rufnummern der letzten 7 Tage"); Debug-Stufe nach Neustart automatisch zurücksetzen. | S |

---

## 3. Top 10 Massnahmen (Nutzen ÷ Aufwand)

1. **Weiterleitung von `tel:`-Aktivierungen reparieren** (U2, S). Ohne das ist Klick-to-Call — das Hauptargument gegen den alten Client — im Normalbetrieb tot.
2. **Klingelton-Pfad und Standardklingelton** (U3, S) plus **Toast als `IncomingCall`-Szenario** (U4, S). Beides zusammen: eingehende Anrufe werden gehört und bleiben sichtbar.
3. **Rückweg aus der Gesprächsansicht** (U1, M). Macht zweiten Anruf, Makeln und begleitete Übergabe wieder erreichbar und entfernt den Hinweis auf ein Layout, das es nicht gibt.
4. **Provisioning erhält Passwörter** (F6, S) und **https-Pflicht** (F13, S). Sonst funktioniert der empfohlene Rollout-Weg nicht.
5. **Registrierungszustand und Fehler in der Hauptansicht** (U5 + O4 + U6, je S). Drei kleine Änderungen, die den grössten Teil der „warum geht nichts?"-Anrufe beim Support vermeiden.
6. **`ConnectivityMonitor` auf den UI-Thread** (F1, S). Ein Nachmittag, beseitigt eine native Race Condition, die sich als „nipp hängt nach dem Aufwachen" zeigen würde.
7. **Fensterlage auf Zweitmonitor** (F2, S) und **Mindestgrösse** (O7, S).
8. **Umschaltleiste mit Selektionszustand und 12-px-Beschriftung** (O1 + O2, S). Der sichtbarste Fluent-Verstoss, eine Stunde Arbeit.
9. **TLS wirklich unterstützen** (F5, M) und **Kontoformular vervollständigen** (U7, M). Erst danach ist §9 erfüllt und T03 prüfbar.
10. **ViewModel-Tests mit NSubstitute** (F12, M) und **Navigation ohne Leck** (F10, S). Die Tests hätten U1, U6, F3 und F4 gefunden.

Danach, in dieser Reihenfolge: F3/F4 (Multi-Konto und Stumm), U8 (asynchroner Start), F8 (Changed-Kette), U15/U16/U17 (Anrufliste), F9 (packaged vs. HKCU), F17/F16 (toter Code, wirkungslose Einstellungen).

---

## 4. Was gut gelöst ist und so bleiben soll

- **Die SDK-Grenze.** `using Linphone` nur unter `Services/Telephony/`, `SipEventBridge` als einziger Listener, eigene Modelle (`CallInfo`, `CallHandle`, `AccountStatus`), erzwungen durch `SdkBoundaryTests`. Das ist der Grund, warum ViewModels überhaupt testbar wären.
- **Fehlertexte nach §15.** `SipErrorCatalog` nennt Ursache und Abhilfe, wird zentral verwendet und ist getestet. „Registration refreshing" wird nach dem Review vom Morgen korrekt als Zwischenzustand behandelt.
- **Sicherheit der Zugangsdaten.** DPAPI mit Entropie, atomares Schreiben, `linphonerc` enthält nachweislich nur HA1, `DiagnosticsBundle` baut die Konfiguration **neu** statt zu kopieren und ist per Test gegen Passwortlecks abgesichert. Kein `Password` in einem Log-Template.
- **Testisolation.** `TestIsolationTests` verhindert, dass ein Testlauf je wieder das Benutzerkonto löscht; `XamlResourceTests` fängt fehlende `StaticResource`-Schlüssel vor dem Start.
- **`ProvisioningParser`**: DTD aus, kein Resolver, wirft nie, nennt Zeile und Spalte; `nippprov pruefen` benutzt denselben Parser.
- **Aufnahmeindikator** nicht abschaltbar, mit Begründung (Rechtslage) im Code und in der UI.
- **`NumberNormalizer`/`ClipResolver`/`SipUri`/`PhoneNumberFormat`** als reine Funktionen mit 100+ Tests — genau die Stellen, an denen Softphones falsch wählen.
- **Tokens.xaml** mit eigenem HighContrast-Wörterbuch, das Systemfarben referenziert statt eigene zu setzen.
- **WindowPlacement** rechnet physische Pixel korrekt um (DPI), begrenzt auf den Arbeitsbereich, speichert beim Schliessen statt bei jeder Bewegung.
- **Dokumentation**: ADRs für jede Abweichung, `docs/sdk-api-notes.md` als Wahrheit über den Wrapper, Testmatrix mit Datum und Build, Kommentare erklären das *Warum* und nennen den Fehler, der zur Regel führte.
- **Startpfad-Robustheit**: Crash-Datei vor Serilog, SDK-Ladeprobe mit sprechender Meldung, Provisioning-Fehler verhindern den Start nicht, Toast/Tray/Hotkey degradieren einzeln.

---

## 5. Offene Fragen (ohne Laufzeittest oder Benutzerinput nicht zu klären)

| # | Frage | Warum sie zählt |
|---|---|---|
| Q1 | Klingelt ein eingehender Anruf lokal **tatsächlich** nicht (U3), oder spielt das SDK trotz Fehlermeldung einen Rückfallton? | Entscheidet zwischen Hoch und kosmetisch. Fünf Minuten am Test-Trunk. |
| Q2 | Verhalten der Status-Pinsel bei manuellem Theme-Wechsel (O3): bleiben LEDs bei „Dunkel" auf hellem Windows in Light-Farben? | Meine Einschätzung beruht auf dem WinUI-Auflösungsmodell für `ThemeResource` auf App-Ebene; am Gerät zu verifizieren. |
| Q3 | Auf welchem Thread ruft `H.NotifyIcon.Core` die Menü-Callbacks (`TrayIconHost.cs:486-490`)? `ExitRequested` → `ExitApplication` stoppt Timer und Core ohne Dispatcher-Umweg. | Wenn nicht UI-Thread: derselbe Verstoss wie F1, beim Beenden. |
| Q4 | Warum UDP als Vorgabe für neue Konten (`SettingsViewModel.cs:52`)? Kann der Test-Trunk kein TLS? | Bestimmt, ob F5/U7 vor dem Piloten nötig sind oder ein ADR genügt. |
| Q5 | Soll der Hotkey annehmen/auflegen (Spec) oder nur nach vorn holen (Umsetzung)? | U13 — Entscheidung des Auftraggebers, dann ADR. |
| Q6 | Unterstützt die Anlage `302 Moved Temporarily` auf die Mailbox-Adresse (U11)? | Sonst muss der Toast-Knopf „Mailbox" weg. |
| Q7 | Reproduziert sich die SEH-Ausnahme im Besetztlampenfeld (F15) bei Team-Änderungen, und was steht im Stacktrace? | Log zeigt sie dreimal, ohne Ort. |
| Q8 | Darf Provisioning über http bleiben (F13)? Gibt es Anlagen ohne TLS-Webserver? | Sicherheitsentscheidung, nicht technisch. |
| Q9 | Wie verhält sich der HKCU-Protokoll-Handler neben der Manifest-Registrierung im installierten MSIX (F9)? Zwei Einträge im „Öffnen mit"-Dialog? | Nur auf einem Rechner mit installiertem Paket prüfbar. |
| Q10 | Soll eine unbehandelte XAML-Ausnahme die App beenden (F19)? | Stabilitätsziel 8 h gegen „lieber sauber sterben". |
| Q11 | Werden die Screenshots aus `docs/review/` auf echter x64-Hardware bestätigt (Schriftgrössen, 9-px-Labels bei 100 %)? | Alle Messungen stammen aus 150 % auf ARM64-Emulation (ADR-001). |
| Q12 | Meldet die Anlage beim Klingeln `Busy` über `presence` (docs/blf-pruefung.md), oder braucht es den `dialog`-Weg? | Entscheidet über einen Tag Umbau im BLF. |

---

## 6. Umsetzung am 05.09.2026

Der Review wurde direkt im Anschluss abgearbeitet. **Stand danach:** Solution baut ohne Warnung, 235 Komponententests (vorher 213) und 7 Architekturtests grün.

### 6.1 Umgesetzt

**Alle Blocker und alle Hoch-Befunde.** Im Einzelnen:

| Befund | Was jetzt gilt |
|---|---|
| U1 | Die Gesprächsansicht hat oben einen Zurück-Pfeil, die Hauptansicht eine Leiste „zurück zum laufenden Gespräch". Zweiter Anruf, Makeln und begleitete Übergabe sind damit erreichbar. Der Hinweistext nennt den richtigen Weg. |
| U2 | Die Aktivierung wird als `AppActivationArguments` durchgereicht und die URI aus `IProtocolActivatedEventArgs` gelesen. Die Kommandozeile ist nur noch die unpackaged-Rückfallebene. |
| U3 | `RingResourcesDir` zeigt auf `share/sounds/linphone/rings`, und ohne eigenen Klingelton wird `oldphone-mono.wav` gesetzt. Fehlt er, steht das als Warnung im Protokoll. |
| U4 | Der Toast ist ein `IncomingCall`-Szenario und stumm — geklingelt wird über das Klingelgerät. |
| U5, O4 | Der Anmeldezustand steht als Text unter der Kontoauswahl, im Fehlerfall rot und mit der Meldung aus `SipErrorCatalog`. |
| U6 | `LastError` der Gesprächsansicht erscheint in einer eigenen `InfoBar`. |
| U7 | Kontoformular um Authentifizierungs-ID, Outbound-Proxy, Registrierungsdauer und Mailboxnummer ergänzt, Netzwerkgruppe um SIP-Port, Zertifikatsprüfung und Keep-Alive. Transport-Vorgabe ist jetzt TLS (§9.2). |
| U9 | Nach dem Hinzufügen wird auf die Antwort der Anlage gewartet (max. 12 s) und erst dann Erfolg oder Fehler gemeldet. |
| U10 | Konto entfernen fragt nach. |
| U11 | „Mailbox" leitet per SIP 302 um und erscheint nur, wenn eine Mailboxnummer hinterlegt ist. |
| U12 | „Anrufe automatisch annehmen" wirkt: der Toast unterbleibt, ein kurzer Ton weist auf das offene Mikrofon hin. |
| U13 | Das Kürzel nimmt an, legt auf oder holt nach vorn (ADR-013). |
| U15 | Anrufliste mit Filtern, Suche und Kontextmenü (Zurückrufen, Nummer kopieren, als Team-Nebenstelle anlegen, Aufnahme zeigen). |
| U16, U17, F24 | Aufnahmepfad, Endgrund und Konto stehen am Gespräch und landen im Verlauf. „Besetzt" und „abgelehnt" entstehen jetzt tatsächlich. |
| U18 | Die Vorschlagsliste ist mit Pfeil-runter und Enter bedienbar. |
| U19 | Einstellungen werden vor dem Speichern geprüft (Präfix, Ports, Kürzel, Provisioning-Adresse, Aufnahmeordner). Ein unbrauchbarer Aufnahmeordner verhindert keinen Anruf mehr. |
| U20, U22 | Sichtbarer Text und Bedienhilfe stimmen überein; „Blind"/„Begleitet" heissen „Sofort abgeben"/„Erst ankündigen". |
| F1 | Der Netzwerkmonitor meldet den Zustand über den gemerkten UI-Kontext an das SDK. |
| F2, O7 | Die gemerkte Lage wird gegen den Bildschirm geprüft, auf dem sie liegt; das Fenster hat eine Mindestgrösse. |
| F3, F4 | Domäne und Stummschaltung gehören zum jeweiligen Gespräch, nicht zum zuletzt eingerichteten Konto oder zum ganzen Core. |
| F5 | Die Wurzelzertifikate von Windows werden als PEM-Bündel bereitgestellt und dem Core übergeben; die Zertifikatsprüfung wirkt. |
| F6, F13 | Provisionierung: fehlendes Passwort holt das hinterlegte, https ist Pflicht, und die Bezugsquelle darf nur die Auslieferung setzen (ADR-012). |
| F7 | Beim Beenden werden Präsenz-Abonnements abbestellt und bis zu einer Sekunde auf die Abmeldung gewartet. |
| F8 | Anzeigezustände (Wähltastatur, Fensterlage) gehen über `SaveViewState` und lösen die Empfängerkette nicht mehr aus. |
| F9 | Autostart und Protokoll-Handler in HKCU nur noch unpackaged; das Entfernen fasst fremde Registrierungen nicht an. |
| F10 | Beide Seiten werden zwischengespeichert, der Rückverlauf wird geleert, das Lambda abgemeldet. |
| F14 | Outlook wird nicht mehr gestartet, sondern nur eine laufende Instanz angesprochen. |
| F15 | Die SEH-Ausnahme im Besetztlampenfeld wird mit vollständiger Ausnahme protokolliert. |
| F16 | Automatische Aussteuerung wirkt jetzt (ADR-011); DSCP und Keep-Alive sind als Grenzen des SDK dokumentiert. |
| F17 | `DialerViewModel`, `TimeOfDayConverter`, `Contact.SubLabel` und `BackspaceCommand` entfernt. |
| F18 | `build.ps1` dokumentiert den Stopp-Parser; README und CLAUDE.md ziehen nach. |
| F20 | Der geschätzte MOS wird angezeigt, „Jitter" heisst „Jitter-Puffer". |
| F22 | Der Architekturtest erkennt auch voll qualifizierte SDK-Verweise ohne `using`. |
| F23 | Die Taktmessung nutzt `Stopwatch` statt `TickCount64`. |
| O1, O2 | Die Umschaltleiste zeigt den aktiven Bereich und beschriftet ihn auf Caption-Grösse. |
| O3 | `ThemeService` färbt die Status- und Präsenzpinsel beim Themenwechsel um, sodass sie der Wahl des Benutzers folgen statt dem Systemthema. |
| O5, O6, O8, O9 | Trefferflächen auf 32 px, Meldungen in eigener Rasterzeile, tabellarische Ziffern statt Consolas, Titelleisten-Icon folgt dem Erscheinungsbild. |
| F12 | 22 neue Tests: `ActiveCallViewModelTests` (Zustandsmaschine mit zwei Gesprächen), `ProvisioningServiceTests` (Passwort, https, Bezugsquelle, Sperren), `CallOutcomeRulesTests` (die Ergebnisableitung als reine Funktion). |

Drei neue ADRs halten die Abweichungen fest: **ADR-011** (Aussteuerung ab Werk aus), **ADR-012** (Provisionierung über https, Bezugsquelle nur aus der Auslieferung), **ADR-013** (Hotkey nimmt an und legt auf).

### 6.2 Bewusst offen geblieben

| Befund | Grund |
|---|---|
| U8 (asynchroner Start) | Der Umbau berührt die Reihenfolge von Provisionierung, Registrierung und erstem Fenster — das ist der empfindlichste Pfad der Anwendung und gehört nicht in denselben Durchgang wie dreissig andere Änderungen. Die 5-Sekunden-Grenze greift nur mit eingetragener Provisioning-Adresse. |
| F11 (Refresh-Muster) | Funktioniert; der Umbau auf `x:Bind` ist Aufräumarbeit ohne Verhaltensänderung und würde die Prüffläche dieses Durchgangs verdoppeln. |
| U14 (weitere Tastenkürzel) | Enter im Zielfeld und Pfeil-runter in die Vorschläge sind umgesetzt. Escape als „auflegen" und Kürzel für Stumm und Halten fehlen noch. |
| U21 (restliche Bedienhilfe-Namen) | Die häufig benutzten Schaltflächen haben Namen; Codec-Pfeile und die beiden Papierkorbsymbole in den Einstellungen fehlen. |
| U23 (Präsenz im Infobereich) | §10 nennt den Menüpunkt, gebaut ist er nicht. Braucht eine Entscheidung, welche Zustände nipp überhaupt veröffentlichen soll. |
| O10, O11, O12 | Buchstabenzeile der Wähltastatur (dekorativ), CompactOverlay (neue Funktion), Ressourcendateien für Texte (grosser Umbau ohne heutigen Nutzen). |
| F19 | Ob eine unbehandelte XAML-Ausnahme die App beenden soll, ist eine Entscheidung, keine Korrektur — siehe offene Frage Q10. |
| F21, F25, F16 (Rest) | Doppelte Grundkonfiguration, Hinweis auf Rufnummern im Diagnosepaket, `DetectNetworkChanges` und TURN-Passwort. Alle klein, alle ohne Alltagswirkung. |
| `SecretStore.ComputeHa1` | In F17 als toter Code genannt, bewusst behalten: §10 nennt HA1 ausdrücklich als Ziel, und der Kommentar erklärt, warum es noch nicht verwendet wird. |

### 6.3 Was jetzt am Gerät zu prüfen ist

Die Änderungen an Telefonie und Windows-Integration lassen sich nicht mit Komponententests abnehmen. Vor dem nächsten Piloten gehören diese Fälle der Testmatrix durchgespielt — sie sind dort markiert:

1. **T21/T22** tel:-Link bei laufender Instanz aus Outlook und aus dem Browser.
2. **T06** Eingehender Anruf: klingelt es am gewählten Gerät, und bleibt der Toast stehen?
3. **T03** Registrierung über TLS mit Zertifikatsprüfung.
4. **T26** Hotkey: annehmen, auflegen, nach vorn holen.
5. **T29/T30** Provisionierung mit und ohne Passwort im Profil, https und http.
6. **T09/T08** Makeln und begleitete Übergabe über den neuen Weg zurück zur Wähltastatur.
7. **T27** Besetztlampenfeld nach einer Team-Änderung — die SEH-Ausnahme aus dem Protokoll steht jetzt mit Stapelüberwachung darin.

---

## 7. Zweite Runde: Review der Umsetzung

Nach der Umsetzung wurde der gesamte Arbeitsstand noch einmal geprüft — diesmal gegen den Diff statt gegen den Ausgangszustand. Das war nötig und hat sich gelohnt: **acht Befunde, drei davon schwer, und alle drei durch die Umsetzung selbst entstanden.** Zwei hätten das Programm im Alltag schlechter gemacht als vorher.

### 7.1 Was die zweite Runde gefunden hat

| # | Schwere | Fundstelle | Befund |
|---|---|---|---|
| R1 | **Hoch** | `Services/Contacts/OutlookContactSource.cs` | Der Ersatz für `Activator.CreateInstance` konnte nie funktionieren: `GetActiveObject` erwartet einen Zeiger auf eine CLSID, übergeben wurde die ProgID als Zeichenfolge. Die Funktion las die ersten Zeichen des Textes als CLSID, scheiterte immer, und das Ergebnis war von „Outlook läuft nicht" nicht zu unterscheiden. **Outlook-Kontakte hätten überhaupt nicht mehr geladen.** Behoben mit `CLSIDFromProgID` davor. |
| R2 | **Hoch** | `Views/ShellPage.xaml.cs` | `NavigationCacheMode.Required` zusammen mit dem bestehenden Abmelden in `OnUnloaded` hätte die Hauptansicht nach der ersten Navigation eingefroren: der Konstruktor läuft mit Zwischenspeicher nur einmal, niemand verbindet die Seite wieder. Nach dem ersten Anruf wären Kontostand, Abzeichen, Anrufliste und Meldungen bis zum Programmende stehengeblieben. |
| R3 | **Hoch** | `Views/ActiveCallPage.xaml.cs` | Dasselbe, zusätzlich mit angehaltenem Dauer-Timer: das zweite Gespräch hätte die Gegenstelle des ersten angezeigt, ohne laufende Zeit. Beide Seiten verbinden sich jetzt in `OnLoaded` und lösen in `OnUnloaded`; nur `Loaded` und `Unloaded` selbst bleiben dauerhaft. |
| R4 | **Hoch** | `App.xaml.cs` | Der Rückfall auf die Kommandozeile galt auch für weitergereichte Aktivierungen. Ein zweiter Start ohne URI, etwa über das Startmenü, hätte die Nummer des ursprünglichen `tel:`-Klicks erneut gewählt — genau der Fehler, den die Änderung beseitigen sollte. Der Rückfall gilt jetzt nur noch für den eigenen Start. |
| R5 | **Mittel** | `App.xaml.cs` | Ohne Aktivierungsargumente beendete sich die zweite Instanz, ohne etwas weiterzureichen, und protokollierte fälschlich eine Weiterleitung. Jetzt macht sie selbst weiter. |
| R6 | **Mittel** | `Services/Telephony/SipService.cs` | Die automatische Annahme rief `Accept` aus dem Zustands-Callback heraus. Das SDK meldet daraufhin sofort die nächsten Zustände, und der äussere Rahmen schrieb danach „eingehend" zurück: die Oberfläche hätte „Annehmen" für ein bereits stehendes Gespräch angeboten. Angenommen wird jetzt im nächsten Durchlauf der Ereignisschleife. |
| R7 | **Mittel** | `Windows/ToastService.cs` | Der Toast wurde anhand der Einstellung unterdrückt, nicht anhand der tatsächlichen Annahme. Scheiterte sie, klingelte der Anruf unsichtbar durch. Der Dienst meldet den Fehlschlag jetzt, und der Toast wird nachgeholt. |
| R8 | **Niedrig** | `MainWindow.xaml.cs` | `BitmapImage` löst in WinUI kein `file:`-URI auf; das Titelleisten-Symbol wäre stumm ausgeblieben. Jetzt über `ms-appx:` mit protokolliertem `ImageFailed`. |

Dazu fünf weitere Punkte, die während der Umsetzung durch eigene Prüfung auffielen: der Zustand der Umschaltleiste nach Rückkehr aus den Einstellungen, die Namensauflösung der Gesprächsleiste bei jedem Zustandswechsel über alle Kontakte, das Anwenden eines Profils auf einem Threadpool-Thread, sowie fehlende Absicherung in vier Ereignishandlern, aus denen eine Ausnahme das Programm beendet hätte.

### 7.2 Was das über den Durchgang sagt

Die drei schweren Befunde teilen eine Ursache: **eine richtige Einzeländerung, deren Wechselwirkung mit bestehendem Code nicht mitgedacht wurde.** `NavigationCacheMode` ist die richtige Antwort auf ein Speicherleck und macht zugleich jede Verdrahtung im Konstruktor zur Falle. Eine laufende Outlook-Instanz anzusprechen statt eine zu starten ist richtig und braucht eine andere Win32-Funktion, als der naheliegende Name vermuten lässt. Ein Anruf automatisch anzunehmen ist richtig und darf nicht in dem Callback geschehen, der von der Annahme selbst erneut betreten wird.

Keiner der drei wäre durch einen Komponententest aufgefallen, und der Build war jedes Mal grün. Gefunden hat sie das Lesen des Diffs im Zusammenhang mit dem umgebenden Code.

### 7.3 Stand

| Prüfung | Ergebnis |
|---|---|
| `dotnet build Nipp.sln -c Debug` | 0 Warnungen, 0 Fehler |
| Nipp.Core.Tests | 235 bestanden |
| Nipp.Architecture.Tests | 7 bestanden |

Die Liste der am Gerät zu prüfenden Fälle aus §6.3 gilt unverändert und wächst um zwei: **Kontakte aus Outlook** (R1) und **zweites Gespräch nach dem ersten** (R3).

---

## 8. Dritte Runde: der Befund aus dem Alltag (05.09.2026, abends)

Gemeldet von Dominic in einem Satz: *„ankommende anrufe werden nicht angezeigt."*

### 8.1 Was los war

| # | Schwere | Fundstelle | Beobachtung |
|---|---|---|---|
| B1 | **Blocker** | `SipService.cs:1698` gegen `MainWindow.xaml.cs:163` und `ToastService.cs:85` | `Track()` legt einen eingehenden Anruf **schon mit `CallStatus.Incoming`** an, damit die Momentaufnahme von Anfang an stimmt — und meldete denselben Wert auch als Vorzustand. Beide Empfänger prüften auf den Übergang *nach* `Incoming`; die Bedingung war damit **nie** wahr. Kein Toast, kein Wechsel in die Gesprächsansicht. Weil nipp im Infobereich lebt (§10, „minimiert starten" ist der Standard), war der Toast der einzige sichtbare Kanal — ein eingehender Anruf war unsichtbar. |
| B2 | **Blocker** | `MainWindow.xaml.cs:163-165` | **Auch bei ausgehenden Anrufen erschien die Gesprächsansicht nie.** `justStarted` verlangte Status `Dialing` oder `Connected` mit Vorzustand `Ended`/`Failed`/`Dialing`. Ein ausgehender Anruf läuft aber `Dialing → Ringing → Connected` (Protokoll `nipp-20260905_001.log:37612-37719`): `Ringing` steht in keiner der beiden Listen, und `Ringing → Connected` erfüllt die Bedingung nicht. Dieselbe Lücke traf das Annehmen über den Toast (`Incoming → Connected`). |
| B3 | **Mittel** | `App.xaml.cs:485` | `BringToFront()` wird bei Tray-Klick, Toast-*Annahme*, `tel:` und Hotkey gerufen — **nicht beim Klingeln**. Selbst mit funktionierender Navigation bliebe sie hinter einem versteckten Fenster. |

**Beleg im Protokoll.** `nipp-20260904.log:139-142` zeigt drei `Eingehender Anruf … von 151`. Zu keinem folgt je eine Zeile `Anruf …: X -> Y` — die wird nur unter `previous != status` geschrieben. Bei ausgehenden Anrufen steht die volle Kette `Dialing -> Ringing -> Connected -> Ended` im selben Protokoll.

**Warum es niemandem früher auffiel.** `ShellViewModel` filtert **nicht** auf den Vorzustand, sondern zählt die aktiven Gespräche. Bei offenem Fenster erschien die schmale Gesprächsleiste korrekt — und genau so wurde T06 am 04.09. als „teilweise bestanden" abgenommen. Fehlend waren Toast und Gesprächsansicht, und bei geschlossenem Fenster eben alles.

**Der erste Behebungsversuch griff zu kurz** und ist hier festgehalten, weil er dieselbe Lehre ein zweites Mal zeigt: er korrigierte den Vergleich (`is not` statt `!=`) und ergänzte `Incoming` in der Übergangsliste — behielt aber die Übergangsliste selbst bei. Damit war der eingehende Anruf behoben und der **ausgehende weiterhin kaputt**, was erst der nächste Anruf am Gerät zeigte. Eine Liste erlaubter Zustandsübergänge zu pflegen heisst, jede Meldereihenfolge des SDK zu kennen; drei Anläufe haben je eine vergessen.

### 8.2 Was das über den Durchgang sagt

**Der Fehler ist alt und war nie eine Regression.** Der Status-Vorgriff in `Track()` stammt aus M2, die Bedingung, die darüber stolpert, aus M3 — die Bedingung war seit ihrer Einführung wirkungslos. Der Umbau U1 aus §2.2 hat den Zurück-Pfeil und die Gesprächsleiste gebracht, an dieser Stelle aber nichts geändert.

Zwei Ursachen, und die zweite ist die eigentliche.

**Erstens: dieselbe Regel stand wörtlich an zwei Orten.** Zwei Kopien sind zwei Gelegenheiten, sie falsch zu haben — und eine Gelegenheit, nur die eine zu korrigieren. Die Frage „beginnt hier ein Anruf zu klingeln?" steht jetzt einmal am Ereignis (`CallStateEventArgs.IsNewIncoming`, `HasJustEnded`) und wird dort geprüft; das war vorher nicht möglich, weil sie in zwei App-Klassen ohne Testbarkeit lag. `Previous` ist dafür `CallStatus?` geworden: ein neuer Anruf hat keinen Vorzustand, und das zu behaupten war die erste Lüge.

**Zweitens, und das ist der Grund für den zweiten Anlauf: die Navigation hing überhaupt an Zustandsübergängen.** Eine Liste erlaubter Übergänge muss jede Meldereihenfolge des SDK kennen — und die ist für ein- und ausgehende Anrufe verschieden. Ein eingehender wird bereits klingelnd angelegt, ein ausgehender läuft über `Ringing`. Jeder Anlauf hat eine der beiden Richtungen vergessen.

Die Navigation hängt jetzt nicht mehr an Zuständen, sondern an der **Kennung**: `MainWindow` merkt sich, welche Anrufe die Ansicht schon geholt haben, und jeder Anruf holt sie genau einmal. Das ist von der Reihenfolge unabhängig, erfüllt §8.2 weiterhin (wer für den zweiten Anruf zurück zur Wähltastatur geht, wird nicht wieder hineingezogen) und hat keinen Zustand mehr, den man vergessen könnte. `HasJustStarted` ist ersatzlos entfallen — die Eigenschaft, die zweimal falsch war, gibt es nicht mehr.

### 8.3 Stand

| Prüfung | Ergebnis |
|---|---|
| `build.ps1 build` (Core und App) | 0 Warnungen, 0 Fehler |
| Nipp.Core.Tests | 256 bestanden |
| Nipp.Architecture.Tests | 7 bestanden |

**Am Gerät abzunehmen, zusätzlich zu §6.3 und §7.3:**

1. **Ausgehender Anruf** — die Gesprächsansicht erscheint. Der einfachste Fall, und der, an dem der erste Behebungsversuch scheiterte.
2. **Eingehender Anruf bei geschlossenem Fenster** — Toast erscheint, Fenster kommt nach vorn, Gesprächsansicht ist zu sehen.
3. Dasselbe bei offenem Fenster, und die Annahme über den Toast-Knopf.
4. **Zurück zur Wähltastatur im Gespräch** — die Ansicht darf einen nicht wieder hineinziehen (§8.2, zweiter Anruf).

**T06 ist damit wieder offen.**
