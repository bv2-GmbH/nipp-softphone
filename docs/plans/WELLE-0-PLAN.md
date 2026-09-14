# Welle 0 — Umsetzungsplan

**Stand:** 13.09.2026. Grundlage ist `REVIEW-2026-09-12.md`, Abschnitt 5, Welle 0.

> ## Umsetzungsstand
>
> **Alle fünf Schritte sind gebaut, committet und gepusht** (13.09.2026).
> Der letzte CI-Lauf auf echter x64-Hardware ist grün, **1155
> Komponententests** und **27 Architekturtests** bestehen.
>
> | Schritt | Stand | Commit |
> |---|---|---|
> | **W0.1** Push und CI | fertig | `03faf6f` |
> | **W0.5** Klammer-Null | fertig, zehn Testfälle | `7135694` |
> | **W0.2** Ausnahmegrenze | fertig, ADR-053 | `7813096` |
> | **W0.3** SIP-Trace maskieren | fertig, Nachtrag zu ADR-022 | `143395a` |
> | **W0.4** Provisionierung | fertig, ADR-054 und Nachtrag zu ADR-019 | `f2a8a93` |
>
> **Was jetzt aussteht, ist kein Code:** der Gerätetag. Die Zeilen **T255 bis
> T262** stehen in `docs/test-matrix.md` und haben kein Ergebnis. **Vorher
> `history.db` und `settings.json` kopieren** — W0.2 legt eine kaputte
> Datenbank beiseite, W0.4 schreibt ein neues Feld in die Einstellungen.
>
> **Drei Abweichungen vom Plan, alle begründet:**
>
> 1. **`CallbackGuard` ist öffentlich, nicht intern.** Ohne
>    `InternalsVisibleTo` liesse sich die wichtigste Zusage des Schrittes nicht
>    prüfen; ein SDK-Typ steht in keiner Signatur.
> 2. **`InitializeAsync` nimmt jetzt die Einstellungen entgegen.** Der Plan
>    hatte übersehen, dass der Port **vor** `Core.Start()` stehen muss und die
>    Methode bis dahin gar keine Einstellungen kannte.
> 3. **Zwei Fehler fand erst der Testlauf**, nicht das Nachdenken: der
>    SQLite-Verbindungspool hielt die kaputte Datei offen, und im
>    Maskierungsmuster stand ein Backspace-Zeichen statt einer Wortgrenze. Beide
>    sind behoben und im Code kommentiert.

**Umfang, entschieden am 13.09.2026:** die fünf Schritte der Welle 0, danach ein Tag am Gerät. Welle 1 und 2 erst nach der Abnahme.

## Die vier Entscheide, auf denen dieser Plan steht

| Frage | Entscheid | Folge für den Plan |
|---|---|---|
| Wer gewinnt bei einem provisionierten, nicht gesperrten Feld? | **Der Benutzer.** | Die Einstellungen führen, was der Benutzer angefasst hat (`UserOverrides`); das Profil setzt nur, was er nie berührt hat. Eine Sperre gewinnt immer. Schritt W0.4. |
| Darf der Support Debug einschalten lassen? | **Ja.** | Der SIP-Trace wird maskiert, Kopfzeilen mit Zugangsdaten geschwärzt. Schritt W0.3. ADR-022 bekommt einen Nachtrag. |
| Wie weit reicht die Umsetzung? | **Welle 0, dann Gerätetag.** | Fünf Schritte, kein Vorgriff auf Welle 1 — auch nicht auf Naheliegendes wie den Fokusklau (C1). |
| Was darf mit Git geschehen? | **Committen und pushen, kein Merge.** | Ein Commit je Schritt auf `review-umsetzung`, nach jedem Push die CI abwarten. `main` bleibt, wo es ist. |

## Arbeitsregeln für die Umsetzung

- **Erst der Test, dann die Änderung.** Jeder Schritt beginnt mit dem Test, der heute rot wäre. Wo eine Klasse das SDK anfasst und nicht testbar ist, wird die Regel in eine reine Hilfsklasse gezogen und dort geprüft.
- **Bauen nur über `.\build.ps1`**, nie ein blankes `dotnet`. Die Tests bauen, während nipp läuft; **`Nipp.App` baut nur, wenn nipp beendet ist** (MSB3027, Task-Manager auf `Nipp.App`, nicht das Symbol). Ich frage vor jedem App-Build, ob nipp geschlossen werden darf.
- **Formatieren nur die eigenen Dateien** (`dotnet format --include …`).
- **Benutzertexte mit Umlauten, ohne Paragrafen- und Methodennamen**, `«…»` in C#-Literalen. Keine Rufnummer, kein Name ins Protokoll — das ist in W0.3 selbst der Auftrag.
- **Jede Abweichung von der Spezifikation als ADR** in `docs/decisions.md`; `CLAUDE.md` bekommt am Ende einen neuen Abschnitt «Aktueller Meilenstein», nicht fünf.
- **Ein Commit je Schritt**, Nachricht nennt Befund und ADR; danach `git push` und `gh run watch`. Ist die CI rot, wird zuerst die CI grün — nichts anderes.
- **Der Arbeitsbaum bleibt sauber:** was nicht zum Schritt gehört, wird nicht angefasst. Die Textregel-Verstösse (A4, A5), die Daten vom 13.09. (A10) und der Toast-Körper (C3) sind Welle 1, auch wenn sie im Weg liegen.

## Reihenfolge und Abhängigkeiten

```
W0.1 Push und CI ─────────────────────────────┐
W0.5 Normalizer (unabhängig, klein) ──────────┤
W0.2 Ausnahmegrenze ──────────────────────────┼──▶ Gerätetag
W0.3 SIP-Trace maskieren ─────────────────────┤
W0.4 Provisionierung (grösster Schritt) ──────┘
```

W0.1 zuerst, weil jeder weitere Commit auf einer geprüften Basis stehen soll. W0.5 direkt danach — eine Stunde, ein sichtbarer Fehler weniger. W0.2 vor W0.4, weil W0.4 den Startpfad umbaut und der dann schon abgesichert sein soll. W0.3 ist unabhängig und kann zwischen W0.2 und W0.4 laufen.

**Aufwand gesamt:** vier bis fünf Arbeitstage, dazu der Gerätetag.

---

## W0.1 — Push, CI, Stand festhalten

**Befund:** 37 Commits seit dem 09.09. ohne Push, 78 vor `main`; elf Dateien der ADR-052-Arbeit nicht committet. Die CI auf echter x64-Hardware hat drei Arbeitstage nicht gesehen.

**Dateien:** nur Git, dazu ein Absatz in `CLAUDE.md`.

**Vorgehen**

1. Die elf geänderten Dateien als einen Commit «ADR-052: die Breite gehört dem Fenster — Doku und Layout» festhalten (Inhalt laut `git diff`: `ShellPage.xaml(.cs)`, `ShellViewModel.Layout.cs`, `Tokens.xaml`, sechs Dokumente). `REVIEW-2026-09-12.md` und dieser Plan kommen als zweiter Commit dazu.
2. `git push origin review-umsetzung`, dann `gh run watch` bis zum Ergebnis.
3. Ist der Lauf rot: Ursache im CI-Protokoll lesen und beheben, bevor irgendetwas anderes geschieht. Ein Unterschied zwischen ARM64-Emulation und echtem x64 wäre genau der Befund, den ADR-001 als Risiko trägt.
4. In `CLAUDE.md` unter «Aktueller Meilenstein» einen Satz, was `review-umsetzung` von `main` trennt und dass der Merge bewusst offen ist.

**Risiko:** keines im Code. Das Risiko ist der heutige Zustand.

**Definition of Done:** `origin/review-umsetzung` ist gleich HEAD, der CI-Lauf ist grün, `CLAUDE.md` nennt den Stand.

**Aufwand:** S — eine halbe Stunde plus vier Minuten CI.

---

## W0.5 — Nummern mit Klammer-Null

**Befund C2:** `+41 (0)79 123 45 67` wird zu `+410791234567`. `KeepDialCharacters` (`NumberNormalizer.cs:182-200`) behält jede Ziffer, `Normalize` gibt bei führendem `+` unverändert zurück (`:71`).

**Dateien:** `src/Nipp.Core/Services/Telephony/NumberNormalizer.cs`, `tests/Nipp.Core.Tests/Services/Telephony/NumberNormalizerTests.cs`.

**Vorgehen**

1. Tests zuerst, vier Fälle: `+41 (0)79 123 45 67` → `+41791234567`; `0041 (0)79 123 45 67` → `+41791234567`; `+41(0)791234567` → `+41791234567`; `079 123 45 67` bleibt wie bisher (die Regel greift nur nach einer Landesvorwahl). Dazu ein Fall, der belegt, dass `(0)` mitten in einer Nummer ohne Vorwahl **nicht** angetastet wird.
2. Ein privater Schritt `DropTrunkZeroAfterCountryCode` vor `KeepDialCharacters`: entfernt genau eine Folge `(0)` — mit beliebigen Leerzeichen davor — die unmittelbar auf `+` oder `00` plus ein bis drei Ziffern folgt. Ein `GeneratedRegex` mit Zeitgrenze, wie `LogMasking` es macht.
3. Derselbe Schritt in der Vorschlagsauflösung (`:215`), damit die Vorschlagsliste beim Tippen von `+41 (0)7` dasselbe sieht wie das Wählen.
4. `IsDialable` bleibt unberührt — die Frage «ist das wählbar?» ändert sich nicht, nur die Antwort auf «wie lautet die Nummer?».

**Risiko:** gering, die Regel ist eng. Notrufe (`112`, `144`) und interne Ziele laufen weiterhin über `IsInternalTarget` vor der Vorwahlprüfung.

**Definition of Done:** die fünf Tests grün, alle bestehenden `NumberNormalizerTests` unverändert grün, kein anderer Aufrufer geändert.

**Aufwand:** S — eine Stunde.

---

## W0.2 — Die Ausnahmegrenze

**Befunde B1, B2, B3, B6, B17, B19:** Ausnahmen aus SDK-Callbacks laufen ungefangen durch den nativen Rahmen; `LinphoneException` wird nirgends gefangen; fünf `async void`-Handler ohne `try`; `CallHistoryStore` ohne einen einzigen `catch`; `SynchronizationContext.Post` ohne Absicherung; `Deute()` ausserhalb des `try` im HID-Lesethread.

**Der Grundsatz:** eine Ausnahme aus einer Ansicht, einem Abonnenten oder einer Fremddatei darf **nie** bis zum SDK oder bis zu `OnUnhandledException` kommen. Sie wird an der Grenze gefangen, mit Kennung und Ursache protokolliert, und der Benutzer bekommt einen Satz, der sagt, was geschehen ist. `OnUnhandledException` selbst bleibt, wie es ist — wer dort ankommt, hat einen Fehler, den niemand vorhergesehen hat, und der soll nicht still weiterlaufen.

**Dateien**

| Datei | Was |
|---|---|
| `Services/Telephony/SipEventBridge.cs` | Alle acht Listener-Callbacks (`:68`, `:137`, `:140`, `:173`, `:217`, `:229`, `:261`, `:264`) laufen durch einen Wächter |
| neu `Services/Telephony/CallbackGuard.cs` | Der Wächter als reine, testbare Klasse: `Run(logger, callbackName, handle?, action)` — fängt `Exception`, protokolliert, wirft nie |
| `Services/Telephony/SipService.cs` | `SetHoldAsync`, `SendDtmfAsync`, `HangUpAsync`, `AcceptAsync`, `TransferAsync`, `DeclineAsync` übersetzen `LinphoneException` in eine `InvalidOperationException` mit deutschem Satz. **Der Grund:** ViewModels kennen keine SDK-Typen (Architekturtest), also darf `LinphoneException` die Telephony-Schicht nicht verlassen |
| `Services/Telephony/TelephonyLog.cs` | Zwei neue Meldungen: «Abonnent {Callback} hat bei Anruf {Handle} eine Ausnahme ausgelöst» und «SDK-Aufruf {Operation} bei Anruf {Handle} fehlgeschlagen» |
| `ViewModels/ActiveCallViewModel.cs` | `Accept`, `HangUp`, `ToggleMute`, `ToggleHold`, `Swap`, `SendDtmf` (`:214-278`, `:409-417`) fangen `InvalidOperationException` und setzen `Hint`, wie Transfer und Aufnahme es schon tun (`:375`, `:401`, `:454`) |
| `Views/ActiveCallPage.xaml.cs` | `OnMuteClick`, `OnHoldClick`, `OnDtmfKeyPressed`, `OnRecordClick` (`:179-243`) laufen durch dieselbe Absicherung wie `OnCallAccelerator` (`:306-346`) |
| `Windows/CardDesignerWindow.xaml.cs` | `OnRunLookupClick` (`:972-980`) ebenso |
| `Services/History/CallHistoryStore.cs` | `Add`, `MarkSeen`, `Purge`, `Load` fangen `SqliteException` und protokollieren; der Konstruktor legt eine Datei, die `EnsureSchema` nicht öffnen kann, als `history.db.kaputt-<Zeit>` beiseite und legt neu an — dasselbe Muster wie `SettingsService.cs:400`. Die Anrufliste ist damit weg, aber nipp startet, und die Datei liegt noch da |
| `Services/Integrations/Context/CallerContextService.cs:487`, `ViewModels/ShellViewModel.cs:1356,1376` | `Post` mit Wächter, Muster aus `HeadsetCallControl.cs:415-431` |
| `Services/Windows/Hid/HidTelephonyDevice.cs:803-826` | `Deute(…)` in das `try` des Lesethreads; der `catch` protokolliert die Ausnahme mit Typ, bevor er `Lost` meldet — heute wird `ex` verworfen |

**Was bewusst nicht in W0.2 ist:** der synchrone SQLite-Schreibzugriff aus dem Callback (B6, zweiter Teil). Ihn auf einen Hintergrundthread zu legen braucht eine Verbindung je Thread und eine Reihenfolgegarantie; das ist Welle 2. In W0.2 wird er nur abgesichert.

**Tests**

- `CallbackGuardTests`: eine werfende Aktion wird protokolliert und wirft nicht; eine erfolgreiche läuft durch; die Protokollzeile trägt den Callback-Namen und die Kennung, aber keinen Inhalt der Ausnahme-Meldung, wenn sie eine Nummer enthält (durch `LogMasking.Line`).
- `ActiveCallViewModelTests`: ein `ISipService`-Fake, dessen `SetHoldAsync` eine `InvalidOperationException` wirft → `Hint` gesetzt, kein Wurf, `ToggleHoldCommand` bleibt ausführbar.
- `CallHistoryStoreTests`: eine Datei mit Zufallsbytes als `history.db` → Konstruktor wirft nicht, Datei liegt als `.kaputt-…` daneben, `Load` gibt eine leere Liste.
- Architekturtest `ExceptionBoundaryTests`: jeder `async void`-Handler unter `Nipp.App` enthält `try` oder ruft den Wächter — textbasiert wie `XamlResourceTests`; und `LinphoneException` kommt ausserhalb von `Services/Telephony/` nirgends vor (ist durch `SdkBoundaryTests` schon abgedeckt, hier nur ausdrücklich).

**ADR-053** — «Die Ausnahmegrenze liegt an der Bridge und am Befehl, nie am SDK». Hält fest, warum `OnUnhandledException` nicht `Handled` setzt.

**Risiko:** ein Wächter, der zu viel schluckt, versteckt den nächsten Fehler. Deshalb keine stille Rückgabe — jede gefangene Ausnahme steht mit Callback-Namen und Anrufkennung im Protokoll, auf Stufe Warnung, nicht Debug.

**Definition of Done:** alle Tests grün; ein absichtlich werfender Abonnent (Testfall) beendet den Prozess nicht; `grep "async void" src/Nipp.App` findet keinen Handler ohne Absicherung; Build ohne Warnungen.

**Aufwand:** M — ein bis anderthalb Tage.

---

## W0.3 — Der SIP-Trace wird maskiert

**Befund E1:** `SdkLogBridge.cs:87-98` reicht jede SDK-Zeile ungefiltert weiter. Heute im Protokoll dieser Maschine: 714 Zeilen mit `Authorization: Digest` oder `WWW-Authenticate`, 4 282 `From:`-Zeilen. `LogMasking.Line` (`:129`) maskiert nur Ziffernfolgen von 7 bis 15 Stellen; die Nebenstelle `905` in nipps eigener Warnzeile bleibt stehen.

**Der Entscheid dahinter:** Debug ist ein Support-Artefakt. Also gilt für den SDK-Trace dieselbe Zusage wie für die eigenen Zeilen (§21.2, ADR-022).

**Dateien:** `src/Nipp.Core/Diagnostics/LogMasking.cs`, `src/Nipp.Core/Services/Telephony/SdkLogBridge.cs:98`, `src/Nipp.Core/Services/Telephony/SipService.cs` (die Präsenz-Warnungen mit `sip:…@`), `tests/…/Diagnostics/LogMaskingTests.cs`, `tests/Nipp.Architecture.Tests/PrivacyLogTests.cs`, `tools/Test-Blf.ps1`, `tools/Test-Ton.ps1`, `tools/Test-Headset.ps1`, `docs/decisions.md` (Nachtrag zu ADR-022).

**Vorgehen**

1. Tests zuerst, mit **echten Zeilen als Vorlage** (aus dem heutigen Protokoll abgeschrieben, Nummern durch Muster ersetzt): ein `REGISTER` mit `Authorization: Digest username="…", realm="…", nonce="…", response="…"`; ein `401` mit `WWW-Authenticate`; ein `INVITE` mit `From: "Max Muster" <sip:0791234567@remote.example>;tag=…`; ein `NOTIFY` mit `sip:905@remote.example`; eine `P-Asserted-Identity`. Erwartung je Zeile steht im Test.
2. Neue Funktion `LogMasking.SipLine(string)`, gerufen aus `SdkLogBridge.OnLogMessageWritten` vor der Protokollzeile. Sie tut drei Dinge, in dieser Reihenfolge:
   - **Kopfzeilen mit Zugangsdaten:** bei `Authorization`, `Proxy-Authorization`, `WWW-Authenticate`, `Proxy-Authenticate` bleiben Schema und `realm`, `nonce` und `response` werden zu `nonce="…"`, `response="…"`. Das reicht, um «hat die Anlage überhaupt eine Challenge geschickt?» zu beantworten, und lässt nichts stehen, mit dem man sich anmelden könnte.
   - **Anzeigenamen:** `"Irgendwer" <sip:` wird zu `"…" <sip:`.
   - **Benutzerteil von `sip:`/`sips:`/`tel:`-Adressen:** ist er rein numerisch und mindestens drei Stellen lang, läuft er durch `Number` (dieselbe Maskierung wie überall). Das eigene Konto (`151bv2`) ist nicht numerisch und bleibt lesbar — es ist die eigene Kennung, keine fremde Rufnummer. Danach wie bisher `Line` für alles, was noch Ziffern trägt.
3. `Number` bekommt einen Test für dreistellige Nebenstellen, und die Präsenz-Warnungen im `SipService` nutzen `LogMasking.Number` für den Benutzerteil.
4. Die drei Auswertungsskripte unter `tools/` lesen den letzten Start aus dem Protokoll. Sie werden gegen ein maskiertes Protokoll geprüft; wo sie Nummern verglichen haben, vergleichen sie jetzt die Kennung. Die Skripte suchen nach Zustandsverläufen und Filterketten, nicht nach Nummern — die Anpassung sollte klein sein, ist aber zu **prüfen, nicht anzunehmen**.
5. `PrivacyLogTests` bekommt die Zusage «`SdkLogBridge` ruft `LogMasking.SipLine`» textbasiert, damit sie niemand still entfernt.
6. `Regex`-Zeitgrenze wie in `Line`; bei Überschreitung wird die Zeile verworfen, nie ungefiltert geschrieben.

**Kosten, offen genannt:** Debug wird ~34 000 Zeilen am Tag durch drei Regex-Läufe schicken. Auf dem UI-Thread, denn dort ruft das SDK den Listener. Gemessen wird das am Gerät (Gerätetag, T256); bleibt es unter einer Millisekunde je Zeile, ist es tragbar. Sonst wandert die Maskierung in den Serilog-Sink als Enricher, hinter die Warteschlange.

**ADR-022, Nachtrag:** «Der SDK-Trace ist Teil des Protokolls und unterliegt derselben Zusage. Was der Support braucht — Zustandsverläufe, Antwortcodes, Filterketten — bleibt lesbar; Zugangsdaten und Rufnummern nicht.»

**Definition of Done:** `grep -c "response=\"[0-9a-f]" nipp-<heute>.log` liefert 0 nach einem Neustart auf Debug; `LogMaskingTests` mit den fünf echten Zeilen grün; `Test-Blf.ps1` läuft gegen ein maskiertes Protokoll durch.

**Aufwand:** S bis M — ein halber bis ein Tag.

---

## W0.4 — Die Provisionierung ehrlich machen

**Befunde A1/E3, A2, B4, B13:** das Profil überschreibt bei jedem Start; der Generator kennt vier Schlüssel nicht; der SIP-Port wirkt nicht; die Zertifikatsprüfung verlangt einen Neustart, den sie nicht braucht.

**Der Entscheid dahinter:** der Benutzer gewinnt. Damit das gilt, müssen die Einstellungen wissen, was der Benutzer angefasst hat — und zwar **an einer Stelle**, nicht in jedem Aufrufer von `Save`.

Der Schritt hat vier Teile, in dieser Reihenfolge, jeder als eigener Commit.

### W0.4a — Ein Katalog der Profilpfade

Heute steht die Schlüsselliste zweimal: als `switch` in `ProvisioningService.ApplyValues` (`:459-540`, 29 Schlüssel) und als Zeichenkettenliste in `Nipp.Provisioning/Program.cs` (25 Schlüssel). Es fehlt die Verbindung zwischen einem Pfad und dem Feld in `NippSettings`, die W0.4b braucht.

**Neu:** `Services/Settings/ProvisioningCatalog.cs` — je Pfad ein Eintrag mit `Path`, `Read(NippSettings) → string?` und `Apply(NippSettings, string raw) → NippSettings?` (null bei unbrauchbarem Wert), dazu die Kennzeichen `NotAllowedRemotely` und `Secret` (für `update.token`). `ApplyValues` iteriert den Katalog statt des `switch`; `nippprov neu` und `nippprov pruefen` lesen dieselbe Liste. Dazu drei Pfade, die heute ausserhalb der Werteliste laufen und für W0.4b als Pfad gebraucht werden: `accounts`, `contacts.team`, `contacts.groups`.

**Tests:** `ProvisioningCatalogTests` — jeder Eintrag überlebt `Read → Apply → Read` unverändert (Rundreise); die Pfadmenge ist gleich der Menge, die `nippprov schema` ausgibt (ein Test im Kern, weil `Nipp.Provisioning` den Kern referenziert); `update.token` kommt nie in `settings.json`.

### W0.4b — Die Einstellungen wissen, was der Benutzer angefasst hat

**Modell:** `NippSettings.UserOverrides : IReadOnlySet<string>` — Profilpfade in Kleinschreibung, persistiert in `settings.json` (PascalCase wie alles dort).

**Die eine Stelle:** `SettingsService.Write` vergleicht den neuen Stand über den Katalog mit `_current` und nimmt jeden Pfad, dessen `Read` sich unterscheidet, in `UserOverrides` auf. Jeder heutige `Save`-Aufrufer (`SettingsViewModel.cs:1145, 1225, 1411`, `ActiveCallViewModel.cs:647`, Team-Sortierung) markiert damit automatisch, ohne eine Zeile zu ändern. Der `ProvisioningService` ruft eine zweite Methode `SaveFromProfile`, die **nicht** markiert.

**In `ApplyProfile` und `ApplyValues`:**

- Ein Pfad, der in `UserOverrides` steht und **nicht gesperrt** ist, wird übersprungen und auf Debug protokolliert («Profilwert für {Pfad} übersprungen — vom Benutzer eingestellt»).
- Ein **gesperrter** Pfad wird immer angewendet und aus `UserOverrides` entfernt — die Sperre ist der Weg des Administrators, einen Benutzerwert zurückzuholen.
- `Accounts` und `Team` folgen derselben Regel über ihre Pfade. Heute ersetzt das Profil die Kontenliste vollständig (`ProvisioningService.cs:296`); ein vom Benutzer hinzugefügtes zweites Konto war damit nach jedem Start weg. Das ist derselbe Befund in anderer Form und wird mit gelöst.

**Was mit bestehenden Installationen passiert:** eine `settings.json` ohne `UserOverrides` hat eine leere Menge — das Profil gewinnt beim **ersten** Start nach dem Update ein letztes Mal, wie heute. Danach gilt die neue Regel. Das steht so in der ADR und im Gerätetag (T257 mit einer kopierten alten Datei).

**Was nicht dabei ist:** ein Knopf «Auf Profilwerte zurücksetzen». Ohne ihn kann nur der Administrator per Sperre zurückholen. Er ist eine Welle-1-Massnahme; die ADR nennt ihn.

**Tests:** `ProvisioningServiceTests` — ein Benutzerwert für `network.keep-alive-seconds` überlebt `ApplyProfile` mit demselben Pfad; ein gesperrter Pfad überschreibt den Benutzerwert und leert die Markierung; ein zweites Benutzerkonto überlebt ein Profil mit einem Konto; `SettingsServiceTests` — `Save` markiert den geänderten Pfad, `SaveFromProfile` nicht; eine alte `settings.json` ohne `UserOverrides` lädt mit leerer Menge (**gegen eine echte Datei nachgebaut**, nicht camelCase — Lehre aus `CLAUDE.md`).

### W0.4c — Der SIP-Port kommt an

`SipService` setzt vor `_core.Start()` (`:330`) die `core.Transports`: der Port aus `Network.SipPort` geht an den Transport, den das erste Konto nutzt (`UdpPort`, `TcpPort` oder `TlsPort`); die beiden anderen bleiben auf dem SDK-Standard. TCP und TLS auf demselben Port geht nicht (zwei Sockets), deshalb nicht «alle drei auf denselben». Die Zuordnung steht in einer reinen Hilfsklasse `TransportPorts.From(NippSettings) → (udp, tcp, tls)` unter `Services/Telephony/`, damit sie ohne SDK testbar ist; `SipService` wendet nur an. `RequiresRestart` behält den Port zu Recht — `Transports` gelten erst nach einem Core-Neustart — und **verliert die Zertifikatsprüfung** (B13), die `Apply` bei jedem Durchlauf sofort setzt (`SettingsApplier.cs:94`).

**Tests:** `TransportPortsTests` — Konto auf TLS mit Port 5061 → `tls = 5061`, andere Standard; Konto auf UDP mit 5060 → `udp = 5060`; kein Konto → alles Standard. `SettingsApplierTests.RequiresRestart` — Zertifikatsprüfung ist kein Grund mehr.

### W0.4d — Dokumentation

`docs/provisioning.md`: der Vorrang so beschreiben, wie er jetzt gebaut ist, mit dem Satz zur Sperre als Rückholweg und dem Absatz zu bestehenden Installationen. `NIPP-BUILD.md §9.2`: unverändert, der Port tut jetzt, was dort steht. `docs/decisions.md`: **ADR-054** «Der Benutzer gewinnt, und die Einstellungen wissen, was er angefasst hat» mit dem Entscheid vom 13.09.2026, der Herkunft (A1/E3), der Migration und dem offenen Rücksetzknopf. **ADR-019** bekommt einen Nachtrag: die Behauptung «im SettingsApplier vollständig umgesetzt» galt für den Port nicht.

**Risiko:** der Transport-Umbau berührt die Anmeldung — am Gerät mit T01 (UDP), T03 (TLS) und einem geänderten Port nachzunehmen. Die `UserOverrides`-Markierung in `Write` läuft bei jedem Speichern über 32 Pfade; das ist ein Vergleich von Zeichenketten und kein Grund zur Sorge, wird aber gemessen.

**Definition of Done:** die vier Tests-Gruppen grün; `nippprov schema` und der Kern nennen dieselbe Liste; ein manuell geänderter Keep-Alive überlebt einen Start mit Profil (Test und Gerätetag); der Port steht nach Neustart im SDK-Protokoll («Listening on udp/tcp/tls port …»); `docs/provisioning.md` sagt, was der Code tut.

**Aufwand:** M bis L — zwei Tage.

---

## Der Gerätetag

Erst nach W0.1 bis W0.5, mit dem gebauten Stand des letzten Commits. **Vorher `history.db` und `settings.json` kopieren** — W0.2 legt eine kaputte Datenbank beiseite, W0.4b schreibt ein neues Feld in die Einstellungen.

**Neue Zeilen für `docs/test-matrix.md`**

| Nr. | Prüfung | Erwartung |
|---|---|---|
| **T255** | Debug einschalten, anmelden, einen Anruf führen, Protokoll lesen | Keine Zeile enthält `response="…"` mit Inhalt, keinen Anzeigenamen in Anführungszeichen, keine fremde Rufnummer im Klartext; die Zustandsverläufe sind weiter lesbar; `Test-Blf.ps1` läuft durch |
| **T256** | Debug an, eine Minute Betrieb, Prozessorlast von `Nipp.App` im Task-Manager | Kein sichtbarer Unterschied zu vorher; die Maskierung läuft auf dem UI-Thread |
| **T257** | Keep-Alive von Hand ändern, nipp beenden, mit Profil starten | Der Handwert steht noch. Dann: denselben Pfad im Profil sperren, starten — der Profilwert steht, das Feld ist ausgegraut |
| **T258** | Eine alte `settings.json` (Kopie von vorher) einspielen, mit Profil starten | Erster Start: Profil gewinnt einmal; danach wie T257 |
| **T259** | SIP-Port im Profil auf einen anderen Wert, Neustart | SDK-Protokoll nennt den neuen Port; Anmeldung gelingt (mit dem Test-Trunk, nie gegen einen Kundentenant) |
| **T260** | `+41 (0)79 …` aus einer Outlook-Signatur ins Nummernfeld einfügen | Vorschlagsliste und Wahl zeigen `+4179…` |
| **T261** | Im Gespräch «Halten» drücken, während die Gegenseite auflegt (mehrfach versuchen) | nipp bleibt stehen, ein Hinweis erscheint, eine Warnzeile mit Kennung im Protokoll |

**Aus der bestehenden Matrix mitzunehmen,** weil W0 sie berührt: **T01** und **T03** (Anmeldung UDP/TLS nach W0.4c), **T04–T09** (Gesprächsbefehle nach W0.2), **T134** («Beenden» beendet), **T188** (Enter im Weiterleitungsfeld), und — je Gerät — **T79/T80** (Headset-Tasten, weil W0.2 den Lesethread anfasst).

**Was am Gerätetag ausdrücklich noch fehlt:** der Fokusklau ohne Toasts (C1, Welle 1). Wer T223 mit abgeschalteten Benachrichtigungen prüft, wird ihn finden — das ist erwartet, kein Rückschritt.

---

## Was ich während der Umsetzung von dir brauche

- **nipp beenden, wenn ich `Nipp.App` baue.** Die Tests bauen daneben, die App nicht (MSB3027). Ich frage jeweils, oder du lässt es während der Arbeit geschlossen.
- **Die Profil-Adresse.** `crm.bv2.ch` antwortet heute auf die Provisioning-URL mit 404. Für die Tests im Kern ist das egal, für T257 bis T259 braucht es ein erreichbares Profil — oder eine Factory-Datei unter `%PROGRAMDATA%\bv2\nipp\nipp-factory.xml`, die ich mit `nippprov neu` erzeuge.
- **Den Gerätetag selbst:** Engage 75, Link 400, PRO 9470, ein Windows-10-Arbeitsplatz, der Test-Trunk.

## Was bewusst liegen bleibt, obwohl es im Weg liegt

| Punkt | Warum nicht jetzt |
|---|---|
| Fokusklau ohne Toasts (C1) | Welle 1, W1.1 — klein, aber ein eigener Entscheid über den Rückfallpfad |
| Keep-Alive als Schalter (B7) | Braucht die SDK-API für das Intervall; ist sie im Wrapper nicht vorhanden, wird das Feld ehrlich zum Schalter. Welle 1, mit W0.4-Kenntnis |
| Textregel-Verstösse, Daten vom 13.09. (A4, A5, A10) | Welle 1, W1.6 — würden hier nur den Diff verwässern |
| Transfer-Ergebnis (B5) | Welle 1, W1.2 — braucht einen neuen Bridge-Callback, der erst nach W0.2 sicher ist |
| SQLite aus dem Callback auf einen Hintergrundthread (B6, Teil 2) | Welle 2 — Verbindungsmodell und Reihenfolge, nicht in einem Nachmittag |
