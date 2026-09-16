# Die Audioqualität — was da rauscht, und woher

**Angelegt am 16.09.2026.** Auftrag von Dominic: «teilweise starkes Rauschen,
Nebengeräusche — können wir da noch etwas optimieren?»

Bezug: NIPP-BUILD.md §8.2 (Qualitätspanel), §9.4 (Audio), §9.5 (Codecs);
**ADR-006 Punkt 3** (der offene Jitter-Befund, als **T38** in der Testmatrix),
ADR-006 Punkt 2 (Echounterdrückung bei 8 kHz), ADR-011 (AGC aus), ADR-029
(Rufton), ADR-001 (keine x64-Maschine), `docs/lehren.md` — «Audio, Töne und
Geräte».

> **Der Satz, der über diesem Plan steht.** ADR-006 Punkt 3 sagt seit dem
> 04.09.2026, was bei Audioproblemen auf dieser Maschine zuerst zu tun ist:
> *auf echter x64-Hardware gegenprüfen, **bevor** daraus eine Fehlersuche im
> Code wird.* Und ADR-001 sagt, dass es diese Maschine nicht gibt. **Runde A
> ist der Weg um diese Zange herum** — die Emulation aus dem Spiel nehmen,
> ohne eine zweite Maschine.

---

## Stand: **die Ursache ist belegt** (16.09.2026, A4 und A6)

> **A6 ist bestanden** (T304). Mit abgeschalteter Rauschunterdrückung — im
> Protokoll nachgewiesen, die Kette beginnt jetzt `MSWASAPIRead → MSResample`
> statt über den Suppressor — war das Gespräch um 21:56 sauber, und Dominic
> hörte kein Rauschen mehr. Die Gegenüberstellung, **zwei Gespräche derselben
> Länge, beide ausgehend, beide PCMU**:
>
> | | mit Filter (10:04, 22 s) | **ohne Filter (21:56, 23 s)** |
> |---|---|---|
> | Spitzenlast eines Filters | **77,24 ms** (`MSNoiseSuppressor`) | **11,99 ms** (`MSResample`) |
> | `Ticker: We are late` | 4 an dem Tag, 3 davon im langen Gespräch | **0** |
> | `Could not get buffer` | 13 in einer Millisekunde | **2**, beide im Rufton |
> | Jitterpuffer **im Gespräch** | 454–530 ms, durchgehend | **36–60 ms**, pendelt sich auf 40,0 ein |
> | Gehör | «teilweise starkes Rauschen» | «klingt super, ohne Rauschen» |
>
> **Damit ist die Kette unten keine Indizienkette mehr.** Was bleibt, sind drei
> Dinge: die **Aufbauphase** stört weiter (siehe unten), die **Senderichtung**
> ist ungemessen — die Gegenseite hört jetzt mehr Hintergrund, und niemand hat
> sie gefragt —, und das Gespräch war **23 Sekunden lang**. Der Alltagsbeleg
> ist ein langes Gespräch; das Gegenstück von 22 Sekunden macht den Vergleich
> zwar sauber, ersetzt ihn aber nicht.
>
> **Die Störung im Rufton bleibt, und sie hat eine eigene Ursache.** Um
> 21:56:25,8 — zwei Sekunden nach dem Läuten, sieben vor dem Verbinden —
> stehen wieder `Could not get buffer` (2×), `cannot write output buffer of 960
> samples` (2×) und `Jitter buffer stays unconverged`. Dasselbe Muster wie um
> 10:04, nur kleiner. **Das ist nicht der Rauschfilter**, sondern der Aufbau
> des Early-Media-Stroms. Eigener Befund, eigene Messung — er trifft den
> Rufton, nicht das Gespräch.

### Was A4 gemessen hat, und warum A6 danach naheliegend war

**Der Rauschfilter überzieht sporadisch den Tick der Audiokette — und er
filtert nur das, was hinausgeht.**

> **Präzisierung vom 16.09.2026, nachts.** Hier stand zuerst, der Filter
> «kostet 77 bis 87 Prozent der Rechenzeit». Das ist wörtlich richtig (die
> Spalte nennt den Anteil **an der Filterkette**), führt aber in die Irre:
> **im Mittel braucht er 0,70 bis 0,88 ms von 10 ms** — das ist harmlos, und
> die ganze Kette ist nicht ausgelastet. **Das Problem sind die Ausreisser.**
> Über sechs Gespräche an zwei Tagen:
>
> | Ticks | mean | **max** | sd |
> |---|---|---|---|
> | 236 | 0,70 | 2,30 | 0,17 |
> | 2 080 | 0,71 | 5,25 | 0,22 |
> | 2 670 | 0,86 | 6,16 | 0,17 |
> | **4 100** | 0,70 | **77,76** | 0,99 |
> | 48 327 | 0,70 | 3,47 | 0,17 |
> | **55 282** | 0,88 | **77,24** | 0,52 |
>
> **In vier von sechs Gesprächen bleibt das Maximum unter 6,2 ms** — darunter
> eines mit 48 327 Ticks, also gut acht Minuten. Nur zweimal schiesst es auf
> **fast genau denselben Wert**, 77,24 und 77,76 ms. Bei einem Mittel von
> 0,7 ms ist das Faktor 110; das ist **kein Rechenaufwand, sondern ein
> Stillstand** — ein verdrängter Thread, ein Seitenfehler oder eine
> Code-Übersetzung im x64-Emulator. **Welches davon, ist nicht gemessen.**
>
> **Das ändert die Konsequenz:** der Filter ist nicht «zu teuer», er hat hier
> seltene Aussetzer. Und das spricht dafür, dass es **an dieser Maschine**
> liegt und nicht am Filter — siehe die Empfehlung unter C0.

Aus den Filterstatistiken des SDK, in **jedem** Gespräch am 15. und 16.09.2026:

```
Name                  Count      min    mean   max     sd     CPU
MSNoiseSuppressor     55282     0.07    0.88   77.24   0.52   79.8
MSOpusEnc                99     0.00    0.10    0.30   0.09    8.6
MSRtpSend             55595     0.00    0.03    2.24   0.04    3.0
MSResample            83226     0.00    0.03    5.58   0.02    3.0
… alle übrigen zusammen unter 8 %
MSWebRTCAEC               0     0.00    0.00    0.00   0.00    0.0
```

**Der Ticker der Audiokette läuft alle 10 ms.** Ein Filter, der einmal
**77,24 ms** braucht, hält diese Kette für **sieben Ticks** an. Und genau das
steht auch im Protokoll:

| Zeit | Was das SDK meldet |
|---|---|
| 10:01:58.673 | `Ticker: We are late of 136 miliseconds.` |
| 10:02:03 | Jitterpuffer springt von 40,0 auf **154,4 ms** |
| 10:02:30.922 | `Ticker: We are late of 128 miliseconds.` |
| 10:02:31 | Jitterpuffer 68,9 ms (er baut sich noch ab) |
| 10:03:16.312 | `Ticker: We are late of 76 miliseconds.` |
| 10:03:21 | Jitterpuffer springt von 43,4 auf **100,0 ms** |

**Jeder Sprung des Jitterpuffers folgt drei bis sechs Sekunden auf eine
Ticker-Verspätung.** Dazu, im kurzen Gespräch um 10:04: `Jitter buffer stays
unconverged for one second, reset it`, dreizehn Mal `Could not get buffer from
the MSWASAPI audio output interface` **innerhalb einer Millisekunde**, und
`cannot write output buffer of 960 samples, not enough space [960=9600-8640]`
— der Ausgabepuffer war zu 90 % voll. Danach stand der Jitterpuffer bei
**454 bis 530 ms** und blieb dort, bis aufgelegt wurde. Das ist **die Zahl aus
ADR-006 Punkt 3** («499 ms»), zweieinhalb Wochen später und in freier Wildbahn.

**Die Kette ist damit gemessen, nicht vermutet:** Rauschfilter überzieht den
Tick → Ticker kommt zu spät → WASAPI bekommt keinen Puffer oder findet ihn
voll → Pakete stauen sich → Jitterpuffer bläht sich auf. Hörbar ist das als
Knacken, Aussetzer und eine wachsende Verzögerung.

### Und der Filter hilft gegen das gemeldete Symptom ohnehin nicht

Die vollständige Filterkette aus demselben Protokoll:

```
senden:    MSWASAPIRead → MSNoiseSuppressor → MSResample → MSEqualizer
           → MSVolume → MSTee → MSAudioMixer → MSUlawEnc → MSRtpSend
empfangen: MSRtpRecv → MSUlawDec → MSAudioMixer → MSGenericPLC
           → MSAudioFlowControl → MSDtmfGen → MSResample → MSWASAPIWrite
```

**`MSNoiseSuppressor` steht nur in der Senderichtung.** Was aus dem Netz
kommt, läuft ungefiltert bis zum Lautsprecher. Der Schalter
«Rauschunterdrückung» in den Einstellungen kann also nichts gegen ein
Rauschen tun, das **ich** höre — er bearbeitet nur, was die Gegenseite hört.
Das war nicht bekannt, und in den Einstellungen steht es nicht.

Der Filter läuft dabei bei **48 000 Hz** (`initialized for 48000 Hz, 1 channel,
frames 10 ms` — die Signatur von RNNoise), obwohl der Codec anschliessend auf
**8 000 Hz** resampelt. Es wird also die sechsfache Datenmenge gefiltert und
das Ergebnis danach weggeworfen.

### Was damit schon entschieden ist

| | |
|---|---|
| **H3 (Komfortrauschen) ist ausgeschlossen** | In den SDP-Angeboten steht **kein `CN`** — nur PCMU, PCMA, GSM, opus und telephone-event. Das Rauschen wird übertragen, nicht erzeugt |
| **H4 ist beantwortet** | Der Rauschfilter läuft, und er ist RNNoise (in `mediastreamer2.dll`, kein eigenes Plugin). **Der Kommentar in `NippSettings` stimmt** — B5 ist damit erledigt, und jetzt trägt er eine Messung |
| **H5 ist gemessen statt behauptet** | `MSWebRTCAEC` hat in **jedem** Gespräch `Count 0`. Die Echounterdrückung läuft nie. ADR-006 Punkt 2 sagte das vorher, jetzt steht die Zahl dahinter |
| **H1 hat einen Mechanismus** | Nicht «die Emulation ist langsam», sondern: **ein bestimmter Filter überzieht den Tick**. Das ist behebbar, ohne auf x64 zu warten |
| **Die Gegenprobe** | **Gemacht (A6, 21:56).** Ohne den Filter: keine Ticker-Verspätung, Spitzenlast 11,99 statt 77,24 ms, Jitterpuffer stabil bei 40 ms — und kein Rauschen mehr. Tabelle ganz oben |

**Das lange Gespräch war übrigens überwiegend in Ordnung:** neun Minuten,
Jitterpuffer stabil bei 40 ms, zwei Störungen von je rund 45 Sekunden. Das
Problem ist **episodisch**, nicht dauerhaft — was zu «teilweise starkes
Rauschen» passt und erklärt, warum es nie jemand festnageln konnte.

---

## Was gemeldet ist

| Frage | Antwort (16.09.2026) | Was daraus folgt |
|---|---|---|
| Wer hört es? | **Ich höre es** (Empfangsrichtung). Ob die Gegenseite auch etwas hört, ist nicht auseinandergehalten worden | Empfang ist der Hauptzweig. Die Senderichtung bleibt offen und wird in A1 mitgeprüft |
| Wo? | **Nur auf dieser Maschine** — ARM64, x64 emuliert | T38 ist Hauptverdächtiger. An echter Hardware ist es nie beobachtet worden, aber auch **nie geprüft** |
| Wobei? | **Externe Anrufe über den Trunk** | Also G.711 (PCMA/PCMU), 8 kHz. Dort schaltet sich die Echounterdrückung laut ADR-006 Punkt 2 selbst ab, und dort resampelt WASAPI 8 ↔ 48 kHz |

**Was «Rauschen» heisst, ist damit noch nicht gesagt.** Ein gleichmässiges
Zischen, ein Knacken, ein kurzes Aussetzen und ein Gurgeln haben verschiedene
Ursachen und werden im Alltag alle «Rauschen» genannt. Die Hypothesentabelle
unten führt deshalb je Hypothese **das Geräusch**, an dem man sie erkennt —
das ist die billigste Messung, die es in diesem Plan gibt.

---

## Was das Protokoll schon hergibt — gemessen, nicht vermutet

Aus `%LOCALAPPDATA%\nipp\logs\nipp-20260916.log` (118 458 Zeilen, sechs Mal
`StreamsRunning`):

1. **`mswasapi: Could not get buffer from the MSWASAPI audio output interface
   960` — 13 Mal, alle innerhalb **einer Millisekunde** (10:04:09,599 bis
   ,600).** Die Wiedergabe bekommt keinen Puffer vom Gerät. Das ist ein
   **einzelner Aussetzer, kein Dauerzustand** — und genau die Fehlerklasse, die
   ADR-006 Punkt 3 als «WASAPI-Pufferfehler» offen gelassen hat. Hörbar wäre
   das als Lücke oder Knacken, nicht als Zischen.
2. **`wasapi: changing output rate to 8000 Hz is not supported by the device.
   Keep 48000 Hz`** — sieben Mal, dazu zwei Mal dasselbe für die Eingabe. Bei
   G.711 wird also **durchgehend resampelt**, 8 kHz auf 48 kHz und zurück.
3. **`wasapi: trying to change output channel to 1 is not supported by the
   device`** — zehn Mal. Mono geht nicht, es wird umgerechnet.
4. **`wasapi: output buffer was filled with at least 30 ms in the last N ms`**
   — der Ausgabepuffer läuft voll; das ist aufgebaute Latenz.
5. **Drei Geräte tauchen auf:** `Microphone (Jabra Engage 75)`, das eingebaute
   `Microphone Array (Qualcomm Aqstic …)` und die Platzhalter
   `Default Playback` / `Default Capture`. §9.4 sagt «ohne Wahl dem
   Windows-Standard folgen» — **welches Gerät das im Gespräch tatsächlich
   war, steht nirgends fest.**

**Und ein Codec-Befund:** in den sechs Gesprächen steht ein Mal `Codec G722`.
Womit die übrigen liefen, sagt das Protokoll nicht in einer Zeile, die man
zählen kann — siehe A4.

---

## Was nipp heute nicht misst

Das ist der Grund, warum sich bisher nichts belegen liess.

`CallQuality` (`Model/CallModels.cs`) trägt **fünf** Werte: Umlaufzeit,
Jitter**puffer**grösse, Empfangs-Paketverlust, Empfangsbandbreite, MOS. Der
Wrapper liefert an `Call.AudioStats` aber **zwölf**. Es fehlen:

| Wert im SDK | Was er beantwortet |
|---|---|
| `ReceiverInterarrivalJitter` | **Der echte Jitter.** §8.2 verlangt ihn ausdrücklich; angezeigt wird seit dem 05.09.2026 die Puffergrösse, weil sie damals «Jitter» hiess. Der richtige Wert wurde nie gelesen |
| `LocalLateRate` | **Verspätet eingetroffen und deshalb verworfen.** Genau das klingt nach Aussetzern, und im Paketverlust taucht es nicht auf |
| `LocalLossRate` | Verlust, wie ihn der eigene Empfänger sieht — gegen `ReceiverLossRate` aus dem RTCP der Gegenseite |
| `SenderLossRate`, `SenderInterarrivalJitter` | **Die Senderichtung.** Ob die Gegenseite ein Problem hat, ist heute nicht ablesbar |
| `UploadBandwidth`, `EstimatedDownloadBandwidth` | Ob die Bandbreite zum Codec passt |
| `Call.PlayVolume`, `Call.RecordVolume` (dBm0) | **Die Pegel.** `RingbackWatch` benutzt `PlayVolume` bereits — damit wird «rauscht das Mikrofon in der Sprechpause?» eine **Zahl** statt eines Eindrucks, und zwar **ohne Gesprächsinhalt** |

Dazu drei Stellschrauben, die nipp **nie setzt** (geprüft: kein Treffer im
Code): `Core.EchoLimiterEnabled`, irgendetwas am Jitter-Puffer über
`Core.Config`, und ein Rauschgatter. Gesetzt wird `NoiseSuppressionEnabled` —
**ob der Filter dahinter im win64-Prebuilt überhaupt existiert, ist offen:**
unter `lib/mediastreamer/plugins/` liegen `libmswasapi.dll`, `libmswebrtc.dll`
und `libmsopenh264.dll`, **kein RNNoise**. Der Kommentar in `NippSettings.cs`
(«RNNoise ab SDK 5.5») ist damit eine Behauptung ohne Messung — der Fall aus
W2.7, Befund A8, zum fünften Mal.

---

## Die Hypothesen, und woran man sie auseinanderhält

| # | Hypothese | Wie es klingt | Wie sie fällt |
|---|---|---|---|
| **H1** | **Der Ticker kommt zu spät, weil der Rauschfilter den Tick überzieht** | Knacken, kurze Löcher, wachsende Verzögerung | **Mechanismus gemessen (A4).** Es fehlt die Gegenprobe: **A6** |
| **H2** | **Resampling 8 ↔ 48 kHz** | Rauheit, blechernes Zischen, immer gleich stark | A5: dasselbe Gespräch mit G.722 (16 kHz) intern. Wenn es dort weg ist, ist es die Schmalbandkette |
| ~~**H3**~~ | ~~Komfortrauschen der Anlage (VAD/CN, RFC 3389)~~ | — | **Ausgeschlossen (A4, 16.09.2026):** kein `CN` in den SDP-Payloads |
| **H4** | **Die Rauschunterdrückung wirkt nicht — auf das, was ich höre** | Das Rauschen der Gegenseite kommt ungefiltert durch | **Bestätigt (A4):** der Filter läuft, steht aber **nur in der Senderichtung**. Für die Empfangsrichtung gibt es keinen. Siehe C8 |
| **H5** | **Keine Echounterdrückung bei 8 kHz, kein Echo-Limiter** (ADR-006 Punkt 2) | Echo, Hall, abgehackte Sprache | **Gemessen (A4):** `MSWebRTCAEC` hat in jedem Gespräch `Count 0`. Nur relevant, wenn das Symptom Echo ist — dann C2 |
| **H6** | **Das falsche Mikrofon** — Windows nimmt das eingebaute Array statt des Jabra | Raumhall, Tastatur, Lüfter; die Gegenseite beschwert sich, man selbst hört nichts | B3: `RecordVolume` in der Sprechpause, und welches Gerät tatsächlich gewählt wurde |
| **H7** | **Netz: verspätete Pakete** | Aussetzer, Gurgeln, Silben fehlen | B1: `LocalLateRate` und `ReceiverInterarrivalJitter`. Heute unsichtbar |
| **H8** | **Das Gerät oder Windows** (Verstärkung, Effekte, exklusiver Modus) | Alles Mögliche, aber **auch ausserhalb von nipp** | A2: eine WAV über dieselbe Kette, ohne SIP |
| **H9** | **Es kommt schon so an** (Trunk, Mobilfunkstrecke, Gegenseite) | Rauschen genau dann, wenn das Ziel dasselbe ist | A3: zweites Softphone am selben Gerät, selbes Ziel. Der Bria-Vergleich hat am 09.09.2026 schon einmal die Ursache gefunden |

**Nach A4 führt H1** — mit einem Mechanismus statt eines Verdachts, und er
erklärt Empfangsrichtung, «nur auf dieser Maschine» und das Episodische
zusammen. **H4 erklärt daneben, warum bisher nichts geholfen hat:** der
Schalter, den man dagegen betätigt hätte, wirkt auf der anderen Richtung.
H6 bleibt offen, falls sich herausstellt, dass die Gegenseite rauscht und
nicht ich.

---

# Runde A — trennen, ohne eine Zeile Code (diese Maschine)

**Ziel: am Ende von Runde A ist bekannt, ob das Geräusch vor oder nach dem
Netz entsteht.** Alles Weitere hängt daran. Kein Eingriff, keine Einstellung
wird verstellt — geschätzt eine Stunde, davon das meiste Warten auf ein
Gespräch.

### A1 — Der Mitschnitt gegen das Gehör · **die schärfste Messung im Plan**

nipp kann aufnehmen (§8.2, Ordner `%LOCALAPPDATA%\nipp\recordings`, es liegt
dort bereits eine Datei vom 04.09.2026). Also: ein **eigenes** Testgespräch
nach draussen, Aufnahme mitlaufen lassen, dabei auf das Rauschen achten,
danach den Mitschnitt anhören.

- **Der Mitschnitt rauscht auch** → das Geräusch ist **vor** der Wiedergabe da:
  Netz, Codec, Anlage, Gegenseite. H2, H3, H7, H9. **H1 und H8 sind raus.**
- **Der Mitschnitt ist sauber, das Headset rauschte** → es entsteht **in der
  Wiedergabekette**: WASAPI, Gerät, Emulation. H1, H8. **Der ganze Netzzweig
  ist raus.**

**Vorbehalt:** ein Mitschnitt ist Gesprächsinhalt. Deshalb ein eigenes
Testgespräch mit einem Gegenüber, das Bescheid weiss — nicht das nächste
Kundengespräch —, und die Datei danach löschen. §8.2 verlangt den sichtbaren
Indikator ohnehin.

**Zu klären dabei:** nimmt nipp **beide** Richtungen gemischt auf oder nur
eine? Steht in `SipService`; wenn getrennt, beantwortet A1 zusätzlich die
Frage nach der Senderichtung.

### A2 — Die Audiokette ohne SIP

Im SDK-Verzeichnis liegen fertige Werkzeuge:
`sdk/extracted/linphone-sdk/win64/bin/mediastreamer2-player.exe`. Eine WAV
über dieselbe WASAPI-Kette abspielen, auf dasselbe Headset.

- **Knackt es dort auch** → H1/H8, und nipp ist als Verursacher draussen. Dann
  ist das Thema **T38**, nicht der nipp-Code.
- **Ist es dort sauber** → die Kette taugt, das Problem entsteht im Gespräch.

### A3 — Die Gegenprobe mit einem zweiten Softphone

Bria (oder ein anderes) am **selben** Headset, **selbem** Trunk, **gleichem**
Ziel, unmittelbar nacheinander. Rauscht es dort gleich, ist es nicht nipp.
Dieser Vergleich hat am 09.09.2026 den Headset-Befund gelöst.

### A4 — Das Protokoll auswerten · **erledigt am 16.09.2026, Ergebnis oben**

Ohne neues Gespräch, sofort möglich — und es hat den Hauptverdächtigen
geliefert. Was gefragt war und was herauskam:

- **Die 13 Pufferfehler um 10:04** — sie lagen **im Early Media** des kurzen
  Gesprächs (10:04:07 Ringing, 10:04:12 Connected), alle dreizehn innerhalb
  **einer Millisekunde**, unmittelbar nach `Jitter buffer stays unconverged`.
  Kein Gerätewechsel, kein Stream-Start.
- **Der Codec:** PCMU in beiden ausgehenden Gesprächen (`MSUlawEnc` /
  `MSUlawDec` in der Kette), Opus nur im Angebot. **Kein `CN`** in den
  SDP-Payloads — H3 ist damit erledigt.
- **Die Geräte:** im Protokoll stehen `Microphone (Jabra Engage 75)`, das
  eingebaute `Microphone Array (Qualcomm Aqstic …)` **und** die Platzhalter
  `Default Capture` / `Default Playback`. Welches im Gespräch aktiv war, steht
  **immer noch nicht** eindeutig da — das bleibt offen und ist ein Grund für
  B2: nipp protokolliert das gewählte Gerät nicht beim Gesprächsaufbau.
- **Die Zeitkorrelation steht** (Tabelle oben) — und sie brauchte keine Uhrzeit
  vom Menschen, weil die Ticker-Verspätungen selbst Zeitstempel tragen.

### A6 — Die Gegenprobe · **bestanden am 16.09.2026, 21:56** (T304)

**Einstellungen → Rauschunterdrückung aus.** Dann telefonieren wie sonst,
mindestens fünf Minuten nach draussen. Danach im Protokoll zählen:

| Was | Mit Filter | **Ohne Filter (gemessen 21:56)** | |
|---|---|---|---|
| `Ticker: We are late` | 4 (16.09.), 1 (15.09.) | **0** | erfüllt |
| Jitterpuffer im Gespräch | Sprünge auf 100–530 ms | **36–60 ms, dann stabil 40,0** | erfüllt |
| `Could not get buffer` | 13 in einem Burst | **2 — beide im Rufton, keiner im Gespräch** | teilweise |
| Spitzenlast eines Filters | 77,24 ms je Tick | **11,99 ms** (`MSResample`) | erfüllt |
| Das Geräusch | «teilweise starkes Rauschen» | **weg** | erfüllt |

Die eine Zeile, die nicht auf null steht, ist der Grund für den neuen Befund
**A7**: die Störung in der Aufbauphase bleibt und gehört dem Rauschfilter
nicht.

**Und die zweite Hälfte der Gegenprobe war die wichtigere:** das Rauschen ist
**weg**. Damit erübrigt sich A1 als Trennmessung für dieses Symptom — die
beiden Dinge, die auseinanderzuhalten waren, sind dasselbe gewesen.

**Was die Gegenprobe nicht misst, ist der Preis:** die Gegenseite hört jetzt
den Hintergrund, den der Filter vorher entfernt hat. Niemand hat sie gefragt.
Genau deshalb muss aus dieser Messung eine Entscheidung werden und kein
Dauerzustand — siehe C0.

### A7 — Die Störung im Rufton · **neu, offen**

Um 21:56:25,8 — zwei Sekunden nach dem Läuten, sieben vor dem Verbinden —
stehen `Could not get buffer` (2×), `cannot write output buffer of 960
samples, not enough space [960=9600-8640]` (2×) und `Jitter buffer stays
unconverged for one second, reset it`. **Ohne** Rauschfilter, also mit einer
anderen Ursache; dasselbe Muster wie um 10:04:09, nur kleiner (2 statt 13).

Beide Male passiert es **im Early Media**, und beide Male springt kurz darauf
der Pegel: um 10:04:09 von −43,3 auf −4,1 dBm0 und die Bandbreite von 10,7 auf
81,6 kbit/s. Das sieht nach dem **Wechsel des Stroms** aus — die Anlage
schickt erst einen leisen, dann den eigentlichen. Ob der Ausgabepuffer diesen
Wechsel nicht verkraftet, ist **Vermutung und nicht gemessen**.

Betroffen ist der **Rufton**, nicht das Gespräch. Das macht es klein — aber
`RingbackWatch` entscheidet aus genau diesen Werten (ADR-029, Nachtrag vom
10.09.2026), und ein Puffer, der in dieser Phase zurückgesetzt wird, ist
dort schon einmal teuer gewesen.

### A5 — Intern gegen extern

Ein internes Gespräch (G.722 oder Opus, 16/48 kHz) unmittelbar gegen ein
externes (G.711, 8 kHz), dasselbe Headset. Trennt H2 von allem anderen.

---

# Runde B — messen, was heute niemand sehen kann

**Erst wenn Runde A den Zweig bestimmt hat.** Klein, ohne Risiko für die
Telefonie, und von §8.2 gedeckt — der Paragraf verlangt «Jitter», und was
angezeigt wird, ist die Puffergrösse.

- **B1 — `CallQuality` vervollständigen.** `ReceiverInterarrivalJitter`,
  `LocalLateRate`, `LocalLossRate`, `SenderLossRate`, `UploadBandwidth`. Modell,
  Anzeige, Tests. Der echte Jitter gehört ins Panel; die Puffergrösse bleibt,
  aber unter ihrem Namen.
- **B2 — Eine Diagnosespur.** Je Sekunde eine Protokollzeile mit diesen Zahlen,
  solange ein Gespräch steht. **Nur Zahlen — keine Nummer, kein Name**
  (§21.2, ADR-022). Nicht dauerhaft auf Information: entweder an `Debug`
  gebunden oder an einen Schalter. `QuietFailures` gilt: ein Fehlschlag beim
  Lesen wird einmal je Sitzung gemeldet, nicht je Sekunde.
- **B3 — Die Pegel mitschreiben.** `PlayVolume` und `RecordVolume` in dBm0, im
  selben Takt. Damit wird H6 messbar: ein Aufnahmepegel, der in der
  Sprechpause nicht in den Keller geht, **ist** das Rauschen.
- **B4 — Die Filterkette einmal je Gespräch protokollieren.** Beim Ton-Befund
  war die letzte Zeile der Kette die ganze Antwort (`MSVoidSink` gegen
  `MSWASAPIWrite`). Für H4 ist sie es wieder: steht ein Rauschfilter drin oder
  nicht?
- **B5 — Den Kommentar belegen oder streichen.** «RNNoise ab SDK 5.5» in
  `NippSettings.AudioSettings`. Entweder B4 zeigt den Filter — dann trägt der
  Kommentar seine Messung nach —, oder er wird gestrichen und der Schalter
  bekommt, was ADR-006 Punkt 2 der Echounterdrückung gegeben hat: **eine
  ehrliche Anzeige**.

---

# Runde C — beheben, je nach Befund

**Nichts hiervon wird gebaut, bevor eine Messung es trägt.** Der Katalog steht
hier, damit nach Runde A/B nicht neu überlegt werden muss — nicht als Vorrat
an Änderungen.

| | Massnahme | Greift bei | Vorbehalt |
|---|---|---|---|
| **C0** | **Der Filter bleibt ab Werk ein — und hier aus.** Als **Benutzereinstellung**, die es schon gibt; ADR-054 sorgt dafür, dass kein Profil sie überschreibt. Kein Code | **H1** | **Entschieden auf Basis von A4/A6 und der Präzisierung oben.** Begründung unter «Warum C0 nicht mehr heisst, den Filter umzubauen» |
| ~~**C0a**~~ | ~~Den Filter bei 8 kHz laufen lassen statt bei 48 kHz~~ | — | **Nicht möglich (geprüft am 16.09.2026):** `msnoisesuppressor.h` kennt genau zwei Methoden, `SET_BYPASS_MODE` und `GET_BYPASS_MODE` — keine Rate, keine Platzierung. Der Filter hängt fest in `AudioStream` (`mediastream.h`), und RNNoise arbeitet auf 48 kHz. Das SDK resampelt erst **danach** (`configuring MSNoiseSuppressor-->MSUlawEnc from rate [48000] to rate [8000]`). **Es gibt keinen Hebel** |
| **C8** | **Sagen, was der Schalter tut.** «Rauschunterdrückung» wirkt **nur auf das, was gesendet wird**. Ein Satz in den Einstellungen, wie ihn ADR-006 Punkt 2 der Echounterdrückung gegeben hat | **H4** | Kostet nichts und ist unabhängig von allem anderen richtig. Ein Schalter, von dem der Benutzer das Gegenteil annimmt, ist schlechter als keiner |
| **C1** | **Jitter-Puffer nachstellen** über `Core.Config` (`rtp/jitter_buffer_*`) | H7, H1 | Ein grösserer Puffer kostet Verzögerung. Es braucht vorher eine Zahl, sonst wird eine Latenz gegen ein Gefühl getauscht |
| **C2** | **Echo-Limiter** (`Core.EchoLimiterEnabled`) — heute nie gesetzt | H5 | Er bremst halbduplex und kann Sprache abhacken. Nur, wenn das Symptom Echo ist |
| **C3** | **Codec am Trunk** — bietet die Anlage G.722 an? | H2 | Ist eine Frage an die Anlage, nicht an nipp. Antwort steht im SDP |
| **C4** | **Mikrofonpegel und AGC.** Heute: 50 = 0 dB neutral, AGC aus (ADR-011, der Hersteller nennt den Algorithmus «very experimental») | H6 | Ein höherer Pegel verstärkt das Rauschen mit. Eher Windows-seitig prüfen: Mikrofonverstärkung, Effekte |
| **C5** | **Das Gerät fest wählen** statt `Default Capture`/`Default Playback` | H6, H8 | Es gibt die Einstellung bereits (§9.4). Möglicherweise ist die ganze Sache eine Gerätewahl und keine Zeile Code |
| **C6** | **Windows: Effekte aus, exklusiver Modus, Abtastrate** | H8 | Nicht nipps Zuständigkeit, aber dokumentierbar — gehört in `docs/lehren.md` und in die Einrichtung |
| **C7** | **T38 abschliessen** — auf geliehener x64-Hardware messen | H1 | Wenn Runde A auf die Emulation zeigt, ist **das** das Ergebnis: dann ist nipp nicht kaputt, sondern der Beleg fehlt, den ADR-001 seit dem 07.09.2026 schuldig ist. Ein Nachmittag, zusammen mit AP7.8 |

---

### Warum C0 nicht mehr heisst, den Filter umzubauen

**Erstens gibt es keinen Hebel.** Geprüft am 16.09.2026 in den SDK-Headern:
der Filter kennt Bypass an und Bypass aus, sonst nichts. Er sitzt fest in der
Senderichtung von `AudioStream`, RNNoise arbeitet auf 48 kHz, und das
Resampling auf 8 kHz passiert erst hinter ihm. C0a ist damit tot.

**Zweitens spricht die Messung dagegen, ihn überall abzuschalten.** In vier von
sechs Gesprächen blieb sein Maximum unter 6,2 ms, in einem davon über acht
Minuten. Der Filter ist also **normalerweise unauffällig**; was hier passiert,
sieht nach der Emulation aus und nicht nach dem Filter. Wer ihn deshalb im
Standard abschaltet, verschlechtert **jeden ausgelieferten Arbeitsplatz** —
dort hört die Gegenseite dann den Hintergrund, den RNNoise bisher entfernt hat
— wegen eines Problems, das es dort womöglich gar nicht gibt.

**Drittens ist der Weg schon gebaut.** Die Einstellung existiert, sie wirkt
sofort (ADR-045), und ADR-054 hält fest, dass kein Provisionierungsprofil sie
wieder umlegt. Für diese Maschine ist damit alles getan, was zu tun ist.

**Was daraus folgt, ist eine Messung und kein Umbau:** auf der geliehenen
x64-Maschine, an der ohnehin T38 und AP7.8 fällig sind, gehört **die
Filterstatistik mitgenommen**. Bleibt das Maximum dort unter 6 ms, ist der
Befund ein Emulationsartefakt und die Sache erledigt. Liegt es auch dort bei
77 ms, ist es ein echter Fehler — und dann braucht es ein ADR, keinen
Schalter.

---

## Was in die Testmatrix kommt

Neue Zeilen ab **T304** (T303 ist die höchste vergebene):

| Nr. | Rüstzeug | Was | Erwartung |
|---|---|---|---|
| T304 | P | **A6 — Gegenprobe ohne Rauschunterdrückung** | **bestanden am 16.09.2026, 21:56** — keine Ticker-Verspätung, Jitterpuffer stabil bei 40 ms, Spitzenlast 11,99 statt 77,24 ms, kein Rauschen. **Offen bleibt: dasselbe über ein langes Gespräch** |
| T310 | P | **A7 — der Rufton**, ausgehend, mit Early Media | Kein `Could not get buffer` und kein Puffer-Reset zwischen Läuten und Verbinden |
| T305 | P | **A1 — Mitschnitt gegen Gehör**, externes Testgespräch | Der Befund steht fest: rauscht der Mitschnitt, ja oder nein |
| T306 | S | **A2 — WAV über dieselbe Kette**, ohne SIP | Sauber, oder derselbe Fehler — dann ist es nicht nipp |
| T307 | P | **A3 — zweites Softphone**, selbes Gerät, selbes Ziel | Vergleichbare Qualität oder nicht |
| T308 | P | **A5 — intern (G.722) gegen extern (G.711)** | Ob das Geräusch an der Schmalbandkette hängt |
| T309 | P | **B1/B2 — die Zahlen im Gespräch**, Jitter, Late-Rate, Pegel | Werte stehen im Panel und im Protokoll, ohne Nummer und ohne Namen |

**Die Rüstzeug-Codes folgen A0 im `BEWEIS-PLAN.md`** — `P` braucht die Anlage,
`S` läuft am Schreibtisch. T306 gehört damit in die Runde A1 des Gerätetags
(«139 Zeilen ohne Anlage»), die anderen in A2.

---

## Was dieser Plan nicht tut

- **Keine Zeile Code vor Runde A.** Das ist keine Formalie: die Hälfte der
  Hypothesen liegt ausserhalb von nipp, und ein Eingriff vorher macht jeden
  späteren Befund unlesbar — genau die Begründung, aus der der `BEWEIS-PLAN`
  den Gerätetag vor die SDK-Tests stellt.
- **Kein ADR, bevor eine Messung dasteht.** Zum vierten Mal in diesem Projekt
  wäre es sonst ein ADR, das eine Ursache behauptet, die niemand geprüft hat
  (W2.7 Befund A8; zuletzt ADR-042 über das Ziehen).
- **Keine Einstellung «zur Sicherheit» umstellen.** Wer AGC, Puffer und
  Rauschunterdrückung gleichzeitig anfasst und es wird besser, weiss
  hinterher nichts.

## Der nächste Schritt

**A4 und A6 sind gemacht, die Ursache steht.** Was jetzt ansteht, ist keine
Fehlersuche mehr, sondern eine **Entscheidung** — und drei Messungen, die sie
tragen müssen:

1. **Ein langes Gespräch ohne den Filter.** 23 Sekunden belegen den Vergleich
   mit den 22 Sekunden vom Vormittag, nicht den Alltag. Kostet nichts: der
   Filter ist ohnehin aus.
2. **Die Senderichtung.** Jemanden fragen, ob es lauter im Hintergrund ist.
   Das ist der Preis, den niemand gemessen hat.
3. **Die Filterstatistik auf echter x64-Hardware.** Ein Gespräch, eine Zeile
   aus der Tabelle: bleibt `MSNoiseSuppressor` dort unter 6 ms, war es die
   Emulation. Gehört an den Tag, an dem T38 und AP7.8 ohnehin fällig sind —
   und ist damit **der erste greifbare Nutzen**, den dieser lange offene
   Posten hat.

**Was jetzt schon gebaut werden kann, ist genau eines: C8** — der Satz in den
Einstellungen, dass die Rauschunterdrückung nur das bearbeitet, was gesendet
wird. Der ist unabhängig von jeder weiteren Messung richtig.

**Ein ADR braucht es vorerst nicht:** nichts weicht vom Standard ab, es ist
eine Benutzereinstellung auf einer Maschine. Der Befund gehört in
`docs/lehren.md` — dorthin, wo steht, was Tage gekostet hat.
