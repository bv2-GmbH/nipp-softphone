# nipp — Windows-Softphone: Bau-Auftrag

**Auftraggeber:** bv2 GmbH (Dominic Brunner)
**Dokumentstand:** Rev. 6, 06.09.2026 (Rev. 1 vom 04.09.2026 als `NIPP-BUILD.md.rev1-20260904.bak` gesichert; Änderungen in §19)
**Adressat dieses Dokuments:** Claude Code, als Arbeitsauftrag von der ersten Zeile Code bis zum signierten Installer.

---

## 0. Wie dieses Dokument zu benutzen ist

Dieses Dokument ist die verbindliche Spezifikation. Arbeite die Meilensteine **M0 bis M8** in Reihenfolge ab. Jeder Meilenstein hat Akzeptanzkriterien; erst wenn die erfüllt und verifiziert sind, gehst du zum nächsten.

Drei Regeln, die über allem stehen:

1. **Verifiziere die SDK-Oberfläche, bevor du gegen sie schreibst.** Die in diesem Dokument genannten C#-Namen sind aus der offiziellen API-Doku recherchiert, aber die generierte `LinphoneWrapper.cs` ist die einzige Wahrheit. Nach dem Einbinden des SDK: Wrapper-Datei öffnen, Signaturen prüfen, Abweichungen in `docs/sdk-api-notes.md` festhalten. Erfinde niemals eine Methode, die du nicht in der Wrapper-Datei gesehen hast.
2. **Kein Feature-Erweitern ohne Auftrag.** Was hier nicht steht, wird nicht gebaut. Video, Chat, Konferenzbrücke und Multi-Tenant-Verwaltung sind explizit ausgeschlossen (siehe §2).
3. **Nichts gegen Produktivsysteme testen, ohne zu fragen.** Registrierungen laufen gegen den ausdrücklich genannten Test-Trunk, nicht gegen Kundentenants.

Wenn du auf einen Widerspruch zwischen diesem Dokument und der Realität des SDK stösst: dokumentiere ihn, wähle die pragmatische Variante, und markiere sie in `docs/decisions.md` als ADR. Nicht stillschweigend abweichen.

**Begleitdokument:** `docs/plans/IMPLEMENTATION-PLAN.md` bricht die Meilensteine M0–M8 in Phasen P0–P9 mit einzelnen Arbeitspaketen herunter und führt die Blocker-Zeitachse und das Risikoregister. Dieses Dokument sagt **was** gebaut wird, der Umsetzungsplan **in welcher Reihenfolge und wann was von Dominic gebraucht wird**. Bei Widerspruch gilt dieses Dokument.

**Zweites Begleitdokument:** `docs/plans/INTEGRATION-PLAN.md` tut dasselbe für die Integrationsplattform aus §21 — Analyse des Bestands, Zielarchitektur, Phasen I0–I8. Auch dort gilt bei Widerspruch dieses Dokument.

**Änderungen an diesem Dokument** werden in §19 protokolliert, nicht stillschweigend eingearbeitet.

---

## 1. Was gebaut wird

Ein Windows-Desktop-Softphone für die SIP-Telefonanlagen von bv2 (Asterisk-basiert), als Ersatz für den zugekauften Drittanbieter-Client. Audio-only, ein bis mehrere SIP-Konten, mit den Integrationen, die im Alltag zählen: Klick-to-Call aus Outlook und dem CRM, Kontakte mit Besetztlampenfeld, Anrufliste, und ein Einstellungsdialog, den ein Supporter im Kundeneinsatz versteht.

**Referenz für das UI:** das freigegebene Mockup (Fluent/WinUI-Entwurf, fünf Ansichten plus Einstellungen in sechs Gruppen). Das Mockup ist verbindlich für Struktur, Informationsdichte und Beschriftungen; es ist nicht verbindlich für Pixelmasse.

**Produktname:** **nipp** — konsequent klein geschrieben, auch am Satzanfang und im Fenstertitel. Im Code wird daraus der PascalCase-Namespace `Nipp` (siehe §15). Branding, Icon und Signaturzertifikat fehlen noch (siehe §16).

---

## 2. Umfang

### Im Umfang (MVP bis M6)

- Registrierung eines SIP-Kontos über UDP/TCP/TLS, mit SRTP
- Ausgehende Anrufe (Wähltastatur, Kurzwahl, Klick-to-Call über `tel:`/`sip:`)
- Eingehende Anrufe mit Windows-Toast (Annehmen / Ablehnen) **— der Knopf «Mailbox» entfällt seit dem 13.09.2026 (ADR-062).**
- Aktives Gespräch: Stumm, Halten, blindes und begleitetes Weiterleiten, DTMF, Aufnahme, zweiter Ruf/Makeln
- Anrufliste lokal **— die Voicemail-Anzeige über MWI entfällt seit dem 13.09.2026 (ADR-062).**
- Kontakte: Team-Nebenstellen mit Presence/BLF, externe Kontakte aus Outlook
- Einstellungen vollständig gemäss §9
- Audiogerätewahl inkl. getrenntem Klingelgerät, Echounterdrückung mit Kalibrierung
- Codec-Verwaltung mit Priorität
- Autostart, Infobereich-Icon, globaler Hotkey, Single-Instance
- Remote-Provisioning und per Richtlinie gesperrte Felder
- Logging und Diagnose-Export

### Ausdrücklich nicht im Umfang

Video, Instant Messaging/Chat, LIME/E2E-Verschlüsselung von Nachrichten, Konferenzserver-Steuerung, CRM-Schreibzugriffe, Mandantenverwaltung, macOS/Linux, Mobile. Nicht wegdiskutieren, nicht „schon mal vorbereiten“ — kein Code, keine leeren Ordner, keine Interfaces „für später“.

### Nicht-funktionale Anforderungen

| Anforderung | Zielwert |
|---|---|
| Kaltstart bis registriert | < 3 s auf einem i5 der 8. Generation |
| Arbeitsspeicher im Leerlauf | < 180 MB |
| CPU im Gespräch | < 6 % auf einem Kern |
| Zeit vom eingehenden INVITE bis Toast | < 400 ms |
| Absturzfreiheit | 8 h Dauerbetrieb mit 50 Anrufen ohne Neustart |
| Unterstützte OS | Windows 10 22H2 und Windows 11, x64 |

---

## 3. Rechtliche Rahmenbedingung — vor dem ersten Release zu klären

Das **linphone-sdk ist dual lizenziert: AGPLv3 oder eine kostenpflichtige proprietäre Lizenz** von Belledonne Communications. Ein Closed-Source-Client, der an Kunden ausgeliefert wird, ist mit AGPLv3 nicht vereinbar, sofern nicht der komplette Quellcode unter AGPLv3 offengelegt wird. Stand 04.09.2026 unverändert; eine öffentliche Preisliste für die kommerzielle Lizenz gibt es nicht.

**Kontaktweg für die kommerzielle Lizenz:** `https://www.linphone.org/en/contact/` → Developer-Formular `https://linphone.typeform.com/to/kCg6gOWV`. Belledonne Communications SARL, Grenoble, +33 9 52 63 65 05.

**Aufgabe für Claude Code:** Diese Anforderung nicht lösen, sondern sichtbar halten. Lege `docs/licensing.md` an, halte den Stand fest, und blocke M8 (Release) mit einem Hinweis darauf. Für die Entwicklungsphase und interne Tests ist AGPLv3 unproblematisch.

**Kein technischer Ausweg.** Wer erwägt, das Copyleft durch einen Stack-Wechsel zu umgehen: PJSIP ist GPLv2+ oder kommerziell, verschiebt das Problem also nur zu einem anderen Anbieter. Der einzige Stack ohne Copyleft (SIPSorcery, BSD-3) hat **keine echte Echounterdrückung** und kein vergleichbares Codec-Ökosystem — für ein produktives Softphone ist das keine Alternative. Die Frage ist kaufmännisch zu klären, nicht technisch.

**Build-Schalter mit Lizenzwirkung**, beide stehen bereits richtig und dürfen beim Selbstbau nicht versehentlich umgestellt werden: `ENABLE_GPL_THIRD_PARTIES` steht auf **NO**, `ENABLE_NON_FREE_FEATURES` (AMR, H.264) auf **OFF**. Einschalten zieht weitere Lizenzbedingungen nach.

Zweiter Punkt: **G.729** ist im SDK optional (bcg729). Die Kernpatente sind abgelaufen, aber wenn der Codec nicht gebraucht wird, gehört er nicht in den Build. Standardmässig deaktiviert — das deckt sich mit den beiden Schaltern oben.

---

## 4. Zielstack

| Komponente | Wahl | Begründung |
|---|---|---|
| Sprache/Runtime | C# / .NET 8 LTS, `net8.0-windows10.0.19041.0` | LTS, breite Toolchain; auf .NET 9+ nur wechseln, wenn das Windows App SDK es verlangt |
| UI | WinUI 3 über Windows App SDK (aktuellste stabile 1.x) | Fluent-Optik des Mockups nativ, Mica, moderne Controls |
| Architektur | MVVM mit `CommunityToolkit.Mvvm` | Source-Generatoren, keine eigene Bindings-Infrastruktur |
| Plattform | x64 only | Das SDK liefert win32-Desktop-Binaries; ARM64 ist nicht Ziel |
| Packaging | MSIX (packaged), Sparse-Package geprüft | Braucht es für Toasts, Protokoll-Handler und Autostart-Registrierung |
| DI | `Microsoft.Extensions.DependencyInjection` | |
| Logging | `LoggingService` des SDK für SIP, Serilog für die App | Zwei Ströme, eine gemeinsame Log-Ordnerstruktur |
| Tray-Icon | `H.NotifyIcon.WinUI` | WinUI 3 hat keinen eigenen NotifyIcon |
| Tests | xUnit, plus FakeItEasy oder NSubstitute | |

**Wichtig zur Bitness und zum Packaging:** WinUI 3 lädt die native `liblinphone`-DLL-Kette zur Laufzeit. Verifiziere in M1 mit einem Wegwerf-Konsolenprojekt, dass die DLLs sich überhaupt laden lassen, bevor du UI-Code schreibst. Wenn MSIX und native DLLs kollidieren, ist die Rückfallebene ein unpackaged Build plus separate Registrierung der Protokoll-Handler — dann aber ohne `AppNotificationManager`, was eine eigene Toast-Lösung nötig macht. Diese Entscheidung fällt in M1, nicht später.

---

## 5. SDK beschaffen

**Aktueller Stand (verifiziert 04.09.2026): linphone-sdk 5.5.18, Tag-Datum 03.09.2026.** Danach existiert nur `5.6.0-alpha`. Gegen 5.5.18 bauen. Der GitHub-Repo ist nur ein Spiegel des GitLab-Repos und hat keine Releases — Änderungen stehen ausschliesslich im `CHANGELOG.md`.

5.5.0 war der Umbau (Submodule eingeklappt, deshalb Fresh-Clone statt `git pull`; RNNoise, HIDAPI/Jabra, **AEC3 statt AECM**, ISAC und iLBC entfernt), 5.5.1–5.5.18 sind Patches ohne eigene Notes.

Drei Wege, in dieser Reihenfolge probieren:

### Weg A — NuGet aus der Registry von Belledonne

Belledonne veröffentlicht **`LinphoneSDK.Windows`** in der eigenen GitLab-Package-Registry, **projectId 411** (im Linphone-Wiki dokumentiert). Das alte Paket `LinphoneSDK` auf nuget.org ist von 2017 (3.12.0.273), UWP-only und als legacy markiert — **nicht verwenden**; es verweist selbst auf die Belledonne-Registry. `LinphoneSDK.Dotnet` existiert nur als Build-Ziel in `cmake/NuGet/README.md` (für MAUI/Multi-Plattform); ein veröffentlichtes Paket dieses Namens war nicht auffindbar.

```xml
<!-- nuget.config im Repo-Root -->
<configuration>
  <packageSources>
    <add key="linphone" value="https://gitlab.linphone.org/api/v4/projects/411/packages/nuget/index.json" />
  </packageSources>
</configuration>
```

**Offen und vor Ort zu prüfen:** ob dieser Feed **anonym** lesbar ist oder einen Deploy-Token braucht. Bei der Recherche war `gitlab.linphone.org:443` nicht erreichbar. Wenn der Feed nicht anonym geht, **nicht daran festbeissen** — dann Weg C nehmen, der ist verifiziert.

Die Windows-Variante existiert in drei Paketierungen — **Win32-Desktop** ist die richtige; UWP-x64 hat kein OpenH264 und kein Lime X3DH, die Store-Bridge-Variante ist ein Kompatibilitätsbuild. Beide sind hier falsch. Zur Begriffsklärung: „Win32" meint die Desktop-API im Gegensatz zu UWP, **nicht** 32-Bit — gebaut wird x64. Der Framework-Moniker der Desktop-Variante ist widersprüchlich dokumentiert (`win` im NuGet-README, `netcore45` im Wiki); beim Einbinden prüfen, welcher greift.

### Weg C — offizielles Prebuilt-ZIP (der verifizierte Weg)

`https://download.linphone.org/releases/windows/sdk/` → **`linphone-sdk-win64-5.5.18.zip`** (313 MB, 03.09.2026). Inhalt am 04.09.2026 geprüft:

- `linphone-sdk/win64/share/linphonecs/LinphoneWrapper.cs` — der C#-Wrapper, also die maßgebliche API-Quelle nach §0 Regel 1
- `bin/liblinphone.dll`, `mediastreamer2.dll`, `belle-sip.dll`
- `lib/mediastreamer/plugins/libmswasapi.dll`, `libmswebrtc.dll`, `libmsopenh264.dll`

Nur `win64` — es gibt kein win32- und kein arm64-ZIP, was zur x64-Festlegung aus §4 passt. **Das ist der einzige nachweislich vollständige Beschaffungsweg und deshalb der Standard für M1**, damit das Go/No-Go-Gate nicht an einer Registry-Frage hängt. Weg A nur, wenn er sich als anonym erreichbar erweist — dann ist er für Updates bequemer.

Achtung auf die Verzeichnisstruktur: die Mediastreamer-Plugins liegen **nicht** neben den Haupt-DLLs, sondern unter `lib/mediastreamer/plugins/`. Siehe §14.2 — das ist die Stelle, an der MSIX-Pakete typischerweise brechen.

### Weg B — SDK selbst bauen (Rückfallebene)

Nötig, wenn weder Paket noch ZIP erreichbar sind oder ein Codec fehlt.

Voraussetzungen: Visual Studio **17 2022**, MSYS2 mit MinGW32/64 (PATH-Reihenfolge `mingw<N>\bin`, `C:\msys64\`, `usr\bin`), CMake ≥ 3.22, Python ≥ 3.6 mit `pystache` und `six`, yasm, nasm, doxygen, 7-Zip im PATH. AV1 ist standardmässig **an** und verlangt zusätzlich Meson, Ninja und Perl.

`-DENABLE_WINDOWS_TOOLS_CHECK=ON` würde die MSYS2-Tools automatisch installieren, aber **die Windows-Presets setzen den Schalter auf OFF** — die Werkzeuge also selbst einrichten. `ENABLE_CSHARP_WRAPPER=ON` ist in den Windows-Presets gesetzt, der Wrapper entsteht damit automatisch.

```bash
git clone https://gitlab.linphone.org/BC/public/linphone-sdk.git --recursive
cd linphone-sdk && git checkout 5.5.18
cmake --preset=windows-sdk -B build-windows
cmake --build build-windows --config RelWithDebInfo
```

Der Preset-Name `windows-sdk` ist in `CMakePresets.json` bei Tag 5.5.18 nachgeprüft und gilt weiterhin. Weitere vorhandene Presets: `windows-64bits`, `windows-32bits`, `windows-ninja-sdk`, `windows-ninja-64bits`, `windows-store-sdk`, `uwp-sdk`, `java-sdk-windows`.

Der C#-Wrapper wird beim Build generiert (`LinphoneWrapper.cs`). Für ein eigenes NuGet-Paket: `-DLINPHONESDK_BUILD_TYPE=Packager -DLINPHONESDK_PACKAGER=Nuget` mit `-DLINPHONESDK_CSHARP_WRAPPER_PATH` und `-DLINPHONESDK_DESKTOP_ZIP_PATH`. Ergebnis landet unter `linphone-sdk/packages`. Bei Änderungen den NuGet-Cache leeren, sonst zieht der Build stillschweigend die alte Version.

**Dokumentiere in `docs/sdk-setup.md`, welcher Weg genommen wurde, mit exakter Version und Commit-Hash.** Reproduzierbarkeit ist hier wichtiger als Eleganz.

**API-Referenz C#:** `https://download.linphone.org/releases/docs/liblinphone/latest/cs/api/`

---

## 6. Architektur

Vier Schichten, strikt in eine Richtung abhängig:

```
UI (WinUI 3, XAML)
   ↓ bindet an
ViewModels (CommunityToolkit.Mvvm)
   ↓ ruft
Services (Telefonie, Einstellungen, Kontakte, Verlauf, Windows-Integration)
   ↓ kapselt
Linphone SDK (LinphoneWrapper.cs + native DLLs)
```

**Kein XAML-Code-Behind ruft das SDK.** Kein ViewModel importiert `Linphone` direkt — der Namespace kommt ausschliesslich in `Services/Telephony/` vor. Das ist die Grenze, an der sich später ein SDK-Wechsel oder ein Mock aufhängen lässt, und sie wird durch einen Architekturtest abgesichert (siehe §13).

### Der zentrale Punkt: die Iterate-Schleife

`Core.Iterate()` muss regelmässig aufgerufen werden, damit das SDK Netzwerkereignisse verarbeitet und Callbacks feuert — auf Desktop macht das SDK das **nicht** selbst.

**Vorgabe:** ein `DispatcherQueueTimer` auf dem UI-Thread mit 20 ms Intervall. Damit laufen alle SDK-Callbacks auf dem UI-Thread und es braucht kein Marshalling. Der Preis: nichts Blockierendes in einem Callback, keine synchronen Datei- oder Netzwerkoperationen. Wenn ein Callback Arbeit auslöst, geht die auf einen Worker und kommt über den Dispatcher zurück.

Der Timer läuft, solange die App läuft — auch wenn das Fenster geschlossen und die App nur im Infobereich ist. Also hängt er am `App`-Lebenszyklus, nicht am `MainWindow`.

### `SipService` — die eine Klasse, die das SDK kennt

Verantwortlich für: Factory- und Core-Initialisierung, Iterate-Timer, Listener-Registrierung (`Core.AddListener`), und die Übersetzung der SDK-Callbacks in .NET-Events mit eigenen, SDK-freien Argumenttypen.

```csharp
public interface ISipService
{
    RegistrationState RegistrationState { get; }
    IReadOnlyList<CallInfo> ActiveCalls { get; }

    Task InitializeAsync(CancellationToken ct);
    Task<CallHandle> PlaceCallAsync(string destination);
    Task AcceptAsync(CallHandle call);
    Task HangUpAsync(CallHandle call);
    Task TransferAsync(CallHandle call, string destination, TransferMode mode);
    Task SetHoldAsync(CallHandle call, bool onHold);
    Task SendDtmfAsync(CallHandle call, char digit);
    Task<bool> StartRecordingAsync(CallHandle call, string path);

    event EventHandler<RegistrationChangedEventArgs> RegistrationChanged;
    event EventHandler<CallStateEventArgs> CallStateChanged;
    event EventHandler<CallQualityEventArgs> QualityUpdated;
    event EventHandler<MessageWaitingEventArgs> MessageWaitingChanged;
    event EventHandler<PresenceEventArgs> PresenceChanged;
}
```

`CallHandle`, `CallInfo`, `RegistrationState` etc. sind eigene Typen in `Services/Telephony/Model/`. Kein `Linphone.Call` verlässt den Service.

### Nachgewiesene SDK-Oberfläche

Diese Namen stammen aus der offiziellen C#-Doku und sind der Ausgangspunkt — in M1 gegen `LinphoneWrapper.cs` verifizieren:

| Zweck | C#-Member |
|---|---|
| Factory-Singleton | `Factory.Instance` |
| Core anlegen | `Factory.Instance.CreateCore(...)`, `CreateCoreWithConfig(...)` |
| Adresse parsen | `Factory.Instance.CreateAddress(string)` |
| Auth-Objekt | `Factory.Instance.CreateAuthInfo(...)`, `ComputeHa1ForAlgorithm(...)` |
| Verzeichnisse vor Core-Start | `Factory.Instance.ConfigDir`, `DataDir`, `CacheDir` |
| Listener | `Core.AddListener(...)`, `Core.RemoveListener(...)` |
| Ereignisschleife | `Core.Iterate()`, `Core.Start()`, `Core.Stop()` |
| Konten | `Core.AccountList`, `Core.AuthInfoList`, `Core.AddAuthInfo(...)` |
| Anrufe | `Core.InviteAddress(...)`, `Core.AcceptCall(...)`, `Core.CurrentCall`, `Core.Calls` |
| Audiogeräte | `Core.AudioDevices`, `Core.ExtendedAudioDevices` |
| Codecs | `Core.AudioPayloadTypes` |
| NAT | `Core.NatPolicy` |
| Verschlüsselung | `Core.MediaEncryption` |
| Transport | `Core.Transports` |
| Provisionierung | `Core.ProvisioningUri` |
| Echo | `Core.EchoCancellerEnabled` |
| DTMF | `Core.Rfc2833DtmfsEnabled` |
| Ports | `Core.AudioPort`, `Core.AudioPortsRange` |
| QoS | `Core.AudioDscp` |

Für Echo-Kalibrierung, PayloadType-Reihenfolge, `AccountParams` und `NatPolicy`-Details: Namen aus der Wrapper-Datei holen, nicht raten.

---

## 7. Projektstruktur

```
nipp/
├── Nipp.sln
├── nuget.config
├── Directory.Build.props            # gemeinsame Version, Nullable, TreatWarningsAsErrors
├── CLAUDE.md                        # Konventionen, siehe §17
├── docs/
│   ├── sdk-setup.md                 # Weg A oder B, Version, Commit
│   ├── sdk-api-notes.md             # Abweichungen der Wrapper-API von diesem Dokument
│   ├── decisions.md                 # ADRs
│   ├── licensing.md                 # AGPL vs. kommerziell, Stand
│   └── test-matrix.md               # manuelle Testfälle gegen die PBX
├── src/
│   ├── Nipp.App/                               # WinUI 3 App
│   │   ├── App.xaml(.cs)                       # DI-Container, Iterate-Timer, Single-Instance
│   │   ├── MainWindow.xaml(.cs)                # NavigationView, Mica, Titlebar
│   │   ├── Views/
│   │   │   ├── DialerPage.xaml
│   │   │   ├── ActiveCallPage.xaml
│   │   │   ├── CallHistoryPage.xaml
│   │   │   ├── ContactsPage.xaml
│   │   │   ├── VoicemailPage.xaml
│   │   │   └── Settings/
│   │   │       ├── SettingsPage.xaml           # Pivot-Container
│   │   │       ├── AccountSettingsView.xaml
│   │   │       ├── NetworkSettingsView.xaml
│   │   │       ├── NatMediaSettingsView.xaml
│   │   │       ├── AudioSettingsView.xaml
│   │   │       ├── CodecSettingsView.xaml
│   │   │       └── AdvancedSettingsView.xaml
│   │   ├── Controls/
│   │   │   ├── SettingCard.xaml                # Label/Beschreibung/Control, wie im Mockup
│   │   │   ├── Keypad.xaml
│   │   │   ├── PresenceBadge.xaml
│   │   │   └── CallQualityPanel.xaml
│   │   ├── Themes/
│   │   │   ├── Tokens.xaml                     # Farben, Radien, Typo-Skala
│   │   │   └── Controls.xaml
│   │   ├── Tray/
│   │   │   └── TrayIconHost.cs
│   │   └── Assets/                             # Icons, Klingeltöne
│   ├── Nipp.Core/                              # ViewModels, Services, Modelle
│   │   ├── ViewModels/
│   │   ├── Services/
│   │   │   ├── Telephony/
│   │   │   │   ├── SipService.cs               # einzige Datei mit "using Linphone"
│   │   │   │   ├── SipEventBridge.cs
│   │   │   │   ├── CodecService.cs
│   │   │   │   ├── AudioDeviceService.cs
│   │   │   │   └── Model/
│   │   │   ├── Settings/
│   │   │   │   ├── SettingsService.cs
│   │   │   │   ├── SettingsSchema.cs           # Tabelle aus §9 als Code
│   │   │   │   ├── ProvisioningService.cs
│   │   │   │   └── PolicyService.cs            # gesperrte Felder
│   │   │   ├── Contacts/
│   │   │   │   ├── OutlookContactSource.cs
│   │   │   │   ├── BlfService.cs
│   │   │   │   └── ClipResolver.cs             # Nummer → Name
│   │   │   ├── History/
│   │   │   │   └── CallHistoryStore.cs         # SQLite
│   │   │   └── Windows/
│   │   │       ├── AutostartService.cs
│   │   │       ├── ProtocolHandlerService.cs   # tel:, sip:, callto:
│   │   │       ├── GlobalHotkeyService.cs
│   │   │       ├── ToastService.cs
│   │   │       └── SecretStore.cs              # DPAPI
│   │   └── Diagnostics/
│   └── Nipp.Provisioning/                      # XML-Schema + Generator (M7)
└── tests/
    ├── Nipp.Core.Tests/
    └── Nipp.Architecture.Tests/                # erzwingt die Schichtgrenze
```

---

## 8. Die Screens

Jeder Screen: Aufbau aus dem Mockup, Verhalten hier. Wo das Mockup und dieser Text sich widersprechen, gilt dieser Text.

### 8.1 Wählen (`DialerPage`)

- Nummernanzeige, Tastatur 3×4 mit Buchstabenzeile, Anrufen und Rücktaste.
- Eingabe per Tastatur **und** per Ziffernblock der Hardware-Tastatur; Enter wählt, Escape leert.
- Nummernnormalisierung: interne Ziele (`*` plus 3–4 Stellen, oder ≤ 4 Ziffern) bleiben unverändert; alles andere wird nach E.164 normalisiert. Regeln: `00` → `+`, führende `0` → `+41` (Länderpräfix aus Einstellungen), bereits `+` bleibt. Die Normalisierung ist eine reine Funktion in `Services/Telephony/NumberNormalizer.cs` und **wird mit Unit-Tests abgedeckt** — das ist die Stelle, an der Softphones typischerweise falsch wählen.
- Live-Auflösung: während der Eingabe passende Kontakte einblenden (Präfixsuche über Name und Nummer, max. 5 Treffer).
- Kurzwahl aus der Provisionierung, „Zuletzt“ aus dem Verlauf.

### 8.2 Aktives Gespräch (`ActiveCallPage`)

- Gegenstelle (Name aus CLIP-Auflösung, sonst Nummer), Dauer als tickender Timer, Chips für Codec und Verschlüsselung.
- Aktionen: Stumm, Halten, Weiterleiten, DTMF-Feld, Aufnahme, zweiter Anruf.
- **Weiterleiten** kann beides: blind (`Transfer`) und begleitet (zweiter Anruf, dann `TransferToAnother`). Im UI eine Auswahl, kein verstecktes Verhalten.
- **Qualitätspanel** aus `CallStats`: Round-Trip, Jitter, Empfangs-Paketverlust, geschätzter MOS, Bandbreite. Aktualisierung im Sekundenrhythmus, nicht bei jedem Iterate.
- Zwei parallele Gespräche: eines aktiv, eines gehalten, Umschalten sichtbar. Mehr als zwei wird abgelehnt mit klarer Meldung.
- Aufnahme schreibt WAV in einen konfigurierbaren Ordner mit Namensschema `JJJJ-MM-TT_HHMMSS_<Nummer>.wav`. **Ein sichtbarer Aufnahmeindikator ist Pflicht** — in der Schweiz ist das Mitschneiden ohne Kenntnis der Gegenseite strafbar; die App darf nicht unbemerkt aufnehmen.

### 8.3 Anrufliste (`CallHistoryPage`)

- Lokale SQLite-Historie: Richtung, Gegenstelle, aufgelöster Name, Zeit, Dauer, Codec, Ergebnis (angenommen/verpasst/abgelehnt/Fehler), Aufnahmepfad.
- Filter: Alle, Verpasst, Eingehend, Ausgehend, Aufgenommen. Suche über Nummer und Name.
- Rückruf per Doppelklick, Kontextmenü mit Kopieren und „Kontakt anlegen“.
- Aufbewahrung konfigurierbar (Standard 365 Tage), Aufräumen beim Start.

### 8.4 Kontakte (`ContactsPage`)

- Zwei Quellen: Team-Nebenstellen (aus Provisionierung, mit BLF) und Outlook-Kontakte.
- **BLF** über `Friend`/`FriendList` mit aktivierten Subscribes; Zustände frei / klingelt / im Gespräch / offline, farblich und als Text (nie nur Farbe — das ist eine Barrierefreiheitsanforderung, kein Stilfrage).
- Outlook-Anbindung: **COM-Interop** gegen ein lokal laufendes Outlook (Rev. 4, ADR-009 — Rev. 1 bis 3 gaben Microsoft Graph vor; die Entra-App-Registrierung lohnt den Aufwand nicht, solange nipp intern läuft). Ergebnis wird lokal gecacht (12 h) — **nie synchron im UI-Thread laden**, was mit COM und dem 20-ms-Iterate-Timer auf demselben Thread doppelt gilt. Drei Punkte, die COM mitbringt: Outlook muss laufen (sonst verständliche Anzeige statt leerer Liste), der Abruf gehört auf einen Worker, und Terminalserver mit mehreren Profilen sind vorher zu klären. Graph bleibt nachrüstbar, wird aber nicht vorgebaut. **Nachtrag Rev. 7:** das gilt weiterhin, und die Begründung ist inzwischen eine andere — ADR-018 und §22.4, nachdem sich zeigte, dass das neue Outlook gar kein COM anbietet.
- `ClipResolver` bedient Toast, Gesprächsansicht und Anrufliste aus demselben Cache.

### 8.5 Voicemail (`VoicemailPage`) — ENTFALLEN

> **Dieser Abschnitt gilt seit dem 13.09.2026 nicht mehr (ADR-062).** Der
> Mailbox-Bereich ist vollständig entfernt: Reiter, Abzeichen, «Mailbox
> anrufen», der Toast-Knopf, das Einstellungsfeld und das MWI-Abo. Er steht
> hier stehen gelassen, damit die Nummerierung der Abschnitte hält und
> nachvollziehbar bleibt, was einmal verlangt war.

- MWI über `Account.setVoicemailAddress` und das Message-Waiting-Ereignis: Anzahl neuer Nachrichten als Badge in der Navigation.
- Liste ist Anzeige plus „Mailbox anrufen“. Kein IMAP, kein Download, keine Wiedergabe im Client — die Nachrichten liegen auf der PBX.

### 8.6 Eingehender Anruf (Toast)

- `AppNotificationManager` mit Buttons Annehmen / Ablehnen / Mailbox. Der Toast erscheint, auch wenn das Fenster geschlossen ist.
- Klick auf Annehmen: App in den Vordergrund, direkt in die Gesprächsansicht.
- **Anruferkontext im Toast (ADR-030, 07.09.2026).** Drei Textzeilen plus die Attributionszeile — mehr nimmt Windows nicht: wer anruft (Name · Firma (Art)), die letzte Arbeit mit Datum und Kollegen, die Zusammenfassung des letzten Gesprächs, darunter die Rufnummer. Der Toast **erscheint sofort** mit Name oder Nummer und wird ersetzt, sobald eine Quelle geantwortet hat; gewartet wird nie. Fällt jede Quelle aus, steht dort genau das, was vorher dort stand.
- Klingeln auf dem separaten Klingelgerät, wenn eines gewählt ist.
- Wenn schon ein Gespräch läuft: Toast mit „Annehmen und halten“ statt „Annehmen“.
- Auto-Annahme (Einstellung) überspringt den Toast, spielt aber einen kurzen Hinweiston.

---

## 9. Einstellungen — vollständige Referenz

Diese Tabelle ist die Quelle für `SettingsSchema.cs`. Jede Zeile: ein Feld, sein Typ, der Standardwert, und wohin es im SDK geht. Die `linphonerc`-Sektion ist nachzutragen, wo sie beim Verifizieren bekannt wird.

### 9.1 SIP-Konto

| Feld | Typ | Standard | Ziel im SDK |
|---|---|---|---|
| Anzeigename | Text | leer | `AccountParams` Identity-Address, Display-Name |
| SIP-Benutzername | Text | leer | Identity-Address Username |
| Authentifizierungs-ID | Text | = Benutzername | `AuthInfo` |
| Passwort | Secret | leer | `AuthInfo`, siehe §11 |
| Domain / Registrar | Text | leer | `AccountParams` Server-Address |
| Outbound-Proxy | Text + Schalter | aus | `AccountParams` Routes |
| Registrierungsdauer | Zahl (s) | 600 | `AccountParams` Expires |
| Konto aktiviert | Schalter | ein | `AccountParams` Register aktivieren |
| ~~Voicemail-Adresse~~ | ~~Text~~ | ~~leer~~ | **entfallen (ADR-062)** |
| Länderpräfix für Normalisierung | Text | `+41` | eigene Logik (§8.1) |

Mehrere Konten sind möglich; das erste registrierte ist Standard für ausgehende Anrufe. Ein Konto lässt sich löschen, wobei `AuthInfo` mitgelöscht wird — sonst bleiben Zugangsdaten verwaist liegen.

### 9.2 Netzwerk & Transport

| Feld | Typ | Standard | Ziel im SDK |
|---|---|---|---|
| Transport | UDP / TCP / TLS | TLS | `AccountParams` Transport, `Core.Transports` |
| SIP-Port | Zahl | 5061 (TLS) | `Core.Transports` |
| Serverzertifikat prüfen | Schalter | ein | `Core` Root-CA + Verifikation; CA aus dem Windows-Zertifikatspeicher exportieren |
| Keep-Alive-Intervall | Zahl (s) | 30 | `Core` |
| IPv6 | Schalter | aus | `Core` |
| Netzwerkwechsel erkennen | Schalter | ein | `Core` Network-Reachability; an `NetworkInformation.NetworkStatusChanged` hängen |
| RTP-Portbereich | Bereich | 7078–7178 | `Core.AudioPortsRange` |
| DSCP Signalisierung / Medien | Zahl | CS3 (24) / EF (46) | `Core.SipDscp`, `Core.AudioDscp` |

### 9.3 NAT & Medien

| Feld | Typ | Standard | Ziel im SDK |
|---|---|---|---|
| STUN-Server | Text | leer | `NatPolicy` |
| ICE | Schalter | ein | `NatPolicy` |
| TURN + Zugangsdaten | Text + Schalter | aus | `NatPolicy` |
| Medienverschlüsselung | Keine / SRTP / ZRTP / DTLS | SRTP | `Core.MediaEncryption` |
| Verschlüsselung erzwingen | Schalter | **aus** (Rev. 3, ADR-007) | `Core` Mandatory-Flag |
| Adaptive Bitrate | Schalter | ein | `Core` |
| Video | Schalter | aus, **im UI ausgegraut** | `Core` Video deaktivieren |

**Zu „erzwingen: aus" (Rev. 3, 04.09.2026):** Rev. 1 und 2 gaben hier „ein" vor. Die aktuelle Telefonanlage von bv2 hat noch keine Verschlüsselung — mit „ein" wäre kein Gespräch möglich. nipp bietet SRTP an und nutzt es, sobald die Gegenseite mitkommt; erzwungen wird es nicht. Begründung und Konsequenzen in `docs/decisions.md`, ADR-007. Der Zustand muss im Gespräch sichtbar sein (§8.2), und ein wegen „erzwingen" gescheitertes Gespräch braucht eine Meldung, die den Grund nennt (§14.7).

### 9.4 Audio

| Feld | Typ | Standard | Ziel im SDK |
|---|---|---|---|
| Mikrofon | Geräteliste | Windows-Standard folgen | `Core` Default-Input-Device |
| Lautsprecher | Geräteliste | Windows-Standard folgen | `Core` Default-Output-Device |
| Klingelgerät | Geräteliste | = Lautsprecher | `Core` Ringer-Device |
| Wiedergabelautstärke | 0–100 | 72 | `Core` Playback-Gain |
| Mikrofonpegel | 0–100 | 50 | `Core` Mic-Gain |
| Echounterdrückung | Schalter + Kalibrieren | ein | `Core.EchoCancellerEnabled`, Kalibrierung starten. Ab SDK 5.5 ist die Implementierung **AEC3** (vorher AECM) |
| Automatische Aussteuerung | Schalter | ein | `Core` AGC |
| Rauschunterdrückung | Schalter | ein | Noise-Suppression; SDK 5.5 bringt **RNNoise** mit (bestätigt) |
| Klingelton | Dateipfad | `Assets/Sounds/nipp-ring.wav` | `Core.Ring` |
| Rufton beim Wählen | Dateipfad | `Assets/Sounds/nipp-ringback.wav` | `Core.Ringback` |

Geräte-Hotplug: `AudioDevices` neu einlesen, wenn Windows ein Gerät meldet. Ein während des Gesprächs verschwindendes Headset darf das Gespräch nicht abreissen lassen — auf das Standardgerät zurückfallen und den Benutzer informieren.

Die Echo-Kalibrierung ist ein eigener Dialog mit Fortschritt (~15 s) und dem Ergebniswert in ms.

### 9.5 Codecs

Tabelle mit Aktiv-Kästchen, Name, Rate, Payload-Type, Bitrate und Reihenfolge (hoch/runter). Reihenfolge bestimmt die Priorität im SDP.

| Codec | Rate | PT | Standard |
|---|---|---|---|
| Opus | 48 kHz | 96 | aktiv, Priorität 1 |
| G.722 | 16 kHz | 9 | aktiv, Priorität 2 |
| PCMA | 8 kHz | 8 | aktiv, Priorität 3 |
| PCMU | 8 kHz | 0 | aktiv, Priorität 4 |
| G.729 | 8 kHz | 18 | inaktiv |
| speex | 16 kHz | 102 | inaktiv |

PCMA und PCMU dürfen nicht beide deaktiviert werden — das UI verhindert es, weil sonst die Verhandlung mit vielen Trunks scheitert.

DTMF-Modus: RFC 2833 (Standard) / SIP INFO / Inband → `Core.Rfc2833DtmfsEnabled` plus SIP-INFO-Gegenstück.

### 9.6 Erweitert

| Feld | Typ | Standard |
|---|---|---|
| Mit Windows starten | Schalter | ein |
| Minimiert im Infobereich starten | Schalter | ein |
| Standard für `tel:`, `sip:`, `callto:` | Schalter | ein |
| Globaler Hotkey Annehmen/Auflegen | Tastenkombination | Strg+Umschalt+A |
| Anrufe automatisch annehmen | Schalter | aus |
| Protokollierung | Aus / Info / Debug | Info |
| Log-Ordner öffnen | Aktion | — |
| Provisioning-URI | Text | leer, `Core.ProvisioningUri` |
| Konfiguration jetzt abrufen | Aktion | — |
| Diagnosepaket erstellen | Aktion | ZIP mit Logs, Config ohne Passwörter, Systeminfo |
| Version / Update prüfen | Anzeige + Aktion | — |

---

## 10. Windows-Integration

| Thema | Vorgabe |
|---|---|
| Single-Instance | `AppInstance.FindOrRegisterForKey`, Weiterleitung der Aktivierungsargumente an die laufende Instanz |
| Protokoll-Handler | `tel:`, `sip:`, `callto:` per MSIX-Manifest; bei unpackaged Build über HKCU-Registry. Aktivierung mit URI → direkt wählen, keine Rückfrage |
| Autostart | MSIX `StartupTask`; unpackaged über `HKCU\...\Run` |
| Infobereich | `H.NotifyIcon.WinUI` mit Kontextmenü: Öffnen, Präsenz setzen, Stumm, Beenden. Schliessen des Fensters beendet die App nicht |
| Toasts | `AppNotificationManager` mit Buttons und Aktivierungs-Argumenten |
| Globaler Hotkey | `RegisterHotKey` per P/Invoke auf einem versteckten Fenster; Konflikte abfangen und melden |
| Zugangsdaten | DPAPI (`ProtectedData`, CurrentUser-Scope), Datei unter `%LOCALAPPDATA%`; **niemals Klartext in `linphonerc`** — wenn möglich HA1 statt Passwort ablegen |
| Klick-to-Call aus Outlook | ergibt sich aus dem `tel:`-Handler |
| Klick-to-Call aus dem CRM | ebenfalls `tel:`-Link; keine eigene Schnittstelle bauen |
| Energieverwaltung | Bei Standby/Resume Registrierung erneuern; `SystemEvents.PowerModeChanged` |
| Datenpfade | Config und `linphonerc` unter `%APPDATA%\nipp`, Logs und Aufnahmen unter `%LOCALAPPDATA%\nipp`, ausgelieferte Factory-Config unter `%PROGRAMDATA%\bv2\nipp` |

---

## 11. Provisionierung

Zwei Ebenen:

1. **Factory-Config** (`linphonerc-factory`), mitgeliefert: Standardwerte für alle Felder aus §9. Was hier steht, gilt als Auslieferungszustand.
2. **Remote-Provisioning** über `Core.ProvisioningUri`: XML von `https://prov.<domain>/<id>.xml`, abgerufen beim Start. Fehler beim Abruf dürfen den Start nicht verhindern — dann gilt die lokale Config, mit Hinweis im UI.

**Gesperrte Felder:** Ein Provisioning-Profil kann Felder als schreibgeschützt markieren. `PolicyService` liest diese Liste, `SettingCard` rendert betroffene Controls ausgegraut mit Schloss-Symbol und Tooltip „Von der Administration festgelegt“. Das ist keine Sicherheitsgrenze, sondern ein Bedienschutz — so auch dokumentieren.

`Nipp.Provisioning` liefert das XML-Schema und einen kleinen Generator, mit dem bv2 ein Profil pro Kunde erzeugt. Keine Web-UI, ein Kommandozeilenwerkzeug reicht.

---

## 12. Meilensteine

### M0 — Repo und Skelett
Solution, Projekte, `Directory.Build.props` (Nullable ein, Warnungen als Fehler), `CLAUDE.md`, `.editorconfig`, `.gitignore`, leere WinUI-App startet mit Mica und Titlebar.
**Akzeptanz:** `dotnet build` grün, App startet, leeres Fenster im richtigen Look.

### M1 — SDK-Spike (der wichtigste Meilenstein)
Wegwerf-Konsolenprojekt: SDK einbinden, native DLLs laden, `Core` erzeugen, Iterate-Schleife, gegen den Test-Trunk registrieren, ein Testanruf zu einer Echo-Nebenstelle. Danach: dieselbe Ladbarkeit aus der WinUI-App heraus prüfen (packaged **und** unpackaged).
**Akzeptanz:** REGISTER 200 OK im Log; ein Gespräch mit hörbarem Audio in beide Richtungen; `docs/sdk-api-notes.md` gefüllt; die Packaging-Entscheidung aus §4 getroffen und als ADR festgehalten.
**Wenn dieser Meilenstein scheitert, wird nicht weitergebaut** — dann melden, mit dem konkreten Fehlerbild.

### M2 — Telefonie-Kern
`SipService` mit Registrierung, ausgehend, eingehend, Auflegen, Zustandsmaschine, Events. Konsolen- oder Minimal-UI genügt.
**Akzeptanz:** Registrierung mit Statusanzeige; ausgehender und eingehender Anruf; Neuregistrierung nach Netzwerkwechsel; keine Abstürze bei 20 Anrufen in Folge.

### M3 — Wählen und Gespräch
`DialerPage` und `ActiveCallPage` vollständig, Nummernnormalisierung mit Tests, Stumm/Halten/DTMF/Weiterleiten/Aufnahme, Qualitätspanel.
**Akzeptanz:** blindes und begleitetes Weiterleiten funktionieren gegen die Test-PBX; zwei Gespräche makeln; Aufnahme erzeugt abspielbares WAV mit sichtbarem Indikator.

### M4 — Einstellungen
Alle sechs Gruppen aus §9, `SettingsSchema`, Persistenz, sofortige Wirksamkeit ohne Neustart (wo möglich), Neustart-Hinweis wo nicht.
**Akzeptanz:** Transportwechsel UDP↔TLS wirkt nach Neuregistrierung; Codec-Reihenfolge ändert das SDP nachweisbar (im SIP-Log); Audiogerätewechsel im laufenden Gespräch funktioniert; Echo-Kalibrierung liefert einen Wert.

### M5 — Kontakte, Verlauf, Voicemail *(Voicemail entfallen, ADR-062)*
SQLite-Verlauf, Outlook über COM (ADR-009; hier stand bis Rev. 6 „über Graph" — das war der Stand von Rev. 1 bis 3 und ist seit dem 04.09.2026 überholt, siehe auch §22.4), `ClipResolver`, BLF, MWI-Badge.
**Akzeptanz:** eingehender Anruf einer bekannten Nummer zeigt den Namen in Toast, Gespräch und Verlauf; BLF wechselt sichtbar, wenn eine Nebenstelle telefoniert; MWI-Badge folgt der Mailbox.

### M6 — Windows-Integration
Toast mit Buttons, Infobereich, Autostart, Protokoll-Handler, Hotkey, Single-Instance, DPAPI-Speicher.
**Akzeptanz:** `tel:`-Link aus Outlook und aus dem Browser wählt; Toast bei geschlossenem Fenster mit funktionierenden Buttons; Autostart überlebt einen Neustart; zweiter Programmstart aktiviert die erste Instanz.

### M7 — Provisionierung und Diagnose
Factory-Config, Remote-Provisioning, gesperrte Felder, Diagnosepaket, Log-Level.
**Akzeptanz:** ein Profil richtet ein Konto vollständig ein; gesperrte Felder sind nicht editierbar; Diagnose-ZIP enthält Logs und **keine** Passwörter (im Test explizit prüfen).

### M8 — Paket und Auslieferung
MSIX mit Code-Signing, Versionierung, Update-Prüfung, Installationsanleitung, Silent-Install-Parameter für die Verteilung.
**Akzeptanz:** Installation und Update auf einem frischen Windows 11; unbeaufsichtigte Installation funktioniert; `docs/licensing.md` ist geklärt oder der Release ist ausdrücklich als intern markiert.

---

## 13. Tests und Verifikation

**Unit-Tests, verbindlich für:** Nummernnormalisierung (mit Fällen: `044 512 84 30`, `+41445128430`, `0041445128430`, `*8010`, `40`, `112`, Ausland `0049...`), Settings-Serialisierung, Provisioning-XML-Parsing (inkl. kaputtes XML), CLIP-Auflösung, Verlaufs-Aufbewahrung.

**Architekturtest, verbindlich:** ein Test, der fehlschlägt, wenn `using Linphone` ausserhalb von `Services/Telephony/` auftaucht. Reflection über die Assemblies oder Quelltext-Scan — beides zulässig.

**Nicht mit Unit-Tests abdeckbar** (SIP, Audio, Windows-Integration): manuelle Testmatrix in `docs/test-matrix.md`, gepflegt ab M2, mit Spalten Testfall / Erwartung / Ergebnis / Datum / Build. Mindestens: Registrierung über alle drei Transporte, Anruf in beide Richtungen, Weiterleitung beide Varianten, Makeln, DTMF gegen ein IVR, Netzwerkwechsel WLAN→VPN, Standby/Resume, Gerätewechsel im Gespräch, verpasster Anruf, Mailbox.

**Testumgebung:** ein dedizierter Test-Trunk auf der Test-PBX, kein Kundentenant. Die Zugangsdaten kommen von Dominic — nicht selbst welche erfinden und nicht produktive verwenden.

---

## 14. Bekannte Fallstricke

Diese Punkte kosten erfahrungsgemäss Tage, wenn man sie spät entdeckt:

1. **Iterate im falschen Thread.** Callbacks kommen dort an, wo `Iterate()` läuft. UI-Thread heisst: nichts blockieren. Ein `await` auf eine Netzwerkoperation in einem Callback friert die App ein.
2. **Native DLL-Kette, und zwar die Plugins.** `liblinphone` bringt eine ganze Reihe abhängiger DLLs mit. Fehlt eine, ist die Fehlermeldung nutzlos (`System.DllNotFoundException` ohne Angabe, welche). Alle DLLs müssen im Ausgabeverzeichnis und im MSIX landen — mit einem Post-Build-Schritt sicherstellen, nicht per Hand kopieren.
   **Der konkrete Bruch:** die Mediastreamer-Plugins liegen unter `lib/mediastreamer/plugins/`, nicht neben den Haupt-DLLs. Genau die werden beim Paketieren übersehen. Ohne `libmswasapi.dll` gibt es kein Audio unter Windows — und die Fehlermeldung sagt nichts darüber. Deshalb: **Plugin-Verzeichnisstruktur relativ erhalten, nicht flach kopieren**, im MSIX ein `runtimes/win-x64/native`-taugliches Layout herstellen, und nach dem Kopieren mit `dumpbin /dependents` gegenprüfen, **bevor** etwas gestartet wird.
3. **MSIX und native Bibliotheken.** Klassische Bruchstelle — belegt in der `System.DllNotFoundException`-Sektion des NuGet-README des SDK und generisch in `microsoft/WindowsAppSDK#2413` (DLL wird nicht ins Paket kopiert). Deshalb der Doppeltest in M1. Dritte häufige Ursache neben fehlenden Plugins: Architektur-Mismatch.
4. **Bitness.** Alles x64. Ein AnyCPU-Projekt, das als x86 startet, lädt die DLLs nicht.
5. **Windows-Firewall.** Beim ersten Start fragt Windows nach der Freigabe. Im MSIX-Manifest die Capability setzen und in der Installationsanleitung erwähnen.
6. **TLS-Zertifikate.** Das SDK will eine CA-Datei, nicht den Windows-Zertifikatspeicher. Root-CAs beim Start exportieren und dem Core als Datei übergeben.
7. **SRTP erzwingen** bricht Gespräche zu Gegenstellen ohne SRTP hart ab. Fehlermeldung muss den Grund nennen, sonst wird der Client für „kaputt“ erklärt.
8. **BLF-Subscribes** erzeugen Last auf der PBX. Nur für Kontakte in der Team-Liste subscriben, nicht für Outlook-Kontakte.
9. **Aufnahme und Recht.** Sichtbarer Indikator, siehe §8.2.
10. **Codec-Reihenfolge** wird gern still ignoriert, wenn man die Liste falsch setzt. Immer im SIP-Log gegenprüfen, nicht dem UI-Zustand glauben.
11. **Doppelte Registrierung.** Wenn dieselben Zugangsdaten schon auf einem Tischtelefon registriert sind, verhält sich die PBX je nach Konfiguration anders. Beim Testen bewusst sauber halten.

---

## 15. Konventionen

- C# 12, Nullable aktiviert, `TreatWarningsAsErrors`, `dotnet format` vor jedem Commit.
- Async durchgängig mit `Async`-Suffix und `CancellationToken`; `async void` nur in Event-Handlern.
- Keine Singletons per `static`; alles über DI.
- **Schreibweise des Produktnamens:** in allen Benutzertexten, im Fenstertitel und in der Dokumentation **nipp** in Kleinbuchstaben, nie „Nipp“ oder „NIPP“. Im Code dagegen `Nipp` als Namespace- und Assembly-Präfix (`Nipp.App`, `Nipp.Core`, `Nipp.Provisioning`); Ordner auf der Platte und das Repo heissen `nipp`.
- **Alle für Benutzer sichtbaren Texte auf Hochdeutsch** (kein „ß“, sondern „ss“), in `Strings/de-CH.resw`. Code, Kommentare und Commits auf Deutsch oder Englisch, aber einheitlich.
- Fehlermeldungen sagen, was passiert ist und was zu tun ist. „Registrierung fehlgeschlagen: Server nicht erreichbar (Zeitüberschreitung nach 5 s). Netzwerkverbindung und Domain prüfen.“ statt „Fehler beim Registrieren“.
- Commits klein und thematisch, mit Meilenstein-Präfix: `M3: Weiterleitung begleitet`.
- Jede Abweichung von diesem Dokument als ADR in `docs/decisions.md`: Kontext, Entscheidung, Konsequenz.

---

## 16. Offene Entscheidungen für Dominic

Diese Punkte nicht selbst entscheiden, sondern beim Erreichen sammeln und gemeinsam klären. Die Reihenfolge nach tatsächlichem Blockierzeitpunkt steht in `docs/plans/IMPLEMENTATION-PLAN.md` §2.

**Drei davon haben Vorlaufzeit und gehören sofort angestossen**, obwohl sie erst spät blockieren: der Test-Trunk (Punkt 5, blockiert bereits M1 und damit alles), die Lizenzanfrage bei Belledonne (Punkt 1) und das Signaturzertifikat (Punkt 2) — Beschaffung dauert dort Wochen.

1. **Lizenz** — ~~AGPLv3-Offenlegung oder kommerzielle Lizenz~~ **entschieden am 11.09.2026: AGPLv3, der Quelltext wird offengelegt** (ADR-040). `LICENSE` und `NOTICE` liegen im Repo. Damit ist die Sperre weg, die seit dem 04.09.2026 jede Abgabe ausser Haus verhindert hat. **Offen bleibt nur der Repo-Wechsel selbst** — das ist keine Lizenzfrage mehr, sondern Handarbeit (`docs/plans/RELEASE-PLAN.md` R10). *Der frühere Eintrag «nipp bleibt vorerst intern» stand hier bis zum 13.09.2026 und war seit zwei Tagen überholt.*
2. **Branding** — der Name **nipp** steht fest; Icon, Wortmarke, Signaturzertifikat und der Anzeigename im Startmenü fehlen noch. Ohne echtes Zertifikat ist das MSIX nur selbstsigniert und damit nicht verteilbar.
3. **Verteilung** — ~~MSIX über Intune, oder klassisches MSI/EXE~~ **entschieden am 07.09.2026: Velopack-Setup, unpackaged und self-contained** (ADR-038). MSIX bleibt gebaut und wartet auf T110 (packaged bekommt keine Toasts) und AP9.2 (ohne Zertifikat nicht installierbar); für Intune-Umgebungen ist es dann wieder die bessere Wahl. Weg und Bedienung in `docs/updates.md`.
4. **Update-Mechanismus** — ~~App-Installer-URL, eigener Update-Check oder Verteilung über Intune~~ **entschieden am 07.09.2026: GitHub Releases mit den Kanälen stable und beta** (ADR-039). Beim Start wird nachgesehen, aber nichts geladen; angewandt wird auf Knopfdruck und nie während eines Gesprächs. Solange das Repo privat ist, kommt das Zugriffstoken über die Provisionierung (`update.token`) und liegt über DPAPI im `SecretStore`.
5. **Test-Trunk** — Zugangsdaten und Nebenstelle für automatisierte Tests.
6. **Outlook-Zugriff** — ~~Graph oder COM~~ **entschieden am 04.09.2026: COM-Interop** (ADR-009). Graph wird nicht gebaut, bleibt aber nachrüstbar. **Die Begründung ist seit dem 06.09.2026 eine andere** (ADR-018, §22.4): auf dem neuen Outlook gibt es kein COM, und Graph ist nicht «unnötig», sondern *zurückgestellt* — die Entra-Registrierung bindet einen Administrator und dauert Wochen. Die Frage kehrt zurück, sobald der erste Arbeitsplatz ausserhalb der Entwicklung dort Kontakte vermisst.
7. **Aufnahme-Ablage** — ~~lokal oder Netzpfad~~ **entschieden am 04.09.2026: lokal, Pfad in den Einstellungen konfigurierbar.** Standard `%LOCALAPPDATA%
ipp
ecordings`. Ein Netzpfad je Kunde ist nicht vorgesehen und wird nicht vorgebaut.

---

## 17. `CLAUDE.md` für das Repo

Beim Anlegen des Repos (M0) diese Datei mit folgendem Inhalt erstellen — sie ist der Kurzkontext für jede weitere Sitzung:

```markdown
# nipp — Projektkontext

Windows-Softphone (WinUI 3, .NET 8, x64) auf Basis des Linphone SDK 5.5.18.
Vollständige Spezifikation: NIPP-BUILD.md — im Zweifel dort nachlesen.
Phasen und Arbeitspakete: docs/plans/IMPLEMENTATION-PLAN.md.

## Grenzen
- `using Linphone` ausschliesslich in `src/Nipp.Core/Services/Telephony/`.
  Ein Architekturtest erzwingt das.
- ViewModels kennen keine SDK-Typen, nur eigene Modelle.
- `Core.Iterate()` läuft im UI-Thread (DispatcherQueueTimer, 20 ms).
  Nichts Blockierendes in SDK-Callbacks.

## Befehle
- Bauen: `dotnet build -c Debug`
- Tests: `dotnet test`
- Formatieren: `dotnet format`
- Paket: siehe docs/packaging.md

## Regeln
- Nullable ein, Warnungen sind Fehler.
- Benutzertexte auf Hochdeutsch (ss statt ß), in Strings/de-CH.resw.
- Kein Feature ohne Auftrag aus NIPP-BUILD.md.
- Abweichungen als ADR in docs/decisions.md.
- Nie gegen Kundentenants testen — nur der Test-Trunk.

## Aktueller Meilenstein
M0 — bei Fortschritt hier aktualisieren.
```

---

## 18. Quellen

Stand der Verifikation: 04.09.2026.

- [linphone-sdk auf GitHub (Spiegel des GitLab-Repos)](https://github.com/BelledonneCommunications/linphone-sdk) — Lizenzmodell AGPLv3/proprietär, Build-Voraussetzungen Windows, CMake-Presets. **Keine Releases im Spiegel** — Tags über `git/refs/tags`, Notes nur im CHANGELOG
- [linphone-sdk Tags](https://github.com/BelledonneCommunications/linphone-sdk/tags) — **5.5.18 vom 03.09.2026** ist der aktuelle stabile Tag, danach nur `5.6.0-alpha`
- [linphone-sdk CHANGELOG](https://github.com/BelledonneCommunications/linphone-sdk/blob/master/CHANGELOG.md) — 5.5.0 vom 25.05.2026: Submodule eingeklappt, RNNoise, AEC3 statt AECM, HIDAPI/Jabra, ISAC und iLBC entfernt; VS 2022 ab 5.4.0
- [Prebuilt-SDKs für Windows](https://download.linphone.org/releases/windows/sdk/) — `linphone-sdk-win64-5.5.18.zip`, Inhalt geprüft (Wrapper + DLL-Kette + Plugins). **Weg C in §5, der verifizierte Beschaffungsweg**
- [NuGet-Paketierung im linphone-sdk](https://github.com/BelledonneCommunications/linphone-sdk/blob/master/cmake/NuGet/README.md) — `LinphoneSDK.Windows`, Win32- vs. UWP- vs. Store-Variante, Packager-Optionen, **`System.DllNotFoundException`-Sektion und NuGet-Cache-Fallen** (§14.2)
- [Linphone-Wiki: Getting started Windows](https://wiki.linphone.org/xwiki/wiki/public/view/Lib/Getting%20started/Windows%20UWP/) — Quelle für **projectId 411** und den Paketnamen; Erreichbarkeit des Feeds war nicht prüfbar
- [LinphoneSDK auf nuget.org](https://www.nuget.org/packages/LinphoneSDK) — legacy, UWP-only, 3.12.0.273 von 2017, verweist selbst auf die Belledonne-Registry; **nicht verwenden**
- [C#-API-Referenz Core](https://download.linphone.org/releases/docs/liblinphone/latest/cs/api/Linphone.Core.html) und [Factory](https://download.linphone.org/releases/docs/liblinphone/latest/cs/api/Linphone.Factory.html) — Member-Namen in §6
- [Linphone C# wrapper (Wiki)](https://wiki.linphone.org/xwiki/wiki/public/view/Lib/Linphone%20C%23%20wrapper/) — dünn, Wrapper-Datei ist die bessere Quelle
- [Belledonne Kontakt](https://www.linphone.org/en/contact/) / [Developer-Formular](https://linphone.typeform.com/to/kCg6gOWV) — Weg zur kommerziellen Lizenz (§3)
- [microsoft/WindowsAppSDK#2413](https://github.com/microsoft/windowsappsdk/issues/2413) — native DLL wird nicht ins MSIX kopiert; das generische Muster hinter §14.2/§14.3

---

## 20. Nachtrag Rev. 5 — Anforderungen von Dominic vom 04.09.2026

Diese Anforderungen kamen nach der Abnahme von M3 dazu. Wo sie §8 widersprechen, **gehen sie vor** — die Oberfläche aus §8 war ein Entwurf, dies ist die Vorgabe.

### 20.1 Oberfläche: Smartphone-Format statt Navigationsleiste

**Ersetzt die Struktur aus §8.** Statt `NavigationView` mit fünf Ansichten ein schmales, platzsparendes Fenster in Smartphone-Proportionen. Aufbau der Hauptansicht **in dieser Reihenfolge von oben nach unten**:

1. **SIP-Konto auswählen**, mit Status-LED je Konto
2. **Eingabefeld**, mit einem `×` ganz rechts im Feld zum Leeren
3. **Wählen-Schaltfläche** rechts neben dem Eingabefeld
4. **Wähltastatur**, ein- und ausblendbar
5. **Inhaltsbereich** — umschaltbar zwischen Kontakten und Anrufliste (Mailbox entfallen, ADR-062)
6. **Umschaltleiste** unten: Kontakte, Anrufliste, Einstellungen (ADR-062)

Das aktive Gespräch bleibt eine eigene Ansicht (§8.2 gilt inhaltlich weiter), erscheint aber im selben schmalen Fenster.

### 20.2 Mehrere SIP-Konten

**Bis zu zehn Konten** gleichzeitig. §9.1 erlaubte mehrere, ohne eine Zahl zu nennen — jetzt ist sie zehn. Jedes Konto hat eine eigene Status-Anzeige, und für ausgehende Anrufe lässt sich eines auswählen.

### 20.3 Anrufliste: nur das Wichtigste

**Ersetzt die Spaltenliste aus §8.3.** Angezeigt werden nur:

- Nummer (beziehungsweise aufgelöster Name)
- Dauer
- Uhrzeit
- was mit dem Anruf passiert ist (angenommen, verpasst, abgelehnt, Fehler)

Die übrigen Felder aus §8.3 werden weiterhin **gespeichert** — sie kosten nichts und werden für die Diagnose gebraucht —, aber nicht angezeigt.

### 20.4 Erscheinungsbild: hell, dunkel, oder wie Windows

Umschaltbar zwischen hellem und dunklem Erscheinungsbild. **Standard ist „wie Windows"** — die Einstellung des Betriebssystems wird übernommen und Änderungen daran werden im laufenden Betrieb nachgezogen.

Die Symbole liegen unter `Icons/` als `nipp-light.png` und `nipp-dark.png` vor und sind einzubauen.

### 20.5 Immer im Vordergrund

Ein Schalter in den Einstellungen, der das Fenster über allen anderen hält. Standard: aus.

---

## 21. Nachtrag Rev. 6 — Integrationsplattform (06.09.2026)

Anforderung von Dominic vom 06.09.2026. Ausgearbeitet in `docs/plans/INTEGRATION-PLAN.md`; dieser Paragraf ist der **Auftrag** dazu und legt Umfang, Grenzen und Sicherheitsregeln fest. Wo der Umsetzungsplan ins Detail geht, gilt er — wo er diesem Paragrafen widerspricht, gilt dieser.

### 21.1 Was gebaut wird

Ein **generisches Integrations-Framework**, über das externe Systeme angebunden werden: CRM, ERP, Ticketing, beliebige REST-APIs. Ausdrücklich **keine** fest verdrahtete Anbindung an ein bestimmtes Produkt.

Zwei Anwendungsfälle, eine gemeinsame Infrastruktur:

1. **Anruferkontext.** Bei einem Anruf wird die Rufnummer normalisiert und an die konfigurierten Systeme geschickt. Die Antworten werden auf ein gemeinsames Kontextmodell abgebildet und auf einer konfigurierbaren Karte in der Gesprächsansicht gezeigt.
2. **Kontaktsuche.** Die bestehende Kontaktliste (§8.4) wird verallgemeinert: Team, Outlook und externe Systeme liefern auf dasselbe Kontaktmodell.

**Es entstehen nicht zwei Frameworks.** HTTP, Authentifizierung, Geheimnisse, Mapping, Protokollierung, Zeitgrenzen, Abbruch, Konfiguration, Zustand und Fehlerbehandlung sind für beide dieselben.

### 21.2 Unverhandelbare Grenzen

Diese Punkte gelten wie §6 und §14 — sie sind der Grund, warum die Plattform ein Softphone nicht gefährdet:

1. **Telefonieren hängt von keiner Integration ab.** Fällt jedes externe System aus, klingelt, wählt und spricht nipp unverändert.
2. **Die Gesprächsansicht erscheint sofort**, bevor irgendein externes System geantwortet hat. Externe Angaben werden nachgeladen und die Anzeige inkrementell ergänzt.
3. **Jede Quelle ist isoliert**: eigene Zeitgrenze, eigener Fehlerzustand, eigener Abbruch. Eine langsame Quelle verzögert keine andere.
4. **Nichts Blockierendes im SDK-Callback** (§14.1). Ein Lookup wird im Ereignis nur angestossen, nie erwartet.
5. **`Services/Integrations/` kennt weder das SDK noch WinUI.** Ein Architekturtest erzwingt beides, wie §6 für `Linphone`.
6. **Keine ausführbaren Skripte und kein frei konfigurierbares Markup.** Berechnete Felder laufen über eine bewusst eingeschränkte Ausdruckssprache ohne Zugriff auf Typen, Dateien oder Umgebung. Karten bestehen aus einer festen Menge eigener Komponenten, nicht aus HTML oder XAML.
7. **Geheimnisse gehören in den `SecretStore`** (DPAPI, §10), niemals in eine Konfigurationsdatei. Protokoll und Diagnosepaket enthalten weder Zugangsdaten noch Rufnummern, Suchtexte oder Antwortinhalte.
8. **Nur https**, mit derselben ausdrücklichen Ausnahme wie bei der Provisionierung (ADR-012). Antwortgrösse und Zeitgrenze sind begrenzt.

### 21.3 Konfiguration

Die Integrationskonfiguration steht in einer **eigenen Datei** `%APPDATA%\nipp\integrations.json` mit eigenem Schema und eigener Version — nicht in `settings.json` (ADR-017). Verteilt wird sie über das Provisionierungsprofil (§11) als Verweis, und `PolicyService` kann sie unter dem Pfad `integrations` sperren.

Beschrieben werden je Quelle: Kennung, Anzeigename, Typ, Endpunkt, Methode, Query, Rumpf, Kopfzeilen, Authentifizierung, Zeitgrenze, aktiv, und welche **Fähigkeiten** sie anbietet. Eine Quelle muss nicht alle Fähigkeiten können; die Oberfläche bietet nur an, was konfiguriert ist.

### 21.4 Entscheidungen vom 06.09.2026

| Punkt | Entscheidung |
|---|---|
| Kontaktsuche | Suchfeld im Kontakte-Tab, sichtbar nur bei aktiver externer Suchquelle. Die Vorschläge unter dem Nummernfeld bleiben lokal und bei fünf Treffern (§8.1) |
| Ausgehende Anrufe | werden ebenfalls nachgeschlagen, Standard ein |
| Interne Nummern | gehen **nicht** an externe Systeme, Standard aus |
| Ablage | eigene Datei, siehe 21.3 |
| Karten | konfigurierbar aus fester Komponentenmenge; ein visueller Designer ist eine spätere Ausbaustufe |
| Erstes echtes Zielsystem | das CRM, ausschliesslich gegen einen Test-Mandanten (§13: nie gegen Kundentenants) |
| Toast (§8.6) | ~~bleibt vorerst unverändert bei Name oder Nummer~~ — **abgelöst durch ADR-030 (07.09.2026):** angereichert, sobald der Kontext da ist. Die Bedingung „bis T06 abgenommen ist" ist erfüllt |
| Anrufliste (§8.3) | darf einen extern aufgelösten Namen speichern |

### 21.5 Nicht im Umfang

Schreiben in Fremdsysteme, OAuth2, Microsoft Graph, ein Cache auf der Platte, ein visueller Karten-Designer und produktspezifische Connectoren gehören **nicht** zum ersten Ausbau. Die Architektur muss sie ermöglichen, ohne dass der Kern umgebaut wird; vorgebaut wird nichts (§2).

> **Nachtrag 07.09.2026:** Der visuelle Karten-Designer und die produktspezifischen Connectoren sind mit **§21.6** vorgezogen — auf Auftrag, nicht als Abweichung. Für alles Übrige in diesem Abschnitt gilt der Satz unverändert.

---

### 21.6 Nachtrag Rev. 8 — Einrichtung und Karten (07.09.2026)

**Anlass.** Drei Sätze nach einem halben Tag mit den beiden angebundenen
Quellen: „Die Einstellungen für das CRM und das Gesprächsjournal finde ich noch nicht
optimal." — „Ich möchte ja auch andere API später noch anbinden können. das CRM
und Call Memory ist nur für uns aktuell." — „Zudem wäre es super wenn ich die
Werte selber in der Info-Card anordnen und auswählen könnte."

**Das ist das Vorziehen von I9 und I12**, nicht eine Abweichung. §21.5 nimmt
einen visuellen Karten-Designer und produktspezifische Connectoren aus dem
**ersten** Ausbau heraus; `docs/plans/INTEGRATION-PLAN.md` führt beide mit dem Vermerk,
dass sie keinen Umbau des Kerns verlangen. Genau das wird hier eingelöst. §21.5
gilt für alles Übrige unverändert.

**Der Befund, der dem Auftrag zugrunde liegt und ihn ändert.** Der dritte Wunsch
traf keine schlechte Bedienung, sondern eine Lücke: `IntegrationConfig` hatte
**kein `cards`**, obwohl `docs/plans/INTEGRATION-PLAN.md` D.2 die Datei genau so
beschreibt. Eine eigene Karte war nicht schwer einzurichten, sondern
unmöglich. Und „Einlesen" ersetzte die **ganze** Konfiguration — eine zweite
Quelle ging nur über Handarbeit im JSON, was den zweiten Wunsch verhinderte.

#### Auftrag

| Punkt | Was gilt |
|---|---|
| **Karten in der Datei** | Karten stehen in `integrations.json` in der Form aus D.2. Leer heisst: es gelten die mitgelieferten. Je Art gilt eine; eine zweite ist ein Befund, keine stille Auswahl (ADR-032) |
| **Karten-Editor** | Ein **eigenes Fenster** mit Felderpalette, Aufbau, Eigenschaften und Vorschau. 400 Pixel (§20.1) tragen keine Palette; die Vorschau ist trotzdem genau 400 Pixel breit (ADR-032) |
| **Quelle hinzufügen** | Ein **Katalog** in nipp: „Eigene REST-API", „das CRM", „das Gesprächsjournal". Die beiden bv2-Einträge sind als **intern bv2** gekennzeichnet — ein Kunde sieht die Namen, das ist entschieden (ADR-033) |
| **Zusammenführen** | Hinzufügen fügt **eine** Quelle ein und lässt die anderen unberührt, auch die globalen Einstellungen. „Einlesen" ersetzt weiter alles — für die Verteilung — und sagt, wie viele Quellen das kostet (ADR-033) |
| **Zugangsdaten** | Je Quelle, mit **Beschriftung** und **woher das Token kommt**. Nie mehr ein freies Feld für den technischen Verweis (ADR-033) |
| **Feldnamen** | Eine Stelle entscheidet, welcher Feldname was bedeutet: der `FieldCatalog`. `role('name')` und `anyOf(...)` fragen nach **Bedeutung** statt nach Quelle; die mitgelieferten Karten nennen deshalb keine Quelle mehr mit Namen |
| **Toast** | Wird eine Kartenart. Ohne eigene Karte gilt exakt ADR-030; eine eingerichtete Karte ersetzt die mitgelieferte Zusammensetzung (ADR-034) |
| **Fähigkeiten** | Werden **angezeigt, nicht geschaltet.** Eine Fähigkeit *ist* ihr Block in der Konfiguration (§21.3); sie abzuschalten hiesse, Endpunkt und Mapping zu löschen. Geschaltet wird die Quelle |

#### Was dabei nicht verhandelbar war

- **Telefonieren hängt von keiner Integration ab** (§21.2). Fällt der Designer
  aus, klingelt und wählt nipp unverändert.
- **Die Abnahme des Toasts ist, dass die bestehenden Tests unverändert
  durchlaufen.** Was am Gerät belegt ist, darf durch Konfigurierbarkeit nicht
  anders werden.
- **Kein Cache auf der Platte** (§21.2). Die Testdaten für die Kartenvorschau
  bleiben im Arbeitsspeicher; damit die Vorschau trotzdem nie leer ist, bringt
  jede Vorlage eine **erfundene** Beispielantwort mit.
- **Eine Vorlage wird nie eingeschaltet ausgeliefert.** Eingeschaltet wird nach
  einem erfolgreichen Testabruf.

#### Was weiterhin JSON bleibt

Endpunkte und Mappings. Ein Formular je Feld wäre eine zweite Beschreibung
derselben Sache, mit eigener Prüfung und eigenen Fehlermeldungen, und müsste
bei jeder Erweiterung nachgezogen werden. **Neu ist nur:** das JSON gehört
jetzt zu **einer** Quelle statt zur ganzen Datei. Eine kaputte Bearbeitung geht
damit auf Kosten dieser Quelle, und die anderen bleiben stehen.

Umsetzung und Phasen: `docs/plans/EINRICHTUNG-PLAN.md`. Karten für Menschen:
`docs/integrations/karten.md`. Feldnamen: `docs/integrations/feldnamen.md`.

---

## 22. Nachtrag Rev. 7 — Bedienung im Alltag (06.09.2026)

Diese vier Anforderungen kamen, nachdem nipp einen ganzen Tag im Alltag benutzt
wurde (`docs/plans/ALLTAG-PLAN.md`) und die Gesamtprüfung vom 06.09.2026 daneben lief. Sie
**ergänzen** §8 und §20, sie ersetzen dort nichts.

Jede ist mit einem ADR entschieden; die Begründung steht dort und wird hier
nicht wiederholt.

### 22.1 Weiterleiten: Vorschlagsliste statt leerem Feld (ADR-023)

**Ergänzt §8.2.** Das Ziel einer Übergabe wird aus einem Feld mit
Vorschlagsliste gewählt, nicht nur getippt.

- **Team-Nebenstellen stehen zuoberst, mit ihrer Präsenzlampe.** Beim
  Weiterverbinden ist „ist die Person überhaupt frei" die eigentliche Frage,
  und das Besetztlampenfeld (§8.4) hat die Antwort bereits abonniert.
- Danach die übrigen Kontaktquellen.
- **Freie Eingabe bleibt möglich.** Eine externe Nummer muss weiterhin ohne
  Umweg eingetippt werden können; die Liste ergänzt das Feld, sie ersetzt es
  nicht.

### 22.2 Wahlwiederholung im leeren Nummernfeld (ADR-024)

**Ergänzt §20.1.** Beim Hineinklicken in das **leere** Nummernfeld erscheinen
die **fünf zuletzt gewählten** Nummern.

- **Nur abgehende Anrufe**, ohne Doppelte. Wer zurückrufen will, hat dafür die
  Anrufliste mit ihren Filtern.
- Je Zeile der aufgelöste Name, darunter die Nummer, rechts wie lange es her
  ist.
- **Nur bei leerem Feld.** Ab dem ersten Zeichen gilt die Vorschlagsliste aus
  §8.1.

### 22.3 Anruferkontext in der Anrufliste (ADR-027)

**Ergänzt §20.3 und §21.1.** Ein Eintrag der Anrufliste lässt sich aufklappen
und zeigt dann, was die angebundenen Quellen zu dieser Nummer wissen — dieselbe
Karte wie im Gespräch.

- **Abgerufen wird beim Aufklappen, gespeichert wird nichts.** §21.2 erlaubt der
  Anrufliste einen extern aufgelösten Namen, keinen Gesprächsinhalt. Die Zusage
  an das das API-Team des Journals („höchstens fünf Minuten im Arbeitsspeicher, nie auf
  die Platte") gilt unverändert.
- **Nur eine Anfrage gleichzeitig.** Wer durch die Liste klickt, bricht die
  vorige ab.
- Ohne Quelle oder ohne Netz bleibt der Bereich leer **mit Begründung**, nie ein
  leerer Kasten.

### 22.4 Outlook-Kontakte: es bleibt bei COM (ADR-018)

**Keine Änderung an §8.4.** Persönliche Kontakte kommen weiterhin über
COM-Automatisierung aus einem laufenden klassischen Outlook.

Microsoft Graph wurde erwogen und **bewusst zurückgestellt**. Der Grund und die
Kosten stehen in ADR-018; hier nur die Folge für die Spezifikation:

- **§8.4 gilt unverändert.** Wo ein klassisches Outlook läuft, funktioniert
  alles wie beschrieben.
- **Wo nur das neue Outlook (`olk.exe`) läuft, bleiben die persönlichen
  Kontakte aus.** Es bietet keine COM-Automatisierung an; der Weg aus §8.4 ist
  dort nicht kaputt, sondern nicht vorhanden. nipp sagt das seit dem 06.09.2026
  in der Oberfläche, wie §8.4 es für den Ausfall verlangt.
- Team-Nebenstellen (§8.4) und die Kontaktsuche über die Integrationsplattform
  (§21) sind davon nicht betroffen.

**Wann diese Entscheidung wieder aufgemacht wird:** sobald der erste
Arbeitsplatz ausserhalb der Entwicklung auf das neue Outlook wechselt und dort
Kontakte vermisst. Die Antwort ist dann vorbereitet (ADR-018, Weg B), und die
Entra-App-Registrierung gehört in demselben Moment angestossen — sie ist der
lange Teil.

### 22.5 Die Tasten am Headset (ADR-028)

**Anlass:** ein Anruf im Alltag am 07.09.2026. Das Gespräch stand achtzehn
Minuten ohne Beanstandung — aber die Auflegen-Taste am Headset tat nichts.

**Auftrag.** Wer ein Headset mit Telefonietasten benutzt, kann damit
**annehmen, auflegen und stummschalten**. Der Zustand des Geräts folgt dem
Gespräch: die Off-Hook-Lampe leuchtet, solange eines läuft, die Ring-Lampe
blinkt, solange es klingelt.

**Warum das ein eigener Auftrag ist und nicht Teil von §9.4.** §9.4 handelt von
der Wahl der Audiogeräte; das ist eine andere Sache als die Tasten darauf. Bis
Version 5.4 hätte es hier auch nichts zu tun gegeben: das Linphone SDK brachte
eine Anbindung über HIDAPI mit, die Jabra-Geräte direkt ansprach. **Mit 5.5.0
wurde sie entfernt** (§5, Änderungsprotokoll des SDK). nipp benutzt 5.5.18 und
hat sie deshalb nicht.

**Die Regeln, die dabei gelten:**

- **Zustandsabhängig, wie am Telefon.** Dieselbe Taste nimmt an, wenn es
  klingelt, und legt auf, wenn ein Gespräch läuft. Ein Druck ohne Anruf ist
  bedeutungslos, nicht falsch.
- **Ein ausgehender Anruf gilt ab dem Wählen als abgenommen.** Sonst wäre die
  Taste ohne Wirkung, solange es beim Gegenüber klingelt — und genau dann
  bricht man einen Anruf am häufigsten ab.
- **Bei zwei Gesprächen (§8.2) trifft die Taste das im Vordergrund** —
  dasselbe, das der Knopf in der Oberfläche auflegt. Zwei verschiedene
  Bedeutungen für „auflegen" wären schlimmer als eine unvollständige.
- **Klingeln schlägt Gespräch.** Klingelt etwas, blinkt die Ring-Lampe, auch
  wenn daneben ein Gespräch läuft. Beide Lampen zugleich wären für das Gerät
  ein Widerspruch.
- **Telefonieren hängt nicht daran.** Fehlt das Headset, bietet es keine
  Telefonietasten an oder verhält sich sein Treiber anders als erwartet, dann
  klingelt und wählt nipp unverändert — nur eben mit der Maus. Dieselbe Regel
  wie für die Integrationen in §21.

**Was ausdrücklich nicht dazugehört:** die Audiowege bleiben beim SDK. Ein
Headset, das über HID ein Gerät umschalten möchte, wird nicht bedient — nipp
wählt seine Geräte nach §9.4 selbst, und zwei Stellen, die dasselbe Gerät
steuern, kämen sich in die Quere.

**Und der Satz gilt auch für zwei Programme (Nachtrag vom 10.09.2026).** Das
Call-Control-Interface eines Headsets wird geteilt: Teams hängt daran wie nipp,
und was das Gerät auf eine Meldung von nipp hin zurückmeldet, liest Teams als
Tastendruck des Benutzers — im Meeting heisst das auflegen. **nipp meldet dem
Gerät deshalb nur, wenn es selbst einen Anlass hat**, also einen eigenen Anruf,
und der Ring-Bericht schweigt, solange ein anderes Programm das Gerät im
Gespräch hält. Was nach dem Annehmen geschieht, ist die Entscheidung des
Benutzers und darf ein fremdes Gespräch kosten. Vollständig in ADR-028,
Nachtrag 4.

Der Weg dahin und die zwei verworfenen Wege stehen in ADR-028.

---

## 23. Nachtrag Rev. 9 — Das breite Fenster (13.09.2026)

**Anlass:** Anforderung von Dominic vom 13.09.2026, nachdem ADR-046 einen Tag
zuvor die Inhaltsbreite gedeckelt hatte. Vollständig in **ADR-047** und
**ADR-048**, der Umsetzungsweg in `docs/plans/BREITBILD-PLAN.md`.

**Dieser Nachtrag ergänzt §20.1, er ersetzt es nicht.** Das schmale Fenster im
Smartphone-Format bleibt die Vorgabe, der Normalfall und der Zustand, in dem
nipp ausgeliefert wird. Was hier steht, tritt **ab einer Breite** hinzu.

### 23.1 Zwei Spalten ab 960 Pixeln

Ab **960 logischen Pixeln** Fensterbreite gliedert sich die Hauptansicht in
zwei Spalten:

- **Links**, in der Reihenfolge aus §20.1: Kontoauswahl, Eingabefeld mit
  Wählen-Schaltfläche, Wähltastatur und der über die Umschaltleiste gewählte
  Bereich (Kontakte oder Anrufliste).
- **Rechts**: die **Team-Nebenstellen als Kacheln**, nach Gruppen gegliedert.
  Sie stehen dort **unabhängig davon, was links gewählt ist**.

**Die Umschaltleiste steht unten und steuert die linke Spalte.** Im Bereich
«Kontakte» zeigt sie links dann nur noch Outlook-Kontakte und Suchtreffer — die
Nebenstellen stehen ja rechts. **Seit ADR-052 steht sie auch nur unter der
linken Spalte**; das Kachelfeld reicht daneben bis an den unteren Rand.

**Die Breite gehört dem Fenster** (ADR-052): der Inhalt füllt es, und die linke
Spalte ist im breiten Layout **fest** 480 Pixel breit, während die Kacheln den
ganzen Rest nehmen. Die Deckelung aus ADR-046 — erst fürs Fenster, dann je
Spalte (ADR-047) — ist damit aufgehoben.

Unterhalb der Schwelle gilt §20.1 unverändert, **ausser dass der Inhalt auch
dort die volle Fensterbreite nutzt**. Die Mindestbreite bleibt 320 Pixel.

### 23.2 Was auf einer Kachel steht

Eine Kachel zeigt **ohne weiteren Klick**:

- den Namen,
- die Firma, wo es eine gibt,
- den Zustand der Nebenstelle als **Farbe und Text** (§8.4 gilt unverändert),
- **jede Nummer einzeln wählbar** — Nebenstelle und Handynummer (ADR-041).

Das ist der Zweck der Form: sie ersetzt «anklicken, aufklappen, lesen» durch
«hinsehen». Die häufigste Frage an ein Softphone lautet *„ist der Kollege
frei?"*.

Umsortieren zwischen Gruppen (ADR-042) gilt für die Kacheln unverändert, mit
der Maus wie über das Kontextmenü.

### 23.3 Ein angeklickter Kontakt klappt in der Zeile auf

**Unabhängig von der Fensterbreite** und in **allen** Kontaktlisten: wer eine
Zeile anklickt, bekommt ihre Angaben **unter dieser Zeile** und nicht am
unteren Rand der Ansicht.

Das ändert ADR-046 in einem Punkt. Der Grund dort — dieselbe Handlung darf
nicht an gleich aussehenden Zeilen zwei Ergebnisse haben — bleibt eingehalten:
es ist für alle drei Listen derselbe Ort.

### 23.4 Ein maximiertes Fenster kommt maximiert zurück

Wer nipp maximiert beendet, findet es beim nächsten Start maximiert vor. Bis
zum 13.09.2026 wurden nur Lage und Grösse gemerkt, und beide wurden zusätzlich
auf 92 Prozent des Arbeitsbereichs begrenzt — das Fenster kam mit Rand ringsum
zurück.

---

## 19. Änderungsprotokoll

### Rev. 9 — 13.09.2026

Anlass: Anforderung von Dominic, nachdem ADR-046 die Inhaltsbreite auf 480
Pixel gedeckelt und dabei festgehalten hatte, dass der Platz eines breiten
Fensters nur mit einer zweiten Spalte zu nutzen wäre — „ein eigener
Entscheid". Vollständig in **§23**; Entscheidungen in ADR-047 und ADR-048.

| § | Was | Verhältnis zum Bestand |
|---|---|---|
| 23.1 | Zwei Spalten ab 960 Pixeln, Nebenstellen rechts als Kacheln | **ergänzt** §20.1; unterhalb der Schwelle gilt §20.1 unverändert. **Nachtrag ADR-052 (12.09.2026):** die Deckelung aus ADR-046 entfällt ganz — der Inhalt füllt das Fenster, links steht fest 480, rechts der Rest |
| 23.2 | Was auf einer Kachel steht — Name, Firma, Zustand als Farbe **und** Text, jede Nummer wählbar | ergänzt §8.4; §8.4 bleibt unverändert bindend |
| 23.3 | Ein angeklickter Kontakt klappt in der Zeile auf, in allen Listen | **ändert** den Ort aus ADR-046; die Regel dahinter — ein Ort für alle Listen — bleibt |
| 23.4 | Ein maximiertes Fenster kommt maximiert zurück | Fehlerkorrektur, kein neuer Auftrag |

### Rev. 7 — 06.09.2026

Anlass: der erste ganze Tag im Alltag (`docs/plans/ALLTAG-PLAN.md`) und die Gesamtprüfung
vom selben Abend (`docs/plans/GESAMT-REVIEW.md`). Vollständig in §22; Entscheidungen in
ADR-018 und ADR-023 bis ADR-027.

**Drei der vier Punkte sind Aufträge, einer ist es ausdrücklich nicht:** §22.4
hält fest, dass Microsoft Graph erwogen und zurückgestellt wurde. Er steht hier,
damit die Frage nicht ein zweites Mal von vorn beantwortet wird.

| § | Was | Verhältnis zum Bestand |
|---|---|---|
| 22.1 | Weiterleiten mit Vorschlagsliste, Team und Präsenz zuoberst | **ergänzt** §8.2, ersetzt nichts |
| 22.2 | Fünf zuletzt gewählte Nummern im leeren Nummernfeld | ergänzt §20.1 |
| 22.3 | Anruferkontext in der Anrufliste, bei Bedarf abgerufen | ergänzt §20.3 und §21.1; §21.2 bleibt unverändert bindend |
| 22.4 | Outlook-Kontakte: es bleibt bei COM, Graph zurückgestellt | **ändert nichts** an §8.4; hält nur fest, was ohne klassisches Outlook fehlt und wann die Frage wiederkommt |
| 22.5 | Annehmen, Auflegen und Stumm über die Tasten am Headset | **ergänzt** §9.4, ersetzt nichts; nachgetragen am 07.09.2026. **Absatz vom 10.09.2026:** das Call-Control-Interface wird mit anderen Programmen geteilt — nipp meldet nur bei eigenem Anlass (ADR-028 Nachtrag 4) |

Nicht in §22, aber im selben Durchgang entschieden und nur als ADR festgehalten,
weil sie nichts an der Spezifikation ändern: ADR-019 (ein Dutzend §9-Felder
bleibt der Provisionierung vorbehalten), ADR-020 (das Besetztlampenfeld kennt
„klingelt" nicht, Präsenz fehlt im Infobereich), ADR-021 (Lokalisierung
zurückgestellt), ADR-022 (Rufnummern im Protokoll maskiert), ADR-025 (das
Nummernfeld bleibt bei lokalen Quellen), ADR-026 (Wähltastatur gehört zum
Kontakte-Tab).

### Rev. 6 — 06.09.2026

Anlass: Anforderung einer generischen Integrationsplattform durch Dominic. Vollständig in §21; Umsetzung in `docs/plans/INTEGRATION-PLAN.md`, Entscheidungen in ADR-015 bis ADR-017.

| § | Was | Verhältnis zum Bestand |
|---|---|---|
| 21 | Integrationsplattform: Anruferkontext und Kontaktsuche über konfigurierbare externe Quellen | neu; **erweitert** §8.2 (Gesprächsansicht) und §8.4 (Kontakte), ersetzt dort nichts |
| 21.2 | Acht unverhandelbare Grenzen, darunter die Schichtgrenze für `Services/Integrations/` | ergänzt §6 und §14 |
| 21.3 | Eigene Konfigurationsdatei, verteilt über das Profil, sperrbar über `PolicyService` | ergänzt §9, §11 und §17 |
| 21.4 | Suchfeld im Kontakte-Tab kehrt zurück | ergänzt ADR-014, wo es entfernt wurde — dort für die lokale Suche, hier für externe Quellen |

### Rev. 5 — 04.09.2026

Anlass: Anforderungen von Dominic nach der Abnahme von M3. Vollständig in §20.

| § | Was | Verhältnis zum Bestand |
|---|---|---|
| 20.1 | Oberfläche im Smartphone-Format, feste Reihenfolge der Elemente | **ersetzt** die Navigationsstruktur aus §8 |
| 20.2 | Bis zu zehn SIP-Konten | präzisiert §9.1 („mehrere Konten sind möglich") |
| 20.3 | Anrufliste zeigt nur Nummer, Dauer, Uhrzeit, Ergebnis | **ersetzt** die Anzeige aus §8.3; gespeichert wird weiterhin alles |
| 20.4 | Hell/dunkel/wie Windows, Symbole aus `Icons/` | neu; §16.2 (Branding) ist damit teilweise erledigt |
| 20.5 | Fenster immer im Vordergrund | neu, ergänzt §9.6 |

### Rev. 4 — 04.09.2026

Anlass: Klärung der offenen Entscheidungen aus §16 mit Dominic.

| § | Was | Grund |
|---|---|---|
| 8.4 | Outlook: **COM-Interop statt Graph** | Die Entra-App-Registrierung lohnt den Aufwand nicht, solange nipp intern läuft (ADR-009). Die drei Nebenwirkungen von COM sind dort benannt |
| 16.1 | Lizenz: **entschieden — vorerst intern** | M8 ist damit nicht mehr blockiert. Die Frage kehrt bei einer Auslieferung an Kunden unverändert zurück (`docs/licensing.md`) |
| 16.3 | Verteilung: **entschieden — Velopack-Setup** (ADR-038) | MSIX wartet auf T110 und AP9.2 und bleibt für Intune-Umgebungen vorgemerkt |
| 16.4 | Update: **entschieden — GitHub Releases, stable/beta** (ADR-039) | Damit ist auch die Zeile Version / Update prüfen aus §9.6 gebaut |
| 16.6 | Outlook-Zugriff: entschieden | siehe 8.4 |
| 16.7 | Aufnahme-Ablage: **lokal, konfigurierbar** | Netzpfad je Kunde ist nicht vorgesehen |

### Rev. 3 — 04.09.2026

Anlass: Erkenntnisse aus dem bestandenen M1-Gate (AP2.3) und die Auskunft, dass die aktuelle Anlage keine Verschlüsselung hat.

| § | Was | Grund |
|---|---|---|
| 9.3 | „Verschlüsselung erzwingen" von **ein** auf **aus** | Mit „ein" wäre gegen die aktuelle Anlage kein Gespräch möglich. SRTP wird weiter angeboten, nur nicht erzwungen (ADR-007) |
| 9.4 | *Vorbehalt vermerkt* | Die Echounterdrückung schaltet sich bei 8 kHz selbst ab; die Zusage „ein" gilt bei Externgesprächen nicht (ADR-006 Punkt 2) |
| 9.5 | *Korrekturen vorgemerkt* | G.722 ist im SDK **aus** statt aktiv, die festen Payload-Types sind nicht setzbar, G.729 ist nicht im Build (`docs/sdk-api-notes.md`) |
| 14.2 | *Ergänzung vorgemerkt* | Das SDK braucht auch **Ressourcendateien** (belr-Grammatiken), nicht nur DLLs — der Fallstrick ist unvollständig beschrieben |

### Rev. 2 — 04.09.2026

Anlass: Verifikation des SDK-Beschaffungsstands vor Beginn von P0. Alle Änderungen sind Faktenkorrekturen aus belegten Quellen; **am Umfang, an der Architektur und an den Meilensteinen wurde nichts geändert.** Rev. 1 liegt als `NIPP-BUILD.md.rev1-20260904.bak` daneben.

| § | Was | Grund |
|---|---|---|
| 0 | Verweis auf `docs/plans/IMPLEMENTATION-PLAN.md` und auf dieses Protokoll | Zwei Dokumente, klare Rollentrennung |
| 3 | Kontaktweg für die kommerzielle Lizenz; `ENABLE_GPL_THIRD_PARTIES`/`ENABLE_NON_FREE_FEATURES` benannt; Absatz „kein technischer Ausweg" | Der Punkt war als Blocker markiert, aber ohne Handlungsweg. Die Stack-Alternativen wurden geprüft und tragen nicht |
| 5 | **Version 5.5.0 → 5.5.18 (03.09.2026)** | Rev. 1 war knapp vier Monate hinterher |
| 5 | **Weg C ergänzt** (Prebuilt-ZIP), als Standard für M1 empfohlen | Der einzige Weg, dessen Inhalt vollständig verifiziert werden konnte. Weg A hängt an einer Registry, deren Erreichbarkeit offen ist — das darf das M1-Gate nicht blockieren |
| 5 | Weg A: projectId **411** und Paketname `LinphoneSDK.Windows` eingesetzt, `LinphoneSDK.Dotnet` als reines Build-Ziel richtiggestellt, offene Token-Frage markiert | Rev. 1 hatte `<projectId>` als Platzhalter und beide Paketnamen als gleichwertig |
| 5 | Begriffsklärung „win32" = Desktop-API, nicht 32-Bit; Moniker-Widerspruch `win`/`netcore45` vermerkt | Missverständnis mit direkter Fehlerwirkung, siehe §14.4 |
| 5 | Weg B: `git checkout 5.5.18`, VS **17** 2022, doxygen, AV1-Zusatzanforderungen, `ENABLE_WINDOWS_TOOLS_CHECK` steht in den Presets auf OFF, Preset-Liste | Presets bei 5.5.18 nachgeprüft; `windows-sdk` gilt weiterhin |
| 9.4 | AEC3 (statt AECM) und RNNoise bestätigt | Annahme aus Rev. 1 belegt |
| 14.2 | Plugin-Verzeichnis `lib/mediastreamer/plugins/` als konkrete Bruchstelle, mit Vorgabe zum Kopieren und `dumpbin`-Gegenprüfung | Rev. 1 warnte allgemein vor der DLL-Kette; jetzt ist die Stelle benannt, an der es tatsächlich bricht — ohne `libmswasapi.dll` kein Audio |
| 14.3 | Belege und dritte Ursache (Architektur-Mismatch) | — |
| 16 | Die drei Punkte mit Vorlaufzeit hervorgehoben | Sie blockieren spät, müssen aber früh angestossen werden |
| 17 | `CLAUDE.md`-Vorlage: SDK-Version und Verweis auf den Umsetzungsplan | — |
| 18 | Quellen um Tags, Prebuilt-Verzeichnis, Lizenzkontakt und WindowsAppSDK-Issue ergänzt, Verifikationsdatum gesetzt | Nachvollziehbarkeit |

**Nicht geändert, aber vermerkt:** ob der NuGet-Feed anonym lesbar ist, bleibt offen und ist in P0 vor Ort zu prüfen (`docs/plans/IMPLEMENTATION-PLAN.md` AP0.3).
