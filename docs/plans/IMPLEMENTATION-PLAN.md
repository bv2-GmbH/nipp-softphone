# nipp — Umsetzungsplan

**Bezug:** `NIPP-BUILD.md` (Spezifikation, Stand 04.09.2026) — verbindlich.
**Dieses Dokument:** wie die Spezifikation phasenweise abgearbeitet wird. Reihenfolge, Arbeitspakete, Gates, Aufwand, Risiken, und was wann von Dominic gebraucht wird.
**Stand:** 04.09.2026 · **Aktuelle Phase:** P5 (M4) — M3 abgenommen, M2 bis auf zwei zurückgestellte Punkte · **Fortschritt:** siehe §8 · **Bezugsrevision der Spezifikation:** Rev. 4

Bei Widerspruch gilt `NIPP-BUILD.md`. Dieses Dokument wird bei jedem Phasenabschluss fortgeschrieben (Statuszeile oben, Häkchen in den Arbeitspaketen).

**Geltungsbereich seit dem 06.09.2026.** Dieses Dokument führt die Phasen **P0–P9** des Telefons. Die Integrationsplattform aus §21 (Rev. 6) hat eine **eigene Phasenreihe I0–I8** in `INTEGRATION-PLAN.md` und wird dort fortgeschrieben — die beiden laufen nebeneinander und sind an einer Stelle verzahnt: **I3 setzt voraus, dass T06 (eingehender Anruf) am Gerät abgenommen ist**, weil es den Anrufpfad berührt.

---

## 1. Phasenübersicht

| Phase | Meilenstein | Ergebnis | Gate am Ende | Aufwand¹ |
|---|---|---|---|---|
| **P0** | — | Werkzeuge, Repo, Zugänge geklärt | Toolchain baut, Test-Trunk-Daten liegen vor | 0.5–1 T |
| **P1** | M0 | Solution + leere WinUI-App | `dotnet build` grün, Fenster startet mit Mica | 1–2 T |
| **P2** | M1 | **SDK-Spike** — Konsole + WinUI, packaged & unpackaged | **Go/No-Go.** REGISTER 200 OK, Audio in beide Richtungen | 2–5 T |
| **P3** | M2 | `SipService` mit Zustandsmaschine und Events | 20 Anrufe in Folge ohne Absturz | 3–5 T |
| **P4** | M3 | Wählen + aktives Gespräch vollständig | Weiterleiten beide Varianten, Makeln, Aufnahme | 4–6 T |
| **P5** | M4 | Einstellungen, alle sechs Gruppen | Codec-Reihenfolge im SIP-Log nachweisbar | 4–6 T |
| **P6** | M5 | Kontakte, Verlauf, Voicemail | CLIP-Name in Toast/Gespräch/Verlauf, BLF wechselt | 4–6 T |
| **P7** | M6 | Windows-Integration | `tel:`-Link wählt, Toast bei geschlossenem Fenster | 3–5 T |
| **P8** | M7 | Provisionierung + Diagnose | Profil richtet Konto ein, ZIP ohne Passwörter | 2–4 T |
| **P9** | M8 | MSIX, signiert, Update-Pfad | Installation und Update auf frischem Win 11 | 2–4 T + Wartezeit |

¹ Netto-Entwicklungstage, Test-PBX verfügbar, ein Entwickler mit Claude Code. Ohne Wartezeiten auf Zertifikate, Entra-Consent oder Lizenzentscheid. Summe: **26–44 Tage**, realistisch mit Reibung **6–9 Wochen**.

### Abhängigkeiten

```
P0 ──► P1 ──► P2 (GATE) ──► P3 ──► P4 ──► P7 ──► P9 (GATE: Lizenz)
                             │      │       ▲
                             │      └─ P5 ──┤
                             └──────── P6 ──┘   P8 nach P5
```

P5 (Einstellungen) und P6 (Kontakte/Verlauf) hängen beide nur an P3/P4 und lassen sich verschränken. P7 braucht P4, weil der `tel:`-Handler in einen funktionierenden Dialer mündet. P9 ist durch die Lizenzfrage blockiert, nicht durch Code.

---

## 2. Blocker-Zeitachse — was wann von Dominic gebraucht wird

Diese Punkte sind aus §16 der Spezifikation gezogen und nach dem Zeitpunkt sortiert, an dem sie tatsächlich blockieren. Nichts davon entscheidet Claude Code selbst.

| Gebraucht ab | Punkt | Wenn es fehlt |
|---|---|---|
| ~~P2~~ | ~~Test-Trunk~~ | **erledigt 04.09.2026:** pbx.example.ch über UDP, Zugangsdaten liegen vor |
| ~~P2~~ | ~~MSIX packaged oder unpackaged~~ | **erledigt 04.09.2026: MSIX packaged** (ADR-008). Beide Varianten laufen |
| ~~P6~~ | ~~Outlook-Zugriff~~ | **erledigt 04.09.2026: COM-Interop** (ADR-009). Graph wird nicht gebaut |
| P6 | Team-Nebenstellenliste für BLF (mindestens Testdaten) | BLF nur mit Dummy-Daten prüfbar |
| ~~P8~~ | ~~Aufnahme-Ablage~~ | **erledigt 04.09.2026: lokal, Pfad konfigurierbar.** Kein Netzpfad je Kunde |
| ~~P9~~ | ~~Lizenz~~ | **erledigt 04.09.2026: nipp bleibt vorerst intern.** M8 ist nicht mehr blockiert; der Release wird ausdrücklich als intern markiert. Die Frage kehrt bei einer Auslieferung an Kunden zurück |
| P9 | Branding: Icon, Wortmarke, Anzeigename im Startmenü | Platzhalter-Icon, Release verschiebt sich |
| P9 | Code-Signing-Zertifikat | **entschärft**, solange nipp intern bleibt: ein selbstsigniertes Paket genügt für die interne Verteilung. Wird wieder zum Blocker, sobald nipp an Kunden geht |
| P9 | Verteilung: Intune-MSIX oder klassisches MSI/EXE | Beides bauen ist teurer als einmal richtig entscheiden |
| P9 | Update-Mechanismus: App-Installer-URL, eigener Check oder Intune | Update-Prüfung bleibt Anzeige ohne Funktion |

**Stand 04.09.2026: sechs der zehn Punkte sind geklärt.** Offen bleiben Branding (§16.2), Verteilung (§16.3), Update-Mechanismus (§16.4) und die Team-Nebenstellenliste für BLF (P6) — keiner davon blockiert die nächsten Phasen.

Die beiden Punkte mit Wochen Vorlaufzeit, Lizenz und Zertifikat, sind durch die Entscheidung „vorerst intern" **entschärft, nicht erledigt**. Sie kehren unverändert zurück, sobald nipp an Kunden ausgeliefert werden soll (`docs/licensing.md`).

---

## 3. Die Phasen im Detail

### P0 — Vorbereitung (0.5–1 T)

Kein Code, nur Bodenarbeit. Verhindert, dass P1/P2 an Werkzeugfragen scheitern.

- [x] **AP0.1** Toolchain prüfen und dokumentieren: .NET 8 SDK, Visual Studio 2022 mit „Windows-Anwendungsentwicklung“, Windows App SDK, Windows 10 SDK 19041+. Versionen nach `docs/environment.md`.
- [x] **AP0.2** `git init`, `.gitignore` (VisualStudio-Vorlage plus `**/linphone-sdk/`, `*.wav`-Aufnahmen), erster Commit mit `NIPP-BUILD.md` und diesem Plan. Remote-Frage klären (GitHub bv2-Org?).
- [x] **AP0.3** SDK-Beschaffung entscheiden — Vorarbeit in §6 erledigt, hier bleibt eine Prüfung: ist `https://gitlab.linphone.org/api/v4/projects/411/packages/nuget/index.json` von hier aus **anonym** erreichbar? Wenn ja, Weg A. Wenn nein, **Weg C** (Prebuilt-ZIP `linphone-sdk-win64-5.5.18.zip`) — der ist verifiziert und blockiert das P2-Gate nicht. Weg B (Selbstbau) nur bei fehlendem Codec; dann MSYS2/CMake/Python vorher einrichten, ein halber bis ganzer Tag extra, der nicht in P2 gehört.
- [x] **AP0.3b** ~~`NIPP-BUILD.md` auf den verifizierten SDK-Stand nachziehen~~ — **erledigt 04.09.2026:** Spezifikation auf Rev. 2 gebracht (Version 5.5.18, Weg C, projectId 411, Plugin-Fallstrick, Lizenzkontaktweg), Änderungen in `NIPP-BUILD.md` §19 protokolliert, Rev. 1 als `NIPP-BUILD.md.rev1-20260904.bak` gesichert.
- [x] **AP0.4** Test-Trunk-Zugangsdaten von Dominic anfordern. **Blockiert P2.**
- [ ] **AP0.5** Lizenzanfrage an Belledonne und Zertifikatsbeschaffung anstossen (Vorlaufzeit, siehe §2).

**DoD:** `docs/environment.md` existiert, Repo initialisiert, Weg A oder B für das SDK ist entschieden, Test-Zugänge angefordert.

---

### P1 — M0: Repo und Skelett (1–2 T)

- [x] **AP1.1** `Nipp.sln`, Projekte `Nipp.App` (WinUI 3, packaged), `Nipp.Core` (classlib `net8.0-windows10.0.19041.0`), `Nipp.Provisioning` (leer bis P8 — anlegen ist ok, die Spec verbietet nur Vorbau *ausgeschlossener Features*), `tests/Nipp.Core.Tests`, `tests/Nipp.Architecture.Tests`.
- [x] **AP1.2** `Directory.Build.props`: `Nullable=enable`, `TreatWarningsAsErrors=true`, `LangVersion=12`, `Platforms=x64`, gemeinsame Versionsnummer. `RuntimeIdentifier=win-x64` — **kein AnyCPU** (Fallstrick §14.4).
- [x] **AP1.3** `.editorconfig`, `nuget.config`, `CLAUDE.md` wortgetreu nach §17 der Spezifikation.
- [x] **AP1.4** DI-Container in `App.xaml.cs` (`Microsoft.Extensions.DependencyInjection`), Serilog mit Ausgabe nach `%LOCALAPPDATA%\nipp\logs`.
- [x] **AP1.5** `MainWindow` mit `NavigationView`, Mica-Backdrop, erweiterte Titlebar, fünf leere Seiten plus Einstellungen. Fenstertitel `nipp` (klein).
- [x] **AP1.6** `Themes/Tokens.xaml` mit Farben, Radien, Typo-Skala aus dem Mockup.
- [x] **AP1.7** Architekturtest schreiben (schlägt bei `using Linphone` ausserhalb `Services/Telephony/` fehl) — **jetzt**, solange er trivial grün ist, nicht erst in P3.
- [x] **AP1.8** `docs/`-Gerüst: `sdk-setup.md`, `sdk-api-notes.md`, `decisions.md`, `licensing.md`, `test-matrix.md`, `packaging.md`.

**DoD:** `dotnet build -c Debug` grün ohne Warnungen, `dotnet test` grün, App startet, Navigation zwischen leeren Seiten funktioniert, Look entspricht dem Mockup.

**Wichtig:** `docs/licensing.md` in dieser Phase mit dem AGPL-Sachverhalt füllen und den P9-Block explizit hinschreiben. Nicht auf später verschieben — der Punkt verschwindet sonst aus dem Blick.

---

### P2 — M1: SDK-Spike ⛔ **GO/NO-GO-GATE** (2–5 T)

Die teuerste Phase, wenn sie schiefgeht, und die einzige, die das Projekt kippen kann. Deshalb in vier harten Schritten, jeder mit eigenem Abbruchkriterium.

- [x] **AP2.1** *Konsolen-Spike laden.* Wegwerf-Projekt `spike/SdkProbe`, SDK nach dem in AP0.3 gewählten Weg. Post-Build-Skript, das die komplette native DLL-Kette ins Ausgabeverzeichnis kopiert — nie von Hand (§14.2) — und dabei **`lib/mediastreamer/plugins/` in der relativen Struktur erhält**; ohne `libmswasapi.dll` gibt es kein Audio, mit einer Fehlermeldung, die nichts dazu sagt (§6.3). Danach `dumpbin /dependents` gegenprüfen, **bevor** etwas startet. Dann `Factory.Instance` abrufen. **Abbruch, wenn die DLLs nicht laden:** Fehlerbild mit Process Monitor einkreisen, welche DLL fehlt.
- [x] **AP2.2** *Wrapper-API verifizieren.* `LinphoneWrapper.cs` öffnen und **jede** Zeile der Tabelle aus §6 gegenprüfen. Jede Abweichung nach `docs/sdk-api-notes.md`. Zusätzlich die in der Spec offen gelassenen Namen ermitteln: Echo-Kalibrierung, `PayloadType`-Reihenfolge, `AccountParams`, `NatPolicy`, `FriendList`-Subscribes, `CallStats`, Recorder.
- [x] **AP2.3** *Registrieren und telefonieren.* Iterate-Schleife (im Spike simpel als `while`-Loop mit 20 ms), Konto gegen den **Test-Trunk**, REGISTER-Verlauf im SDK-Log prüfen. Anruf zur Echo-Nebenstelle, Audio in beide Richtungen hörbar. Alle drei Transporte durchprobieren (UDP/TCP/TLS) — inklusive des CA-Datei-Fallstricks (§14.6): Root-CAs aus dem Windows-Zertifikatspeicher in eine PEM exportieren und dem Core übergeben.
- [x] **AP2.4** *Ladbarkeit aus WinUI, doppelt.* Dieselbe Initialisierung aus `Nipp.App` heraus, **einmal packaged (MSIX) und einmal unpackaged**. Das ist der Fallstrick §14.3. Ergebnis als ADR in `docs/decisions.md`: packaged oder unpackaged, mit Konsequenzen für Toasts, Protokoll-Handler und Autostart (P7).
- [x] **AP2.5** `docs/sdk-setup.md` füllen: Weg, exakte Version, Commit-Hash, NuGet-Feed, Kopier-Skript.

**DoD / Gate:** REGISTER 200 OK im Log · ein Gespräch mit hörbarem Audio in beide Richtungen · `sdk-api-notes.md` gefüllt · Packaging-ADR geschrieben.

**Bei Scheitern:** nicht weiterbauen, sondern melden — mit konkretem Fehlerbild. Fallback-Optionen (nur als Entscheidungsgrundlage für Dominic, kein Eigenentscheid): unpackaged Build, oder SDK-Selbstbau nach Weg B, oder — falls die native Kette unter WinUI 3 grundsätzlich unbrauchbar ist — Alternativstack (siehe §6).

---

### P3 — M2: Telefonie-Kern (3–5 T)

- [x] **AP3.1** Eigene Modelltypen in `Services/Telephony/Model/`: `CallHandle`, `CallInfo`, `RegistrationState`, `TransferMode`, sowie die fünf EventArgs-Typen. **Kein `Linphone.*` verlässt den Service** — der Architekturtest aus AP1.7 bewacht das.
- [x] **AP3.2** `SipService`: Factory-/Core-Init mit `ConfigDir`/`DataDir`/`CacheDir` **vor** dem Core-Start, `Core.AddListener`, Start/Stop am App-Lebenszyklus.
- [x] **AP3.3** Iterate-Timer: `DispatcherQueueTimer`, 20 ms, am `App`-Lebenszyklus (nicht am `MainWindow` — die App läuft im Infobereich weiter). Regel im Code kommentieren: nichts Blockierendes in Callbacks (§14.1).
- [x] **AP3.4** `SipEventBridge`: SDK-Callbacks → .NET-Events mit SDK-freien Argumenten.
- [x] **AP3.5** Anruf-Zustandsmaschine: ausgehend, eingehend, angenommen, gehalten, beendet, Fehler. Zwei parallele Anrufe erlaubt, ein dritter wird mit klarer Meldung abgelehnt.
- [x] **AP3.6** Netzwerkwechsel: `NetworkInformation.NetworkStatusChanged` → `Core`-Reachability, Neuregistrierung. Standby/Resume über `SystemEvents.PowerModeChanged`.
- [x] **AP3.7** Fehlermeldungs-Katalog anlegen: SIP-Fehlercode → Text nach dem Muster aus §15 („was passiert ist und was zu tun ist“). Auch der SRTP-Fall aus §14.7 gehört hier hin.
- [x] **AP3.8** Minimal-UI zum Prüfen (Statuszeile, Nummernfeld, Anrufen/Auflegen) — darf provisorisch sein, P4 ersetzt es.
- [x] **AP3.9** `docs/test-matrix.md` mit den Fällen aus §13 anlegen und ab jetzt bei jedem Meilenstein durchziehen.

**DoD:** Registrierung mit Statusanzeige · ausgehend und eingehend · Neuregistrierung nach Netzwerkwechsel · 20 Anrufe in Folge ohne Absturz · Testmatrix erstmals ausgefüllt.

---

### P4 — M3: Wählen und Gespräch (4–6 T)

- [x] **AP4.1** `NumberNormalizer` als reine Funktion — **zuerst die Tests**, dann die Implementierung. Fälle aus §13 wörtlich: `044 512 84 30`, `+41445128430`, `0041445128430`, `*8010`, `40`, `112`, `0049…`. Plus Landesprefix aus den Einstellungen.
- [x] **AP4.2** `DialerPage`: Tastatur 3×4 mit Buchstabenzeile, Hardware-Ziffernblock, Enter wählt, Escape leert, Rücktaste.
- [x] **AP4.3** Live-Auflösung während der Eingabe (Präfixsuche Name und Nummer, max. 5 Treffer). Quelle vorerst der Verlauf, in P6 kommen Kontakte dazu. — **offen.** Das Eingabefeld zeigt nur die normalisierte Form; eine Vorschlagsliste gibt es nicht. Die Voraussetzung (Kontakte, `ClipResolver`) steht seit P6, der Aufsatz fehlt.
- [x] **AP4.4** `ActiveCallPage`: Gegenstelle, tickender Timer, Chips für Codec und Verschlüsselung.
- [x] **AP4.5** Gesprächsaktionen: Stumm, Halten, DTMF-Feld, zweiter Anruf, Makeln zwischen aktiv und gehalten.
- [x] **AP4.6** Weiterleiten, beide Varianten sichtbar getrennt: blind (`Transfer`) und begleitet (zweiter Anruf → `TransferToAnother`). Kein verstecktes Verhalten.
- [x] **AP4.7** Aufnahme: WAV nach `JJJJ-MM-TT_HHMMSS_<Nummer>.wav`, konfigurierbarer Ordner, **sichtbarer Indikator ist Pflicht** (§8.2 — Rechtslage Schweiz). Der Indikator wird als eigenes Akzeptanzkriterium geprüft, nicht nebenbei.
- [x] **AP4.8** `CallQualityPanel` aus `CallStats`: RTT, Jitter, Paketverlust, MOS, Bandbreite — Aktualisierung **im Sekundenrhythmus, nicht pro Iterate**.
- [x] **AP4.9** `Keypad`- und `SettingCard`-Controls als wiederverwendbare Bausteine (SettingCard wird in P5 gebraucht). — **halb.** `Keypad` gibt es und wird an drei Stellen verwendet. `SettingCard` gibt es nicht; die Einstellungsseite setzt `Expander` mit einem gemeinsamen Stil ein. Funktioniert, aber AP8.4 (Schloss-Symbol je gesperrtem Feld) wäre mit einem Control sauberer — heute wird auf Gruppenebene gesperrt.

**DoD:** Normalisierungs-Tests grün · Weiterleiten beide Varianten gegen die Test-PBX · Makeln funktioniert · Aufnahme erzeugt abspielbares WAV mit sichtbarem Indikator · Qualitätspanel zeigt plausible Werte.

---

### P5 — M4: Einstellungen (4–6 T)

- [x] **AP5.1** `SettingsSchema.cs`: die Tabellen aus §9.1–9.6 als Code — Feld, Typ, Standardwert, SDK-Ziel, Sperrbarkeit. Eine Quelle, aus der UI und Persistenz beide lesen.
- [x] **AP5.2** `SettingsService`: Persistenz nach `%APPDATA%\nipp`, Serialisierung mit Tests (§13), Migrationspfad für künftige Schemaänderungen.
- [x] **AP5.3** `SecretStore` mit DPAPI (CurrentUser). **Niemals Klartext in `linphonerc`** — wo möglich HA1 statt Passwort (`ComputeHa1ForAlgorithm`).
- [x] **AP5.4** Die sechs Views: Konto, Netzwerk, NAT/Medien, Audio, Codecs, Erweitert — auf Basis von `SettingCard`.
- [x] **AP5.5** Wirksamkeit ohne Neustart, wo das SDK es zulässt; wo nicht, sichtbarer Neustart-Hinweis. Pro Feld entscheiden und in `SettingsSchema` hinterlegen.
- [x] **AP5.6** `AudioDeviceService`: Geräteliste, getrenntes Klingelgerät, **Hotplug** (`AudioDevices` neu einlesen), Rückfall auf das Standardgerät bei Geräteverlust im Gespräch **ohne Gesprächsabbruch** plus Benutzerhinweis. — **halb.** Geräteliste und getrenntes Klingelgerät stehen. Das Ereignis `ISipService.AudioDevicesChanged` wird ausgelöst, aber **von niemandem abonniert**: wird im Gespräch ein Headset abgezogen, gibt es weder einen Hinweis noch einen nachvollziehbaren Rückfall. Ob das SDK von selbst umschaltet, ist ungeprüft.
- [x] **AP5.7** Echo-Kalibrierungsdialog mit Fortschritt (~15 s) und Ergebniswert in ms.
- [x] **AP5.8** `CodecService`: Tabelle mit Aktivieren und Reihenfolge, `Core.AudioPayloadTypes` in der richtigen Ordnung. Sperre: PCMA und PCMU nie beide aus. G.729 standardmässig inaktiv (§3). **Verifikation zwingend im SIP-Log**, nicht am UI-Zustand (§14.10).
- [x] **AP5.9** Mehrere Konten; Löschen entfernt die `AuthInfo` mit — keine verwaisten Zugangsdaten.
- [x] **AP5.10** Video-Schalter existiert, ist aber ausgegraut und aus (§9.3). — **offen.** `NatMediaSettings.VideoEnabled` steht im Modell auf `false`, in der Oberfläche erscheint nichts. Der Sinn des Punktes war, sichtbar zu machen, dass Video bewusst aus ist statt vergessen — das leistet er so nicht.

**DoD:** Transportwechsel UDP↔TLS wirkt nach Neuregistrierung · Codec-Reihenfolge im SIP-Log nachweisbar geändert · Audiogerätewechsel im laufenden Gespräch funktioniert · Echo-Kalibrierung liefert einen Wert · Serialisierungstests grün.

---

### P6 — M5: Kontakte, Verlauf, Voicemail (4–6 T)

- [x] **AP6.1** `CallHistoryStore` auf SQLite: Richtung, Gegenstelle, aufgelöster Name, Zeit, Dauer, Codec, Ergebnis, Aufnahmepfad. Aufbewahrung konfigurierbar (Standard 365 Tage), Aufräumen beim Start — **mit Test** (§13).
- [x] **AP6.2** `CallHistoryPage`: Filter (Alle/Verpasst/Eingehend/Ausgehend/Aufgenommen), Suche über Nummer und Name, Rückruf per Doppelklick, Kontextmenü Kopieren und Kontakt anlegen.
- [x] **AP6.3** `OutlookContactSource`: **Graph zuerst** (angemeldeter Benutzer, Entra-Consent), COM-Interop nur als Rückfallebene. Cache 12 h, **nie synchron im UI-Thread**.
- [x] **AP6.4** `ClipResolver`: Nummer → Name aus einem Cache, der Toast, Gesprächsansicht und Verlauf gleichermassen bedient. Mit Tests (§13).
- [x] **AP6.5** `BlfService` über `Friend`/`FriendList` mit Subscribes. Zustände frei / klingelt / im Gespräch / offline **farblich und als Text** — nie nur Farbe (Barrierefreiheit, §8.4). Subscribes **nur** für Team-Nebenstellen, nicht für Outlook-Kontakte (PBX-Last, §14.8).
- [x] **AP6.6** `ContactsPage` mit beiden Quellen getrennt sichtbar, `PresenceBadge`-Control.
- [x] **AP6.7** MWI: Voicemail-Adresse am Konto, Message-Waiting-Ereignis → Badge in der Navigation. `VoicemailPage` ist Anzeige plus „Mailbox anrufen“ — kein IMAP, kein Download, keine Wiedergabe.

**DoD:** eingehender Anruf einer bekannten Nummer zeigt den Namen in Toast, Gespräch und Verlauf · BLF wechselt sichtbar, wenn eine Nebenstelle telefoniert · MWI-Badge folgt der Mailbox · Verlaufstests grün.

---

### P7 — M6: Windows-Integration (3–5 T)

Der Zuschnitt dieser Phase hängt an der Packaging-ADR aus P2. Bei „unpackaged“ sind AP7.2/7.3/7.4 deutlich aufwendiger.

- [x] **AP7.1** Single-Instance mit `AppInstance.FindOrRegisterForKey`, Weiterleitung der Aktivierungsargumente an die laufende Instanz.
- [x] **AP7.2** Protokoll-Handler `tel:`, `sip:`, `callto:` — MSIX-Manifest oder HKCU. Aktivierung mit URI wählt **direkt**, ohne Rückfrage. Damit erledigt sich Klick-to-Call aus Outlook und aus dem CRM ohne eigene Schnittstelle.
- [x] **AP7.3** `ToastService` mit `AppNotificationManager`: Annehmen / Ablehnen / Mailbox, erscheint auch bei geschlossenem Fenster, **< 400 ms nach INVITE**. Läuft schon ein Gespräch: „Annehmen und halten“. Auto-Annahme überspringt den Toast, spielt aber einen Hinweiston. Klingeln auf dem separaten Klingelgerät.
- [x] **AP7.4** Autostart: `StartupTask` oder `HKCU\...\Run`. Minimiert im Infobereich starten.
- [x] **AP7.5** `TrayIconHost` (`H.NotifyIcon.WinUI`): Kontextmenü Öffnen / Präsenz / Stumm / Beenden. **Fenster schliessen beendet die App nicht.**
- [x] **AP7.6** `GlobalHotkeyService` per `RegisterHotKey` auf einem versteckten Fenster, Standard Strg+Umschalt+A, Konflikte abfangen und melden.
- [x] **AP7.7** Firewall-Freigabe: Capability im Manifest, Hinweis in der Installationsanleitung (§14.5).
- [ ] **AP7.8** Nichtfunktionale Ziele erstmals messen: Kaltstart < 3 s, Leerlauf < 180 MB, CPU im Gespräch < 6 %, INVITE→Toast < 400 ms. Ergebnisse nach `docs/performance.md`. Was reisst, wird hier behoben, nicht in P9.

**DoD:** `tel:`-Link aus Outlook und aus dem Browser wählt · Toast bei geschlossenem Fenster mit funktionierenden Buttons · Autostart überlebt einen Neustart · zweiter Programmstart aktiviert die erste Instanz · Messwerte dokumentiert.

---

### P8 — M7: Provisionierung und Diagnose (2–4 T)

- [x] **AP8.1** Factory-Config `linphonerc-factory` mit allen Standardwerten aus §9, ausgeliefert unter `%PROGRAMDATA%\bv2\nipp`.
- [x] **AP8.2** `ProvisioningService` über `Core.ProvisioningUri`, Abruf beim Start. **Fehler beim Abruf darf den Start nicht verhindern** — lokale Config gilt weiter, mit Hinweis im UI.
- [x] **AP8.3** XML-Parsing mit Tests, **inklusive kaputtem XML** (§13).
- [x] **AP8.4** `PolicyService` für gesperrte Felder; `SettingCard` rendert ausgegraut mit Schloss und Tooltip „Von der Administration festgelegt“. Im Code und in der Doku als **Bedienschutz, nicht als Sicherheitsgrenze** kennzeichnen.
- [x] **AP8.5** `Nipp.Provisioning`: XML-Schema plus Kommandozeilen-Generator, ein Profil pro Kunde. Keine Web-UI.
- [x] **AP8.6** Diagnosepaket als ZIP: Logs, Config **ohne Passwörter**, Systeminfo. Log-Level Aus/Info/Debug, „Log-Ordner öffnen“.
- [x] **AP8.7** Test, der das Diagnose-ZIP auf Passwortspuren prüft — explizit, nicht per Augenschein (§12 M7).

**DoD:** ein Profil richtet ein Konto vollständig ein · gesperrte Felder sind nicht editierbar · ZIP enthält Logs und nachweislich keine Passwörter.

---

### P9 — M8: Paket und Auslieferung (2–4 T + Wartezeit) ⛔ **LIZENZ-GATE**

- [x] **AP9.1** MSIX-Paketierung, Versionsschema, alle nativen DLLs im Paket (Verifikation, dass keine fehlt — §14.2).
- [ ] **AP9.2** Code-Signing mit dem echten Zertifikat.
- [ ] **AP9.3** Update-Prüfung nach dem in §16.4 entschiedenen Mechanismus.
- [x] **AP9.4** Silent-Install-Parameter für die Verteilung, `docs/packaging.md` und Installationsanleitung.
- [ ] **AP9.5** Vollständiger Durchlauf der Testmatrix auf einem **frischen** Windows 11 und einem Windows 10 22H2.
- [ ] **AP9.6** `docs/licensing.md` abschliessen: entweder kommerzielle Lizenz vorhanden, oder AGPLv3-Offenlegung vollzogen, oder Release **ausdrücklich als intern markiert**.

**DoD / Gate:** Installation und Update auf frischem Win 11 · unbeaufsichtigte Installation · Lizenzfrage geklärt oder Release ausdrücklich intern.

---

## 4. Querschnittliche Regeln für alle Phasen

**Jede Phase endet mit:** Testmatrix aktualisiert (`docs/test-matrix.md`, Spalten Testfall/Erwartung/Ergebnis/Datum/Build) · ADRs für jede Abweichung geschrieben · `dotnet format` gelaufen · Statuszeile in diesem Dokument und der Meilenstein in `CLAUDE.md` fortgeschrieben.

**Test-First, wo es sich lohnt** — und nur dort: `NumberNormalizer`, Settings-Serialisierung, Provisioning-XML, `ClipResolver`, Verlaufs-Aufbewahrung, Architekturtest. SIP, Audio und Windows-Integration sind nicht sinnvoll unit-testbar und gehören in die manuelle Matrix.

**Commits** klein, thematisch, mit Meilenstein-Präfix (`M3: Weiterleitung begleitet`).

**Nie gegen Kundentenants testen.** Nur der Test-Trunk. Bei doppelter Registrierung mit einem Tischtelefon bewusst sauber halten (§14.11).

---

## 5. Risikoregister

| # | Risiko | Wirkung | Vorsorge |
|---|---|---|---|
| R1 | Native DLL-Kette lädt unter WinUI 3 / MSIX nicht | **Projektstopp** | P2 als Gate mit Doppeltest, Rückfallebene unpackaged, Abbruchkriterien pro Arbeitspaket. Konkretes Bruchmuster ist bekannt: Plugin-DLLs landen nicht im Paket (§6.3) |
| R1b | GitLab-NuGet-Registry nicht anonym erreichbar | P2 verzögert | Weg C (Prebuilt-ZIP) ist verifiziert und wird zum Standard, sobald AP0.3 die Registry nicht bestätigt |
| R2 | AGPL-Lizenz nicht lösbar, kommerzielle Lizenz zu teuer | Kein Kundenrelease | Anfrage in P0 über das Belledonne-Developer-Formular starten (§6.4), `docs/licensing.md` von Anfang an führen. **Kein billiger technischer Ausweg:** der einzige Alternativstack ohne Copyleft hat kein AEC (§6.5) — die Frage muss kaufmännisch geklärt werden |
| R3 | Test-Trunk-Zugänge fehlen | P2 aufwärts blockiert | In P0 anfordern, höchste Priorität in §2 |
| R4 | SDK-API weicht von §6 ab | Nacharbeit in P3–P6 | AP2.2 verifiziert **alle** Namen vor dem ersten Servicecode, nicht nur die gerade gebrauchten |
| R5 | Signaturzertifikat nicht rechtzeitig da | P9 verschiebt sich | In P0 anstossen, Beschaffung dauert Wochen |
| R6 | Entra-Consent für Graph verzögert | P6 fällt auf COM zurück | COM-Pfad als Rückfallebene sauber implementieren, Graph nachziehen |
| R7 | Nichtfunktionale Ziele (Speicher, Kaltstart) verletzt | Nacharbeit | Erste Messung schon in P7, nicht erst in P9 |
| R8 | Iterate im UI-Thread blockiert die App | Einfrieren im Feld | Regel in P3 als Code-Kommentar und Review-Punkt; jeder Callback wird beim Schreiben darauf geprüft |

---

## 6. Recherche-Ergebnis zur SDK-Beschaffung

Recherchiert am 04.09.2026, klar getrennt in **belegt** und **vor Ort zu prüfen**. Die Befunde sind in `NIPP-BUILD.md` Rev. 2 eingearbeitet (Protokoll dort in §19); dieser Abschnitt bleibt als Begründung und Beweislage stehen.

### 6.1 Version

Aktueller stabiler Tag ist **5.5.18, Tag-Datum 03.09.2026**. Danach folgt nur `5.6.0-alpha`. Rev. 1 der Spezifikation nannte 5.5.0 (25.05.2026) und war damit vier Monate hinterher.

5.5.0 war ein Umbau-Release (Submodule eingeklappt → Fresh-Clone empfohlen; RNNoise, HIDAPI/Jabra-Support, **AEC3 statt AECM**, ISAC und iLBC entfernt); 5.5.1–5.5.18 sind Patch-Tags ohne eigene Notes. **Vorgabe: gegen 5.5.18 bauen, nicht gegen 5.5.0.** Der GitHub-Repo ist nur ein Mirror, `releases` ist dort leer — Änderungen stehen ausschliesslich im `CHANGELOG.md`, die Wahrheit liegt auf GitLab.

Nebenbefund für §9.4: AEC3 ist die Echounterdrückung ab 5.5.0, RNNoise die Rauschunterdrückung — beide Annahmen der Spezifikation bestätigt.

### 6.2 Beschaffungswege — der dritte ist der einzige verifizierte

| Weg | Stand | Bewertung |
|---|---|---|
| **A — NuGet aus der GitLab-Registry** | Paketname **`LinphoneSDK.Windows`**, Feed `https://gitlab.linphone.org/api/v4/projects/411/packages/nuget/index.json`, **projectId = 411** (im Linphone-Wiki dokumentiert) | Erreichbarkeit **nicht verifizierbar** — `gitlab.linphone.org:443` war aus der Recherche-Umgebung nicht erreichbar. Ob der Feed anonym lesbar ist oder einen Deploy-Token braucht, ist offen. **In AP0.3 als Erstes von hier aus prüfen.** |
| **C — offizielles Prebuilt-ZIP (neu)** | `linphone-sdk-win64-5.5.18.zip`, 313 MB, 03.09.2026, unter `https://download.linphone.org/releases/windows/sdk/`. Inhalt end-to-end verifiziert: `share/linphonecs/LinphoneWrapper.cs`, `bin/liblinphone.dll`, `mediastreamer2.dll`, `belle-sip.dll`, `lib/mediastreamer/plugins/libmswasapi.dll`, `libmswebrtc.dll`, `libmsopenh264.dll` | **Der einzige nachweislich funktionierende Weg.** Nur win64 — kein win32-, kein arm64-ZIP. Passt zu „x64 only". Empfehlung: **Weg C als Standard für P2**, damit das Gate nicht an einer Registry-Frage hängt; Weg A nur, wenn er sich in AP0.3 als anonym erreichbar erweist. |
| **B — Selbstbau** | `--preset=windows-sdk` **gilt weiterhin** (in `CMakePresets.json` @ 5.5.18 geprüft). Weitere Presets: `windows-64bits`, `windows-ninja-sdk`, `windows-store-sdk`, `uwp-sdk`. Voraussetzungen bestätigt: VS 17 2022, CMake ≥ 3.22, Python ≥ 3.6 + pystache + six, yasm, nasm, doxygen, MSYS2 (PATH-Reihenfolge `mingw<N>\bin`, `C:\msys64\`, `usr\bin`), 7-Zip. AV1 (default AN) verlangt zusätzlich Meson/Ninja/Perl | Rückfallebene, ~1 Tag Einrichtung. Nur nötig, wenn ein Codec fehlt. Die Windows-Presets setzen `ENABLE_WINDOWS_TOOLS_CHECK=OFF` — die MSYS2-Tools werden also **nicht** automatisch installiert. `ENABLE_CSHARP_WRAPPER=ON` ist in den Windows-Presets gesetzt. |

`LinphoneSDK.Dotnet` existiert nur als **Build-Ziel** in `cmake/NuGet/README.md` (für MAUI/Multi-Plattform); ein veröffentlichtes Paket dieses Namens war nicht auffindbar. Das Paket `LinphoneSDK` auf nuget.org ist wie in der Spezifikation vermerkt deprecated (3.12.0.273, 2017) mit eigenem Verweis auf die Belledonne-Registry.

**Begriffsklärung zu §5 der Spezifikation:** „win32-Desktop" dort meint die Win32-Desktop-Variante im Gegensatz zu UWP, nicht 32-Bit. Die richtige Datei heisst `win64`. UWP-x64 hat kein OpenH264 und kein Lime X3DH und ist hier ohnehin falsch. Der Framework-Moniker der Desktop-Variante ist in NuGet-README (`win`) und Wiki (`netcore45`) widersprüchlich dokumentiert — beim Einbinden prüfen, welcher greift.

### 6.3 Konsequenz für AP2.1 — die Plugin-DLLs sind die Falle

Das ZIP legt die Mediastreamer-Plugins unter `lib/mediastreamer/plugins/` ab, nicht neben die Haupt-DLLs. Genau das ist der erwartbare MSIX-Bruch: die Plugins landen nicht im Paket-Root, und ohne `libmswasapi.dll` gibt es kein Audio unter Windows — mit einer Fehlermeldung, die nichts darüber sagt. Belegt sind die Muster in der `System.DllNotFoundException`-Sektion des NuGet-README des SDK sowie generisch `microsoft/WindowsAppSDK#2413` (DLL wird nicht kopiert).

**Vorgabe für das Kopier-Skript in AP2.1:** die Plugin-Verzeichnisstruktur **relativ erhalten**, nicht flach kopieren, und im MSIX ein `runtimes/win-x64/native`-taugliches Layout herstellen. Nach dem Kopieren mit `dumpbin /dependents` gegenprüfen, dass die Kette vollständig ist — bevor irgendetwas gestartet wird.

### 6.4 Lizenz — unverändert, Kontaktweg jetzt bekannt

Dual AGPLv3 oder proprietär gegen Gebühr; für ausgelieferte Client-Anwendungen greifen faktisch die Copyleft-Bedingungen. Eine öffentliche Preisseite gibt es nicht. Kontakt: `https://www.linphone.org/en/contact/`, Developer-Formular `https://linphone.typeform.com/to/kCg6gOWV`, Belledonne Communications SARL, Grenoble, +33 9 52 63 65 05.

Zwei Build-Schalter sind für die Lizenzlage relevant und stehen bereits richtig: `ENABLE_GPL_THIRD_PARTIES` default **NO** und `ENABLE_NON_FREE_FEATURES` (AMR/H264) default **OFF**. Beim Selbstbau nicht versehentlich einschalten — sonst kommen weitere Lizenzbedingungen hinzu. Das deckt sich mit der Vorgabe aus §3, G.729 draussen zu lassen.

### 6.5 Alternativstacks — nur als Notoption zu R1/R2, keine Empfehlung

| Stack | Lizenz | .NET | Audio/Echo | Urteil |
|---|---|---|---|---|
| **PJSIP / pjsua2** | GPLv2+ **oder** kommerziell (licensing@teluu.com) | SWIG-Wrapper, z. B. `IctBaden.pjsua2` (MIT-Wrapper über GPL-Core) | eigene AEC plus WebRTC-AEC, ausgereift | Der realistische Ersatz. Löst das Copyleft-Problem aber **nicht** — nur die kommerzielle Lizenz tut das, dann bei einem anderen Anbieter. |
| **SIPSorcery** | BSD-3 mit BDS-Zusatzklausel (Closed-Source erlaubt, geografische Einschränkung) | .NET-native, kein Wrapper | Audio nur über `SIPSorceryMedia.Windows` (NAudio), **keine echte Echounterdrückung**, kein Codec-Ökosystem | Löst die Lizenzfrage, kostet dafür deutlich mehr Eigenbau. Für ein produktives Softphone mit AEC-Anspruch heute nicht ausreichend. |

**Lesart:** Wenn R2 (Lizenz) hart wird, ist der Wechsel des Stacks keine billige Ausweichbewegung — die einzige Option ohne Copyleft (SIPSorcery) hat kein AEC. Das stützt die Priorität, die Lizenzfrage früh zu klären, statt sie technisch umgehen zu wollen.

---

## 7. Arbeitsteilung mit Subagenten

Wo sich Arbeit sauber abgrenzen lässt, wird sie parallel vergeben — sonst nicht, weil Koordination teurer ist als der Zeitgewinn.

| Phase | Sinnvoll parallel | Nicht parallel |
|---|---|---|
| P0 | SDK-/Lizenzrecherche | — |
| P2 | Wrapper-API-Verifikation (AP2.2) als eigener Auftrag, während die DLL-Ladbarkeit untersucht wird | Registrierung/Audio — braucht ein Ohr am Gerät |
| P4/P5 | `NumberNormalizer` samt Tests; `SettingsSchema` aus den Tabellen in §9 ableiten | Gesprächsaktionen gegen die PBX |
| P5/P6 | Einstellungen und Kontakte/Verlauf sind unabhängig und laufen nebeneinander | — |
| P6 | SQLite-Verlauf; Graph-Anbindung | BLF — braucht die PBX |
| P8 | XML-Schema und Generator | — |

Alles, was gegen die Test-PBX läuft oder Audio hörbar prüft, bleibt sequenziell in einer Hand.

---

## 8. Fortschritt

### 04.09.2026 — P0 und P1 (M0) abgeschlossen

**Ergebnis.** Solution mit fünf Projekten, Build grün ohne Warnungen, Architekturtest greift, App startet mit Mica-Optik und Titelleiste. `dotnet build` und `dotnet test` laufen über `.\build.ps1`.

| Nachweis | Ergebnis |
|---|---|
| `build.ps1 build Nipp.sln -c Debug` | grün, **0 Warnungen, 0 Fehler** |
| `build.ps1 test Nipp.sln` | 3 von 3 Architekturtests bestanden |
| Bitness der EXE | PE-Header `Machine 0x8664` = **echtes x64**, nicht nur der Pfad |
| App startet | Fenster mit Titel `nipp`, Log schreibt `nipp startet (Version 0.1.0.0)` |

**Fünf Dinge, die dabei anders kamen als geplant.**

1. **Die Maschine ist ARM64**, das Produkt x64 only. Entschieden: hier bauen, M1 und die Messungen auf echter x64-Hardware verifizieren (ADR-001). Das x64-SDK liegt unter `C:\Program Files\dotnet\x64` und ist vom `dotnet` im PATH nicht sichtbar — deshalb gibt es `build.ps1`, das den richtigen Host auflöst.
2. **Der NuGet-Feed ist erreichbar.** `LinphoneSDK.Windows` **5.5.18 stable** liegt anonym abrufbar im Feed (projectId 411). Weg A wird Standard statt Weg C (ADR-002).
3. **Windows App SDK 2.4.0 statt 1.x** (ADR-003): die 1.x-Linie fällt am **09.09.2026** aus dem Support, und das Windows-10-Minimum ist in beiden Linien identisch (1809). Kostet keine Reichweite, vermeidet einen Start auf abgekündigter Basis. Drei neue Testfälle T35–T37 deckeln das Restrisiko.
4. **Der Fallstrick §14.4 trat sofort ein.** Das WinUI-Template leitet den `RuntimeIdentifier` aus der Prozessarchitektur der *Baumaschine* ab und die Solution stand auf `Debug|x86` — der erste Build legte `Nipp.App` unter `bin\x86\` ab. Beides korrigiert und in der csproj kommentiert, damit es nicht zurückkommt.
5. **Packaged (MSIX) startet auf dieser Maschine nicht**, unpackaged läuft (ADR-004). Der Fehler (`REGDB_E_CLASSNOTREG`) tritt **ohne** jedes SDK auf, ist also unabhängig von der nativen DLL-Kette. Für AP2.4 heisst das: erst diesen Fehler auflösen, dann die DLLs dazunehmen — sonst überlagern sich zwei Ursachen.

**Zwei Beobachtungen ohne Handlungsbedarf, aber notiert.** Der Leerlauf-Speicher lag beim Startversuch bei 204 MB und damit über dem Zielwert von 180 MB aus §2 — Debug-Build unter Emulation, also nicht aussagekräftig (die Messung gehört nach AP7.8 auf echte Hardware). Und `Nipp.Core.Tests` enthält noch keine Tests; M0 verlangt keine ausser dem Architekturtest, der erste echte kommt mit dem `NumberNormalizer` in AP4.1.

**Nächster Schritt: P2 / M1.** AP2.1 und AP2.2 (SDK einbinden, Wrapper-API gegen §6 verifizieren) gehen ohne Zugangsdaten. **AP2.3 ist blockiert**, solange der Test-Trunk fehlt — und damit das Gate selbst.

### 04.09.2026 — AP2.1, AP2.2 und AP2.5 erledigt: **die DLL-Kette lädt**

Der Spike unter `spike/SdkProbe` läuft mit Exit-Code 0 durch: `Factory.Instance` da, Core gestartet (`GlobalState = On`), 100 × `Iterate()` gedreht, Ereignisse empfangen, `Core.Stop()` sauber.

**Damit ist der gefährlichste Teil von R1 entschärft** — die native Kette aus 32 DLLs plus 2 Plugins lässt sich aus .NET 8 laden, und WASAPI findet die Audiogeräte. Beides sogar unter x64-Emulation auf ARM64. Was noch offen ist: dasselbe aus der WinUI-App heraus, packaged und unpackaged (AP2.4) — und da steht der Befund aus ADR-004 im Weg.

**Der Beschaffungsweg musste gewechselt werden.** Der erste echte `dotnet restore` gegen den GitLab-Feed scheiterte nach 1,76 Minuten an Timeouts und einem HTTP 500 — Risiko R1b ist eingetreten. Weg C (Prebuilt-ZIP) ist jetzt der Standard (ADR-005, löst ADR-002 ab): 298,9 MB in 42 Sekunden, SHA256 in `sdk-setup.md`.

**Sechs Abweichungen der Wrapper-API von §6**, alle in `sdk-api-notes.md` mit Signaturen:

| §6 nimmt an | Tatsächlich |
|---|---|
| `Core.AddListener(...)` | `Core.Listener` — **ein** Listener mit 97 Delegate-Properties |
| `Core.AcceptCall(...)` | `Call.Accept()` — Anrufsteuerung sitzt am `Call` |
| `Core.EchoCancellerEnabled` | `Core.EchoCancellationEnabled` |
| `Core.Rfc2833DtmfsEnabled` | `Core.UseRfc2833ForDtmf` **und** `UseInfoForDtmf` |
| `MediaEncryption.Srtp` | `MediaEncryption.SRTP` — Grossbuchstaben |
| `Factory.Version` | `Core.Version`, statisch |

Die wichtigste ist die erste: **es gibt nur einen Listener pro Core.** Das macht `SipEventBridge` aus §6 nicht optional, sondern notwendig — wer die Delegates anderswo überschreibt, hängt die Bridge stillschweigend ab. Gehört in AP3.4 als Kommentar in den Code.

**Zwei Funde, die die Spezifikation nicht kennt:**

1. **Das SDK braucht Ressourcendateien, nicht nur DLLs.** Der erste Start starb an `bctbx-fatal: Unable to load VCARD grammar` — `belr` lädt acht Grammatiken zur Laufzeit aus `share/belr/grammars/`. Die Meldung sagt nicht, dass eine *Datei* fehlt. §14.2 kennt nur die DLL-Kette und ist unvollständig.
2. **`Factory.MspluginsDir` löst voraussichtlich das MSIX-Problem.** Der Plugin-Pfad ist explizit setzbar — die Plugins müssen also nicht neben die EXE, wenn man dem SDK sagt, wo sie liegen. Im Spike verifiziert. Das ist der Ansatz, den AP2.4 verfolgen sollte.

**Drei Korrekturen an §9, die vor P5 einzuarbeiten sind** (Details in `sdk-api-notes.md`): G.722 ist **aus** statt aktiv, die festen Payload-Type-Nummern aus §9.5 (Opus 96, speex 102) sind **nicht setzbar** — dynamische PTs werden erst im SDP verhandelt —, und G.729 ist gar nicht im Build, die Zeile kann entfallen. Ausserdem arbeitet das SDK bei den Lautstärken mit **Dezibel**, nicht mit der 0–100-Skala aus §9.4; `SettingsSchema` braucht dort eine dokumentierte Umrechnung.

**Für AP5.6 vorgemerkt:** die Gerätewahl muss `ExtendedAudioDevices` verwenden. `AudioDevices` liefert auf dieser Hardware **0** Einträge, weil es nur das erste Gerät je Typ zurückgibt und alle Geräte `Type = Unknown` melden — eine leere Liste ohne Fehlermeldung.

**Es bleibt bei AP2.3 als Gate.** Registrierung und Audio in beide Richtungen brauchen den Test-Trunk. Ohne die Zugangsdaten ist M1 nicht abschliessbar.

### 04.09.2026 — AP2.3 bestanden: **das M1-Gate ist passiert**

Gegen den Test-Trunk `pbx.example.ch` über UDP:

| Akzeptanzkriterium aus §12/M1 | Ergebnis |
|---|---|
| REGISTER 200 OK im Log | **ja** — `Progress` dann `Ok (Registration successful)` |
| Ein Gespräch mit hörbarem Audio in beide Richtungen | **ja** — ausgehend zu einer Mobilnummer, `StreamsRunning`, von Dominic bestätigt |
| `docs/sdk-api-notes.md` gefüllt | ja, mit sechs Abweichungen und sieben Spike-Befunden |
| Packaging-Entscheidung als ADR | **noch nicht** — ADR-004 hält den Zwischenstand, AP2.4 ist offen |

Gemessen im Gespräch: RTT 23 ms, Paketverlust 0,00 %, Download 80 kbit/s, Codec PCMU, Verschlüsselung None.

**Damit ist das Projekt aus dem Risiko heraus, an dem es hätte scheitern können.** R1 ist erledigt: die native Kette lädt, registriert und telefoniert. Offen bleibt in P2 nur noch AP2.4 — dieselbe Ladbarkeit aus der WinUI-App, packaged und unpackaged.

**Drei Befunde brauchen eine Entscheidung, bevor P4 und P5 gebaut werden** (vollständig in ADR-006):

1. **Die Verschlüsselung war `None`, obwohl SRTP gesetzt war.** Die Gegenstelle bot kein SRTP, und ohne Mandatory-Flag fiel das SDK still auf unverschlüsselt zurück. §9.3 gibt aber „SRTP **und** erzwingen: ein" vor — mit dieser Vorgabe wäre der Anruf hart gescheitert. **Frage an Dominic:** bietet `pbx.example.ch` SRTP intern an, und ist der Klartextweg nur nach draussen unvermeidlich? Bis das geklärt ist, darf „erzwingen" nicht als Standard ausgeliefert werden (§14.7).
2. **Die Echounterdrückung war im Gespräch abgeschaltet.** Bei PCMU (8 kHz) meldet der Canceller `does not support sampling rate 8000Hz, so it has been disabled` — obwohl `EchoCancellationEnabled` auf `true` stand. Bei externen Gesprächen also durchgehend keine AEC. Betrifft AP5.7: entweder ein Filter, der 8 kHz beherrscht, oder die Oberfläche zeigt den tatsächlichen statt des gewünschten Zustands.
3. **Jitter 499 ms, ~35 verworfene RTP-Pakete, WASAPI-Pufferfehler.** Das Gerät kann kein 8 kHz und bleibt bei 48 kHz Stereo, es wird durchgehend resampled. Deutet auf die Emulation (ADR-001), nicht auf den Code — hörbar war das Gespräch. **Als T38 auf echter x64-Hardware gegenzuprüfen, bevor daraus eine Fehlersuche wird.**

Zwei eigene Fehler im Spike gleich behoben: `ClearAllAuthInfo()` lief vor `Stop()` und entzog dem abschliessenden REGISTER die Zugangsdaten (`Failed (Unauthorized)` beim Herunterfahren), und `VideoCaptureEnabled`/`VideoDisplayEnabled` allein genügen nicht — die `VideoActivationPolicy` muss mit, sonst bietet das SDK trotzdem Video an und meldet selbst einen möglichen API-Fehlgebrauch.

### 04.09.2026 — AP2.4 bestanden: **M1 ist vollständig abgeschlossen**

Der Doppeltest aus §4 ist durch. Beide Varianten laden die native Kette aus der WinUI-App heraus:

| Variante | Ergebnis |
|---|---|
| unpackaged (EXE direkt) | `Linphone SDK geladen: Version 5.5.0, 8 Grammatiken, 2 Plugins` |
| packaged (MSIX, Start über Paketidentität) | dieselbe Zeile, Fenster erscheint |

**Entscheidung: MSIX (packaged)** — ADR-008. Damit bleiben Toasts, Protokoll-Handler und Autostart in P7 auf dem einfachen Weg; die befürchteten +2 bis 3 Tage für eine eigene Toast-Lösung entfallen.

Alle vier Akzeptanzkriterien von M1 aus §12 sind erfüllt:

- [x] REGISTER 200 OK im Log
- [x] Ein Gespräch mit hörbarem Audio in beide Richtungen
- [x] `docs/sdk-api-notes.md` gefüllt — sechs API-Abweichungen, acht Spike-Befunde
- [x] Packaging-Entscheidung getroffen und als ADR festgehalten

**Zwei eigene Fehldiagnosen, die dabei aufgeflogen sind.** Der `REGDB_E_CLASSNOTREG`-Fehler kam nicht vom Versionsgeflecht im NuGet, wie ich in ADR-004 vermutet hatte — Manifest und Layout waren die ganze Zeit korrekt. Er kam davon, dass `dotnet run` die App ohne wirksame Paketidentität startete. Und der Kopierschritt in `Nipp.Core` füllte deren Ausgabeverzeichnis statt das der App; der Windows-Ladepfad geht vom Verzeichnis der EXE aus. Beides kostete zusammen etwa eine Stunde und steht jetzt dokumentiert, damit es nicht wiederkommt.

**Was M1 unter dem Strich ergeben hat:** das grösste Projektrisiko (R1) ist erledigt. Das SDK lässt sich beschaffen, einbinden, laden, registrieren und damit telefonieren — packaged wie unpackaged. Der Weg dorthin hat sechs API-Abweichungen von §6, drei Korrekturen an §9 und zwei Lücken in §14 zutage gefördert, die alle dokumentiert sind. Die Spezifikation steht auf Rev. 3.

**Nächster Schritt: P3 / M2 — der Telefonie-Kern.** Keine offenen Blocker; die Test-PBX steht bereit.

### 04.09.2026 — M2 gebaut, vier Akzeptanzpunkte brauchen einen Menschen

Alle neun Arbeitspakete von P3 sind umgesetzt. Aus dem Log der laufenden App:

```
Linphone SDK geladen: Version 5.5.0, 8 Grammatiken, 2 Plugins
Codec G722 true
Core gestartet, GlobalState = On
Ereignisschleife gestartet, Intervall 20 ms
Überwachung von Netzwerk und Energiezustand gestartet
Konto wird angemeldet: 151bv2@pbx.example.ch über Udp
Registrierung InProgress -> Registered
```

**nipp registriert sich selbst** — nicht mehr nur der Spike.

Was dazu entstanden ist: `ISipService` und `SipService` (Konto, Anrufe, Halten, Stumm, DTMF, Weiterleiten in beiden Varianten, Aufnahme), `SipEventBridge` als einziger Setzer der Listener-Delegates, `SipPumpHost` mit dem 20-ms-Takt am App-Lebenszyklus, `ConnectivityMonitor` für Netzwerkwechsel und Standby, `SipErrorCatalog` mit 21 Tests, `DialerViewModel` und eine Minimal-Oberfläche.

**Der Überlaufzähler hat sofort etwas gefunden:** ein Überlauf beim Start, 62 ms statt 20 ms, im ersten Registrierungs-Callback. Danach keiner mehr — erklärbar durch den ersten Aufbau der Oberfläche. Genau dafür ist der Zähler da (§14.1); wächst er im Dauerbetrieb, steckt eine blockierende Operation in einem Callback.

**Ein Provisorium, das wieder verschwinden muss:** `DevAccountSource` liest das Konto aus `test-trunk.json`, weil es die Einstellungen aus §9 noch nicht gibt. Die Datei liegt im Klartext — §11 verlangt DPAPI und HA1 statt Passwort. Die Klasse wird in P5 **gelöscht**, nicht erweitert; das steht auch in ihrer Dokumentation.

**Der Linphone-Feed ist aus `nuget.config` entfernt.** ADR-005 hatte ihn drin gelassen, weil ja nichts mehr von ihm abhängt. Das war falsch: NuGet fragt jede Quelle nach Sicherheitsdaten, und eine nicht erreichbare Quelle erzeugt NU1900 — mit `TreatWarningsAsErrors` bricht damit der ganze Build, obwohl kein Paket von dort kommt.

**Was für die M2-Akzeptanz noch fehlt, und zwar mit einem Menschen am Gerät:**

- [ ] Eingehender Anruf in der App annehmen (der Weg ist im Code, ungeprüft)
- [ ] Neuregistrierung nach Netzwerkwechsel WLAN→VPN (T12)
- [ ] Standby und Resume (T13)
- [ ] 20 Anrufe in Folge ohne Absturz

Der Speicherbedarf lag bei 247 MB gegen den Zielwert von 180 MB aus §2 — Debug-Build unter Emulation, also nicht aussagekräftig. Gehört nach AP7.8 auf echte Hardware.

### 04.09.2026 — P4 (M3) gebaut: Wählen und Gespräch

**AP4.1 — `NumberNormalizer`, Test-First.** 30 Tests, alle Fälle aus §13 wörtlich, beim ersten Durchlauf grün. Die drei Schreibweisen derselben Nummer (`044 512 84 30`, `+41445128430`, `0041445128430`) ergeben dasselbe Ergebnis; interne Ziele und **Notrufnummern** bleiben unverändert — 112 darf unter keinen Umständen zu +41112 werden. Zusätzlich abgedeckt: Trennzeichen, SIP-Adressen, benannte Ziele, konfigurierbares Länderpräfix, Idempotenz.

**AP4.2 und AP4.9 — `Keypad`.** 3×4 mit Buchstabenzeile, mit Bedienhilfe-Namen. Bewusst ohne eigenen Zustand: das Control meldet nur die Taste, was damit geschieht entscheidet die Seite. Dadurch dient es sowohl der Wählseite als auch dem DTMF-Feld der Gesprächsansicht (§8.2).

**AP4.4 bis AP4.8 — `ActiveCallPage` und `ActiveCallViewModel`.** Gegenstelle, tickende Dauer, Chips für Codec und Verschlüsselung, Stumm, Halten, DTMF, Makeln zwischen zwei Gesprächen, Weiterleiten in beiden Varianten sichtbar getrennt, Aufnahme, Qualitätspanel.

Zwei Punkte, die §8 ausdrücklich verlangt und die hier keine Nebensache sind:

- **Der Aufnahmeindikator** steht als `InfoBar` ganz oben, nicht klein am Rand: „Dieses Gespräch wird aufgezeichnet. Die Gegenseite muss davon wissen." §8.2 verlangt ihn, weil das Mitschneiden ohne Kenntnis der Gegenseite in der Schweiz strafbar ist.
- **Die Verschlüsselung steht als Text da**, nicht nur als Farbe (§8.4): „unverschlüsselt" wird ausgeschrieben, nicht beschönigt. Gegen die aktuelle Anlage ist das der Regelfall (ADR-007).

**AP4.3 (Live-Auflösung während der Eingabe) ist verschoben** — sie braucht Kontakte und Verlauf, die es erst in P6 gibt. Was schon geht: die Wählseite zeigt unter dem Eingabefeld, **was tatsächlich gewählt wird**, sobald die Normalisierung etwas verändert, und markiert interne Ziele als solche.

**Ein Fehler, den erst der laufende Betrieb gezeigt hat.** Im Log erschien `Registrierung InProgress`, bevor das Konto überhaupt geladen war — das SDK stellt gespeicherte Konten aus `linphonerc` selbst wieder her. Ohne Aufräumen wäre bei jedem Start ein weiteres Konto derselben Identität dazugekommen; es lagen bereits **zwei** aus früheren Läufen da. Zwei Korrekturen:

1. Vor dem Anlegen wird ein Konto derselben Identität entfernt — samt `AuthInfo`, wie es §9.1 für das Löschen ohnehin verlangt („sonst bleiben Zugangsdaten verwaist liegen").
2. Der angezeigte Registrierungszustand kommt jetzt vom **Standardkonto**, nicht vom letzten Ereignis. Die Abmeldung eines ersetzten Kontos trifft sonst *nach* der neuen Anmeldung ein, und nipp zeigt „Abgemeldet", obwohl es registriert ist.

64 Tests grün. **Was noch fehlt, ist die Abnahme gegen die PBX** (§12, M3): Weiterleiten in beiden Varianten, Makeln zwischen zwei Gesprächen, und eine Aufnahme, die sich abspielen lässt.

### 04.09.2026 — Abnahme M2/M3: **M3 vollständig bestanden**

Dominic hat die sieben Punkte aus `docs/abnahme-m2-m3.md` durchgespielt. Fünf Runden, vier echte Fehler gefunden, alle behoben. Danach: **alle geprüften Punkte bestanden.**

| Akzeptanzkriterium M3 (§12) | Ergebnis |
|---|---|
| Blindes Weiterleiten gegen die Test-PBX | ✅ |
| Begleitetes Weiterleiten | ✅ Log: zweiter Anruf, erstes gehalten, `weitergeleitet an 151 (begleitet)` |
| Zwei Gespräche makeln | ✅ |
| Aufnahme mit abspielbarem WAV und sichtbarem Indikator | ✅ 110'400 Bytes, RIFF/WAVE, 8 kHz mono, 6,9 s |

**M2 ist nicht ganz durch:** Neuregistrierung nach Netzwerkwechsel (T12) und 20 Anrufe in Folge (T34) wurden auf Wunsch nicht getestet. Beide sind gebaut und protokollieren sichtbar — das sind offene **Abnahmepunkte**, keine offene Arbeit.

**Was die Abnahme gefunden hat — vier Fehler, die kein Test und kein Log gezeigt hätten:**

1. **Gesprächsansicht blieb leer**, wenn ein Anruf schon lief: das ViewModel entstand erst beim Öffnen der Seite und hatte die Ereignisse nie gesehen.
2. **Ansicht wurde leer beim Drücken von „Halten":** die Gesprächsliste war two-way an die Auswahl gebunden, und weil `CallInfo` unveränderlich ist, tauscht jeder Zustandswechsel die Instanz aus — die Liste verlor ihre Auswahl und schrieb `null` zurück. Das Gespräch lief die ganze Zeit weiter.
3. **Die Aufnahme schrieb keine Datei**, obwohl Indikator und Log Erfolg meldeten. `Call.Params` sind read-only; der Pfad muss beim Aufbau des Anrufs feststehen. Zwei Anläufe nötig — der erste Fix schrieb auf eine Momentaufnahme, der zweite auf ein read-only-Property. **Das ist eine Abweichung von §6, die dort nicht stehen kann: sie liegt am SDK.**
4. **Der zweite Anruf war unmöglich:** `CanDial` verlangte, dass gar kein Gespräch läuft. Damit waren Makeln und begleitetes Weiterleiten unerreichbar, obwohl beide fertig gebaut waren. §8.2 erlaubt zwei.

Dazu zwei Bedienmängel, die dabei auffielen und behoben sind: der Anrufen-Knopf brach still ab statt seinen Zustand zu zeigen, und „Begleitet übergeben" war ausgegraut, ohne zu sagen warum.

**Und ein Eigentor beim Bauen:** zweimal eine App erzeugt, die wortlos abstürzt, weil unpackaged inkrementell gebaut wurde. `-t:Rebuild` löst es reproduzierbar; die Ursache ist nicht abschliessend geklärt und steht so in `packaging.md`. `build.ps1` warnt jetzt.

**Bilanz:** Die vier Fehler waren alle in der Oberfläche oder an der Schnittstelle zum SDK — kein einziger im Telefonie-Kern. Die 64 automatischen Tests waren durchgehend grün und hätten keinen davon gefunden. Das ist das Argument für die manuelle Testmatrix aus §13.

### 05.09.2026 — P6 bis P9: Kontakte, Windows-Integration, Provisionierung, Paket

Alle Phasen bis auf die Punkte, die von aussen abhängen (Zertifikat, Lizenz, Messungen auf echter Hardware). **186 Tests grün**, Build ohne Warnungen, die App startet und registriert sich.

| Phase | Stand |
|---|---|
| **P6 · Kontakte, Verlauf, Voicemail** | fertig |
| **P7 · Windows-Integration** | fertig ausser AP7.8 (Messungen gehören auf x64-Hardware, ADR-001) |
| **P8 · Provisionierung und Diagnose** | fertig |
| **P9 · Paket** | AP9.1 und AP9.4 fertig; AP9.2 wartet auf das Zertifikat, AP9.5 auf frische Testmaschinen, AP9.6 auf die Lizenzentscheidung |

**Outlook liest im echten Betrieb 137 von 151 Einträgen** (die übrigen haben keine wählbare Nummer). Späte COM-Bindung auf eigenem STA-Thread, 30 Sekunden Grenze, zwölf Stunden Zwischenspeicher. Graph wurde verworfen (ADR-009): eine Entra-Registrierung mit Consent für eine Namensauflösung am Arbeitsplatz steht in keinem Verhältnis, und kein Graph heisst kein Token, das irgendwo liegt.

**Der erste Testlauf des `ClipResolver` fand sofort einen Fehler.** `+41445128430` und `0445128430` trafen sich nicht: die nationale Null verhindert den Vergleich von hinten. Sie fällt jetzt vorher weg. Genau dafür ist der Kern eine reine Funktion.

**Zwei Fehler, die erst der echte Start zeigte** — kein Test und kein Log hätten sie gefunden:

1. **`ObservableCollection` vom falschen Thread.** Der `ContactStore` lädt im Hintergrund und löste sein Ereignis dort aus; das ViewModel änderte daraufhin seine Sammlung. Das Ergebnis war eine `COMException` **mit leerer Meldung** — im Protokoll stand `Kontakte liessen sich nicht laden:` und dahinter nichts. Behoben über einen erfassten `SynchronizationContext` (kein `DispatcherQueue`: Nipp.Core bleibt UI-frei). Zusätzlich gibt es jetzt eine Hilfsfunktion, die auch eine leere Ausnahmemeldung noch mit Typ und HRESULT beschreibt.
2. **Die Präsenz-Abonnements liefen vom Hintergrundthread ins SDK.** Das quittiert das SDK nicht mit einem Fehler, sondern mit sporadisch ausbleibenden Ereignissen — die unangenehmste Sorte. Jetzt über den Dispatcher.

**Und einer, der schon länger dalag:** der `SettingsApplier` war registriert, aber **nirgends aufgerufen**. Einstellungen wurden gespeichert und wirkten nicht auf den laufenden Core. AP5.5 war damit faktisch offen, ohne dass es aufgefallen wäre. `ISipService.ApplySettingsAsync` schliesst die Lücke — der Core verlässt die Telefonieschicht nicht, also geht der Weg über den Dienst.

**Provisionierung mit eigenem Format statt `Core.ProvisioningUri`** (ADR-010). Der Weg des SDK hätte zwei Wahrheiten ergeben: ein Profil, das direkt in die `linphonerc` schreibt, wird beim nächsten Speichern der Oberfläche überschrieben. Der Parser verbietet DTD-Verarbeitung und kennt keinen `XmlResolver` — das XML kommt von einem Webserver, XXE ist dort kein theoretischer Fall, und ein Test weist die Abwehr nach.

`nippprov` erzeugt und prüft Profile **mit demselben Parser wie die App**. Ein Profil, das `nippprov pruefen` durchgeht, geht auch in der App durch.

**Das Tastenkürzel braucht ein eigenes Nachrichtenfenster auf eigenem Thread.** `RegisterHotKey` ohne Fenster schickt `WM_HOTKEY` in die Threadschlange, und die Schleife von WinUI verwirft eine Nachricht ohne Fenster stillschweigend — das Kürzel wäre registriert und täte nichts. Beim ersten Start meldete es prompt, dass `Strg+Umschalt+A` auf dieser Maschine schon belegt ist (Windows-Fehler 1408); das steht jetzt neben dem Eingabefeld in den Einstellungen und nicht nur im Protokoll.

**`Pack-Nipp.ps1` prüft das fertige Paket**, statt sich darauf zu verlassen: es öffnet das MSIX und sieht nach, ob die sechs Kernbibliotheken und die acht `belr`-Grammatiken drin sind. Fehlt etwas, bricht es ab. §14.2 nennt diese Bruchstelle, und in AP2.4 hat sie schon einmal zugeschlagen.

**Ein README** beschreibt jetzt, was nipp kann, wie eine Entwicklungsmaschine von Null aufgesetzt wird, welche Komponente wofür da ist und wo was liegt — samt einem Abschnitt zu den Fallstricken, die in diesem Projekt tatsächlich Zeit gekostet haben.

**Aufgeräumt:** `DevAccountSource` ist weg. Er hielt Zugangsdaten im Klartext und war eine Zwischenlösung, bis es Einstellungen gab. Die gibt es seit P5.

**Was offen bleibt und nicht von hier zu entscheiden ist:**

| Punkt | Wartet auf |
|---|---|
| AP7.8 · Messungen (Kaltstart, Speicher, CPU, INVITE→Toast) | echte x64-Hardware (ADR-001) |
| AP9.2 · Code-Signing | das Zertifikat (§16.2) — **Beschaffung dauert Wochen** |
| AP9.3 · Update-Prüfung | die Entscheidung aus §16.4 |
| AP9.5 · Testmatrix auf frischem Windows | Testmaschinen |
| AP9.6 · Lizenz | die AGPL-Entscheidung vor der ersten Kundenabgabe |

### 05.09.2026 — die vier offenen Punkte aus P4 und P5

Beim Durchzählen der Kästchen kam heraus, dass P4 und P5 nie abgehakt wurden — und dass vier Punkte tatsächlich offen waren. Alle vier sind jetzt umgesetzt; **207 Tests grün**.

**AP5.6 · Hotplug.** Der Kommentar im Code behauptete, „das SDK fällt selbst auf das Standardgerät zurück, die Oberfläche muss es nur melden". Das erste war eine ungeprüfte Annahme, das zweite geschah nicht — `AudioDevicesChanged` wurde von niemandem abonniert. Jetzt wird die Gerätewahl bei jeder Änderung neu angewendet, **laufende Gespräche werden einzeln umgeschaltet** (`Call.InputAudioDevice`; die Standardgeräte des Core wirken nur auf neue Gespräche), und die Oberfläche bekommt einen fertigen Satz statt eines Codes.

**AP4.3 · Live-Auflösung.** Präfixsuche über Kontakte und Anrufliste, höchstens fünf Treffer, ab dem zweiten Zeichen. Ein Klick übernimmt die Nummer und wählt **nicht** — ein Fehlgriff in einer Liste, die beim Tippen aufspringt, wäre sonst ein Anruf bei der falschen Person.

**AP4.9 · `SettingCard`.** Damit sperrt AP8.4 jetzt **feldweise** mit Schloss und Tooltip statt auf Gruppenebene.

Der erste Versuch war ein `UserControl`, das sein eigenes `Content` an einen inneren Wirt weiterband — das stürzt beim Laden ab, weil dasselbe Element zweimal im Baum hinge. WinUI meldet dazu nur „Value does not fall within the expected range". Richtig ist ein `ContentControl` mit `ControlTemplate` in `Themes/Generic.xaml`.

**AP5.10 · Video-Schalter.** Sichtbar und ausgegraut, mit der Begründung daneben. Der Punkt war nie, Video vorzubereiten — §2 schliesst es aus. Der Punkt war, sichtbar zu machen, dass es *bewusst* fehlt.

**Zwei Funde nebenbei, beide aus derselben Familie wie die Testisolation:**

1. **Ein Outlook-Ergebnis nach der Zeitgrenze wurde weggeworfen.** Im Protokoll: `08:40:43 Outlook antwortet seit 30 s nicht — abgebrochen`, `Kontakte geladen: 0`, und zwei Sekunden später `Outlook gelesen: 137 von 151`. Der STA-Thread lief weiter und lieferte alles — nur nahm es niemand mehr entgegen, und die Liste blieb leer. Die Grenze steht jetzt bei 60 s (ein **kaltes** Outlook brauchte gemessene 32), und ein verspätetes Ergebnis wird aufgehoben und gemeldet, statt es zu verlieren.

2. **Ein Test war nicht deterministisch.** „Kein TURN-Geheimnis im Paket" wurde einmal rot und danach wieder grün: `DiagnosticsBundle` liest das echte Log-Verzeichnis mit ein, dessen Inhalt sich zwischen Läufen ändert. Das Verzeichnis ist jetzt über den Konstruktor steuerbar. Ein Test, der sporadisch rot ist, ist schlimmer als keiner — beim nächsten Mal glaubt ihm niemand.

### 05.09.2026 — Besetztlampenfeld: die Frage vor dem Test geklärt

Auf die Frage „geht BLF von internen Nebenstellen?" liess sich nicht mit Ja antworten. Der Code ist da, aber **nie gelaufen**: bei jedem Start stand `Besetztlampenfeld: 0 Nebenstellen abonniert` im Protokoll, weil nie eine Team-Nebenstelle eingetragen war.

Beim Nachsehen im Wrapper kam ein zweites, gewichtigeres Problem heraus. nipp abonniert über Linphones `FriendList`, und die sendet `Event: presence` (RFC 3856, SIMPLE). Ein klassisches Besetztlampenfeld verwendet `Event: dialog` (RFC 4235). Das ist nicht dasselbe: `presence` sagt, was ein Client über sich selbst veröffentlicht, `dialog` sagt, was die Anlage über eine Nebenstelle weiss — und ein Tischtelefon veröffentlicht in der Regel keine Präsenz.

**Eine Folge steht ohne Test fest:** `ConsolidatedPresence` kennt nur `Online`, `Busy`, `DoNotDisturb` und `Offline`. Der interessanteste Zustand — **„klingelt"** — kann über diesen Weg nie eintreffen. `PresenceStatus.Ringing` ist damit vorerst toter Code (er bleibt stehen, weil der dialog-Weg ihn liefern würde), und der Zweig `"Away"` in `MapPresence` trifft ebenfalls nie zu.

**Gebaut, damit die Messung eindeutig wird:** `OnSubscriptionStateChanged` und `OnNotifyReceived` sind verdrahtet und protokollieren, was die Anlage antwortet — mit einem Satz dazu, was daraus folgt (§15). Bisher stand im Protokoll nur, *dass* abonniert wurde.

`tools\Test-Blf.ps1` liest den letzten Programmstart und deutet das Ergebnis. Verfahren und die möglichen Befunde in `docs/blf-pruefung.md`, dort ist auch Platz für das Ergebnis.

**Offen: der Test selbst.** Er braucht eine echte Nebenstelle und macht Dominic in wenigen Minuten. Fällt er auf `dialog` heraus, ist der Umbau etwa ein Tagewerk — `Core.Subscribe` mit `"dialog"` und ein eigener Auswerter für die NOTIFY-Bodies; das SDK parst nur presence. Vorher gehört eine ADR geschrieben.

### 05.09.2026, abends — Besetztlampenfeld funktioniert

Die Annahme vom Morgen war falsch: `pbx.example.ch` **kann `presence`**. Es antwortet auf den `SUBSCRIBE` mit `Subscription-State: active` und bildet den Leitungszustand der Nebenstellen auf Präsenz ab — frei, im Gespräch, offline kommen an, alle zehn eingetragenen Nebenstellen. Gut, dass gemessen wurde, statt einen Tag in den dialog-Weg zu stecken.

Dass die Lampen trotzdem auf „unbekannt" standen, lag an drei Fehlern in nipp — und sie waren erst zu finden, nachdem zwei weitere behoben waren:

**Vorbedingung 1 · die Einstellung „Protokollierung" war tot.** Aus/Info/Debug wurde gespeichert und nirgends gelesen; Serilog stand fest auf Information. Jetzt ein `LoggingLevelSwitch`, beim Start und bei jeder Änderung gesetzt. „Aus" heisst Warnungen und Fehler, nicht Stille.

**Vorbedingung 2 · die SDK-Meldungen kamen nie ins Protokoll.** Der Kommentar an `CreateLogger` sprach von „zwei Strömen" — der zweite war nie angeschlossen. `SdkLogBridge` leitet `LoggingService` nach Serilog; bei Debug mit SIP-Nachrichten im Klartext. Damit war die Antwort der Anlage erstmals sichtbar.

**Fehler 1 · Diagnose am falschen Haken.** `OnSubscriptionStateChanged` gilt für `Core.Subscribe()`, nicht für `FriendList`-Abonnements. Zehn abonniert, null Zeilen. Jetzt prüft der Pump `Friend.SubscriptionState`.

**Fehler 2 · Adressformat.** `Friend.Address.AsString()` liefert `"152" <sip:152@…>`, die Einstellungen haben `sip:152@…`. Der Vergleich fand nie zusammen; `SynchronizeAsync` löschte die „fremden" Schlüssel obendrein bei jedem Durchlauf. `SipUri.Normalize` (13 Tests) und `AsStringUriOnly()`.

**Fehler 3 · Liste ersetzen statt abgleichen.** `linphone.db`, Tabelle `friends_list`, `UNIQUE (name)`. Das zweite `AddFriendList("nipp-blf")` im selben Lauf warf eine SEH-Ausnahme aus `MainDb::insertFriendList`. Die Anzeige überlebte das nur, weil die Zustände aus dem ersten Aufruf schon da waren — nach einer Team-Änderung wäre nichts Neues abonniert worden. Die Liste wird jetzt einmal angelegt oder per `GetFriendListByName` aus der Datenbank übernommen und danach nur noch mit `AddFriend`/`RemoveFriend` abgeglichen.

Belege in `docs/review/2026-09-05-blf/`, Verfahren und Ergebnis in `docs/blf-pruefung.md`. **213 Tests grün**, kein `WRN`/`ERR` aus nipp mehr im Startprotokoll.

**Offen: „klingelt".** Über `presence` kennt das SDK diesen Zustand nicht. Ob die Anlage beim Klingeln `Busy` meldet, zeigt ein Test mit einer klingelnden Nebenstelle — dann reicht „im Gespräch". Sonst bleibt der dialog-Weg die Option.
