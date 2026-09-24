# Alleingang — was ohne Dominic abzuarbeiten ist

**Angelegt am 24.09.2026.** Auftrag: «Was könntest du noch erledigen, ohne
dass ich etwas machen muss? Erstell einen Plan, um möglichst viel
abzuarbeiten.»

Bezug: `docs/test-matrix.md` (137 offen von 319, gezählt am 24.09.2026 mit
`Zaehle-Matrix.ps1`), `docs/plans/BEWEIS-PLAN.md` (A1 und E7 — die vierzehn
verbliebenen `S`-Zeilen und warum jede liegenblieb), `docs/plans/REVIEW-2026-09-12.md`
(W2.1), `docs/plans/HEADSET-FREMDBELEGUNG-PLAN.md` (H5),
`docs/plans/ZIEHVORSCHAU-PLAN.md` (V8-Rest), `docs/plans/EINRICHTUNG-PLAN.md` (K4).

> **Stand 24.09.2026, abends: Runde 1 bis 3 sind durch.** Fünf Zeilen haben
> ein Ergebnis bekommen — **T174, T72, T246, T288** bestanden, **T311
> teilweise**, und die fehlende Hälfte ist ein Befund. Dazu ist der offene
> Punkt der Ziehvorschau (Zug auf einen Gruppenkopf) gemessen. Aus 137
> offenen Zeilen sind **132** geworden. Was dabei herauskam, steht unten
> unter «Was die Runde gebracht hat».
>
> **Offen aus diesem Plan:** Runde 4 (H5, K4-Ziehen) und Runde 5 (W2.1) —
> und dort wartet B1 weiter auf einen Entscheid.

---

## Die Trennlinie, und wo sie wirklich verläuft

**Von den 137 offenen Zeilen sind 120 ohne dich nicht zu haben** — und zwar
nicht aus Bequemlichkeit, sondern weil ihnen etwas fehlt, das kein Skript
herstellt:

| | Zeilen | was fehlt |
|---|---|---|
| **P / P+H** | 91 | die Anlage, ein echter Anruf, die drei Headsets — und bei mehreren ein **Ohr** |
| **F / F+P** | 17 | ein frischer Rechner. **Geprüft am 24.09.2026:** Windows Sandbox ist auf dieser Maschine nicht installiert, Hyper-V ebenso wenig, die Konsole läuft **ohne Adminrechte**, und die Maschine ist ARM64 — ein x64-Windows-Gast ist hier ohnehin keiner |
| **W** | 4 | ein Windows-10-Arbeitsplatz |
| **O** | 4 | klassisches Outlook mit COM (ADR-018) |
| **X** | 1 | echte x64-Hardware |
| **H** | 4 | ein Headset am Schreibtisch |

**Übrig bleiben 17 Zeilen, und davon sind neun erreichbar** — sechs sofort,
drei über eine Bauarbeit. Dazu kommt ein Posten ohne Prüfzeile, der grösser
ist als alle neun zusammen: **W2.1**, die Tests für die SDK-Schicht.

**Drei Zeilen bleiben auch hier liegen, mit Grund:**

| | warum nicht |
|---|---|
| **T25** (Autostart nach Neustart) | ein Windows-Neustart nimmt dir alles Offene weg. Das ist ein Eingriff, den ich nicht nebenbei mache |
| **T93** (Klingelton) | «weicher, mit einer Pause» kann kein Skript hören |
| **T128** (Start ohne Netz) | das WLAN zu trennen kappt die Verbindung, über die ich messe |
| **T21/T22** (`tel:`-Links) | T21 braucht Outlook mit COM; T22 endet in einem **echten Wählversuch** und gehört damit zur Anlage |
| **T95** (Logo-Wechsel) | machbar — aber es verändert dein Startmenü, deine Taskleiste und den Symbolcache sichtbar. **Nur auf Zuruf**, der Rückweg ist da (`tools/Reset-IconCache.ps1`) |

---

## Die Reihenfolge, und warum sie so ist

**Dein nipp ist dein Telefon.** Alles, was nipp beendet oder neu startet,
kostet dich die Erreichbarkeit für ein paar Minuten. Deshalb steht **vorne,
was nipp gar nicht anfasst** (Runden 1 und 2), und die Messrunden am
laufenden Programm (Runde 3) kommen gebündelt in **einem** Fenster — nicht
verteilt über den Tag.

**Und repariert wird gesammelt, nicht mitten in einer Messrunde** (CLAUDE.md):
sonst prüft die halbe Runde gegen einen anderen Build als die andere.

```
Runde 1: Rüstzeug bauen          (nipp läuft weiter, du merkst nichts)
Runde 2: der frische Klon        (läuft nebenher, eigene Verzeichnisse)
   │
Runde 3: eine Messrunde am Programm   ← hier ist nipp ein paar Minuten weg
   │
Runde 4: bauen, was Zeilen freischaltet   (H5, K4-Ziehen, Zug auf Gruppenkopf)
   │
Runde 5: W2.1 — und die braucht deinen Entscheid, nicht deine Hand
```

---

## Runde 1 — Rüstzeug, ohne nipp anzufassen

**Ertrag: schaltet T288, T72 und T246 frei.** Alle drei scheiterten an
demselben: es gab keine Quelle, die die nötige Datenlage liefert. Die
Attrappe unter `nipp-testaufbauten\attrappe` liegt fertig da und kann das —
sie muss nur mehr können.

| | Was | Wie |
|---|---|---|
| **R1.1** | Eine **grosse Antwort** (über 8 KB) in der Attrappe | Neuer Pfad `/gross` mit erfundenen Daten — genug Felder, dass die Antwort sicher über 8 KB liegt. Vorlage `quelle-gross.json` daneben, wie die drei bestehenden |
| **R1.2** | **Zwei Personen, eine Zentrale** | Zwei erfundene Kontakte derselben Firma mit **identischer** Geschäftsnummer, dazu je eine eigene Durchwahl — das ist die Datenlage aus T72 |
| **R1.3** | **Ein Name, der doppelt vorkommt** | Ein erfundener Kontakt in der Attrappe, dessen Name **gleich** einer Nebenstelle im Team ist — die Datenlage aus T246 |
| **R1.4** | Ein **Zieh-Helfer** in `tools/Test-Ui.ps1` | `Move-NippMaus` / `Invoke-NippZug`: echtes `SendInput` über zwei UIA-Rechtecke, mit Zwischenschritten (der Zug braucht Bewegung — `VorschauSchwelle`, ADR-066). Das Muster steht in `Beende-Nipp.ps1` schon da |

**R1.4 ist das Stück mit dem längsten Hebel.** Über UI Automation lässt sich
nicht ziehen, und genau daran hängen drei offene Sachen: der **Zug auf einen
Gruppenkopf** (V8), **hell bei 150 %** im selben Zug, und später **T101**.
Gestern hat sich dasselbe schon einmal gezeigt: ein `InvokePattern` bewegt den
Fokus gar nicht, und deshalb wäre die Falle aus ADR-044 über UIA nie gestellt
worden.

**Fertig, wenn:** die Attrappe alle vier Pfade bedient, die drei neuen
Vorlagen importierbar sind, und ein Zug im Team-Bereich über das Werkzeug
sichtbar etwas verschiebt.

---

## Runde 2 — Der frische Klon (T174)

Läuft nebenher und in eigenen Verzeichnissen: klonen nach
`C:\dev_claude\nipp-klon`, SDK nach `docs/sdk-setup.md` holen (aus dem
Abhängigkeits-Repo, ADR-069), Prüfsumme vergleichen, `.\build.ps1 build` und
`test`.

**Das ist die Gegenprobe zum Öffentlichmachen:** was nur auf dieser Maschine
liegt, fällt hier auf — beim letzten Mal war es eine Quelldatei, die eine
`.gitignore`-Regel verschluckt hatte. Kostet gut eine halbe Stunde Bauzeit und
sonst nichts.

**Fertig, wenn:** T174 ein Ergebnis trägt — und falls nicht, steht in der
Zeile, welche Datei fehlte.

---

## Runde 3 — Eine Messrunde am laufenden Programm

**Hier ist nipp ein paar Minuten weg.** Gesammelt, in einem Durchgang, gegen
**einen** Build. Sichern vorher: `settings.json`, `integrations.json` **und die
Geheimnisdatei** — das hat zweimal eine Anmeldung gekostet (Befund A1-9), und
`Sichern-Und-Zuruecksetzen.ps1` im Aufbau macht es inzwischen mit.

| Zeile | Was gemessen wird | Warum sie jetzt geht |
|---|---|---|
| **T288** | Karten-Designer, «Abrufen» gegen die grosse Antwort: Vorschau zeigt Werte, die Zeile sagt «Testabruf», die Statuszeile «übernommen» | R1.1 |
| **T72** | Kontaktsuche nach der Firma: **beide** Personen als eigene Zeile | R1.2 |
| **T246** | Name tippen, der im Team **und** in der Quelle steht: **genau eine** Liste, der Kollege unten mit Herkunftsabzeichen | R1.3 |
| **T311** | Nebenstelle um eine Mobilnummer ergänzen, zurück auf «Kontakte», aufklappen — **ohne Neustart** sichtbar; dasselbe mit Name und SIP-Adresse, und die Gegenprobe | **der Grund des Scheiterns ist weg:** die Zeile scheiterte an der Werkzeugsteuerung (Formular erst nach mehrfachem Scrollen, Klick landete im fremden Fenster). Seit gestern sind beide Listen gedeckelt und der Stift holt das Formular ins Bild und den Fokus hinein (ALLTAG-PLAN-3, Meldung B) |
| **V8-Rest** | Zug auf einen **Gruppenkopf**, und derselbe Zug **hell bei 150 %** | R1.4 |

**Nach der Runde:** Befunde sammeln, dann reparieren — nicht dazwischen.

---

## Runde 4 — Bauen, was Zeilen freischaltet

Drei Posten, alle klein bis mittel, alle ohne Gerät zu bauen und zu testen.
**Abgenommen wird zwei davon erst am Gerät** — das ist kein Einwand, es ist
die Reihenfolge.

| | Was | Umfang | Abnahme |
|---|---|---|---|
| **B4.1** | **H5 — der Notausgang**: eine Einstellung «Signale ans Headset senden», vorbelegt mit ein. Für den Fall, dass ein Gerät sich anders verhält als die beiden, die wir kennen | klein: ein Pfad in `ProvisioningCatalog`, eine Abfrage in `HeadsetSignalGate.Erlaubt`, ein Schalter auf der Seite (kein «Speichern», ADR-045) | Komponententests am Schreibtisch; am Headset erst mit A3. **Braucht eine neue Prüfzeile — T330**, und sie wird im selben Commit in die Matrix eingetragen (CLAUDE.md) |
| **B4.2** | **K4, das Ziehen im Karten-Designer** — ein Feld aus der Palette auf eine Zeile | mittel: anderer Baum, andere Vorlagen als das Team-Ziehen. Das Muster steht seit ADR-065/066 (Druck merken, acht Pixel, Ziel ist die **Liste**, Vorschau ist der Auftrag) | **T101** — die Zeile steht seit jeher da und sagt selbst, dass sie auf K4 wartet. Messbar mit R1.4 |
| **B4.3** | **Der Zug auf einen Gruppenkopf** | offen laut Plan: was passiert, wenn auf einem Gruppenkopf abgelegt wird, ist ungeklärt — erst messen (Runde 3), dann entscheiden, ob es eine Reparatur braucht | V8 |

**Nicht in dieser Runde: A1-19.** Die zehn `async void` mit engem
`when`-Filter sind in ADR-072 **bewusst offen gelassen** — «der nächste Absturz
aus einem `async void` macht die Entscheidung rückgängig». Das ist entschieden
und kein liegengebliebener Posten.

---

## Runde 5 — W2.1, und was daran deine Entscheidung ist

**Der grösste offene Posten ohne Prüfzeile.** Rund 20 000 von 47 800 Zeilen
ohne Test, darunter `SipService` (2 750) und ganz `Nipp.App`. Teil B des
Beweisplans hat ihn in zehn Etappen zerlegt (B0 bis B9), jede mit «zu bauen»,
«Tests» und «fertig, wenn» — **das ist fertig durchdacht und wartet nur darauf,
gebaut zu werden.**

**Und genau hier steht ein Satz im Weg, der von dir stammt und gut ist:**

> «Erst der Gerätetag, dann die Tests. Wer sie vorher umbaut, weiss hinterher
> bei jedem Befund nicht, ob er alt ist oder gerade entstanden.»

Der Gerätetag ist **angefangen, nicht durch** — 91 Zeilen brauchen noch Anlage
oder Headset. Deshalb teile ich die Etappen in zwei Hälften:

| | Etappen | fasst an | mein Vorschlag |
|---|---|---|---|
| **sicher** | **B0** (der ADR zu «testbar»), **B6** (`SettingsApplier`), **B7** (die HID-Schicht), **B8** (die drei Anläufe zur Ansichtsnavigation) | nicht die Anrufkette | **machen** — sie können den Gerätetag nicht verfälschen |
| **wartet** | **B1** (Zustandsmaschine der Anrufe), B2–B5 | `SipService`, `SipEventBridge` — genau das, was am Gerät läuft | **erst nach A2/A3**, oder auf deinen ausdrücklichen Zuruf |

**B1 ist die grösste und die wertvollste Etappe** — sie holt die sechs teuer
bezahlten Regeln aus einem Kommentar in Tests. Sie ist auch die, die einen
Gerätetag wertlos machen kann, wenn sie davor läuft. Das ist deine
Entscheidung, nicht meine.

---

## Was das zusammen bringt

| Runde | Zeilen | Zeit | du |
|---|---|---|---|
| 1 — Rüstzeug | (schaltet 4 frei) | ein paar Stunden | nichts |
| 2 — frischer Klon | **T174** | eine halbe Stunde, nebenher | nichts |
| 3 — Messrunde | **T288, T72, T246, T311** + V8-Rest | eine Stunde | nipp ist ein paar Minuten weg |
| 4 — bauen | **T101** (über K4), T330 neu | ein Tag | nichts |
| 5 — W2.1 sicher | keine Matrixzeile, aber der Beweis | mehrere Tage | **ein Entscheid** zu B1 |

**Gezählt: sechs offene Zeilen bekommen ein Ergebnis** (T174, T288, T72, T246,
T311, T101), dazu der V8-Rest und eine neue Zeile für H5. Aus 137 offenen
werden 131 — **und das ist ehrlich wenig gemessen an der Arbeit**, weil der
Rest an der Anlage, an Headsets, an einem frischen Rechner und an einem
Windows-10-Arbeitsplatz hängt.

**Der eigentliche Ertrag liegt woanders:** in W2.1 und darin, dass die
Werkzeuge danach das Ziehen können. Was am Gerät zu prüfen bleibt, bleibt am
Gerät — daran ändert kein Plan etwas.

---

## Was ich dich fragen müsste, bevor es losgeht

1. **B1 jetzt oder nach dem Gerätetag?** (Runde 5 — die einzige echte
   Entscheidung in diesem Plan.)
2. **T95** — darf ich ein Logo wechseln, den Symbolcache zurücksetzen und
   danach zurückbauen? Es verändert Startmenü und Taskleiste sichtbar.
3. **Die zwei offenen Entscheidungen aus `VERMITTELN-PLAN.md`** — externe
   Ziele und was die Vorschlagsliste anbietet. Ich kann eine Vorlage schreiben,
   entscheiden kannst nur du.


---

# Was die Runde gebracht hat (24.09.2026)

## Die Zeilen

| Zeile | Ergebnis |
|---|---|
| **T174** | **bestanden.** Frischer Klon von GitHub, SDK nach Anleitung, Prüfsumme stimmt, **0 Warnungen, 0 Fehler**, 1311 Tests grün. Die ganze Kette lief in **4 Minuten** — veranschlagt war eine halbe Stunde |
| **T72** | **bestanden.** Zwei erfundene Personen einer Firma mit derselben Zentrale stehen als **zwei** Zeilen da |
| **T246** | **bestanden.** Ein Name, der im Team und in der Quelle steht, ergibt **genau eine** Liste — ein Treffer mit Herkunftsabzeichen «Team» |
| **T288** | **bestanden, an beiden Stellen.** 14 618 Zeichen Antwort gegen eine Vorschaugrenze von 8192: Werte kommen an, «Testabruf» statt «Beispieldaten», «übernommen» statt «geantwortet», keine Rufnummer im Protokoll |
| **T311** | **bestanden** (nach der Reparatur desselben Abends). Nummer, Name und SIP-Adresse erscheinen sofort ohne Neustart, die Lampe jetzt auch |
| **Ziehvorschau V8** | Der **Zug auf einen Gruppenkopf trägt**; die Zeile landet an zweiter Stelle, weil der Zeiger am unteren Rand des Kopfes steht. Offen bleibt «hell bei 150 %», eine Augenprüfung |

## Befund 1 — die Lampe hing einen Takt zu spät an der neuen SIP-Adresse · **repariert und nachgemessen**

**Gemessen**, zweimal hintereinander am laufenden Programm:

| | im Protokoll |
|---|---|
| direkt nach der SIP-Änderung | `Besetztlampenfeld: 11 Nebenstellen abonniert (0 neu)` |
| nach der **nächsten** Team-Änderung | `Besetztlampenfeld: 12 Nebenstellen abonniert (1 neu)` |

**Die Ursache steht im Code und ist nicht geraten.** `BlfService` abonniert im
Konstruktor `SettingsService.Changed` und synchronisiert dort; gelesen wird
dabei der **`ContactStore`**. Den aktualisiert aber erst
`ShellViewModel.OnSettingsChanged` über `ReloadTeam()` — und das ist ein
**zweiter Abonnent desselben Ereignisses**. Wer zuerst läuft, entscheidet die
Reihenfolge der Registrierung, und der Dienst wird vor dem ViewModel erzeugt.
Der `BlfService` zählt also den Stand von vorher.

**Der Kommentar an der Stelle sagt es fast selbst:** «Wer die Kontakte lädt,
synchronisiert danach ohnehin selbst.» Das stimmt für
`ShellViewModel.ReloadContactsAsync` — für `ReloadTeam()` aus
`OnSettingsChanged` stimmt es nicht, und dort fehlt der Aufruf. **Dieselbe
Familie wie A1-13:** eine Zusage, die an einer Stelle gilt und an der anderen
nicht, und beide sehen gleich aus.

**Was der Benutzer merkt:** wer die SIP-Adresse einer Nebenstelle korrigiert,
hat bis zur nächsten Änderung (oder bis zum Neustart) eine Lampe, die nichts
meldet. Der Zustand steht dann ehrlich auf «unbekannt» — es sieht also nicht
kaputt aus, es ist nur still.

**Nicht repariert — und der naheliegende Griff ist der falsche.** Ein
`_blf.SynchronizeAsync()` hinter `ReloadTeam()` in
`ShellViewModel.OnSettingsChanged` wäre zwei Zeilen Arbeit und bräche genau
die Zusage, die im `BlfService` als Begründung steht: dann synchronisieren
**beide** Abonnenten bei jeder Einstellungsänderung, und im Protokoll stehen
zwei gleiche Zeilen für einen Vorgang. **Das ist eine Entscheidung, keine
Fehlersuche**, und sie gehört dir:

| | Weg | Preis |
|---|---|---|
| **a)** | `BlfService.OnSettingsChanged` lädt den Team-Teil des Stores **selbst** nach, bevor er zählt | Der Dienst wird unabhängig von der Reihenfolge — aber `ReloadTeam()` läuft dann auf dem speichernden Thread, und zweimal statt einmal |
| **b)** | `BlfService` hört **nicht mehr** auf die Einstellungen; wer den Store lädt, synchronisiert (so, wie der Kommentar es ohnehin behauptet) | Die klarere Zuständigkeit — aber das Besetztlampenfeld hinge dann an der Oberfläche, und das ist eine Kernfunktion |
| **c)** | Der `ContactStore` meldet nach `ReloadTeam()` eine Änderung, auf die der Dienst hört | Die Reihenfolge wird zur Eigenschaft der Daten statt zur Eigenschaft der Registrierung — dafür wird ein Ereignis lauter, das heute bewusst still ist |
| **d)** | nichts tun | Wer eine SIP-Adresse korrigiert, hat bis zur nächsten Änderung eine stille Lampe. Sie zeigt «unbekannt» und lügt nicht |

**Meine Empfehlung war (c)**, weil sie die Ursache trifft: vorher entschied
die Erzeugungsreihenfolge im Container, was ein Dienst zu sehen bekommt, und
das ist an keiner Stelle aufgeschrieben.

> **Am 24.09.2026 so gebaut.** `ContactStore.TeamReloaded` meldet nach
> `ReloadTeam()`, dass der Team-Block steht; der `BlfService` hängt daran
> statt an `SettingsService.Changed`. Fünf Tests über die ganze Kette, und die
> **Gegenprobe gegen den alten Stand** fällt bei zweien durch — mit genau dem
> gemessenen Symptom (erwartet `sip:301`, tatsächlich `sip:201`). Die
> Gegenprobe aus ADR-060 steht als eigener Test daneben: **ein Speichern
> erzeugt genau einen Synchronisierungslauf.**
>
> **Am laufenden Programm nachgemessen, zweimal:** eine neu angelegte
> Nebenstelle mit SIP-Adresse und eine geänderte Adresse ergeben beide sofort
> «12 Nebenstellen abonniert (1 neu)». Vorher stand dort «11 (0 neu)».

## Befund 2 — drei Gründe, warum ein Mausklick ins Leere geht

Alle drei am Werkzeug, keiner am Programm, und alle drei sehen gleich aus:
nipp reagiert scheinbar nicht. Sie stehen ausführlich in `docs/lehren.md` und
als Kommentar in `tools/Test-Ui.ps1`:

1. **DPI** — ohne `SetProcessDPIAware()` landet ein Klick bei 150 % rund 70
   Pixel daneben.
2. **Vordergrund** — das Fenster der messenden Konsole steht selbst vorn und
   fängt den Klick ab. **Das ist die Erklärung für den Satz, an dem T311 am
   22.09.2026 liegenblieb**: «ein Mausklick auf die errechnete Stelle landete
   in einem fremden Fenster». Es war kein fremdes Programm.
3. **`SetCursorPos` erzeugt keinen Zug** — die acht Pixel aus ADR-065 zählen
   an Zeigerereignissen, die den Eingabestapel durchlaufen. Es braucht
   `SendInput`.

**Und eine vierte Stelle, die keine des Werkzeugs ist:** Team-Zeilen lassen
sich nur im **Umsortier-Modus** ziehen. Ohne ihn bleibt jeder Zug folgenlos —
ohne Fehler, ohne Protokollzeile. Das hat eine halbe Stunde gekostet und sah
aus wie ein kaputtes Ziehen.

## Was dabei entstanden ist

| | |
|---|---|
| `tools/Test-Ui.ps1` | `Set-NippVordergrund`, `Show-NippElement` (scrollt ins Bild), `Get-NippPunkt`, `Move-NippMaus`, `Invoke-NippKlick`, `Invoke-NippRad`, `Invoke-NippZug`; `Get-NippElement` kennt jetzt `-Id` |
| `nipp-testaufbautenttrappe` | vierte Betriebsart `/gross` (über 8 KB), drei neue erfundene Datensätze, `quelle-gross.json`, `team-schnipsel.json` |
| `C:\dev_claude
ipp-klon` | der frische Klon aus T174 — kann stehen bleiben oder weg, er kostet nur Platz |

## Was die Runde den Arbeitsplatz gekostet hat

nipp war **zweimal für rund zwei Minuten** weg (einmal zum Einspielen, einmal
zum Zurückspielen). Einstellungen, Quellen und die Geheimnisdatei sind
gesichert unter `nipp-testaufbautenlleingang-sicherung-20260924-211515` und
**zurückgespielt**; nipp ist danach wieder angemeldet, mit elf abonnierten
Nebenstellen wie zuvor.
