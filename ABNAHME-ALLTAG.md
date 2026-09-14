# Abnahme im Alltag — worauf heute zu achten ist

**Angelegt am 07.09.2026 morgens**, nach dem ersten Abnahmeblock am Gerät.

Diese Liste ist kein Testplan, den man abarbeitet, sondern eine Merkhilfe für
Dinge, die **bei gewöhnlichen Anrufen** ohnehin vorkommen. Was auffällt, gehört
hierher; was funktioniert, wird in `docs/test-matrix.md` eingetragen.

**nipp läuft mit Protokollstufe Debug.** Das ist Absicht — nur so steht der
SIP-Verkehr im Protokoll, und nur damit lässt sich hinterher belegen, was
passiert ist. Die Dateien werden dadurch gross (rund 20'000 Zeilen pro Stunde
Betrieb); nach der Abnahme gehört die Stufe zurück auf `Information`.

---

## Schon bestanden (07.09.2026 morgens)

| # | Was | Ergebnis |
|---|---|---|
| **T66** | Eingehender Anruf bei **verstecktem** Fenster | Toast erschien, Fenster kam aus dem Infobereich nach vorn |
| **T06** | Eingehender Anruf, Annehmen | über den Toast angenommen, Gespräch stand |
| **T23** | Toast bei geschlossenem Fenster, Knöpfe | „Annehmen" auf dem Toast baute das Gespräch auf |
| **T28** | Name statt Nummer | „Dominic Brunner" in Toast und Anrufliste |
| **T11** | Tastentöne | gehen als RFC2833 hinaus (im SDK-Protokoll belegt) |
| Halten | verbunden → gehalten → verbunden | im Protokoll belegt |

**T66 war der Testfall, an dem alles hing.** Bis zum 06.09. war das Fenster nach
dem ersten Verstecken taub, und ein eingehender Anruf holte die
Gesprächsansicht nicht mehr. Das ist jetzt widerlegt.

---

## Woran heute zu denken ist

### Bei jedem eingehenden Anruf — ohne Zusatzaufwand

1. **Steht der richtige Name?** In Toast, Gesprächsansicht **und** Anrufliste
   derselbe. Bis heute morgen stand in der Gesprächsansicht die Nummer, wo
   Toast und Liste einen Namen hatten — behoben, aber es ist der Fall, der es
   am schnellsten wieder zeigt.
2. **Kommt die Anruferkarte?** Bei einer externen Nummer sollten Cockpit und
   Anrufgedächtnis unter den Knöpfen etwas zeigen. Bei einer internen
   Nebenstelle **nicht** — das ist Absicht (§21.4).
3. **Klingelt es hörbar?** Am eingestellten Klingelgerät.

### Wenn ohnehin weiterverbunden wird

4. **Weiterleiten öffnen** — erscheinen die Team-Nebenstellen mit
   Präsenzlampe? Ein Klick übernimmt die Nummer, gewählt wird noch nicht.
5. **„Sofort abgeben"** benutzen: klingelt das Ziel, endet das eigene
   Gespräch? (T07)
6. **„Erst ankündigen"** braucht ein zweites Gespräch. Wenn es sich ergibt: das
   Ziel zuerst anrufen, ankündigen, dann übergeben. (T08)

### Wenn zwei Gespräche zusammenkommen

7. **Makeln** — hörbar und sichtbar umschalten? (T09)
8. **Ein dritter Anruf** wird abgewiesen. Neu seit dem 06.09.: es gibt dafür
   eine Meldung, nicht nur eine Zeile im Protokoll. (T10)

### Nebenbei, wenn es sich anbietet

9. **tel:-Link** aus Outlook oder dem Browser anklicken — wählt nipp direkt,
   ohne Rückfrage? (T21, T22)
10. **Anrufliste**: einen Eintrag anklicken. Erscheint darunter, was Cockpit
    und Anrufgedächtnis wissen? Bei einer internen Nummer steht dort
    „Zu dieser Nummer wird nichts nachgeschlagen." (T-neu, §22.3)
11. **Wahlwiederholung**: der Pfeil rechts im leeren Nummernfeld zeigt die
    fünf zuletzt gewählten. Ein Klick ins Feld allein zeigt **nichts** —
    das war ausdrücklich so gewünscht.
12. **Verpasster Anruf**: erscheint er in der Liste, und trifft der Filter
    „Verpasst"? (T16)

---

## Neu seit dem Vormittag: das Headset (07.09.2026, 10:15)

Zwei Befunde aus dem langen Anruf sind behoben. Die Taste am Headset legt auf,
und das ist am Gerät bestätigt. **Beim Freizeichen war die Abnahme falsch** —
siehe unten. Was von der Liste noch nicht geprüft ist, steht dabei.

13. **Das Freizeichen beim Wählen — die Abnahme war falsch, die Ursache eine
    andere.** Am Vormittag wurde eine echte Ursache behoben (der Tonspieler
    des SDK liest eine veraltete Geräte-API, und die war leer) und T78 als
    bestanden eingetragen. **Das Protokoll deckt das nicht:** die beiden
    Anrufe, die als Beleg dienten, waren intern und gingen
    `Dialing → Connected` — ohne Rufzustand gab es überhaupt keinen Rufton zu
    hören.

    Bei **externen** Anrufen fehlt `startRingbackTone` vollständig. Der Anruf
    geht `OutgoingProgress → OutgoingEarlyMedia`, weil die Anlage ein `183`
    **mit SDP** schickt; das SDK hält Early Media damit für die Audioquelle
    und schweigt selbst — nur sendet die Anlage kein RTP. nipp spielt den
    Rufton jetzt nach 800 ms selbst (ADR-029).

    **Zu prüfen: T78a** (intern — kommt überhaupt ein Rufzustand?),
    **T78b** (extern mit Headset — der eigentliche Fall) und **T78c**
    (Ausgabegerät namentlich gewählt). Danach `tools\Test-Ton.ps1`: es sagt,
    welchen Weg der Ton genommen hat. „Gehört / nicht gehört" ist kein
    Nachweis — genau daran ist diese Abnahme gescheitert.

14. **Die Taste am Headset — Auflegen bestätigt.** Sie tat nichts, weil das SDK seine
    Jabra-Anbindung mit Version 5.5.0 entfernt hat. nipp spricht das Gerät
    jetzt selbst über HID an — angebunden ist das Jabra Link 400 laut
    Protokoll. **Zu prüfen, in dieser Reihenfolge:**
    - ~~Im Gespräch die Taste drücken: legt auf? (T79)~~ **bestätigt**
    - Beim Klingeln die Taste drücken: nimmt an? (T80)
    - Ausgehenden Anruf mit der Taste abbrechen, während es beim Gegenüber
      klingelt? (T81)
    - **Der wichtigste:** ein Gespräch in der Oberfläche beenden, dann beim
      nächsten Anruf die Taste drücken. Nimmt sie an? Wenn sie stattdessen
      auflegt, ist der Gabelzustand nicht nachgezogen — und ab da wäre jeder
      zweite Druck falsch. (T82)
    - Stummtaste im Gespräch (T83), und ob die Lampe am Headset leuchtet,
      solange eines läuft (T84).

Fällt eines davon aus, genügt die Uhrzeit. Die Protokollzeilen heissen
„Auflegen am Headset gedrueckt", „Annehmen am Headset gedrueckt" und
„Stummtaste am Headset gedrueckt" — steht keine davon da, kam der Tastendruck
nicht an; steht sie da und nichts geschah, liegt es am Anruf danach.

---

## Zwei Dinge, die heute **nicht** funktionieren werden

**Das Tastenkürzel.** In den Einstellungen steht `Ctrl+Shift+A`, und das ist
laut Protokoll von einer anderen Anwendung belegt — nipp meldet das korrekt,
kann es aber nicht registrieren. Wer den Hotkey braucht, trägt ein anderes
Kürzel ein und drückt „Übernehmen"; die Meldung darunter sagt sofort, ob es
geklappt hat. (T26)

**TLS.** Das Konto läuft über **UDP**. §9.2 gibt TLS als Standard vor, und die
Zertifikatsprüfung ist eingebaut, aber ungeprüft (T03). Das ist eine
Umstellung in den Einstellungen mit Neustart und gehört nicht zwischen zwei
Kundengespräche.

---

## Neu seit dem 11.09.2026: Handynummer, Kontaktdetails und Gruppen

Drei Wünsche aus dem Alltag, gebaut und **am Gerät nicht abgenommen**
(ADR-041).

**Der Kollege hat jetzt eine Handynummer.** Einzutragen in den Einstellungen
unter „Mobil (optional)" oder über ein Profil (`mobile="…"`). Sie ist wählbar,
steht in der Vorschlagsliste — und **wird bei einem eingehenden Anruf erkannt**.
Das ist der eigentliche Gewinn: auf dem neuen Outlook gibt es kein COM, eine
Handynummer war bisher also nirgends auflösbar. Wichtig für die Abnahme: sie
erzeugt **kein zweites Präsenz-Abo** (**T162**), und ein Klick auf den Kollegen
wählt weiterhin die interne Nummer.

**Ein Klick auf einen Kontakt öffnet einen Bereich darunter.** Darin die
Präsenz mit Farbe und Text und **jede Nummer einzeln wählbar** — auch bei
Outlook-Kontakten, wo die zweite Nummer bisher nur über Doppelklick und Flyout
zu erreichen war. Der Doppelklick wählt weiterhin. Zu prüfen: **T160**.

**Und es gibt eigene Gruppen neben „Team".** Anzulegen in den Einstellungen,
auch leer; die erste Gruppe ist die Standardgruppe und darf umbenannt werden.
Eine Gruppe zu entfernen kostet **keinen** Eintrag — die Einträge wechseln in
die Standardgruppe, und die Rückfrage sagt es.

**Die Zeile, die hier zählt, ist T163:** vier Gruppen und über hundert
Outlook-Kontakte, dann auf den Kontakte-Tab wechseln und scrollen. **Der Fehler
wäre kein Absturz, sondern ein Ruckeln** — die Kontaktliste teilt sich den
Thread mit `Core.Iterate()`. Dazu **T165** (Sortieren mit mehreren Gruppen,
einmal mit einer zugeklappten) und **T168**: eine `settings.json` von vorher
**vor dem Start kopieren**.

---

## Neu seit dem 11.09.2026: farbige Toast-Knöpfe und der Karten-Designer

Zwei kleine Dinge aus dem Alltag, beide gebaut und **am Gerät nicht
abgenommen**.

**Der Toast bekennt Farbe.** „Annehmen" steht grün, „Ablehnen" rot; „Mailbox"
bleibt grau — drei farbige Knöpfe nebeneinander wären keine Aussage mehr. Zu
prüfen (**T154**) beim nächsten eingehenden Anruf, ganz nebenbei; dabei T90 bis
T92 mitfahren lassen. Erscheinen die Farben **nicht**, ist das kein Fehler,
sondern der eingebaute Rückfall: `hint-buttonStyle` ist eine neuere
Toast-Fähigkeit, und nipp fragt vorher nach. **Auf einem Windows-10-Gerät ist
nur wichtig, dass nichts abstürzt** (**T155**).

**Der Karten-Designer stolperte an zwei Stellen, und beide waren Layout.** Ein
Knopf lag über der Palette-Liste, und die vier Bausteinknöpfe standen in einer
Reihe in einer 240 Pixel schmalen Spalte — abgeschnitten wurden ausgerechnet
„Linie" und „Abstand". **Deshalb liessen sie sich scheinbar nicht löschen: der
Knopf dazu war weg, nicht der Befehl.** Löschen geht jetzt auf vier Wegen: das
✕ direkt am Baustein, das Kontextmenü darauf, die Entf-Taste über dem
Aufbau-Bereich und der beschriftete Knopf rechts. Strg+Z und Strg+Y gibt es
jetzt wirklich — sie standen seit K4 in den Kurzhinweisen.

**Und die Vorschau nimmt eine eigene Rufnummer.** Eintragen, „Abrufen" drücken,
und die Karte zeigt, was die eingerichteten Quellen zu **dieser** Nummer sagen
— derselbe Weg wie der Testabruf in den Einstellungen, also mit der Zeile
darunter, die sagt, woher die Werte kommen. Zu prüfen: **T156** (Layout,
schmalstes Fenster), **T157** (die vier Löschwege), **T158** (Abruf, und
**nichts davon im Protokoll**) und **T159** — der Designer offen, während ein
Gespräch läuft. Das ist die Zeile, die zählt: er teilt sich den Thread mit
`Core.Iterate()`.

---

## Neu seit dem 10.09.2026: Teams am selben Headset, und der Rufton

Zwei Meldungen vom 10.09.2026 sind behoben und **am Gerät noch nicht
abgenommen** — beide gehören in die nächste Sitzung mit dem Headset.

**„Ein Anruf auf nipp wirft mich aus dem Teams-Meeting."** Ursache: nipp meldet
dem Headset „es klingelt", das Gerät verhandelt daraufhin seinen Zustand und
meldet den Wechsel zurück — und Teams hängt am selben Interface und liest das
als Tastendruck. nipp meldet dem Gerät jetzt **nur bei eigenem Anlass**, und der
Ring-Bericht schweigt, solange ein anderes Programm das Gerät im Gespräch hält.

Zu prüfen (**T147**, je Gerät und einmal mit Notebook-Audio): Meeting läuft,
anrufen lassen, **nichts drücken** — das Meeting muss stehen bleiben. Danach
**T150** (dasselbe mit einem Teams-1:1-Gespräch) und **T151** (im Meeting
annehmen; dass Teams dann das Gerät verliert, ist in Ordnung). Und die
Gegenprobe **T153** ohne Teams: T80, T80b, T79, T84 müssen unverändert gehen —
**besonders T80b**, das Annehmen durch Herausnehmen aus der Ladeschale, denn
genau daran hängt der Bericht, der jetzt manchmal unterbleibt.

Auswertung: `tools\Test-Headset.ps1` (neuer fünfter Abschnitt). Bleibt der
Rauswurf trotz „Ausgangsbericht unterdrueckt" im Protokoll, liegt es nicht am
Schreiben — dann steht im ADR, was als Nächstes zu prüfen ist.

**„Beim Wählen höre ich nicht den Rufton der Anlage."** nipp entschied nach
800 ms auf einem Messwert, der nur einmal pro Sekunde entsteht, und legte
seinen eigenen Ton **168 Millisekunden** über den Anfang des Anlagentons.
Jetzt entscheidet der Paketzähler, und ein Strom, der gerade anläuft, hat
Vorrang.

Zu prüfen (**T143**): nach aussen wählen und **auf den ersten Moment hören** —
kein Fremdton am Anfang. `tools\Test-Ton.ps1` sagt es auch: es darf keine Zeile
„Eigener Rufton gestartet" geben, wenn die Anlage einen Strom schickt.

**Und eine Frage, die keine Messung ersetzt:** klingt der fremde Ton nur am
**Anfang** anders oder **durchgehend**? Bei „durchgehend" ist es nicht diese
Regel, sondern der Jitter-Puffer, der den Anfang des Anlagentons beschädigt
(**T146**) — dann bitte melden, das ist eine eigene Spur.

---

## Neu seit dem Mittag: fünf Wünsche aus dem Alltag (07.09.2026, 12:45)

Alles gebaut, Build ohne Warnungen, 723 Komponententests und 16
Architekturtests grün. **Am Gerät ist davon nichts abgenommen** — die
Testfälle sind T78a bis T78c und T90 bis T95.

15. **Der Toast zeigt jetzt den Anruferkontext** (ADR-030). Vier Angaben, mehr
    lässt Windows nicht zu — drei Textzeilen plus die kleine Zeile darunter:

        Alexander Ruoss · Ruoss AG (Kunde)
        04.09. · Migration Telefonie · A. Vignola
        Zuletzt: Embro-Werk mit Herr Reibi besprechen, dieser ruft zurück.
        +41 44 395 40 16

    Der Toast **erscheint sofort** und wird ersetzt, sobald eine Quelle
    geantwortet hat. Gewartet wird nie: fällt Cockpit oder das Anrufgedächtnis
    aus, steht dort wortgleich das, was vorher dastand.

    **Zu prüfen: T90** (beide Quellen — erscheint er sofort und blinkt er
    nicht mehrfach?), **T91** (Quellen aus — steht dort noch das Alte?) und
    **T92**: nach dem Anruf das Benachrichtigungscenter öffnen. Dort darf
    **nichts** mehr stehen. Eine Gesprächszusammenfassung, die dort liegen
    bleibt, überlebt jedes Zeitlimit — genau die Grenze aus ADR-027.

    **Eine Lücke ausdrücklich:** „wer von uns intern dran war" kommt aus
    Cockpit, vom Techniker der letzten Zeiterfassung. Das Anrufgedächtnis
    kennt zum letzten *Anruf* nur `callee_name` = „bv2 GmbH" — die Firma,
    nicht die Person. Personenscharf bräuchte das ein neues Feld dort.

16. **Der Klingelton ist neu, und er ist wählbar** (ADR-031). Ein
    aufsteigender Dreiklang mit weichem Einsatz, danach drei Sekunden Stille.
    Die Stille ist der Punkt: das SDK spielt in Schleife, und eine durchgehend
    klingende Datei wird damit zum Dauerton.

    Die sechs sanften Töne des SDK waren keine Option — sie sind `.mkv`, und
    die dafür nötige DLL fehlt im Paket. Deshalb ein eigener, erzeugt von
    `tools\Build-Sounds.py`.

    In den Einstellungen unter Audio: Auswahl (nipp, Telefonglocke, Spielzeug,
    eigene Datei) und **„Probe hören"** auf dem Klingelgerät. **T93.** Wenn
    der neue Klang auch nicht gefällt, sind es jetzt zwei Klicks statt einer
    Codeänderung.

17. **Der Info-Bereich in den Einstellungen** hat Symbol, Version, Copyright,
    `kontakt@bv2.ch` und die Website. **T94.** Die Version stand bis heute
    nirgends zur Laufzeit; wer im Support danach gefragt wurde, konnte sie
    nicht nennen. Mit dabei die **Buildart** („unpackaged") — sie hat zweimal
    Fehlersuche gekostet, weil packaged keine Toasts bekommt.

18. **Das alte Logo beim Anheften.** Der Code war nicht schuld: die gebaute
    EXE trug alle sieben Stufen des neuen Symbols byteidentisch.
    Festgehalten wurde es an drei anderen Stellen, und die stehen jetzt in
    `docs/packaging.md`:

    - der **Symbolcache** des Explorers (der Pin liest das Symbol nicht neu,
      solange der Pfad gleich bleibt),
    - ein **verwaistes registriertes Paket**: `bv2.nipp` zeigt auf ein
      Verzeichnis, in dem längst kein `AppxManifest.xml` mehr liegt, weil der
      letzte Build unpackaged war — Windows behält dann seine gecachten
      Kachelbilder aus der Zeit der Registrierung,
    - **fehlende Zielgrössen**: für eine angepinnte packaged App liest Windows
      `Square44x44Logo.targetsize-<N>_altform-unplated.png`, und davon lag nur
      die **24er** im Paket. Für einen 32-Pixel-Platz wurde sie hochskaliert.

    Die Familie wird jetzt vollständig erzeugt. **T95** ist das Anpinnen nach
    dem Zurücksetzen des Caches — ohne diesen Schritt bleibt jedes
    Symbol-Update unsichtbar, egal was im Code steht.

19. **Und der Rufton beim Wählen** — siehe Punkt 13 oben. Das ist der einzige
    der fünf, bei dem die frühere Abnahme zurückgenommen werden musste.

20. **Nachtrag 07.09.2026, 14:10 — der Weg zurück ins Fenster war zu.**
    Gemeldet: „Ich habe die App geschlossen und kann sie nicht mehr öffnen per
    Doppelklick auf das Tray-Symbol." Zwei Ursachen, beide behoben und geprüft:

    - Der Klick trifft **nicht** als Mausereignis ein. H.NotifyIcon führt das
      Symbol im neuen Nachrichtenmodus, und dort schickt die Shell
      `NIN_SELECT` — gemeldet als `KeyboardEvent.Select` über ein anderes
      Ereignis. Ein Doppelklick existiert dort begrifflich nicht, der Handler
      wartete auf etwas, das nie kommt.
    - Und er kommt auf einem **Fremdthread**. `AppWindow.Show()` warf von dort
      „Unzulässiges Fenster. Es gehört zu einem anderen Thread."

    Geprüft mit der echten Shell-Nachricht gegen nipps Nachrichtenfenster,
    zweimal hintereinander: verstecken, klicken, Fenster ist da — ohne
    Ausnahme im Protokoll (**T96**).

    **Was dabei auffiel und offen ist (T97):** nach einem Explorer-Neustart
    fehlte die Zeile „Symbol im Infobereich angelegt". Ob nipps Symbol von
    selbst zurückkommt, ist ungeprüft — fällt es aus, ist nipp bei
    geschlossenem Fenster nur über den Task-Manager erreichbar.

21. **Zum Logo beim Anpinnen** (Punkt 18): am Gerät nachgemessen. Die EXE
    trägt in **allen sieben Grössen** das neue Symbol, und die Shell-API
    liefert es auch so aus — der Code war nie das Problem. Beseitigt sind
    jetzt die zwei Stellen, die es festhielten: die verwaiste Registrierung
    des Pakets `bv2.nipp` (sie zeigte auf ein Verzeichnis ohne
    `AppxManifest.xml`) und der Symbolcache des Explorers. Dafür gibt es
    `tools\Reset-IconCache.ps1`. **Bleibt zu tun:** den Pin lösen und neu
    anheften, falls er noch alt aussieht (T95).

---

## Neu seit dem Abend: die Anrufliste merkt sich, was gesehen wurde (07.09.2026)

Vier Dinge sind anders, alle in der **Anrufliste** (ADR-035 bis ADR-037,
`docs/plans/ANRUFLISTE-PLAN.md`). Auf der Testmatrix stehen sie als **T111 bis T119**.

1. **Ein verpasster Anruf steht fett, bis er einmal angeklickt wurde.** Danach
   normal, und die Zahl im Abzeichen wird um eins kleiner. Bisher zählte das
   Abzeichen *alle* verpassten Anrufe eines ganzen Jahres und wurde nie kleiner
   — ausser man löschte die Liste.
   **Worauf zu achten ist:** die Zahl muss nach einem **Neustart von nipp**
   stimmen (T112). Das ist der eigentliche Test; alles davor hätte auch
   funktioniert, wenn der Zustand nur im Arbeitsspeicher stünde.
   Im Kontextmenü der Liste steht neu **„Alle als gesehen markieren"**.

2. **Der Bereich unter der Liste klappt jetzt auf** statt in 220 Pixeln zu
   scrollen — bis zu 60 % der Höhe, mit Uhrzeit und Ergebnis in der Kopfzeile
   und einem Kreuz zum Schliessen.
   **Worauf zu achten ist:** nach einem längeren Blick in die Anrufliste einmal
   **auf den Kontakte-Tab wechseln und scrollen** (T113). Ruckelt es dort, hat
   die Kontaktliste ihre Virtualisierung verloren — ein Fehler, der nicht
   abstürzt, sondern nur zäh wird, und zwar auf dem Thread, der das SDK bedient.

3. **Was in diesem Bereich steht, ist jetzt eine Karte** und in den
   Einstellungen einzurichten (Integrationen → Anruferkarte → **Anrufliste**).
   Vorher standen dort maschinell gebaute Beschriftungen wie „Letzte arbeit
   zeile". Neu ansprechbar sind auch Angaben zum Anruf selbst: `call.time`,
   `call.duration`, `call.outcome`.

4. **Im Designer:** ein **Abstand** neben der Linie, ein Haken **„Beschriftung
   zeigen"** (aus heisst „Hans Muster" statt „Name: Hans Muster") und ein Knopf
   **„Von anderer Karte übernehmen …"**.

**Vor dem ersten Lauf `history.db` kopieren.** Die Datei bekommt beim ersten
Start eine neue Spalte — die erste Wanderung ihrer Geschichte —, und die
Anrufliste ist das einzige, was nipp nicht wiederherstellen kann. Sie liegt
unter `%LOCALAPPDATA%\nipp\`.

---

## Neu seit dem Abend: nipp hat einen Installer (07.09.2026)

Ab jetzt wird nipp **installiert** statt aus dem Ausgabeverzeichnis gestartet,
und es holt seine Updates selbst (ADR-038, ADR-039). Das ändert den Alltag an
drei Stellen.

**Das Setup liegt unter `dist\releases\nipp-win-stable-Setup.exe`.** Keine
Adminrechte nötig, keine Voraussetzungen auf dem Rechner — .NET und das Windows
App SDK sind mit im Paket. Installiert wird nach `%LocalAppData%\nipp`.

**Beim ersten Start meldet sich Windows mit „Unbekannter Herausgeber".** Das ist
kein Fehler, sondern der fehlende Zertifikatskauf (AP9.2): „Weitere
Informationen" → „Trotzdem ausführen". **Bitte einmal notieren, wie die Meldung
genau aussieht** — das ist T130, und es ist der Satz, den später ein Kollege am
Telefon zu hören bekommt.

**Updates stehen in den Einstellungen unter „Aktualisierung".** Beim Start sieht
nipp nach, lädt aber **nichts** von selbst. Wenn dort eine neue Fassung steht:
„Laden", dann „Neu starten und aktualisieren". Während eines Gesprächs ist der
Knopf abgeblendet — das ist gewollt und einen Versuch wert (T125).

**Vor dem ersten Update `history.db` kopieren.** Sie liegt unter
`%LOCALAPPDATA%\nipp\` und ist das einzige, was nipp nicht wiederherstellen
kann. Nach dem Update nachsehen, ob Anrufliste, Konto und die eigenen Karten
noch da sind (T124).

**Was heute noch nicht geht:** das Setup ist unsigniert, und der Update-Abruf
braucht ein Token für das private Repo — steht keines im Provisioning, sagt
nipp „nicht möglich" und telefoniert unbeeindruckt weiter (T132). Das ist der
richtige Zustand, kein Fehler.

---

## Wenn etwas auffällt

Die Uhrzeit genügt. Das Protokoll unter
`%LOCALAPPDATA%\nipp\logs\nipp-20260907.log` hält alles fest, und mit einem
Zeitstempel ist die Stelle in Sekunden gefunden. Ein Bildschirmfoto hilft, ist
aber nicht nötig.

**Was nicht ins Protokoll gehört und deshalb dort auch nicht steht:**
vollständige Rufnummern. Sie werden seit dem 06.09. maskiert (`…567`), interne
Nebenstellen bleiben lesbar. Wer eine Datei zur Diagnose weitergibt, muss sie
also nicht mehr vorher durchsehen (ADR-022).
