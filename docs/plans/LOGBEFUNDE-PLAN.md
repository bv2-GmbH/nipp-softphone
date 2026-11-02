# Was vierzehn Tage Protokoll zeigen — und was davon zu reparieren ist

**Stand: 30.10.2026.** Gelesen wurden die Protokolle vom **17. bis 30.10.2026**
(224 MB, vierzehn Dateien) sowie `crash.txt` und `settings.json` desselben
Arbeitsplatzes. Ausgewertet mit `Zeige-Gespraeche.ps1` (60 Gespräche) und über
gruppierte Fehlerzeilen.

**Jede Zahl hier ist gezählt, nicht geschätzt** (W2.7, Befund A8). Wo eine
Ursache nur wahrscheinlich ist, steht „vermutlich" — und die Messung, die sie
belegen oder widerlegen würde, steht daneben.

---

## Umsetzungsstand (30.10.2026, abends)

**Runde 1 und Runde 2 sind durch, Runde 3 zur Hälfte.** Gebaut in einem Zug,
zentral getestet (1 492 Tests grün), als **0.9.16** ausgeliefert und auf dem
Arbeitsplatz installiert.

| Schritt | Stand |
|---|---|
| **R1.0** Anklopfton (L12) | **gebaut und am 02.11.2026 gehört** — der Ton ist ersetzt, die Gegenseite blieb verständlich, der wartende Anruf fiel auf. **Die zweite Ausgabekette bleibt** (1 687 Durchläufe) und hat in diesem Gespräch **nichts gekostet**: `Hoerbar 0`, `PufVoll 0`. Sie gehört ab jetzt zu L6, nicht mehr zu L12 (ADR-081, Nachtrag) |
| **R1.1** Kontenabgleich (L1) | **gebaut und gemessen** — T335 am Schreibtisch bestanden: das Geisterkonto ist weg, `No listening point matching` von 108 auf null, das echte Konto angemeldet |
| **R1.2** Thread-Wechsel (L2) | **gebaut** — zwölf Empfänger über `Threading/UiDispatch.cs`. T336 braucht das Gerät |
| **R1.3** Zwei Störungsarten (L4) | **gebaut, und T338 ist am 02.11.2026 bestanden** — die Berichtszahlen stimmen auf die Zeile mit dem Protokoll überein (25 = 25, 2 = 2, 1 = 1), die Gegenprobe steht bei null. `late wakeup by` kam an diesem Tag nicht vor und bleibt ungeprüft |
| **R1.4** Aufnahmepfad (L8) | **gebaut, T340 halb** — der Pfad steht jetzt auf `bv2\`; dass eine **Aufnahme** dort landet, ist ungeprüft |
| **R1.5** Tastenkürzel (L11) | **gebaut** — die Anzeige gab es längst, es fehlte die Bindung im XAML |
| **R2.1–R2.3** Release (L3) | **erledigt.** 0.9.16 ist draussen, installiert, und **T337 ist am 02.11.2026 bestanden**: `Hoerbar` trägt in fünf Gesprächen eine Zahl, und zwar null |
| **R3.1/R3.2** Anmeldelücken (L5) | **gemessen**, siehe unten |
| **R3.3** Entprellung | **nicht geändert** — die Messung spricht dagegen, siehe unten |
| **R3.4** L6 in den Audioplan | **eingetragen**, ohne Deutung |
| **L9** Provisionierungsadresse | **geleert** auf diesem Arbeitsplatz (Entscheid vom 30.10.2026). Die zweite Hälfte — dass nipp bei 404 deutlicher wird — ist **nicht** gebaut |
| **L7** Toasts | **eigener Befund**, neue Prüfzeile **T342**. Nicht untersucht |
| **L10** Debug | **bleibt** — die A7-Jagd läuft (Entscheid vom 30.10.2026) |

### Was R3.1 ergeben hat, und warum R3.3 damit vorerst erledigt ist

`tools/Zeige-Anmeldeluecken.ps1` misst die Minuten ohne `Registered`, je Tag
und Konto. Über die vierzehn Tage, für das echte Konto:

- **197 vollständig beobachtete Lücken, zusammen 726 Minuten**, längste 225
  Minuten (23.10.).
- **Der Median liegt bei 0,08 Sekunden.** 150 der Lücken sind kürzer als fünf
  Sekunden, 177 kürzer als dreissig; **nur elf sind länger als zehn Minuten.**

**Damit beantwortet die Messung die Frage, die R3.3 gestellt hat, und die
Antwort lautet «Sekunden».** Die Salven aus L5 kosten im Normalfall
Augenblicke; die Gesamtminuten kommen fast vollständig aus den wenigen langen
Lücken, und die sehen nach ausgeschaltetem Rechner aus — **gemessen ist das
aber nicht**, und wer die Entprellung doch anfassen will, misst das zuerst.
Die Entprellung bleibt bei zwei Sekunden.

**Die Zahlen des zweiten Kontos gehören nicht hierher:** «Beispiel
Arbeitsplatz» zeigt Lücken von über 500 Minuten am Tag, aber das ist L1 — das
Geisterkonto, das sich gar nicht anmelden konnte. Seit R1.1 gibt es es nicht
mehr.

---

## Das Bild in einem Absatz

An zwölf von vierzehn Tagen läuft nipp unauffällig: 60 Gespräche, kein
einziger Absturz mit Datenverlust, die Anmeldung steht. Von den **6 851
Fehlerzeilen** der vierzehn Tage gehen **rund 5 000 auf zwei Netzwerkausfälle**
am 22. und 23.10. und **211 auf ein Konto, das es gar nicht mehr geben
sollte**. Die Befunde unten sind deshalb nicht „nipp ist kaputt", sondern:
**drei Reparaturen, die sich lohnen, zwei Messlücken, die uns bei der
Audio-Jagd blind machen, und drei Einstellungen, die falsch stehen.**

**Nachgetragen am 30.10.2026, und es ist der schwerste der Liste:** **L12** stammt nicht aus der Fehlerliste, sondern aus einer Meldung — der zweite eingehende Anruf übertönt das laufende Gespräch. Im Protokoll steht er siebenmal, und er erzeugt **keine einzige Fehlerzeile**. Das ist die nützlichste Lehre dieser ganzen Auswertung: **was am meisten stört, muss sich nicht beschweren.**

---

## Die Befunde, nach Wirkung

| Nr. | Befund | Gemessen | Wirkung |
|---|---|---|---|
| **L12** | **Der zweite Anruf übertönt das laufende Gespräch** | 7 Fälle in 14 Tagen, Ton bei voller Amplitude alle 2 s | **Ein Kundengespräch wird unbrauchbar** |
| **L1** | Ein Konto im SDK, das in den Einstellungen nicht steht | 211 Fehlerzeilen, 132 Registrierungsversuche in 14 Tagen | Falscher Kontozustand, Rauschen im Protokoll |
| **L2** | Absturz beim Audiogerät-Wechsel über das Infobereich-Menü | 1× am 20.10., `crash.txt` | nipp ist weg, mitten im Betrieb |
| **L3** | Die Hörbarkeitsmessung aus ADR-080 kommt im Alltag nicht an | 60 von 60 Gesprächen melden `?` | Die Zahl, auf die CLAUDE.md verweist, gibt es nicht |
| **L4** | `SdkStoerung` zählt zwei Störungstexte nicht | 149 + 8 Vorkommen, keines im Bericht | Der Gesprächsbericht untertreibt |
| **L5** | Netzwerkwechsel kommen in Salven durch die Entprellung | 2 126 erkannt, 979 ans SDK gemeldet | Jede Meldung kostet eine Neuanmeldung |
| **L6** | Die Audiokette wird je Gespräch mehrfach neu aufgesetzt | 304 Initialisierungen auf 60 Gespräche | Gehört zu A7, noch ungedeutet |
| **L7** | Toasts sind auf diesem Arbeitsplatz nicht verfügbar | 3× `COMException 0x8007007E` | §8.6 ist nicht erfüllt |
| **L8** | `RecordingDirectory` zeigt in das Velopack-Verzeichnis | Einstellung gelesen, Ordner liegt an | Eine Deinstallation nähme Aufnahmen mit (ADR-079) |
| **L9** | `ProvisioningUri` zeigt auf eine Kontaktsuch-Adresse | 3× `404` beim Start | Provisionierung läuft bei jedem Start ins Leere |
| **L10** | Debug ist dauerhaft eingeschaltet | 16 MB am Tag, `"Logging": "Debug"` | Entscheidung, kein Fehler — siehe unten |
| **L11** | Das Tastenkürzel zum Annehmen ist dauerhaft belegt | 6× Windows-Fehler 1408 | Die Funktion gibt es auf diesem Rechner nicht |

---

## L1 — Ein Konto, das es nicht mehr gibt

**Gemessen.** `settings.json` kennt **ein** Konto. Das Protokoll führt **zwei**:
daneben läuft „Beispiel Arbeitsplatz" `<sip:151@pbx.example.ch>` — das Testkonto
von der Test-PBX. In vierzehn Tagen erzeugt es

- **108×** `No listening point matching for [tls://pbx.example.ch:5061]`,
- **103×** `Unable to subscribe to the conference event package (RFC 4575)`,
- **79×** `Registrierung None`, **44×** `InProgress`, **9×** `Failed`.

Es erscheint an zehn der vierzehn Tage, jeweils kurz nach dem Start.

**Die Ursache ist belegt, nicht vermutet.** `%APPDATA%\nipp\linphonerc` trägt
unter `[proxy_0]` genau dieses Konto mit `reg_sendregister=1`. **Das SDK führt
seine eigene Kontenliste und schreibt sie selbst fort.** nipp gleicht beim
Start nur in eine Richtung ab: was in den Einstellungen steht, wird angelegt —
was im SDK steht und in den Einstellungen fehlt, bleibt stehen.
`RemoveAccountAsync` räumt sauber auf (samt `AuthInfo`, §9.1), **wird aber nur
von der Einstellungsseite gerufen** (`SettingsViewModel`, zwei Stellen). Eine
Kontenliste, die über ein Provisionierungsprofil ersetzt wird, geht also nie
durch diesen Weg.

**Das ist dieselbe Klasse Fehler wie der Schutz an der toten Methode:** die
Aufräumfunktion ist da, sie ist richtig, und sie läuft im entscheidenden Fall
nicht.

**Vorschlag.** Beim Start, nach dem Anlegen der Konten: jedes Konto in
`Core.AccountList`, dessen Identität in den Einstellungen nicht vorkommt, über
denselben Weg entfernen, der auch die `AuthInfo` mitnimmt. Eine Protokollzeile
auf `Information` je entferntem Konto — sonst verschwindet hier still eine
Anmeldung, und das ist genau die Sorte Stille, die dieses Projekt zweimal
bezahlt hat.

**Vorsicht an einer Stelle:** Ein Konto, das gerade erst angelegt wurde und
dessen Eintrag in den Einstellungen noch nicht geschrieben ist, darf der
Abgleich nicht wegräumen. Er gehört deshalb an den Start, nach dem ersten
vollständigen Anwenden — nicht an `SettingsService.Changed`.

**Prüfzeile: T335.**

---

## L2 — Der Absturz beim Audiogerät-Wechsel

**Gemessen.** `crash.txt`, 20.10.2026 11:51:47:

```
System.Runtime.InteropServices.COMException (0x80070580):
Unzulässiges Fenster. Es gehört zu einem anderen Thread.
   at ...IAppWindowStaticsMethods.GetFromWindowId(...)
   at Nipp.App.MainWindow.get_AppWindowRef()
   at Nipp.App.MainWindow.OnSettingsChanged(Object sender, NippSettings settings)
   at Nipp.App.Windows.TrayIconHost.UsePlaybackDevice(String deviceId)
   at H.NotifyIcon.Core.PopupMenu.Show(...)
```

**Die Ursache steht im Stack.** Das Kontextmenü des Infobereich-Symbols läuft
im Nachrichtenfenster von `H.NotifyIcon`, **nicht im UI-Thread**. Wer dort das
Audiogerät umstellt, schreibt die Einstellungen, `SettingsService.Write` meldet
`Changed`, und `MainWindow.OnSettingsChanged` greift auf das `AppWindow` zu —
auf dem falschen Thread.

**Dieselbe Wurzel, zwei weitere Belege.** Im Protokoll stehen zwei Warnungen
`Ein Abonnent von OnAccountRegistrationStateChanged hat eine Ausnahme
ausgeloest`: einmal `InvalidOperationException: UpdateToolTip failed.`
(23.10.), einmal eine `COMException` ohne Meldung (20.10., **dieselbe Sekunde**
wie der Absturz). Die Ereignisse der Dienste erreichen die Oberfläche also an
mehreren Stellen ohne Thread-Wechsel. Dass nipp nur einmal gestorben ist, liegt
daran, dass die anderen beiden Wege einen Schutz hatten (ADR-053) — **dieser
hier lag ausserhalb**, genau wie der Rahmen in ADR-053 beschreibt.

**Vorschlag.** `MainWindow.OnSettingsChanged` und die übrigen Empfänger von
Dienstereignissen in `Nipp.App` über die `DispatcherQueue` des Fensters führen.
**Nicht** den einen Zugriff absichern — die Regel ist der Thread, nicht die
Zeile, sonst steht in vier Wochen der nächste Zugriff daneben.

**Prüfzeile: T336.**

---

## L3 — ADR-080 ist gebaut und nicht ausgeliefert

**Gemessen.** `Zeige-Gespraeche.ps1 -Tage 14` zeigt in **allen 60** Gesprächen
`Hoerbar = ?`. Die Rohzeile bestätigt es:

```
Gespraech 7692fd85 ausgewertet: Dauer 21:08, ... Verwuerfe 40 (max 75 ms),
Ticker spaet 1 (max 1147 ms), Pufferfehler 3, Messungen 1255, SDK-Stufe Debug
```

Kein `davon hoerbar`. Im Repo steht es seit Commit `2507b9d` vom 12.10.2026.
Installiert ist **0.9.15** (`%LOCALAPPDATA%\nipp\current\sq.version`), und die
Fassung ist älter als der Commit.

**Das heisst:** CLAUDE.md nennt seit dem 12.10. die Spalte `Hoerbar` als „die
Zahl, auf die es bei einer Meldung ankommt" — und auf dem einzigen
Arbeitsplatz, der sie liefern könnte, gibt es sie nicht. Wer in den letzten
achtzehn Tagen eine Audiomeldung untersucht hätte, wäre auf `?` gestossen und
hätte die Erklärung in CLAUDE.md gefunden („Protokoll von vor dem 12.10."), die
hier **falsch** ist.

**Vorschlag.** Ein Release **0.9.16**. Es ist der einzige Befund in dieser
Liste, der nichts kostet ausser dem Vorgang selbst — und er beantwortet
nebenbei die offene Gegenprobe aus `AUDIOQUALITAET-PLAN.md`.

**Und die Lehre dazu gehört aufgeschrieben:** eine Messung, die nur im Repo
steht, misst nichts. Das ist die Schwester des Satzes über den Update-Pfad, der
sich nie selbst ausliefert.

---

## L4 — Zwei Störungstexte, die niemand zählt

**Gemessen**, in vierzehn Tagen:

| Text | Vorkommen | Von `SdkStoerung` erfasst |
|---|---|---|
| `output buffer was filled with at least N ms ... asking to drop` | 489 | ja (`Verwurf`) |
| `MSTicker: We are late of N miliseconds.` | 43 | ja (`TickerZuSpaet`) |
| `Could not get buffer from the MSWASAPI audio output interface` | 122 | ja (`Pufferfehler`) |
| **`cannot write output buffer of N samples, not enough space`** | **149** | **nein** |
| **`MSTicker: late wakeup by N ms`** | **8** | **nein** |

Beide ungezählten stehen als **`[ERR]`** im Protokoll, kommen also auch ohne
Debug durch. Die 149 verteilen sich nicht gleichmässig: **80 davon allein am
30.10.**, und zwar um 16:31 — eine halbe Stunde vor dem Gespräch `7692fd85`,
das mit 40 Verwürfen und `Ticker spaet max 1147 ms` das schlechteste der
letzten Woche ist.

**Die Deutung ist offen, und das ist wichtig.** `cannot write output buffer ...
[480=9600-9120]` heisst: der Ausgabepuffer ist voll, 480 Abtastwerte passen
nicht mehr hinein. Das ist das Gegenstück zu `Could not get buffer` und
**vermutlich** derselbe Vorgang aus der anderen Richtung. Ob es hörbar ist,
sagt die Zeile nicht — und genau deshalb gehört sie gezählt, bevor jemand
daraus eine Ursache macht.

**Vorschlag.** Zwei Muster in `SdkStoerung.Lies` ergänzen, zwei Arten in
`StoerungsArt`, die Zeilen im Wortlaut in `SdkStoerungTests`, zwei Spalten in
`Zeige-Gespraeche.ps1`. **Nicht** in die bestehenden Arten einsortieren: ein
voller Ausgabepuffer und ein verworfener Puffer sind nicht dasselbe, und wer
sie zusammenzählt, kann sie nie wieder trennen.

**Prüfzeile: T338.**

---

## L5 — Netzwerkwechsel in Salven

**Gemessen**, vierzehn Tage:

- **2 126×** `Netzwerkwechsel erkannt, Internet erreichbar: true`, **242×** `false`
- **979×** `Netzwerk erreichbar: true` an das SDK gemeldet
- **331×** `Registrierung Registered`

Am 29.10. kamen die Wechsel so: `08:10:49, :49, :50, :51, :55, :57, 08:11:01,
:04, :06, :10, :14, :15` — zwölf Meldungen in sechsundzwanzig Sekunden. Die
Entprellung in `ConnectivityMonitor` steht auf **zwei Sekunden**
(`DefaultDebounceDelay`).

**Die Signaturprüfung aus ADR-060 arbeitet** — von 2 368 Wechseln kommen 1 041
beim SDK an, sie hält also mehr als die Hälfte zurück. **Aber 81 Meldungen an
einem Tag sind immer noch 81 mögliche Neuanmeldungen**, und jede ist ein
Zeitfenster, in dem ein Anruf nicht durchkommt.

**Was hier noch nicht gemessen ist** — und ohne diese Messung ist jede Änderung
geraten: wie lange nipp nach einer solchen Salve tatsächlich abgemeldet war.
Die Protokollzeilen tragen die Zustände, aber niemand hat die Lücken addiert.

**Vorschlag, in dieser Reihenfolge.**

1. **Erst messen:** ein Werkzeug, das aus den `Registrierung …`-Zeilen die
   Zeiträume ohne `Registered` summiert — je Tag und je Konto. Das beantwortet
   die Frage, die zählt: **wie viele Minuten am Tag war nipp nicht
   erreichbar?**
2. **Dann entscheiden.** Ist die Antwort „Sekunden", ist L5 Rauschen und die
   Entprellung bleibt. Ist sie „Minuten", ist das Entprellfenster der erste
   Verdächtige — und dann gehört die **Gegenprobe** dazu: ein längeres Fenster
   darf den echten Netzwechsel nicht verschlucken (ADR-060 nennt sie
   ausdrücklich die wichtigere Hälfte).

**Prüfzeile: T339.**

---

## L12 — Der zweite Anruf übertönt das laufende Gespräch

**Gemeldet am 30.10.2026 von Dominic:** „wenn ich mit nipp in einem call bin und
ein zweiter rein kommt erscheint die Makeln-Funktion aber ich höre den aktuellen
Anruf nicht mehr." **Nachvollzogen am selben Tag im Protokoll** — der
Mechanismus ist belegt, die Wahrnehmung noch nicht gemessen (siehe unten, das
ist der Unterschied, auf den es hier ankommt).

**In vierzehn Tagen ist der Fall siebenmal eingetreten** — ein eingehender Anruf
während eines verbundenen Gesprächs, am 19., 22., 23., zweimal am 26. und
zweimal am 29.10.

**Was das SDK tut.** Beim zweiten Anruf, am 23.10. um 16:00:02, mitten in einem
Gespräch, das seit 15:53 lief:

```
[ToneManager] startNamedTone
[ToneManager] playTone playing DTMF tone
[ToneManager] using audio from current call.
Playing tones of frequencies 440,0 Hz, duration=300,
              amplitude=1.000000 interval=2000, repeat_count=0
```

Drei Dinge daran, und jedes einzelne genügt:

- **`amplitude=1.000000`** — Vollaussteuerung. Der Ton ist so laut, wie das
  Gerät kann.
- **`interval=2000, repeat_count=0`** — alle zwei Sekunden, **unbegrenzt**. Er
  hörte an diesem Tag erst nach **elf Sekunden** auf, als der Anrufer aufgab.
- **`using audio from current call`** — er geht in den Audioweg des
  **laufenden** Gesprächs, nicht auf das Klingelgerät.

**Und es ist nicht nur ein Ton.** Die Abschlussstatistik desselben Vorgangs
zeigt eine **zweite Ausgabekette**, die parallel auf dasselbe Gerät geschrieben
hat:

```
MSWASAPIWrite   1091   0.00  0.01  0.06  0.00   4.1
MSFilePlayer    1091   0.00  0.00  0.01  0.00   0.7
```

1 091 Durchläufe bei 10 ms sind genau die elf Sekunden. **Zwei Schreiber auf
demselben Ausgabegerät** — das ist zugleich die bisher plausibelste Erklärung
für einen Teil von L6 (304 Ausgabe-Initialisierungen auf 60 Gespräche) und für
die Situation, in der L4 auftritt.

**nipp steuert diesen Ton nirgends.** `SetTone`, `ToneID` und
`CallToneIndicationsEnabled` kommen im gesamten Code nicht vor. Was hier
passiert, ist das unveränderte Standardverhalten des SDK — niemand hat es je
entschieden.

**Verschärfend, aber nicht ursächlich:** `playback_gain_db=6.600000` in
`linphonerc`. Die Wiedergabe ist um 6,6 dB angehoben (aus der
Lautstärkeeinstellung, `SettingsApplier`), und der Ton geht denselben Weg.

### Was gemessen ist und was nicht

**Gemessen:** der Ton, seine Amplitude, sein Intervall, seine Dauer, sein Weg
in das laufende Gespräch, und die zweite Ausgabekette.

**Nicht gemessen:** dass der erste Strom dabei *verstummt*. Im Protokoll läuft
er weiter — `MSAudio stream: receiving RTCP SR` kommt während der ganzen elf
Sekunden im Fünf-Sekunden-Takt, und im Fenster steht **kein einziger** Verwurf
und kein Pufferfehler. **Was das Protokoll zeigt, ist ein zweiter, voll
ausgesteuerter Schreiber über einem weiterlaufenden Gespräch.** Ob das in der
Wahrnehmung „ich höre nichts mehr" ergibt oder ob zusätzlich etwas den ersten
Strom unterdrückt, **entscheidet ein Hörtest, keine Protokollzeile** — und
genau deshalb steht hier keine Ursache, die über das Gemessene hinausgeht.

### Der Auftrag dazu

§8.2 sagt: „Zwei parallele Gespräche: eines aktiv, eines gehalten, Umschalten
sichtbar." **Ein Anklopfton steht in der Spezifikation nicht** — weder dass es
ihn gibt noch wie er klingt. Dass das aktive Gespräch Vorrang hat, ist die
nächstliegende Lesart von §8.2 und deckt sich mit der Meldung.

### Vorschlag

**Nicht** `CallToneIndicationsEnabled = false`: das nähme auch Besetztton und
den Ton bei abgerissener Verbindung mit, und beide sind nützlich.

**Sondern `Core.SetTone(ToneID.CallWaiting, …)`** mit einer eigenen Datei — der
Weg ist in `NippSounds` schon gebahnt, nipp bringt eigene Töne mit (ADR-075).
Zu entscheiden ist dabei **nur eine Frage, und sie gehört gehört, nicht
gerechnet:** wie leise und wie selten ein Anklopfton sein muss, damit er
auffällt, ohne das Gespräch zu zerstören. Ein einzelner kurzer Ton beim
Eintreffen ist der nächstliegende Anfang — der Hinweis, dass jemand wartet,
steht ohnehin auf dem Bildschirm.

**Zweite Hälfte, und sie ist die unsichere:** ob die zweite Ausgabekette damit
verschwindet. Bleibt sie, ist sie getrennt zu behandeln — dann war der Ton nur
das Hörbare an einem Problem, das darunter liegt. **Das ist die eigentliche
Messung an dieser Zeile**, und sie gehört vor jede weitere Änderung.

**Prüfzeile: T341.**

---

## L6 — Die Audiokette wird zu oft aufgebaut

**Gemessen**, vierzehn Tage, 60 Gespräche:

- **304×** `output initialized for [...]` (130 Default Playback, 104 interner
  Lautsprecher, 70 Jabra)
- **282×** `trying to change output channel to N is not supported by the device`
- **191×** `changing output rate to N Hz is not supported by the device`
- **69×** `input initialized`

**Fünf Ausgabe-Initialisierungen je Gespräch bei einer Eingabe-Initialisierung**
— das passt nicht zusammen, und eine Erklärung dafür steht hier nicht. Es
könnte der eigene Rufton sein (ADR-075 baut eine zweite Kette auf), es könnten
die sieben `Audiogeraet verschwunden`-Meldungen sein, es könnte ein
Gerätewechsel bei jedem Zustandswechsel sein.

**Hier steht ausdrücklich keine Ursache.** Das ist eine Beobachtung, die zu A7
gehört und in `AUDIOQUALITAET-PLAN.md` aufgenommen werden sollte — zusammen mit
der Frage, ob die abgelehnten Raten ein zusätzliches Resampling erzwingen.

---

## L7 bis L11 — die kleineren Sachen

**L7, Toasts.** 3× `Benachrichtigungen nicht verfuegbar: COMException
0x8007007E (Das angegebene Modul wurde nicht gefunden.)` — am 20., 21. und
29.10., jeweils beim Start. §8.6 verlangt Toasts; ausgeliefert wird unpackaged,
**weil** packaged keine bekommt (T110). Dass sie unpackaged auch nicht kommen,
ist der eigentliche Befund. Gehört zu T110 und ist dort zu notieren.

**L8, der Aufnahmepfad.** `"RecordingDirectory":
"C:\Users\…\AppData\Local\nipp\recordings"` — **ohne `bv2\`**. Der Ordner liegt
bereits an (21.10.). Das ist das Verzeichnis, in das Velopack installiert:
Aufnahmen gingen bei einer Deinstallation mit, und ADR-079 ist für diesen Pfad
nicht angekommen. **Ursache vermutlich:** `DatenUebernahme` verschiebt Dateien,
aber ein Pfad, der als Zeichenkette **in den Einstellungen** steht, wird dabei
nicht umgeschrieben. Zu prüfen und, falls bestätigt, einmalig mitzuziehen.
**Prüfzeile: T340.**

**L9, die Provisionierungsadresse.** `ProvisioningUri` zeigt auf die Adresse
einer **Kontaktsuche**, nicht auf ein Provisionierungsprofil. Folge: bei jedem
Start eine Warnung `nicht abrufbar: 404 (Not Found)`. Das ist zuerst eine
falsche Einstellung auf diesem Arbeitsplatz. Es ist aber auch eine Frage an
nipp: eine Adresse, die mit 404 antwortet, könnte deutlicher sagen, dass dort
vermutlich gar kein Profil liegt.

**L10, Debug.** `"Logging": "Debug"`, 16 MB am Tag, 224 MB im Bestand (Grenze:
14 Dateien à 64 MB). **Das ist keine Fehlfunktion, sondern vermutlich Absicht**
— T332 verlangt Debug ausdrücklich für die Filterstatistik, und die A7-Jagd
läuft. Die Frage ist nur, ob sie noch läuft. Steht sie, gehört die Stufe zurück
auf normal; dafür ist ADR-077 gebaut worden.

**L11, das Tastenkürzel.** 6× `Ctrl+Shift+A ... ist bereits von einer anderen
Anwendung belegt (Windows-Fehler 1408)`, bei jedem Start, über alle vierzehn
Tage. Die Einstellung steht auf diesem Wert, die Funktion gibt es also nicht.
nipp meldet es korrekt im Protokoll — **sichtbar ist es in der Oberfläche
nicht.** Wer das Kürzel einstellt und es bleibt wirkungslos, erfährt den Grund
nirgends. Ein kleiner Befund mit einer klaren Antwort: die Einstellung soll den
Fehlschlag zeigen.

---

## Umsetzung, in vier Runden

Die Reihenfolge folgt **nicht** der Wirkung, sondern dem Rüstzeug — und der
Regel, dass gesammelt repariert wird, nicht mitten in einer Messrunde.

### Runde 1 — Schreibtisch, ein Nachmittag

| Schritt | Was | Befund |
|---|---|---|
| **R1.0** | **Den Anklopfton ersetzen** (`Core.SetTone(ToneID.CallWaiting, …)`), danach hören — und messen, ob die zweite Ausgabekette bleibt | L12 |
| **R1.1** | Kontenabgleich beim Start: SDK-Konten ohne Eintrag in den Einstellungen entfernen, mit Protokollzeile | L1 |
| **R1.2** | Dienstereignisse in `Nipp.App` über die `DispatcherQueue` | L2 |
| **R1.3** | Zwei Störungsarten in `SdkStoerung`, Tests im Wortlaut, zwei Spalten im Werkzeug | L4 |
| **R1.4** | `RecordingDirectory` beim Datenumzug mitziehen (erst prüfen, ob die Vermutung trägt) | L8 |
| **R1.5** | Die Einstellungsseite zeigt, wenn ein Tastenkürzel belegt ist | L11 |

**R1.0 zuerst**, und zwar vor allem anderen in dieser Liste: es ist der
einzige Befund, der ein laufendes Kundengespräch unbrauchbar macht. Er
braucht die Anlage und ein Ohr — alles Übrige in Runde 1 ist ohne Gerät
prüfbar und mit Tests abzudecken. Danach **R1.1 und R1.2**, die beiden, die
im Betrieb etwas kaputtmachen.

### Runde 2 — Release

| Schritt | Was | Befund |
|---|---|---|
| **R2.1** | `VersionPrefix` auf 0.9.16 ziehen (Doppelwahrheit vermeiden, siehe CLAUDE.md) | — |
| **R2.2** | `Release-Nipp.ps1 -Version 0.9.16 -Channel stable -Publish` | L3 |
| **R2.3** | Auf dem Arbeitsplatz installieren, ein Gespräch führen, `Zeige-Gespraeche.ps1` — die Spalte `Hoerbar` muss eine Zahl tragen | L3 |

**R2 kommt nach R1**, nicht davor: dann trägt dasselbe Release alle
Reparaturen, und die Messung aus R2.3 misst gleich mit, ob R1.3 ankommt.

### Runde 3 — Messen, bevor entschieden wird

| Schritt | Was | Befund |
|---|---|---|
| **R3.1** | Auswertung der Anmeldelücken bauen (Minuten ohne `Registered`, je Tag) | L5 |
| **R3.2** | Damit die vierzehn Tage rückwirkend auswerten — die Protokolle liegen vor | L5 |
| **R3.3** | Erst danach über die Entprellung entscheiden, mit Gegenprobe | L5 |
| **R3.4** | L6 in `AUDIOQUALITAET-PLAN.md` aufnehmen, ohne Deutung | L6 |

### Runde 4 — was eine Entscheidung braucht

| Frage | Befund |
|---|---|
| Läuft die A7-Jagd noch, oder geht die Protokollstufe zurück auf normal? | L10 |
| Ist die Provisionierungsadresse auf diesem Arbeitsplatz ein Versehen, oder soll sie weg? | L9 |
| Toasts unpackaged: gehört das zu T110, oder ist es ein eigener Befund? | L7 |

---

## Was in diesem Plan ausdrücklich **nicht** steht

- **Eine Ursache für die Aussetzer (A7).** Die 149 ungezählten Zeilen sind ein
  Verdacht und kein Befund. Sie werden in R1.3 **zählbar gemacht**, damit die
  nächste Messung sie sieht — mehr behauptet L4 nicht.
- **Dass die Netzwerkwechsel Anrufe gekostet haben.** Das ist die Frage von
  R3.1, nicht ihre Antwort.
- **Dass die rund 5 000 Fehlerzeilen vom 22. und 23.10. ein nipp-Problem
  sind.** An beiden Tagen war das Netz weg; nipp hat sich jedes Mal selbst
  wieder angemeldet. Was man daran kritisieren kann, ist die **Lautstärke** des
  Protokolls, nicht das Verhalten.
