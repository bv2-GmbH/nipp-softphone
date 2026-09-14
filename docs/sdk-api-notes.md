# Abweichungen der Wrapper-API von der Spezifikation

**Stand: AP2.2 erledigt am 04.09.2026.** Geprüft gegen `LinphoneWrapper.cs` aus `linphone-sdk-win64-5.5.18.zip` — 62.253 Zeilen, 2.726 öffentliche Deklarationen, 105 Klassen, 103 Enums, Namespace `Linphone`.

Regel 1 aus §0: die generierte Wrapper-Datei ist die einzige Wahrheit. **Niemals eine Methode verwenden, die hier nicht als vorhanden bestätigt ist.**

---

## Die vier Abweichungen von §6

### 1. Es gibt kein `AddListener` — nur genau einen Listener

**§6 nimmt an:** `Core.AddListener(...)`, `Core.RemoveListener(...)`
**Tatsächlich:** `Core.Listener` — ein einzelnes Property vom Typ `CoreListener`, das **97 Delegate-Properties** trägt.

```csharp
// So nicht (existiert nicht):
core.AddListener(myListener);

// So:
core.Listener.OnCallStateChanged = (core, call, state, message) => { ... };
core.Listener.OnAccountRegistrationStateChanged = (core, account, state, message) => { ... };
```

**Das ist die wichtigste Abweichung, weil sie den Entwurf berührt.** §6 legt `SipEventBridge` als Übersetzer von SDK-Callbacks in .NET-Events an — das bleibt richtig, wird aber sogar **notwendiger** als gedacht: da es nur einen Listener gibt, kann nicht jeder Interessent seinen eigenen anmelden. Die Bridge ist der einzige Ort, der die Delegates setzt, und verteilt von dort an beliebig viele .NET-Event-Abonnenten. Wer die Delegates anderswo überschreibt, hängt die Bridge stillschweigend ab.

Für nipp gebraucht und alle als vorhanden bestätigt: `OnCallStateChanged`, `OnAccountRegistrationStateChanged`, `OnRegistrationStateChanged`, `OnCallStatsUpdated`, `OnMessageWaitingIndicationChanged`, `OnNotifyPresenceReceived`, `OnAudioDevicesListUpdated`, `OnDtmfReceived`, `OnTransferStateChanged`.

### 2. Anrufe werden am Anruf angenommen, nicht am Core

**§6 nimmt an:** `Core.AcceptCall(...)`
**Tatsächlich:** `Call.Accept()`, dazu `Call.AcceptWithParams(CallParams)`, `AcceptEarlyMedia()`, `AcceptEarlyMediaWithParams(...)`, `AcceptUpdate(...)`, `AcceptTransfer()`

Die Anrufsteuerung sitzt durchgehend am `Call`-Objekt: `Pause()`, `Resume()`, `Transfer(string referTo)`, `TransferToAnother(Call dest)`, `SendDtmf(sbyte)`, `StartRecording()`, `StopRecording()`. Für `ISipService` heisst das: `CallHandle` muss das `Linphone.Call`-Objekt intern halten — was §6 ohnehin vorsieht, weil kein `Linphone.Call` den Service verlassen darf.

### 3. Echounterdrückung heisst anders — und liefert das Kalibrierergebnis mit

**§6 nimmt an:** `Core.EchoCancellerEnabled`
**Tatsächlich:** `Core.EchoCancellationEnabled`

Dazu drei Nachbarn, die §9.4 und AP5.7 direkt bedienen:

| Member | Typ | Bedeutung |
|---|---|---|
| `Core.EchoCancellationEnabled` | `bool` | der Schalter |
| `Core.StartEchoCancellerCalibration()` | `void` | startet die Kalibrierung (~15 s laut §9.4) |
| `Core.EchoCancellationCalibration` | `int` | **das Ergebnis in ms** — genau der Wert, den der Dialog aus AP5.7 anzeigen soll |
| `Core.EchoLimiterEnabled` | `bool` | separater Begrenzer, in §9 nicht vorgesehen — nicht anfassen |
| `Core.EchoCancellerFilterName` | `string` | Filterwahl; ab SDK 5.5 ist AEC3 der Standard (ADR-003) |

### 4. Der DTMF-Modus sind zwei Schalter, nicht einer

**§6 nimmt an:** `Core.Rfc2833DtmfsEnabled`
**Tatsächlich:** `Core.UseRfc2833ForDtmf` (`bool`) **und** `Core.UseInfoForDtmf` (`bool`)

§9.5 verlangt die Wahl „RFC 2833 / SIP INFO / Inband". Das lässt sich aus den zwei Schaltern abbilden: RFC2833 an / INFO aus, RFC2833 aus / INFO an, beide aus für Inband. Die Kombination „beide an" ist kein sinnvoller Zustand und muss im UI ausgeschlossen werden — `SettingsSchema` bildet also **einen** Aufzählungswert auf **zwei** Schalter ab.

---

## Bestätigt wie in §6 beschrieben

| Zweck | Member | Signatur im Wrapper |
|---|---|---|
| Factory-Singleton | `Factory.Instance` | `static public Linphone.Factory Instance` (Schreibweise `static public`, nicht `public static`) |
| Core anlegen | `CreateCore` | `public Core CreateCore(string configPath, string factoryConfigPath, IntPtr systemContext)` |
| Core mit Config | `CreateCoreWithConfig` | `public Core CreateCoreWithConfig(Config config, IntPtr systemContext)` |
| Adresse parsen | `CreateAddress` | `public Address CreateAddress(string addr)` |
| Auth-Objekt | `CreateAuthInfo` | `public AuthInfo CreateAuthInfo(string username, BearerToken accessToken, ...)` |
| HA1 berechnen | `ComputeHa1ForAlgorithm` | `public string ComputeHa1ForAlgorithm(string userid, string password, string realm, string algo)` |
| Verzeichnisse | `ConfigDir`, `DataDir`, `CacheDir` | je `public string` — **vor** dem Core-Start setzen |
| Ereignisschleife | `Iterate`, `Start`, `Stop` | `public void Iterate()`, `Start()`, `Stop()` |
| Konten | `AccountList`, `AuthInfoList`, `AddAuthInfo` | `IEnumerable<Account>`, `IEnumerable<AuthInfo>`, `void AddAuthInfo(AuthInfo)` |
| Anruf aufbauen | `InviteAddress` | `public Call InviteAddress(Address addr)` |
| Aktueller Anruf | `CurrentCall`, `Calls` | `public Call CurrentCall`, `IEnumerable<Call> Calls` |
| Audiogeräte | `AudioDevices`, `ExtendedAudioDevices` | je `IEnumerable<AudioDevice>` |
| Codecs | `AudioPayloadTypes` | `IEnumerable<PayloadType>`, aktivieren über `PayloadType.Enable(bool)` |
| NAT | `NatPolicy`, `CreateNatPolicy` | `public NatPolicy NatPolicy`, `public NatPolicy CreateNatPolicy()` |
| Verschlüsselung | `MediaEncryption` | `public enum MediaEncryption` |
| Transport | `Transports` | `public Transports Transports` |
| Provisionierung | `ProvisioningUri` | `public string ProvisioningUri` |
| Ports | `AudioPort`, `AudioPortsRange` | `public int AudioPort`, `public Range AudioPortsRange` |
| QoS | `AudioDscp`, `SipDscp` | je `public int` |

## Die in §6 offen gelassenen Namen — jetzt belegt

| Zweck | Member | Gebraucht in |
|---|---|---|
| Konto-Parameter | `Core.CreateAccountParams()` → `AccountParams` | AP3.2, AP5.1 |
| BLF | `Core.CreateFriendList()`, `CreateFriend()`, `FriendList.SubscriptionsEnabled` | AP6.5 |
| CallStats | `RoundTripDelay` (float), `JitterBufferSizeMs` (float), `ReceiverLossRate` (float), `DownloadBandwidth` (int) | AP4.8 |
| Aufnahme | `Call.StartRecording()`, `StopRecording()` (gibt `int` zurück), `CallParams.RecordFile` | AP4.7 |
| Weiterleiten | `Call.Transfer(string)` blind, `Call.TransferToAnother(Call)` begleitet | AP4.6 |
| Halten | `Call.Pause()`, `Call.Resume()` | AP4.5 |
| Stumm | `Core.MicEnabled` (bool) | AP4.5 |
| DTMF senden | `Call.SendDtmf(sbyte)`, `Core.CancelDtmfs()` | AP4.5 |
| TLS-Wurzelzertifikate | `Core.RootCa` (string — eine **Datei**, siehe §14.6) | AP2.3 |
| Klingelgerät | `Core.RingerDevice` (string), `Core.Ring` (string, Pfad) | AP5.6 |
| Lautstärken | `Core.PlaybackGainDb` (float), `Core.MicGainDb` (float) — **dB, nicht 0–100** | AP5.1 |
| Voicemail | `Account.VoicemailAddress` (Address), Ereignis `OnMessageWaitingIndicationChanged` | AP6.7 |
| Netzwerk | `Core.NetworkReachable` (bool), `Core.KeepAliveEnabled`, `Core.Ipv6Enabled` | AP3.6, AP5.1 |
| Audio | `Core.AdaptiveRateControlEnabled`, `Core.NoiseSuppressionEnabled` | AP5.1 |
| Video abschalten | `Core.VideoEnabled` (bool) | §9.3 |
| SDK-Protokoll | Klasse `LoggingService` mit `LoggingService.Listener` und `OnLogMessageWritten` | §4, AP1.4 |

**Achtung bei den Lautstärken:** §9.4 spezifiziert „0–100" für Wiedergabe und Mikrofon, das SDK arbeitet aber mit **Dezibel** (`PlaybackGainDb`, `MicGainDb`, je `float`). `SettingsSchema` braucht dort eine Umrechnung — eine lineare Abbildung von 0–100 auf einen dB-Bereich, die in AP5.1 festzulegen und zu dokumentieren ist. Stillschweigend den Rohwert durchzureichen wäre falsch.

---

## Befunde aus dem laufenden Spike (AP2.1, 04.09.2026)

Der Spike unter `spike/SdkProbe` lief erfolgreich durch: DLL-Kette geladen, Core gestartet, Iterate-Schleife gedreht, Ereignisse empfangen. Dabei kamen sieben Dinge heraus, die man dem Wrapper allein nicht ansieht.

### 1. Es sind nicht nur DLLs — das SDK braucht Ressourcendateien

Der erste Startversuch brach ab:

```
belr-error- Could not load grammar vcard_grammar.belr because the file could not be located.
bctbx-fatal- Unable to load VCARD grammar.
```

`belr` lädt acht Grammatiken (`cpim`, `ics`, `identity`, `mwi`, `sdp`, `sip`, `vcard`, `vcard3`) zur **Laufzeit aus Dateien** unter `share/belr/grammars/`. Fehlen sie, stirbt der Core-Start — mit einer Meldung, die nicht sagt, dass eine *Datei* fehlt und keine DLL.

**§14.2 warnt vor der DLL-Kette, aber nicht davor. Der Punkt gehört dort ergänzt.**

### 2. `Factory.MspluginsDir` ist der Hebel für das MSIX-Problem

Die Factory hat setzbare Ressourcenpfade, die **vor** `CreateCore` gesetzt werden müssen:

| Property | Wofür |
|---|---|
| `TopResourcesDir`, `DataResourcesDir` | Wurzel der Ressourcen, darunter `belr/grammars/` |
| `SoundResourcesDir`, `RingResourcesDir` | Klänge und Klingeltöne |
| `ImageResourcesDir` | Bilder |
| **`MspluginsDir`** | **Mediastreamer-Plugins** |
| `LiblinphonePluginsDir` | liblinphone-Plugins (für nipp leer) |

`MspluginsDir` ist der wichtige: damit lässt sich der Plugin-Pfad **explizit** setzen, statt auf die DLL-Suchpfade von Windows zu hoffen. Genau das ist der Ansatz für §14.2/§14.3 im MSIX — die Plugins müssen nicht neben die EXE, wenn man dem SDK sagt, wo sie liegen. Im Spike verifiziert:

```
mediastreamer: Loading ms plugins from [...\lib\mediastreamer\plugins]
bctbx: libmswasapi plugin loaded
bctbx: libmswebrtc 5.4.0 plugin loaded
```

### 3. Für die Gerätewahl `ExtendedAudioDevices` nehmen, nicht `AudioDevices`

Auf der Testmaschine:

| Aufruf | Ergebnis |
|---|---|
| `Core.SoundDevicesList` | **4** rohe Kartennamen |
| `Core.ExtendedAudioDevices` | **4** Geräte |
| `Core.AudioDevices` | **0** |

`AudioDevices` liefert laut Wrapper-Doku „nur das erste Gerät je Typ" — und alle Geräte melden hier `Type = Unknown`, weshalb die Liste leer bleibt. **Für AP5.6 ist `ExtendedAudioDevices` die richtige Quelle.** Wer `AudioDevices` nimmt, bekommt auf mancher Hardware eine leere Geräteliste, ohne dass ein Fehler auftritt.

WASAPI selbst funktioniert, auch unter x64-Emulation auf ARM64: `Default Capture`, `Microphone Array (Qualcomm Aqstic)`, `Default Playback`, `Speakers (Qualcomm Aqstic)`.

### 4. `Core.Version` meldet die Nebenversion, nicht die Patchversion

Das ZIP heisst `5.5.18`, `Core.Version` gibt **`5.5.0`** zurück. Die Patchversion ist über die API nicht feststellbar. **Deshalb ist die SHA256 in `sdk-setup.md` der einzige verlässliche Nachweis**, welcher Stand verbaut ist — nicht ein Wert, den man zur Laufzeit abfragen kann.

### 5. Die Codec-Vorgaben aus §9.5 weichen ab

Vorgefunden: 11 Audio-Codecs. Standardmässig **an**: opus (48 kHz, stereo), speex (16 und 8 kHz), PCMU, PCMA. Standardmässig **aus**: GSM, **G722**, speex 32 kHz, BV16, L16 (44,1 kHz mono und stereo).

Drei Abweichungen von der Tabelle in §9.5:

| §9.5 verlangt | Tatsächlich | Folge für AP5.8 |
|---|---|---|
| G.722 aktiv, Priorität 2 | **aus** | muss aktiv geschaltet werden, ist nicht der Auslieferungszustand |
| Opus PT 96, speex PT 102 | **PT = -1** | dynamische Payload-Types werden erst beim SDP verhandelt. Die festen Nummern in §9.5 sind nicht setzbar und dort irreführend |
| G.729 inaktiv | **gar nicht vorhanden** | bcg729 ist nicht im Build — genau wie §3 es will. Die Zeile in §9.5 kann entfallen |

Zusätzlich vorhanden, in §9.5 nicht vorgesehen: speex 32 kHz, BV16, L16. Bleiben aus.

### 6. Weitere Standardwerte, die §9 setzen muss

| Wert | Auslieferungszustand | §9 verlangt |
|---|---|---|
| `EchoCancellationEnabled` | `true` | ein ✔ |
| `EchoCancellerFilterName` | **leer** | — (AEC3 ist ab 5.5 Standard, ADR-003) |
| `NoiseSuppressionEnabled` | `true` | ein ✔ |
| `AdaptiveRateControlEnabled` | `true` | ein ✔ |
| `UseRfc2833ForDtmf` | `true` | RFC 2833 ✔ |
| `UseInfoForDtmf` | `false` | ✔ |
| `AudioPortsRange` | **-1 bis -1** (nicht gesetzt) | 7078–7178 — **muss gesetzt werden** |
| `PlaybackGainDb` / `MicGainDb` | `0` / `0` | 72 / 50 auf einer 0–100-Skala — Umrechnung nötig, siehe oben |
| `MediaEncryption` unterstützt | None, SRTP, ZRTP, DTLS — **alle vier** | SRTP als Standard ✔ |

### 7. Namensfalle: `Nipp.Core` gegen `Linphone.Core`

Unser Namespace heisst `Nipp.Core`, die zentrale SDK-Klasse heisst `Linphone.Core`. Innerhalb von `Nipp.Core.*` löst der Compiler ein blankes `Core` auf den **eigenen Namespace** auf:

```
error CS0234: Der Typ- oder Namespacename "Version" ist im Namespace
              "Nipp.Core" nicht vorhanden. (Möglicherweise fehlt ein
              Assemblyverweis.)
```

Die Meldung deutet auf einen fehlenden Verweis, obwohl alles vorhanden ist. **Abhilfe:** in jeder Datei unter `Services/Telephony/` einen Alias setzen —

```csharp
using LinphoneCore = Linphone.Core;
```

— und `LinphoneCore.Version` schreiben. Das gilt für alle Dateien, die ab P3 dazukommen, und ist in `SdkLoadProbe.cs` mit Begründung kommentiert.

### 7b. Weitere Namenskollisionen — und eine mit besonders irreführender Meldung

Zu `Nipp.Core` / `Linphone.Core` sind zwei dazugekommen:

| Name | Kollidiert mit | Wirkung |
|---|---|---|
| `CallStatus` | `Linphone.CallStatus` und unser Modell | `CS0104: mehrdeutiger Verweis` — klar zu lesen |
| **`LogLevel`** | `Linphone.LogLevel` und `Microsoft.Extensions.Logging.LogLevel` | **Meldung verschweigt die Ursache** |

Der `LogLevel`-Fall ist der unangenehme. In einer Datei mit `using Linphone` kann der Quellgenerator für `[LoggerMessage]` das Attribut nicht mehr auflösen. Die Fehlermeldung lautet dann:

```
CS8795: Die partielle Methode "…" muss einen Implementierungsteil aufweisen,
        weil sie Zugriffsmodifizierer verwendet.
```

— sieben Mal, für jede Meldung einzeln, und kein Wort über den eigentlichen Grund. Die einzige brauchbare Zeile steht am Ende: `CS0104: "LogLevel" ist ein mehrdeutiger Verweis`.

**Abhilfe:** Klassen mit `[LoggerMessage]` in eigene Dateien **ohne** `using Linphone` legen. So liegen `TelephonyLog`, `ConnectivityLog` und `SettingsApplierLog` bereits.

### 7c. `Core.RingerDevice` ist veraltet

§9.4 verlangt ein separates Klingelgerät. `Core.RingerDevice` (string) ist seit dem **29.08.2025** veraltet; der Ersatz ist eine Kombination aus `Core.ExtendedAudioDevices` und `AudioDevice.UseForRinging` (bool je Gerät).

Beim Setzen das Flag an **allen** Geräten zurücksetzen und nur am gewählten setzen — sonst klingelt es mit der Zeit auf mehreren.

### 8. Drei Startmeldungen, die harmlos sind, aber im Log auffallen

Damit sie nicht später als Fehler untersucht werden:

- `PushNotificationConfig ... pn-param '' should be of the form teamID.bundleIdentifier.services` — Push-Konfiguration für Mobilgeräte, für nipp bedeutungslos.
- `There is no NatPolicy with ref [...]` — beim ersten Start ohne Konfiguration erwartbar; verschwindet, sobald eine NatPolicy gesetzt ist (AP5.1).
- `MSMFoundationCapDesk CoInitialize already call with different options [RPC_E_CHANGED_MODE]` — Video-Capture-Initialisierung, betrifft nipp nicht (Video ausgeschlossen).

Nicht harmlos und zu erledigen: `Default local ringtone file '...\share\sounds/notes_of_the_optimistic.mkv' does not exist`. Das SDK sucht einen Standard-Klingelton, der im win64-Paket nicht enthalten ist. §9.4 verlangt eine mitgelieferte WAV — die muss nipp selbst beisteuern (AP1.5/§16.2 Branding).

---

## Mitgelieferte Klingeltöne

Das SDK bringt WAV-Dateien mit, darunter `share/liblinphone-tester/sounds/oldphone.wav`. §9.4 verlangt eine „mitgelieferte WAV" als Standard-Klingelton — die Frage, ob eine davon genügt oder bv2 eine eigene will, gehört zu §16.2 (Branding).
