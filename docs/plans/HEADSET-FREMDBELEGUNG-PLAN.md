# Fremdbelegung des Headsets — nipp beendet fremde Gespräche

> **Stand 14.09.2026: umgesetzt (ADR-068).** H1 bis H4 und H6 sind erledigt,
> 1245 Tests grün, am Gerät geprüft (T298, T299, T301). **Offen: H5**, der
> Notausgang als Einstellung — und **T300**, die eine Annahme, die keine
> Messung ist: gefragt werden die Standardgeräte, nicht das Headset.

Stand 14.09.2026. Gemeldet aus der Benutzung: «ich war in einem Teams-Meeting,
ein Anruf kam in nipp rein, und als ich ihn **abgelehnt** habe, war das Meeting
weg.»

Vorgänger: **ADR-028** (die Tasten am Headset) mit Nachtrag 4, §22.5.
Betroffen: `HeadsetSignalGate`, `HookWatch`, `HeadsetCallControl`.

---

## 1. Was gemessen ist

Am 14.09.2026 am Gerät, mit einem Jabra PRO 9470 und einem laufenden
Teams-Meeting:

    12:50:21.776  klingelt=true          — Teams laeuft weiter
    12:50:25.486  Anruf abgelehnt (durch den Benutzer)
    12:50:25.528  imGespraech=false, klingelt=false, stumm=false
    12:50:25.731  Geraet antwortet: Usages 0x2A 0x97, Gabel abgenommen=false

**Der Ring stört das Meeting nicht.** Es endet, wenn nipp den Anruf ablehnt
und dabei seinen **Abschlussbericht** schickt. Das Gerät verhandelt daraufhin
seinen Zustand (ADR-028 Nachtrag 2), meldet den Wechsel auf der Eingangspipe,
und weil das Handle geteilt geöffnet ist, bekommt Teams dieselbe Meldung. Für
Teams ist das ein Tastendruck des Benutzers, und der bedeutet im Meeting
auflegen.

**Und das Gerät meldete während des ganzen Meetings `abgenommen=false`** —
09:41:16, :17, :19 und durchgehend am Mittag. Ein Teams-**Meeting** setzt den
Gabelzustand offenbar nicht, anders als ein Teams-**Anruf**.

### Die erste Diagnose war falsch

Sie lautete: der Ring-Bericht sei schuld. Das lag nahe, weil ADR-028
Nachtrag 4 genau diesen Fall beschreibt — **gemessen war es nicht**, und die
Messung sagt etwas anderes. Zum vierten Mal in diesem Projekt eine Ursache,
die aus einem früheren Befund abgeleitet und nicht geprüft wurde; notiert,
weil dieser Plan dieselbe Falle noch zweimal aufgestellt bekommt.

## 2. Warum der gebaute Schutz nicht greift

`HeadsetSignalGate.Erlaubt` hat zwei Ausgänge, und **beide gehen an diesem
Fall vorbei**:

```csharp
if (leer && !jeGemeldet)            → "ohne Anlass"     // greift nur beim Start
if (fremdbelegt && gewuenscht.Klingelt) → "Fremdbelegung"  // greift nur beim Ring
return new SignalUrteil(true, leer ? "Ende" : "Anruf");
```

- **Der Abschlussbericht wird gar nicht geprüft.** Er ist der `leer`-Fall, und
  weil nipp vorher geklingelt hat, ist `jeGemeldet` wahr — er geht durch.
- **Und selbst wenn er geprüft würde**, wäre die Antwort falsch:
  `HookWatch.Fremdbelegung` verlangt `_gemeldet`, also einen off-hook
  meldenden Gerätezustand. Den gibt es im Meeting nicht.

Ein dritter Punkt, hier ohne Wirkung, aber falsch: `Fremdbelegung(calls.Count
> 0)` ergibt beim Klingeln **immer** `false`, weil nipp dann einen eigenen
Anruf hat — und der Ring ist der einzige Fall, für den die Prüfung heute
existiert. Wer nur die Klingelbedingung repariert, repariert nichts.

## 3. Die Zwickmühle

**Wer klingelt, muss auch aufhören zu klingeln.** Den Abschlussbericht
einfach zu unterdrücken hiesse möglicherweise, das Headset im Klingelzustand
stehen zu lassen — mit blinkender Lampe und je nach Gerät mit Ton, bis
irgendetwas anderes es zurücksetzt. §22.5 verlangt ausdrücklich, dass der
Gerätezustand dem Gespräch folgt.

**Am 14.09.2026 gemessen (M1): genau so ist es.** Mit unterdrücktem
Abschlussbericht **klingelt das Jabra weiter** — und zwar auch noch, nachdem
der Anrufer seinerseits aufgelegt hat. Es hört nicht von selbst auf.

Damit ist der einfache Weg erledigt, und der Rest ist der grosse: nipp muss
schweigen, **bevor** es klingelt.

## 4. Die Wege zur Wahl

| | Was es tut | Aufwand | Bewertung |
|---|---|---|---|
| **A — Audio-Sitzung als Fremdbelegung** | Hält ein anderer Prozess die Audio-Sitzung des Headsets, schweigt nipp **ganz** — kein Ring, kein Abschluss | gross | **der tragfähige Weg**, wenn M2 hält: die Frage nach dem Abschluss stellt sich dann gar nicht |
| ~~B — Abschluss nur, wenn der Ring hinausging~~ | Buchführung im Gate: was nipp gemeldet hat, nimmt es zurück | klein | **erledigt durch M1** — ohne Abschluss klingelt das Gerät weiter, und den gemeldeten Fall träfe es ohnehin nicht: hier ging der Ring hinaus |
| C — Einstellung «Headset-Signale senden» | Der Benutzer schaltet es ab | sehr klein | verlagert die Entscheidung auf jemanden, der die Kette nicht kennt. Als **Notausgang** neben A vertretbar |
| D — gar keine Ausgangsberichte mehr | Keine Lampen, keine Ring-Anzeige | klein | steht gegen §22.5, und die Tasten (annehmen/auflegen) blieben davon unberührt — aber es ist der sichere Rückfall, wenn A scheitert |

**Vorgeschlagen: A**, mit C als Notausgang. B ist aufgeführt, weil es naheliegt
und den Fall nicht trifft — das gehört festgehalten, damit es niemand zweimal
denkt.

## 5. Was vor dem Bauen zu messen ist

**M1 — Bleibt das Headset im Klingelzustand, wenn der Abschlussbericht
ausbleibt? ✔ erledigt am 14.09.2026: ja.** Es klingelt weiter, auch nachdem
der Anrufer aufgelegt hat, und hört nicht von selbst auf. Ein Gespräch
annehmen und auflegen war unauffällig, und der nächste Anruf klingelte wieder
normal — das Gerät nimmt also keinen dauerhaften Schaden, es bleibt nur im
Ring stehen. **Die Sonde ist wieder ausgebaut.**

**M2 — Zeigt die Audio-Sitzung ein Teams-Meeting zuverlässig an? ✔ erledigt
am 14.09.2026: ja, und deutlich.** In Ruhe meldet die Abfrage «keine aktive»,
im Meeting steht Teams **in beiden Richtungen** da — Wiedergabe und Aufnahme.
Der Unterschied ist damit genau der, den der Gabelzustand nicht liefert.

**M3 — Erkennt nipp seine eigene Sitzung sicher wieder? ✔ erledigt: ja.** Die
eigene Sitzung kommt mit der eigenen Prozesskennung und ist damit sicher
auszunehmen.

**M4 — Was kostet die Abfrage? ✔ erledigt: 3,2 bis 10,8 ms** für beide Geräte
zusammen. **Damit gehört sie nicht in den Pump-Takt** (20 ms, §14.1) — aber
dorthin muss sie auch nicht: sie läuft in `PushState`, also bei einem
Anrufzustandswechsel, und das sind eine Handvoll Aufrufe je Gespräch.

**Alle vier sind erledigt.** Zwei Dinge sind dabei aufgefallen, die der
Entwurf tragen muss:

- **Ein drittes Programm hielt mit.** Neben Teams stand eine
  Aufnahmeanwendung in der Liste — aber nur, solange Teams lief; in Ruhe war
  die Liste leer. **Das Risiko bleibt:** ein Hintergrunddienst, der dauerhaft
  eine aktive Sitzung hält, brächte nipp dauerhaft zum Schweigen. Dagegen
  steht der Notausgang aus H5.
- **Gemessen wurden die Standardgeräte, nicht das Headset.** Solange das
  Headset das Standardgerät ist, ist das dasselbe — sonst nicht. **Das ist
  eine Annahme und keine Messung**; sie gehört in H6 geprüft, indem im
  Meeting ein anderes Standardgerät eingestellt wird.

## 6. Die Runden

**H1 — M1 messen ✔ erledigt am 14.09.2026.** Ergebnis in Abschnitt 9: das
Gerät klingelt weiter. Weg B ist damit gestrichen, Weg A ist der einzige, der
den Fall löst.

**H2 — M2 bis M4 messen ✔ erledigt am 14.09.2026.** Ergebnisse in Abschnitt 9.

**H3 — `AudioSessionWatch` im Kern ✔ erledigt.**: fragt, ob ein **fremder** Prozess die
Sitzung eines Geräts aktiv hält. Reine Abfrage, ohne Zustand, mit Tests gegen
eine eingeschobene Aufzählung — `Nipp.App` hat kein Testprojekt, und die
Entscheidung «schweigen oder nicht» kostet im Fehlerfall fremde Gespräche.

**H4 — `HeadsetSignalGate` bekommt die Fremdbelegung für alle Berichte ✔ erledigt.**, nicht
nur für den Ring; `HookWatch.Fremdbelegung` behält den Gabelweg als zweite
Quelle (sie erkennt den Teams-**Anruf**, den die Audio-Sitzung auch erkennt,
aber sie kostet nichts). Und `eigeneAnrufe` zählt nur **verbundene** Anrufe,
nicht klingelnde.

**H5 — der Notausgang** (C) — **offen**: eine Einstellung «Signale ans Headset senden»,
vorbelegt mit ein. Für den Fall, dass ein Gerät sich anders verhält als die
beiden, die wir kennen.

**H6 — am Gerät prüfen ✔ erledigt am 14.09.2026** (T298, T299, T301; T300 steht aus).: Meeting läuft, Anruf kommt, **ablehnen** — Meeting
läuft weiter. Dasselbe mit **annehmen** (dann darf Teams das Gerät verlieren,
das ist die Wahl des Benutzers) und mit **wegklingeln lassen**. Dazu die
Gegenprobe ohne fremdes Programm: Lampen und Ring verhalten sich wie in §22.5
beschrieben.

## 7. Was danach zu schreiben ist

- **ADR-068** — die Fremdbelegung hängt an der Audio-Sitzung, nicht am
  Gabelzustand. Was ADR-028 Nachtrag 4 entschieden hat, bleibt in seiner
  Absicht gültig; **seine Erkennung trägt den gemeldeten Fall nicht**.
- **CLAUDE.md**: der Absatz über `HeadsetSignalGate.Erlaubt` nennt heute nur
  den Ausgangsreport ohne eigenen Anruf. Er muss sagen, dass **jeder** Bericht
  ein Eingriff ist, auch der, der aufräumt.
- **docs/test-matrix.md**: die drei Fälle aus H6.
- **docs/stand.md**.

## 8. Was dieser Plan nicht tut

- **Die Tasten am Headset bleiben, wie sie sind.** Annehmen, auflegen und
  stumm sind Eingaben; dieser Plan handelt ausschliesslich von dem, was nipp
  **hinausschickt**.
- **Kein Eingriff in die Audiowege.** Die bleiben beim SDK (§22.5).
- **Keine Erkennung, welches Programm das Gerät hält.** Es genügt zu wissen,
  **dass** eines es hält; welches, ist für die Entscheidung ohne Belang und
  wäre ein Name im Protokoll, der dort nichts zu suchen hat.

## 9. Protokoll

| Datum | Was gemessen wurde | Ergebnis |
|---|---|---|
| 14.09.2026 | Ring gegen Abschlussbericht, mit laufendem Teams-Meeting | **Der Ring stört nicht, der Abschlussbericht beendet das Meeting** |
| 14.09.2026 | Gabelzustand des Jabra während eines Teams-Meetings | durchgehend `abgenommen=false` — die heutige Erkennung kann nicht anschlagen |
| 14.09.2026 | **M1** — Abschlussbericht unterdrückt, Anruf abgelehnt | **Das Headset klingelt weiter**, auch nachdem der Anrufer aufgelegt hat; es hört nicht von selbst auf. Gespräch annehmen und auflegen unauffällig, der nächste Anruf klingelt wieder normal |
| 14.09.2026 | Nebenbefund bei M1: «es dauert sehr lange, bis ich Audio habe» (beim Annehmen) | **Nicht nipp** — im Protokoll `IncomingReceived` 13:05:16.581, `Connected` :17.958, `StreamsRunning` :18.054, also **96 ms** bis zum Strom. Was danach kommt, ist ungemessen; **vermutlich** der Aufbau der DECT-Funkstrecke des Geräts. Der Durchgang lief mit der Sonde, die das Headset im Ring hielt — **eine Gegenprobe ohne Sonde fehlt** |
| 14.09.2026 | **M2** — Sitzungen bei klingelndem Anruf, mit und ohne Teams | **ohne Teams:** `Wiedergabe [keine aktive], Aufnahme [keine aktive]` · **im Meeting:** `Wiedergabe [ms-teams, ms-teams, <Aufnahmeprogramm>], Aufnahme [ms-teams, <Aufnahmeprogramm>]`. Der Unterschied ist eindeutig |
| 14.09.2026 | **M3** — eigene Sitzung | Im eigenen Gespräch steht die eigene Prozesskennung in der Liste und ist sicher auszunehmen |
| 14.09.2026 | **M4** — Kosten | **3,2 bis 10,8 ms** für beide Geräte. Nicht pumptauglich, aber in `PushState` unbedenklich |
| 14.09.2026 | **H6** — vier Fälle am Gerät: ablehnen im Meeting, annehmen und auflegen, ohne Teams, wegklingeln lassen | **alle vier bestanden** |
