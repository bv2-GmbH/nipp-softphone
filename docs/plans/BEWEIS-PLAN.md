# Der Beweis — Gerätetag und Tests für die SDK-Schicht

**Angelegt am 13.09.2026.** Auftrag von Dominic: ein Plan für die beiden
Posten, die den Merge nach `main` trennen — **W2.8** (der Tag am Gerät) und
**W2.1** (Tests für die SDK-Schicht).

Bezug: `docs/plans/REVIEW-2026-09-12.md` (Befund 4 und die Massnahmen W2.1 und
W2.8), `docs/test-matrix.md`, `docs/plans/RELEASE-PLAN.md` R9, ADR-001, ADR-053.

**Stand:** **A0 ist umgesetzt** (13.09.2026) — die Matrix trägt die Spalte
`Rüstzeug`, die Zahlen unten sind gezählt statt geschätzt. Alles andere ist offen.

> **Der Satz aus der Standortbestimmung, um den es hier geht:** „Was das
> Projekt vor allem braucht, ist nicht mehr Fähigkeit, sondern Beweis."
> Es liegt kein Code zwischen `review-umsetzung` und `main` — es liegen
> **252 Zeilen der Testmatrix ohne Ergebnis** dazwischen (255 waren es, bis A0
> drei überholte gestrichen hat).

---

## Die Reihenfolge, und warum sie so ist

**Erst der Gerätetag, dann die Tests.** W2.1 fasst 3 007 Zeilen an, die am
Gerät laufen und die dort **noch nie gemessen wurden**. Wer sie vorher umbaut,
weiss hinterher bei jedem Befund nicht, ob er alt ist oder gerade entstanden.
Der Gerätetag ist der Nullpunkt, gegen den Teil B sich prüfen lässt.

**Eine Ausnahme, und sie ist gross:** von den 252 offenen Zeilen brauchen
**139 weder die Anlage noch ein Headset noch einen zweiten Rechner** —
Oberfläche, Einstellungen, Karten-Designer, Layout, Tastatur, Erscheinungsbild
und die Wanderungen der Konfigurationsdateien. Die gehören **vor** den
Gerätetag und **an diese Maschine**: jeder Befund, der erst am Gerät auffällt,
kostet dort eine Stunde statt fünf Minuten.

```
A0 Rüstzeug eintragen → A1 Schreibtisch-Runde → A2 Anlage → A3 Headsets
   (erledigt)             (139 Zeilen)            (72)        (20)
                                                       │
                    A4 frischer Rechner → A5 Windows 10 → A6 x64
                         (16)                (4)             (1)
                                                       │
                                              B0 … B9  (W2.1)
```

---

# Teil A — Der Gerätetag (W2.8, R9)

## A0 — Rüstzeug eintragen · **umgesetzt am 13.09.2026**

**Das Problem des Gerätetags ist nicht die Zahl der Zeilen, sondern das
Umrüsten.** Wer die Matrix von oben nach unten abarbeitet, steckt dreimal
dasselbe Headset um und fährt zweimal den Windows-10-Rechner hoch.

`docs/test-matrix.md` trägt jetzt **eine Spalte** — `Rüstzeug` — mit einem Code
je Zeile. Mehrfachnennung ist erlaubt (`P+H`), die **teuerste** Angabe bestimmt
die Runde. **288 Zeilen gestempelt, keine offen geblieben.**

| Code | Bedeutung | geschätzt | **gezählt (offen)** | Runde |
|---|---|---|---|---|
| **S** | Schreibtisch: diese Maschine, kein Anruf, kein Gerät | ~110 | **139** | A1 |
| **P** | Anlage (Test-Trunk), Anrufe, kein Headset nötig | ~70 | **71** (+1 `S+P`) | A2 |
| **P+H** / **H** | Headset — Engage 75 / Link 400 / PRO 9470 | ~25 | **17 + 3** | A3 |
| **F** | frischer Rechner (Installation, Update, Deinstallation) | ~20 | **14** (+2 `F+P`) | A4 |
| **W** | Windows-10-Arbeitsplatz | ~8 | **4** | A5 |
| **X** | echte x64-Hardware (ADR-001) | 4 | **1** Zeile (T38) | A6 |
| | | | **252 offen von 282** | |

**Die Schätzung lag in der richtigen Richtung, aber zu tief, und zwar zugunsten
der Sache:** **139 statt 110** der offenen Zeilen brauchen weder Anlage noch
Headset noch einen zweiten Rechner. Mehr als die Hälfte des Rückstands ist am
Schreibtisch abzuarbeiten.

**Was das Zählen sonst noch gefunden hat — und das war der eigentliche Ertrag:**

- **Drei Zeilen waren überholt und niemand hatte sie gestrichen:** T17 (Mailbox
  und MWI), T205 (Detailbereich unter der Liste) und T232 (Mailboxnummer
  eintragen). Bei T205 stand die Streichung seit dem 12.09.2026 sogar im
  Fliesstext über der Tabelle („T205 und T206: beide sind überholt") — T206 war
  gestrichen, T205 nicht. Damit sind aus 255 offenen Zeilen **252** geworden,
  ohne dass jemand etwas geprüft hätte.
- **Vier weitere Zeilen verlangten etwas, das ADR-062 entfernt hat** — T154 (ein
  dritter Toast-Knopf «Mailbox»), T193 («1 neue Nachricht»), T210 (`Strg+4`) und
  T213 (der Mailbox-Bereich in der Umschaltleiste). Sie sind korrigiert, nicht
  gestrichen: der Rest ihrer Erwartung gilt weiter. **Wer sie vorher am Gerät
  geprüft hätte, hätte vier Fehlschläge gemeldet, die keine sind.**
- **Zwei Zeilen waren als Tabelle kaputt:** in T197 und in der Testumgebung ganz
  oben war aus einem Pfad mit `\nipp` ein echter Zeilenumbruch geworden — beide
  rendern seit jeher als abgebrochene Zeile. T79 trug **eine Spalte zu viel**.
  Behoben.

**Damit ist beantwortet, was vorher eine Vermutung war:** „ein Arbeitstag am
Gerät" sind in Wahrheit **72 Zeilen an der Anlage, 20 an den Headsets, 16 auf
einem frischen Rechner und 4 auf Windows 10** — und 139, die gar kein Gerät
brauchen.

## A1 — Die Schreibtisch-Runde · ~2 Tage · an dieser Maschine

Alle Zeilen mit **S**, nach Rüstzeug geordnet und nicht nach Nummer:

| Block | Zeilen (Auswahl) | Vorbereitung |
|---|---|---|
| **Wanderungen der Dateien** | T168, T197, T231, T257, T258, T262, T280 | **`settings.json`, `integrations.json` und `history.db` kopieren, bevor irgendetwas davon läuft** |
| **Einstellungen und Provisionierung** | T194–T201, T202, T243, T245, T259, T286 | `nippprov` zur Hand, ein Profil mit gesperrtem und ungesperrtem Pfad |
| **Karten-Designer** | T100–T104, T107, T108, T119, T156–T158, T283, T288 | eine Quelle eingerichtet; für T288 eine Antwort **über 8 KB** |
| **Anrufliste ohne Anlage** | T111–T115 | verpasste Anrufe von Hand in `history.db` schreiben (steht so in der Matrix) |
| **Layout und Breite** | T211–T221, T251–T254, T287, T290, T291 | vierzig Nebenstellen in vier Gruppen, 137 Outlook-Kontakte |
| **Tastatur und Sprachausgabe** | T185–T193, T209, T210, T217, T218, T233–T237, T239 | Sprachausgabe von Windows |
| **Erscheinungsbild und Kontrast** | T74, T75, T94, T269–T271, T282 | hell/dunkel, 100 % und 150 %, Kontrastmodus **im laufenden Betrieb** |
| **Import und Vorlagen** | T169–T173, T201 | eine Vorlage aus dem privaten Vorlagen-Repo, dazu eine **mit Token darin** — sie muss abgelehnt werden |
| **Frischer Klon** | T174 | zweites Verzeichnis, SDK nach `docs/sdk-setup.md` |

**Regel für die Runde:** Ein Fehlschlag wird **eingetragen und liegen
gelassen**, nicht sofort behoben. Wer mitten in der Runde repariert, prüft die
restlichen Zeilen gegen einen anderen Build als die vorherigen. Reparaturen
kommen gesammelt nach A1; danach laufen die betroffenen Zeilen erneut.

**Fertig, wenn:** jede S-Zeile ein Ergebnis, ein Datum und einen Build trägt.

### Der Stand der Runde (17.09.2026)

**Gemessen wird gegen den Debug-Build von `4a7427b`**, der seit dem 16.09.2026
um 23:40 läuft und mit `HEAD` codegleich ist — die drei Commits dazwischen
fassen nur Dokumentation an. Vor dem ersten Schreibzugriff sind `%APPDATA%\nipp`
und die teuren Dateien aus `%LOCALAPPDATA%\nipp` (`history.db`, `secrets.dat`)
gesichert; der Rückweg liegt als `Wiederherstellen.ps1` daneben und ist trocken
geprüft.

**Die Einordnung der 107 offenen S-Zeilen**, nach dem, was sie wirklich
brauchen — nicht nach dem Stempel:

| | Zeilen | Was das heisst |
|---|---|---|
| **maschinell** | 55 | über den UIA-Baum, die Dateien und das Protokoll abnehmbar |
| **gemeinsam** | 35 | ein Teil messbar, ein Teil braucht Augen, Hände oder einen Eingriff ins System |
| **nur am Menschen** | 8 | Farbe, Kontrast, Ruckeln, abgeschnittene Beschriftungen |
| **hier nicht prüfbar** | 9 | das Rüstzeug fehlt trotz Stempel `S` — siehe unten |

**Neun Zeilen tragen `S` und sind es nicht.** Vier hängen an Outlook-Kontakten,
die es auf dieser Maschine nicht gibt (ADR-018): **T42** und **T46** sind damit
gar nicht herstellbar, **T219** und **T220** ebenso. **T101** ist kein Testfall,
solange K4 nicht gebaut ist — die Erwartung sagt es selbst. **T228** verlangt
echtes Wählen, unter anderem der `112`, und die Anlage ist von hier aus
erreichbar; die Zeile gehört an den Test-Trunk. **T132** erreicht den geprüften
Pfad nie, weil `UpdateService.CheckAsync` bei `!IsInstalled` sofort zurückkehrt
— sie gehört zu `F`, und ihre Erwartung («privates Repo») ist seit dem
14.09.2026 ohnehin fraglich. **T169** endet beim Testabruf an einer echten
Anbieter-API. Und **T86** ist in die andere Richtung falsch gestempelt: es
braucht **kein** Headset, sondern keins — und gerade steckt eines.

#### Die Provisionierungsrunde, am Abend des 17.09.2026

**Sieben Zeilen an einem Aufbau: T29, T30, T31, T167, T245, T257 und T258.**
Fünf bestanden, zwei teilweise.

**Der Aufbau hat sich gerechnet.** Er lag seit dem Nachmittag fertig daneben —
Werksdatei, Profilserver auf `127.0.0.1:8099`, fünf Profile, ein Skript zum
Sichern und Zurücksetzen. Die Rüstzeit bis zur ersten Messung betrug damit
wenige Minuten statt einer Stunde, und sieben Zeilen liefen dahinter durch.
**Das ist die Begründung für A0 am konkreten Fall:** nicht die Zahl der Zeilen
ist der Aufwand, sondern das Umrüsten.

**Was dabei sonst noch abgefallen ist:**

- **`nippprov show` gibt es nicht** — T167 nennt den Befehl, das Werkzeug kennt
  `neu`, `pruefen` und `schema`. Die Zeile ist korrigiert, nicht gestrichen:
  gemeint war `pruefen`. Hätte jemand sie am Gerät abgearbeitet, wäre ein
  Fehlschlag gemeldet worden, der keiner ist — derselbe Fall wie die vier
  Mailbox-Zeilen aus A0.
- **ADR-054 trägt.** Beide Richtungen sind jetzt am laufenden Programm belegt:
  der Handwert schlägt das Profil (T257 Stufe 1), die Sperre holt ihn zurück und
  löscht die Markierung (Stufe 2), und eine Konfiguration ohne `UserOverrides`
  lässt das Profil gewinnen (T258). Das war bis heute nur in Komponententests
  bewiesen.
- **Der Rückweg ist trocken **und** nass geprüft.** `-Aktion Wiederherstellen`
  hat den Ausgangszustand vollständig hergestellt: Keep-Alive 30, die
  Provisioning-Adresse der Maschine, alle sechs Einträge in `UserOverrides`,
  zehn Nebenstellen, keine graue Gruppe, keine Leiste. Nachgesehen, nicht
  angenommen.

#### Die Attrappenrunde, unmittelbar danach

**Vier Zeilen an der lokalen REST-Attrappe: T40, T43, T44 und T45.** Drei
bestanden, eine teilweise. Damit stehen **90 S-Zeilen offen**, 211 insgesamt.

**Was dabei gut war — und es ist die Hälfte, die sonst niemand sieht:** die
Entprellung und die Generationen arbeiten genau wie beschrieben. Sechs
Anschläge in einer halben Sekunde ergeben **eine** Anfrage je Quelle mit dem
vollständigen Suchtext, und eine überholte Antwort einer langsamen Quelle
verwirft sich selbst — eigens dafür gemessen, indem mitten in die
Fünf-Sekunden-Antwort hinein umgetippt wurde. Eine langsame Quelle hält die
schnelle nicht auf; die Trefferliste stand nach 839 ms, während darüber noch
«Attrappe langsam wird gefragt …» lief.

**Was nicht gut war, steht als A1-8 unten** und betrifft den Fall, für den die
ganze Vorsicht gebaut wurde: eine Quelle, die annimmt und schweigt.

**Und eine Grenze dieser Maschine:** «lokale Treffer sofort» aus T43 ist hier
kaum prüfbar, weil es ohne Outlook fast keine lokalen Kontakte gibt (ADR-018).
Gemessen ist die Nebenläufigkeit der beiden fremden Quellen, nicht das
Verhältnis lokal gegen fremd.

#### Die Designer-Runde, am selben Abend

**Zehn Zeilen: T100, T103, T104, T106, T107, T119, T156, T157, T158, T283.**
Acht bestanden, zwei mit einer offenen Hälfte. Damit stehen **80 S-Zeilen
offen**, 201 insgesamt.

**Der Karten-Designer trägt.** Das ist die Nachricht, und sie war nicht
selbstverständlich: er ist der grösste Einzelteil, den niemand je systematisch
geprüft hatte. Der Rückgängig-Stapel stimmt auf den Schritt genau — 55
Eingaben, 50 Schritte zurück, fünf Stände aus dem Stapel gefallen, nichts
abgestürzt. Die Mindestbreite greift für alle vier Kartenarten, die Palette
liegt bei 1000 logischen Pixeln um zwölf Pixel über dem ersten Knopf statt
darunter. Alle vier Löschwege entfernen denselben Baustein. Ein abbrechender
Ausdruck sperrt das Speichern und nennt die Stelle; dieselbe Karte von Hand in
die Datei geschrieben, und nipp nimmt die mitgelieferte und schreibt den Grund
ins Protokoll. Und die Vorschau vergisst die echte Antwort beim Neustart, wie
§21.2 es verlangt.

**Was offen bleibt und warum:**

- **T288** (Antwort über 8 KB) braucht eine Quelle, die eine grosse Antwort
  liefert. Die Attrappe hat acht erfundene Kontakte und kommt nicht annähernd
  hin; das ist ein eigener kleiner Aufbau. **Die halbe Erwartung ist trotzdem
  gemessen** — die Statuszeile sagt «übernommen» und nicht «geantwortet»
  (siehe T158).
- **T108** (Quelle entfernen, neu anlegen, Token bleibt) braucht eine Quelle
  **mit** Token. Die Attrappe hat bewusst keins (ADR-040), und die beiden
  echten Quellen sind die des Arbeitsplatzes — daran wird nicht geübt.
- **T102 und T105** sind `P`: sie brauchen einen echten Anruf.
- Die zweite Hälfte von **T106** braucht einen echten Toast.

#### Die Layoutrunde, zum Abschluss des Abends

**Sechs Zeilen: T213, T214, T217, T218, T221 und T254.** Alle sechs bestanden,
zwei mit einer Einschränkung, die an der Sache liegt und nicht am Programm.
Damit stehen **74 S-Zeilen offen**, 195 insgesamt.

**Gemessen mit vierzig erfundenen Nebenstellen in vier Gruppen**, eingespielt
bei beendetem nipp (die Lehre vom Nachmittag) und hinterher zurückgesetzt. Die
SIP-Adressen zeigen auf `pbx.example.ch`, eine Domain, die es nicht gibt —
damit baut nipp keine vierzig Präsenz-Abos gegen die echte Anlage auf, und der
Zustand bleibt «unbekannt», was für T218 ein gültiger Zustand ist.

**Was dabei gut aussah:**

- **Der Gruppenumbruch stimmt auf die Reihe** (T254). Sechs Kacheln, dann vier,
  dann ein neuer Gruppenkopf auf einer neuen Reihe — Köpfe bei Y=97, 543, 988,
  1434, Kachelreihen sauber dazwischen. Keine Gruppe teilt sich eine Reihe mit
  einer anderen.
- **Das Kachelfeld steht beim Bereichswechsel auf den Pixel still** (T213):
  X=761, Y=79, Breite 2087 — vorher wie nachher. Die Umschaltleiste endet bei
  X=496 und liegt damit nur unter der linken Spalte.
- **Zwei Züge hintereinander sitzen und überleben den Neustart** (T214), über
  das Kontextmenü. Das ist die Gegenprobe zu T176, wo der zweite Zug stumm
  verloren ging.
- **`WindowPlacement` speichert `maximized` als Wort** und nicht als Viererpaar
  (T221). Genau daran hing der Fehler, den die Zeile prüft: ein maximiertes
  Fenster kam sonst als «beinahe volles Fenster mit Rand ringsum» zurück.
- **Der UIA-Name einer Kachel nennt die Gruppe mit** (T218): «Alina Ammann,
  Support, 201, unbekannt».

**Zwei Dinge sind bewusst nicht gemessen worden, und beide aus demselben
Grund — sie greifen nach draussen:**

- **«Enter wählt»** aus T217 hätte einen echten Wählversuch über die
  angemeldete Anlage ausgelöst. Die Zeile gehört insoweit an den Test-Trunk.
- **Das Ziehen mit der Maus** aus T214. Der Zug gehört seit ADR-065 uns und
  beginnt nach acht Pixeln Bewegung; mit synthetischen Zeigerereignissen ist
  das nicht verlässlich nachzustellen. Gemessen ist der Weg ohne Maus, der
  dieselbe Funktion (`TeamLayout.Move`) benutzt.

**Und einmal beinahe ein Befund, der keiner war:** zwischen Kachel und
Nummernknopf liegt ein Tab-Stopp ohne Namen. Nachgesehen statt gemeldet — es
ist ein `InputSiteWindowClass`-Pane von WinUI über den ganzen Bildschirm, kein
Bedienelement der App. **Das ist die Gegenprobe, die A1-1 und der namenlose
«Entfernen»-Knopf aus T157 bestanden haben und dieses hier nicht.**

### Befunde aus A1

Eingetragen und liegen gelassen, wie die Regel oben es verlangt.

- **A1-1 — Die beiden Schieberegler in der Audio-Gruppe haben keinen
  vorlesbaren Namen.** `SettingsPage.xaml:346` und `:348`: «Wiedergabelautstärke»
  und «Mikrofonpegel» stehen als eigener `TextBlock` daneben statt als `Header`
  oder `AutomationProperties.Name`. Im UIA-Baum sind es die **einzigen zwei
  bedienbaren Elemente ohne Namen** — geprüft über alle drei Reiter und alle
  sieben Einstellungsgruppen, sonst trägt jedes einen. Eine Sprachausgabe liest
  dort einen Wert ohne die Grösse, zu der er gehört (§8.4). Es sind auch die
  einzigen zwei `Slider` im ganzen Projekt.
- **A1-2 — Das Symbol im Infobereich wird doppelt angesagt.** Der Name lautet
  «nipp nipp — angemeldet»; `TrayIconHost.cs:346` setzt «nipp — angemeldet»,
  und der Anzeigename kommt **vermutlich** von Windows davor — gemessen ist nur
  das Ergebnis, nicht die Ursache.
- **A1-3 — «Präsenz setzen» fehlt im Infobereich-Menü**, und damit ist
  **Befund C8 aus `REVIEW-2026-09-12.md` am laufenden Programm bestätigt.**
  Das Menü trägt: Öffnen · Stumm schalten · Wiedergabe · die Wahl des
  Wiedergabegeräts · «Klingelton stumm für 30/60 Minuten» (ADR-055) · Beenden.
  §10 verlangt die Präsenz; sie ist nicht da. Der Entscheid steht aus — bauen
  oder aus §10 streichen.

- **A1-4 — «Exit() ist zurückgekehrt, ohne den Prozess zu beenden» steht bei
  jedem einzelnen Beenden da, und niemand hat hingesehen.** Der Kommentar in
  `App.xaml.cs:1098` sagt über die Zeile danach: *«Hierher kommt niemand, und
  genau das ist die Aussage.»* `Application.Exit()` verlässt die
  Nachrichtenschleife des Hauptthreads und kehrt nicht zurück, **wenn es
  wirkt**. In `%LOCALAPPDATA%\nipp\logs\beenden.txt` steht die Zeile
  fünfmal — am 14.09. (zweimal), 15.09., 16.09. und 17.09.2026, jedes Mal 42
  bis 77 ms nach «Dienste freigegeben, der Prozess endet jetzt regulär».
  **Der Fehler vom 07. bis 12.09.2026, der als behoben gilt** (`docs/lehren.md`,
  ADR zu `Application.Exit()` auf dem falschen Thread), **erfüllt damit weiter
  seine eigene Anzeigebedingung.**
  Was den Prozess dann beendet, ist **nicht gemessen**: der Drei-Sekunden-
  Wächter kommt als Erklärung in Frage, seine Meldung («Beenden hat drei
  Sekunden nicht genügt») steht aber in derselben Datei **nicht**. Es kann
  ebenso das reguläre Auslaufen des Prozesses sein. Die beiden Messungen von
  heute — **4,1 s** und **1,5 s** bis zum Prozessende — sprechen eher gegen
  den Wächter, denn 1,5 s ist vor seinen drei.
  **Nebenbefund im selben Pfad:** `AppLog.ExitForced` meldet «Beenden hat
  **acht** Sekunden nicht genügt», der Wächter wartet aber **drei**
  (`App.xaml.cs:1007`). Zwei Zahlen für dieselbe Sache, und die falsche steht
  in der Meldung, die der Support zu sehen bekäme.
  **Nachtrag vom Abend des 17.09.2026, aus der Provisionierungsrunde:** die
  Datei reicht weiter zurück als oben steht — die erste Zeile trägt den
  **13.09.2026**, nicht den 14., und bis zum Abend stehen **dreizehn** Paare
  darin. Die Runde hat nipp achtmal über das Infobereich-Menü beendet und dabei
  die Zeit gestoppt: **1,38 bis 1,40 Sekunden** bis zum Verschwinden des
  Prozesses, jedes Mal, und zwischen «Dienste freigegeben» und der Exit-Zeile
  liegen 42 bis 190 ms. **Damit spricht mehr gegen den Drei-Sekunden-Wächter
  als vorher:** acht Messungen unter 1,5 s, keine einzige Meldung des Wächters.
  Was den Prozess beendet, bleibt ungemessen.
- **A1-5 — Gruppen ohne Treffer verschwinden nicht.** T248 verlangt es; mit 40
  Nebenstellen in vier Gruppen und einem Treffer in einer davon stehen die
  drei anderen als «HRN (0)», «Hotline (0)», «Testgruppe (0)» weiter da,
  jeweils mit einem echten Rechteck von 48 Pixeln Höhe. Die Zählzeile
  («1 von 40 · Filter aufheben») und der Verweis selbst arbeiten richtig.
  Nur wenn **gar keine** Nebenstelle passt, verschwinden alle Köpfe — der
  Fall, den T250 abdeckt und der deshalb bestanden aussah.

- **A1-6 — Das Schloss an einem gesperrten Feld erscheint erst beim zweiten
  Aufklappen der Gruppe.** Gemessen an «Serverzertifikat prüfen»
  (`network.verify-certificate`) mit einem Profil, das `network` sperrt: nach
  frischem Start und **einmaligem** Aufklappen von «Netzwerk und
  Verschlüsselung» steht rechts nur der ausgegraute Schalter, kein Schloss.
  Gruppe zu, Gruppe auf — **jetzt** steht es da. Nach einem Neustart
  reproduziert, beide Male mit Bildbeleg.
  **Die Sperre selbst wirkt sofort und vollständig**: alle Bedienelemente der
  Gruppe sind von Anfang an `IsEnabled=false`, es geht allein um die Anzeige.
  **Vermutliche Ursache:** `SettingsPage.ApplyPolicy` findet die Karten über
  den visuellen Baum, und der Inhalt eines nie aufgeklappten `Expander` steht
  noch nicht darin. `OnGroupExpanding` ist genau dafür gebaut und verschiebt
  die Frage mit `DispatcherQueue.TryEnqueue` um einen Durchlauf — **gemessen
  ist, dass das nicht reicht**; warum, ist es nicht. `Expanded` statt
  `Expanding` wäre der naheliegende Versuch.
  **Was daran zählt:** der Kommentar über `FindSettingCards` sagt das Problem
  richtig voraus («der Inhalt … ist unter Umständen noch nicht erzeugt.
  Deshalb wird zusätzlich beim Aufklappen erneut gefragt») — die Abhilfe steht
  da, greift aber nicht, und niemand hat nachgesehen. Das ist dieselbe Sorte
  Satz wie in Befund A8 der Welle 2.7.

- **A1-7 — Ein getippter Zahlenwert wirkt beim Verlassen des Feldes nicht; er
  erreicht die Datei erst beim Beenden.** Dreimal am Keep-Alive-Feld gemessen
  (Werte 20, 25, 45): tippen, Tab auf das nächste Feld — das Feld zeigt den
  neuen Wert, der Fokus ist nachweislich weiter, und `settings.json` trägt
  **weiterhin den alten**. Im Protokoll steht dazu auf Debug «Nichts zu
  speichern — die Einstellungen sind unveraendert», also hat auch das **Modell**
  den Wert nicht. Erst ein späterer Anlass schreibt ihn: einmal das Zu- und
  Aufklappen der Gruppe (dann sofort «Einstellungen gespeichert»), einmal das
  Beenden von nipp — nach dem Neustart stand 45 in der Datei.
  **Der Wert geht also nicht verloren, aber er wirkt nicht, wenn er soll**, und
  das ist genau die Zusage aus ADR-045 («jede Änderung wirkt, sobald sie gemacht
  ist; Zahlenfelder beim Verlassen des Feldes»). Wer nipp danach hart beendet,
  verliert ihn doch.
  **Vermutliche Ursache:** `OnLosingFocus` ruft `ViewModel.ApplyEdits()`, und
  `FocusManager.LosingFocus` läuft **vor** `LostFocus` — eine `NumberBox`
  überträgt ihren getippten Text aber erst mit `LostFocus` in `Value` und damit
  in die Bindung. `ApplyEdits` liest dann noch den alten Stand. Gemessen ist die
  Wirkung und die Code-Stelle, **nicht die Ereignisreihenfolge**.
  **Tragweite: vier Felder**, alle `NumberBox` — «Dauer der Anmeldung
  (Sekunden)», «Anrufliste aufbewahren (Tage)», «SIP-Port» und «Keep-Alive».
  Bei Freitextfeldern stellt sich die Frage nicht, dort steht der Text schon
  während des Tippens in der Bindung.
  **Und eine Warnung für jede weitere Messung am Gerät:** wer einen Wert über
  die Oberfläche setzt und gleich danach die Datei liest, misst den alten Wert
  und hält ihn für einen Fehlschlag. So ist dieser Befund entstanden.

- **A1-8 — Eine Quelle, die annimmt und schweigt, wird «übersprungen» genannt,
  schweigend protokolliert und vom Schutzschalter nicht gezählt.** Gemessen an
  einer lokalen Attrappe, die die Verbindung annimmt und nie antwortet:
  - **Die Oberfläche sagt «Attrappe tot übersprungen»** — Quelle ohne Grund und
    ohne Hinweis, was zu tun ist. Der Zustand `SearchState.Timeout` mit der
    Meldung «antwortet nicht» **ist gebaut** (`ShellPage.xaml.cs:831`) und wird
    hier nicht erreicht. Zum Vergleich: dieselbe Quelle **ganz weg** meldet
    sauber «Attrappe schnell ist nicht erreichbar. Netzwerk und Adresse prüfen.»
  - **Im Protokoll steht nichts.** Die ganze nipp-Sitzung enthält zu diesen
    Anfragen keine einzige Zeile — auch nicht auf Debug. Das ist genau die
    stille Rückgabe, gegen die `QuietFailures` (W1.7) gebaut wurde.
  - **Der Schutzschalter greift nicht.** Sieben Anfragen in rund vierzig
    Sekunden, alle in die Zeitgrenze, und die achte ging genauso hinaus —
    `FailuresBeforeBreak = 5` hätte nach der fünften eine Minute Pause bedeutet.
  **Vermutliche Ursache, an einer Stelle:** `IntegrationHttpClient` hat zwei
  Zweige für `OperationCanceledException` (Zeile 286 und 291). Der erste,
  `when (cancellationToken.IsCancellationRequested)`, liefert `Skipped` **ohne**
  Meldung, **ohne** Protokollzeile und **ohne** `ReportFailure` — gedacht für
  «das Gespräch ist vorbei oder die Suche überholt». Der zweite liefert
  `Timeout` mit allem dreien. Gemessen ist, dass hier der erste greift; **warum
  das äussere Token gesetzt ist, wenn in Wahrheit die eigene Zeitgrenze
  zuschlägt, ist nicht gemessen.**
  **Warum das mehr ist als eine Formulierung:** der Schutzschalter ist genau
  für die tote Quelle gebaut, und sie ist der Fall, in dem er nicht zählt. Und
  wer im Support danach sucht, findet im Protokoll keine Spur.

- **A1-9 — Eine Provisionierungsrunde nimmt das gespeicherte Passwort mit, und
  `settings.json` zurückzuspielen holt es nicht zurück.** Beim ersten Start mit
  einem Profil, das ein `<accounts>` mitbringt, wurde
  die Geheimnisdatei unter `%LOCALAPPDATA%` überschrieben (422 → 374 Bytes, Zeitstempel
  genau der Moment des Profilabrufs). Nach dem Rückweg über
  `-Aktion Wiederherstellen` stand das eigene Konto wieder in der Konfiguration,
  aber nipp meldete «Zugangsdaten abgelehnt» — das Geheimnis fehlte. Behoben,
  indem `secrets.dat` aus der Sicherung von 17:54 zurückgespielt wurde; danach
  meldet sich nipp wieder an.
  **Ob das ein Fehler ist, ist offen:** ein Profil **ersetzt** die Kontenliste,
  und dass die Geheimnisse der ersetzten Konten mitgehen, ist vertretbar.
  **Was nicht vertretbar ist: es steht nirgends.** Weder sagt nipp es, noch
  nennt es die Provisionierungsdokumentation, und der Rückweg des Aufbaus
  sichert die Datei nicht — dass sie hier vorlag, war der A1-Sicherung zu
  verdanken und kein Verdienst des Verfahrens.
  **Für jeden weiteren Gerätetag:** wer ein Profil einspielt, sichert
  `secrets.dat` mit. `Sichern-Und-Zuruecksetzen.ps1` und seine LIESMICH gehören
  entsprechend ergänzt.

- **A1-10 — Strg+Z und Strg+Y im Karten-Designer wirken genau einmal, dann
  hängt der Fokus im Vorschaufeld.** Gemessen: drei Bausteine eingefügt, Fokus
  nachweislich auf einer Zeile im Aufbau, dann dreimal Strg+Z hintereinander —
  **ein** Schritt ging zurück, die anderen zwei taten nichts. Nach jedem
  Tastendruck steht der Fokus auf «Rufnummer für die Vorschau», einem Textfeld,
  und dort greifen die Kurzbefehle nicht mehr. Setzt man den Fokus von Hand
  zurück in den Aufbau, wirkt der nächste Tastendruck wieder — auch das
  gemessen, in beide Richtungen.
  **Die Knöpfe «Rückgängig» und «Wiederholen» sind davon nicht betroffen** und
  arbeiten über fünfzig Schritte tadellos (T103). Es geht allein um die
  Tastatur, und die ist der Weg, den jemand benutzt, der gerade tippt.
  **Vermutliche Ursache:** die beiden `KeyboardAccelerator` hängen am Grid
  (`CardDesignerWindow.xaml`, Zeilen 30–39) und gelten damit nur, solange der
  Fokus in ihrem Bereich liegt. Nach dem Rückgängigmachen wird der Aufbau neu
  erzeugt, das fokussierte Element verschwindet, und WinUI vergibt den Fokus
  weiter. **Gemessen ist der Fokussprung und seine Folge, nicht die Zuständigkeit
  der Accelerators.**
  **Nebenbefund an derselben Stelle:** der Knopf «Entfernen» im
  Eigenschaftenbereich trägt **keinen** UIA-Namen — die Beschriftung liegt als
  eigener Text darin. Dieselbe Sorte wie A1-1.

- **A1-11 — Der Ausdruckstext einer ausgewählten Zeile im Karten-Designer hat
  1,16:1 Kontrast, und zwar in beiden Themen.** Gemessen an den Pixeln des
  laufenden Programms, nach der Formel aus `ThemedBrushTests`:

  | | Beschriftung | Ausdruck darunter |
  |---|---|---|
  | **dunkel** | schwarz auf `#4CC2FF` — **10,47:1** | `#C5C5C5` auf `#4CC2FF` — **1,16:1** |
  | **hell** | weiss auf `#0067C0` — **5,67:1** | `#5D5D5D` auf `#0067C0` — **1,16:1** |

  Die Beschriftung schaltet also korrekt auf die Akzentfläche um, **die zweite
  Zeile nicht**: sie behält ihre Sekundärfarbe aus dem Thema. Verlangt sind
  4,5:1 für Schrift.
  **Das ist ADR-044 wörtlich, an einer neuen Stelle:** «Als Fläche erbt der Text
  seine Farbe vom Thema und stand im Dunkeln fast weiss auf `#FCE100` — rund
  1,4:1.» Dort war es ein Statuston, hier die Auswahlfarbe einer Liste; der
  Mechanismus ist derselbe, und die Zahl ist sogar schlechter.
  **Warum es zählt:** der Ausdruck ist die einzige Stelle, an der dasteht, was
  eine Zeile **tut** — und lesbar ist er genau dann nicht, wenn man sie
  bearbeitet.

**Was die Runde sich selbst beigebracht hat:** **Wer `settings.json` bei
laufendem nipp ändert, verliert die Änderung.** Beim Beenden schreibt nipp
seinen Stand vollständig zurück — die vierzig Nebenstellen waren nach dem
Neustart wieder zehn. Das gilt für jede Zeile, die eine Datei einspielt
(T116, T168, T231, T258, T280): **erst beenden, dann schreiben, dann
starten.** Dass Dominics eigene Konfiguration diesen Fehlgriff unbeschadet
überstanden hat, war Glück und keine Vorsichtsmassnahme.

**Am Werkzeug nachgerüstet**, weil ohne das rund zehn Zeilen nicht messbar
waren: `Test-Ui.ps1` kann jetzt ein **zweites Fenster** ansprechen
(`Get-NippWindow -Titel`, für den Karten-Designer), liefert je Element das
**Rechteck** und ob es überhaupt dargestellt wird, kennt den
**Skalierungsfaktor** (150 % auf dieser Maschine — jede Zahl der Matrix ist
logisch gemeint) und kann das Fenster auf eine **logische Breite** ziehen.
Dabei gemessen: ein Element, das gerade nicht dargestellt wird, liefert kein
Rechteck, sondern `Rect.Empty` mit vier unendlichen Werten — ohne Prüfung
wirft die Umwandlung, und zwar je Element einmal.

## A2 — Der Tag an der Anlage · 1 Tag · Test-Trunk

**Nur der Test-Trunk auf der Test-PBX. Nie ein Kundentenant** (§13). Vorher
sicherstellen, dass dieselben Zugangsdaten **nicht gleichzeitig** auf einem
Tischtelefon registriert sind (§14.11).

Nach Aufbau geordnet, nicht nach Nummer:

1. **Die Grundkette** — T02, T03 (TCP und TLS), dann T04–T09 als Durchlauf.
   **Diese sechs sind der Prüfstein, den Teil B nach jeder Etappe wiederholt** —
   deshalb hier einmal sauber und mit Protokoll auf Debug.
2. **Die Absturzpfade aus Welle 0** — T261 (Halten, Stumm, Auflegen und DTMF im
   Moment des Auflegens, mehrere Anläufe), T264 (Weiterleitung an eine Nummer,
   die es nicht gibt), T68.
3. **Die Maskierung** — T255 und T256 **im selben Durchlauf** wie Punkt 1:
   Debug an, anmelden, anrufen, dann die Datei durchsuchen und die
   Prozessorlast gegenlesen. T57 und T70 fahren mit.
4. **Rufton und Early Media** — T142–T146, mit `tools\Test-Ton.ps1`.
5. **Anruferkontext und Karten** — T48–T57, T90–T92, T181–T184, T244, T246.
6. **Besetztlampenfeld und Präsenz** — T27, T162, mit `tools\Test-Blf.ps1`.
7. **Netz** — T284, T285, T135, T136, T52, T128.
8. **Das Lange zum Schluss** — T34 (acht Stunden, fünfzig Anrufe) läuft
   **nebenher**, ab Punkt 1, und wird am Ende des Tages abgelesen.

**Fertig, wenn:** die P-Zeilen ein Ergebnis haben und T04–T09 als
Referenzdurchlauf im Protokoll stehen, auf den Teil B sich berufen kann.

## A3 — Die Headset-Runde · ½ Tag · Engage 75, Link 400, PRO 9470

**Je Gerät dieselbe Kette**, nicht je Testfall alle drei Geräte — das Umstecken
ist der Aufwand:

T78a, T78c, T81–T86, T15, T14, T207, T265, T276, T152.

Dazu die Teams-Gegenprobe **mit angeschlossenem Headset**: T147–T151, T153.
**T150 ist die wichtigste** — sieht die Erkennung einer Fremdbelegung nichts,
trägt allein die Regel „keine Berichte ohne Anlass" (ADR-028 Nachtrag 4), und
das gehört dann so in die Matrix geschrieben.

**Fertig, wenn:** jede H-Zeile ein Ergebnis **je Gerät** trägt. Ein Gerät, das
nicht zur Verfügung steht, bekommt „nicht geprüft — Gerät fehlt" und **kein
leeres Feld**; ein leeres Feld ist später nicht von „noch nicht dran" zu
unterscheiden.

## A4 — Der frische Rechner · ½ Tag · R9

Windows 11 ohne .NET 8 und ohne Windows App SDK.

T120–T134, T140, T272, T273, T278, T279, T275.

**Drei davon entscheiden über die Auslieferungsform:** T120 (ohne Laufzeit —
die Zusage von self-contained), T122 (`tel:` **nach** einem Update; zeigt der
Registrierungseintrag in den `current`-Ordner, ist ab dem ersten Update
Schluss) und T131 (Toasts — der Grund, aus dem überhaupt unpackaged
ausgeliefert wird).

**T134 ist die Abnahme der Korrektur vom 12.09.2026** („Beenden" beendete
nicht): im **Task-Manager** auf `Nipp.App` prüfen, nicht auf das Symbol im
Infobereich.

**Vor T124 eine Kopie von `history.db` anlegen.**

## A5 — Windows 10 · 2 Stunden

T35, T36, T37, T155, T224. **Vor der Freigabe zwingend** — sie decken das
Restrisiko von ADR-003 ab, und T224 ist der Rückfallpfad ohne Toasts, in dem
der Fokusklau aus W1.1 überhaupt greift.

## A6 — Die x64-Stunde · 1 Nachmittag · ADR-001

**T38** (Jitter und WASAPI-Puffer), **AP7.8** (die vier nichtfunktionalen Ziele
aus §2), **AP9.5** und das **M1-Gate** in seiner Beweiskraft.

Es braucht kein eigenes Gerät: ein Kundenrechner bei einer Installation oder
ein Testrechner genügt. **Wer einmal Zugang zu echter x64-Hardware hat, hängt
A4 und A5 an denselben Termin** — dann ist der frische Rechner gleichzeitig der
x64-Rechner, und drei Runden werden zu einer.

## Wie ein Ergebnis eingetragen wird

Drei Spalten stehen schon da: `Ergebnis`, `Datum`, `Build`. Dazu:

- **„bestanden"** allein genügt nur, wenn die Erwartung genau eingetroffen ist.
  Sonst der **beobachtete** Zustand in einem Satz — so wie T28 und T34 es heute
  vormachen („bestanden für Toast und Anrufliste. **Nicht** in der
  Gesprächsansicht: dort stand 151").
- **Ein Fehlschlag wird zur Zeile in einem Befundabschnitt am Ende der Matrix**,
  mit Datei und Vermutung — **und mit „vermutlich", solange nichts gemessen
  ist** (W2.7, Befund A8).
- **Keine Rufnummer, kein Name** in den Ergebnissen (§21.2).

## Abbruchkriterien

Der Merge nach `main` wartet, wenn eine dieser Zeilen **nicht** besteht:
T120, T122, T131 (Auslieferungsform), T35–T37 (Windows 10), T255 (Maskierung),
T261 (Absturz im Gespräch), T04–T09 (die Grundkette).

Alles andere ist ein Befund und kein Halt.

---

# Teil B — Tests für die SDK-Schicht (W2.1)

## Der Stand, gemessen

| Datei | Zeilen | Tests |
|---|---|---|
| `SipService.cs` | 3 007 | **keine** |
| `SipEventBridge.cs` | 338 | **keine** |
| `SettingsApplier.cs` | 499 | **keine** |
| `Hid/HidTelephonyDevice.cs` | 1 062 | **keine** |
| `HeadsetCallControl.cs` | 684 | **keine** |

Kein Komponententest berührt eine dieser Klassen. Die vier Architekturtests,
die ihre Namen nennen, prüfen Grenzen, nicht Verhalten.

## B0 — Was „testbar" hier heisst · braucht einen ADR (ADR-066)

**Die Massnahme W2.1 schlägt eine `ISdkCore`-Fassade vor. Dieser Plan weicht
davon ab, und das gehört in einen ADR.**

**Warum die Fassade allein nicht trägt:** `SipService` berührt **46
Core-Member**, dazu zehn an `Call` und weitere an `Account`, `Friend`,
`FriendList`, `AuthInfo`, `Player`, `CallParams` und `Address`. Alle diese
Typen sind im C#-Wrapper **gewöhnliche Klassen ohne virtuelle Member und ohne
Schnittstelle** — es gibt nichts zum Überschreiben. Eine Fassade müsste also
**jeden einzelnen** dieser Member nachbauen: rund achtzig Durchreichungen, und
sie wäre selbst ungetestet, weil sie genau die Schicht ist, die niemand fahren
kann. Der Beweis wäre um eine Schicht verschoben, nicht erbracht.

**Was stattdessen gilt — und dieses Haus macht es bereits vor:**
`RingbackWatch`, `TransferOutcomes`, `TransportPorts`, `DoNotDisturb`,
`SipErrorCatalog`, `ToneCardChooser`, `HeadsetPolicy`, `HeadsetSignalGate` und
`HookWatch` sind **herausgetrennte Entscheidungen ohne SDK-Typ**, jede mit
Tests. Nicht eine davon brauchte eine Fassade.

> **Die Regel:** Was das SDK **liest**, wird an **einer** Stelle in eine eigene
> Momentaufnahme übersetzt. Was daraus **folgt**, entscheidet eine reine Klasse
> ohne SDK-Typ. Was das SDK **tut**, bleibt in `SipService` und wird weiterhin
> am Gerät geprüft.

Damit bleibt ein ungetesteter Rest — **die Ausführung** —, und der ist genau
der Teil, für den es Teil A gibt. Der ADR sagt das ausdrücklich, damit niemand
später „SipService ist getestet" liest und mehr hineindeutet, als dasteht.

## B1 — Die Zustandsmaschine der Anrufe · die grösste Etappe

**Heute:** `OnBridgeCallStateChanged` (`SipService.cs:1782`–`1946`) mischt vier
Dinge — SDK lesen, entscheiden, protokollieren, melden. Darin stecken die
Regeln, die dieses Projekt teuer bezahlt hat und die heute nur als Kommentar
dastehen:

- ein neuer Anruf hat **keinen** Vorzustand — sonst ist ein eingehender Anruf
  unsichtbar: der Toast bleibt aus, das Fenster wechselt nicht;
- der dritte Anruf wird **vorgemerkt**, nicht sofort abgelehnt (Reentranz);
- die automatische Annahme wird **vorgemerkt**, nicht im Callback ausgeführt;
- ein Zwischenzustand ohne eigene Aussage lässt den bisherigen stehen;
- der Endgrund wird **beim letzten Ereignis** festgehalten, danach ist der
  Anruf aus der Verwaltung;
- der eigene Rufton endet, sobald der Anruf nicht mehr läutet.

**Zu bauen:**

1. `Model/CallSnapshot.cs` — ein `record`: Nummer, Anzeigename, Zustand,
   Meldung, Codec, Verschlüsselung, Endgrund, `EarlyMedia`, Kennung des Kontos.
   **Kein SDK-Typ.**
2. In `SipEventBridge`: die Momentaufnahme **dort** lesen, wo ohnehin schon
   `using Linphone` steht — die Bridge ist die Stelle, die das SDK übersetzt,
   das ist ihr Zweck. `SdkCallStateEventArgs` trägt künftig die Momentaufnahme.
   **Der `Call` bleibt daneben stehen**, solange `SipService` ihn zum Ausführen
   braucht.
3. `Telephony/CallFlow.cs` — rein, ohne SDK: nimmt die Momentaufnahme und die
   laufenden Anrufe, gibt eine **Entscheidung** zurück (`Ablehnen`, `Anlegen`,
   `Aktualisieren`, `Entfernen`) samt neuem `CallInfo`, Vorzustand und den
   Vormerkungen (automatische Annahme, Rufton stoppen).
4. `OnBridgeCallStateChanged` führt nur noch aus, was `CallFlow` entschieden
   hat.

**Tests (`CallFlowTests`):** die sechs Regeln oben je einzeln; dazu das Rennen
eingehend gegen ausgehend, der dritte Anruf, der Anruf, der endet, während ein
zweiter klingelt, und derselbe Zustand zweimal hintereinander.

**Fertig, wenn:** `OnBridgeCallStateChanged` unter 40 Zeilen liegt und keine
Entscheidung mehr trifft; `CallFlowTests` deckt jede der sechs Regeln.

## B2 — Die Anmeldung

`OnBridgeRegistrationChanged` (`:1603`), `ReadDefaultAccountStatus` (`:1718`),
`NormalizeIdentity` (`:1696`) und die drei Wörterbücher `_accountSettings`,
`_accountStates`, `_accountMessages`.

**Zu bauen:** `AccountRegistry` — hält die Kontozustände und beantwortet
„welcher Zustand gilt für das Standardkonto?" und „was ändert sich, wenn für
dieses Kürzel dieser Zustand kommt?". `NormalizeIdentity` wandert mit.

**Tests:** Kontowechsel im laufenden Gespräch, zehn Konten (`MaxAccountCount`),
ein Zustand für ein Kürzel, das nicht mehr eingetragen ist, und die Frage, wann
`AccountsChanged` überhaupt gemeldet werden muss — **die Gegenprobe aus
ADR-060**: eine Meldung ohne Änderung ist ein Auftrag ohne Anlass.

## B3 — Der Gerätewechsel

`ReactToDeviceChange` (`:2136`) und `RetargetRunningCalls` (`:2211`) — 130
Zeilen Entscheidung über Audiogeräte, ausgelöst aus einem SDK-Callback.

**Zu bauen:** `AudioDeviceChoice` — nimmt die bisherige und die neue
Geräteliste plus die Wahl des Benutzers und gibt zurück, welches Gerät gilt und
ob laufende Gespräche umgehängt werden müssen.

**Tests:** Headset kommt, Headset geht, Headset kommt zurück; das vom Benutzer
namentlich gewählte Gerät verschwindet; zwei Geräte mit demselben Namen.

## B4 — Die Präsenz

`CheckPresenceSubscriptions` (`:1470`) — läuft alle fünf Sekunden aus `Pump()`.

**Zu bauen:** `PresenceWatch` — entscheidet aus Zuständen und Zeit, was zu
erneuern ist. **Ohne Zeitgeber:** die Uhr kommt als Parameter herein, wie bei
`RingbackWatch`.

**Tests:** eine Anmeldung, die nie bestätigt wird; eine, die abläuft; eine
Nebenstelle, die zwischendurch aus der Liste fällt.

## B5 — Adresse, Konto, Aufnahmepfad

`ToDialableAddress` (`:852`), `DomainOf` (`:871`), `IdentityOf` (`:2677`),
`BuildRecordingPath` (`:962`), `MapCallStatus` (`:2598`), `ReadEndReason`
(`:2713`).

Die sechs sind **schon fast rein** — sie brauchen kein neues Gebilde, nur die
Trennung von dem einen SDK-Zugriff, den sie je noch haben, und dann Tests.
**Die billigste Etappe, und sie deckt den Weg, auf dem die Klammer-Null
gefunden wurde.**

**Tests:** eine Nebenstelle ohne Domäne; eine E.164-Nummer; eine Nummer mit
Leerzeichen; ein Aufnahmepfad für eine Nummer mit Zeichen, die Windows im
Dateinamen nicht annimmt.

## B6 — `SettingsApplier`

499 Zeilen, die Einstellungen auf den Core schreiben. **Der Teil, der
entscheidet, _was_ geschrieben wird, gehört heraus** — der Teil, der schreibt,
bleibt.

**Tests:** dass ein unveränderter Satz Einstellungen **nichts** auslöst
(ADR-060); dass der SIP-Port ankommt (ADR-019 Nachtrag — „gebaut, nicht
angeschlossen" zum fünften Mal); dass eine Sperre die Benutzermarkierung löscht
(ADR-054).

## B7 — Die HID-Schicht

Zwei Stellen in `HidTelephonyDevice.cs` sind reine Entscheidung und heute
unerreichbar:

- **`Deute`** (`:858`) — aus einem Report wird ein Tastendruck. `HookWatch` ist
  geprüft, die Deutung darum herum nicht. Ein Report ist ein `byte[]`; ein Test
  braucht **kein Gerät**.
- **`WriteLoop` / `Schreibe`** (`:604`, `:692`) — das Sammelfenster von 60 ms
  und die drei Bits. **Ein Ausgangsreport ohne eigenen Anruf ist ein Eingriff in
  das Gespräch eines anderen Programms** (ADR-028 Nachtrag 4); die Regel steht
  in `HeadsetSignalGate` und ist geprüft, das Zusammenfassen der Zustände
  darunter ist es nicht.

**Zu bauen:** `HidReportDeutung` und `LampenSammler`, beide rein. Die
Geräteöffnung, die Threads und die Ströme bleiben unangetastet.

## B8 — Die drei Anläufe zur Ansichtsnavigation

Aus der Geschichte dieses Projekts: eine Ausnahme aus `ContentFrame.Navigate`,
geworfen im Zustands-Callback. Das ist **kein** Test von `SipService`, sondern
von `CallbackGuard` — und `ExceptionBoundaryTests` erzwingt heute nur, dass der
Wächter **dasteht**, nicht, dass er trägt.

**Zu bauen:** ein Test, der einen Abonnenten anmeldet, der wirft, und prüft: die
übrigen Abonnenten bekommen ihr Ereignis trotzdem, die Ausnahme steht im
Protokoll, und nichts verlässt den Rahmen.

## B9 — Die Gegenprobe

**Nach jeder Etappe B1 bis B7: T04 bis T09 an der Anlage wiederholen** — genau
die sechs, die A2 als Referenzdurchlauf hinterlegt hat. Eine Etappe ohne
Gegenprobe gilt als nicht abgeschlossen.

**Und jede Etappe ist ein eigener Commit.** Sieben kleine Diffs sind zu prüfen,
einer über 3 000 Zeilen ist es nicht.

## Was Teil B ausdrücklich nicht tut

| | Warum |
|---|---|
| **Keine `ISdkCore`-Fassade über achtzig Member** | Sie verschiebt den Beweis, statt ihn zu erbringen — Begründung in B0, festzuhalten als ADR-066 |
| **Kein Testprojekt für `Nipp.App`** | 9 715 Zeilen hinter WinUI; der Zustand, der zählt, liegt seit ADR-032 ff. ohnehin im Kern (`CardDraft`, `CardDesignerViewModel`) |
| **Kein Umbau von `Pump()`** | Die Schleife ist der Taktgeber; was sie ruft, wird testbar, sie selbst bleibt |
| **Keine Änderung an `OutlookContactSource`** | Unangetastet, wie seit der Migration |
| **B9 wird nicht durch Tests ersetzt** | Ein Test beweist die Entscheidung, nicht das Gerät |

## Fertig, wenn

- `SipService.cs` trägt keine Entscheidung mehr, die nicht in einer eigenen,
  geprüften Klasse steht — messbar daran, dass die Datei **unter 2 000 Zeilen**
  liegt und keine der in B1 bis B5 genannten Methoden mehr enthält;
- `CallFlow`, `AccountRegistry`, `AudioDeviceChoice`, `PresenceWatch`,
  `HidReportDeutung` und `LampenSammler` haben Tests;
- die sechs Regeln aus B1 stehen als Test da, nicht als Kommentar;
- T04–T09 nach jeder Etappe wiederholt und eingetragen;
- ADR-066 steht in `docs/decisions.md`.

---

## Aufwand, offen gesagt

| | |
|---|---|
| A0 Rüstzeug | ~~½ Tag~~ **erledigt** |
| A1 Schreibtisch | **2 Tage** |
| A2 Anlage | 1 Tag |
| A3 Headsets | ½ Tag |
| A4 frischer Rechner | ½ Tag |
| A5 Windows 10 | 2 Stunden |
| A6 x64 | 1 Nachmittag, **fremdes Gerät nötig** |
| B0–B9 | **3 bis 4 Tage** |

**Rund neun Arbeitstage**, davon zwei an fremder Hardware. Das ist die ehrliche
Zahl; „ein Tag am Gerät" war sie nie.

**Der kürzeste Weg zum Merge nach `main`,** wenn nicht alles zu haben ist:
A0 → A1 → A2 → die Abbruchkriterien aus A4 und A5. Teil B und A6 wandern dann
hinter den Merge — aber **A6 bleibt vor der ersten Kundenabgabe fällig**, und
das steht seit ADR-001 so da.
