# Stand und Vorgeschichte

Was zuletzt gebaut wurde, was am Gerät noch aussteht, und die Chronologie
dahinter. **Von unten nach oben lesen, wenn man den Faden sucht.**

Dieser Abschnitt stand bis zum 13.09.2026 in `CLAUDE.md` und ist von dort
herausgelöst worden (W2.7): eine Datei mit den Regeln wächst nicht mit
jedem Arbeitstag, eine mit dem Stand schon.

Was am Gerät zu prüfen ist, steht vollständig in `docs/test-matrix.md`;
die Tabelle hier fasst nur die Gruppen zusammen.

---


**Stand 17.09.2026, abends.** 1252 Komponententests und 34 Architekturtests
grün — **lokal.** Die CI ist es nicht, und das ist der erste Punkt.

## Zwei Aufbauten abgearbeitet — elf Zeilen, vier neue Befunde

Am Abend des 17.09.2026 sind die beiden Aufbauten, die seit dem Nachmittag
fertig danebenlagen, benutzt worden — der Provisionierungsserver und die lokale
REST-Attrappe —, und daran anschliessend der Karten-Designer.
**Zweiunddreissig Zeilen haben ein Ergebnis.** Offen sind damit **190 von
303**, davon 69 am Schreibtisch.

## Die Tastaturrunde findet die Wurzel von A1-7

**T190, T191, T233, T237, T239.** Der Fokus bleibt beim Tippen auf der
Wähltastatur, Escape und Alt+Links navigieren zurück, «Abbrechen» trägt in den
destruktiven Dialogen den Fokus und Enter wählt ihn, und die Nummernknöpfe
sagen die Art der Nummer statt nur die Ziffern.

**Zwei Dinge halten nicht, und sie hängen zusammen:**

- **A1-12 (neu):** nach einem **Mausklick** auf die Wähltastatur bleibt der
  Fokus auf dem Knopf, und die nächste getippte Ziffer geht verloren. Die
  Tastaturseite derselben Regel ist in Ordnung — die Korrektur vom 12.09.2026
  hält. Der Kommentar über der Stelle behauptet allerdings, mit der Maus sei
  der Rücksprung «richtig»; das ist die Absicht, nicht das Verhalten.
- **A1-7 ist grösser als gedacht.** Der STUN-Server kommt beim Verlassen des
  Feldes nicht an — **und auch beim Beenden nicht**. Damit ist die Vermutung
  widerlegt, es gehe nur um `NumberBox`-Felder: betroffen sind **alle neun**
  aus `ErstBeimVerlassen` (SIP-Port, Keep-Alive, STUN, Landesvorwahl,
  Aufnahmeordner, Aufbewahrungsdauer, beide Tastenkürzel, Provisioning-Adresse),
  und genau diese Felder sind vom automatischen Speichern ausgenommen. Läuft
  `ApplyEdits` zu früh, gibt es keinen zweiten Weg auf die Platte.

## Und das Layout im breiten Fenster hält ebenfalls

**Sechs Zeilen: T213, T214, T217, T218, T221, T254**, gemessen mit vierzig
erfundenen Nebenstellen in vier Gruppen. Der Gruppenumbruch stimmt auf die
Reihe, das Kachelfeld steht beim Bereichswechsel auf den Pixel still, zwei
Verschiebungen hintereinander sitzen und überleben den Neustart (die
Gegenprobe zu T176), und ein maximiertes Fenster kommt maximiert zurück —
`WindowPlacement` speichert dafür das Wort `maximized` statt vier Zahlen.

**Zwei Dinge sind bewusst nicht gemessen worden:** «Enter wählt» hätte einen
echten Wählversuch über die Anlage ausgelöst, und das Ziehen mit der Maus ist
seit ADR-065 ein eigener Vorgang, der sich mit synthetischen Zeigerereignissen
nicht verlässlich nachstellen lässt. Beides steht mit Grund in der Matrix.

## Der Karten-Designer trägt

**Zehn Zeilen: T100, T103, T104, T106, T107, T119, T156, T157, T158, T283.**
Das war nicht selbstverständlich — er ist der grösste Einzelteil, den niemand
je systematisch geprüft hatte, und sein Zustand liegt seit ADR-046 im Kern,
weil `Nipp.App` kein Testprojekt hat.

Der Rückgängig-Stapel stimmt auf den Schritt genau: 55 Eingaben, 50 Schritte
zurück, die fünf ältesten Stände aus dem Stapel gefallen, nichts abgestürzt.
Die Mindestbreite von 1000 logischen Pixeln greift für alle vier Kartenarten,
und die Palette liegt dort **über** dem ersten Knopf statt darunter. Alle vier
Löschwege entfernen denselben Baustein. Ein abbrechender Ausdruck sperrt das
Speichern und nennt die Stelle; dieselbe Karte von Hand in die Datei
geschrieben, und nipp nimmt die mitgelieferte und schreibt den Grund ins
Protokoll. Die Vorschau vergisst die echte Antwort beim Neustart (§21.2). Und
mit offenem Designer folgt das **Hauptfenster** einem Themenwechsel — die
Stelle, an der `ThemeService` sein Wurzelelement hätte verlieren können.

**Zwei Befunde daraus:**

- **A1-10 — Strg+Z und Strg+Y wirken genau einmal.** Jeder Tastendruck wirft
  den Fokus in das Vorschau-Textfeld, und dort greifen die Kurzbefehle nicht
  mehr: dreimal Strg+Z hintereinander nahm **einen** Schritt zurück. Die
  Knöpfe sind davon nicht betroffen.
- **A1-11 — 1,16:1 Kontrast, in beiden Themen.** Der Ausdruckstext einer
  ausgewählten Zeile behält seine Sekundärfarbe, während die Beschriftung
  daneben korrekt auf die Akzentfläche umschaltet (10,47:1 dunkel, 5,67:1
  hell). Das ist ADR-044 wörtlich an einer neuen Stelle — und die Zahl ist
  schlechter als die 1,4:1, die damals den Anlass gaben.

## Die Attrappenrunde — was trägt und was nicht

**T40, T43, T45 bestanden, T44 teilweise.** Die Entprellung und die Generationen
arbeiten genau wie beschrieben: sechs Anschläge in einer halben Sekunde ergeben
**eine** Anfrage je Quelle mit dem vollständigen Suchtext, und eine überholte
Antwort verwirft sich selbst — eigens gemessen, indem mitten in eine
Fünf-Sekunden-Antwort hinein umgetippt wurde. Eine langsame Quelle hält die
schnelle nicht auf.

**Aber der Fall, für den die ganze Vorsicht gebaut ist, hat einen Befund
(A1-8):** eine Quelle, die die Verbindung annimmt und dann schweigt, heisst in
der Oberfläche «übersprungen» — **ohne Grund und ohne Hinweis, was zu tun
ist** —, hinterlässt im Protokoll **keine einzige Zeile**, und wird vom
Schutzschalter **nicht gezählt**: sieben Anfragen in vierzig Sekunden, wo nach
fünf eine Minute Pause gelten sollte. Der Zustand «antwortet nicht» ist
gebaut und wird nicht erreicht. Zum Vergleich: dieselbe Quelle **ganz weg**
meldet sauber «ist nicht erreichbar. Netzwerk und Adresse prüfen.»

**Und ein Befund, der dem Verfahren gilt (A1-9):** ein Profil, das ein Konto
mitbringt, überschreibt die Geheimnisdatei — nach dem Rückweg stand das eigene
Konto wieder da, aber nipp meldete «Zugangsdaten abgelehnt». Repariert aus der
Sicherung von 17:54. **`settings.json` zurückzuspielen genügt nicht**, und das
steht nirgends: weder sagt nipp es, noch nennt es die Dokumentation, noch
sichert der Aufbau die Datei. Wer einen Gerätetag mit Profilen fährt, muss das
wissen.

## Die Provisionierungsrunde — sieben Zeilen, zwei neue Befunde

**T29, T30, T31, T167, T245, T257 und T258.** Fünf bestanden, zwei teilweise.

**Was jetzt am laufenden Programm bewiesen ist:** ADR-054 in beide Richtungen.
Ein von Hand eingestellter Keep-Alive-Wert überlebt ein Profil, das etwas
anderes will, und das Protokoll sagt auf Debug, dass es ihn übersprungen hat;
eine **Sperre** holt den Profilwert zurück und löscht die Markierung, und beim
nächsten Start bleibt es dabei; eine Konfiguration von vor dem 13.09.2026 — ohne
das Feld `UserOverrides` — lässt das Profil gewinnen. Das stand bisher nur in
Komponententests. Dazu: ein Profil richtet ein Konto vollständig ein (T29),
Gruppen entstehen in der Reihenfolge des Profils (T167), ein nicht erreichbarer
Server hält nipp nicht vom Start ab und sagt es in der Oberfläche (T30), und
`integrations` grautet beide Gruppen aus, die dazugehören (T245).

**Zwei neue Befunde, beide liegen gelassen** (Regel der Runde), vollständig in
`docs/plans/BEWEIS-PLAN.md`:

- **A1-6 — das Schloss an einem gesperrten Feld kommt zu spät.** Nach frischem
  Start und einmaligem Aufklappen der Gruppe fehlt es; zu- und wieder
  aufklappen, dann steht es da. Reproduziert, mit Bildbeleg. Die **Sperre**
  wirkt dabei von Anfang an — es geht nur um die Anzeige. Der Kommentar über
  `FindSettingCards` sagt das Problem richtig voraus und beschreibt eine
  Abhilfe, die nicht greift.
- **A1-7 — ein getippter Zahlenwert wirkt nicht beim Verlassen des Feldes.**
  Dreimal gemessen: das Feld zeigt den neuen Wert, der Fokus ist weiter, und
  Datei **und Modell** tragen den alten («Nichts zu speichern» im Protokoll).
  Geschrieben wird er erst bei einem späteren Anlass — beim Zuklappen der
  Gruppe oder beim Beenden. Betroffen sind die **vier** `NumberBox`-Felder,
  darunter SIP-Port und die Dauer der Anmeldung. ADR-045 verspricht das
  Gegenteil.
  **Und es ist zugleich eine Warnung für jede weitere Messung:** wer einen Wert
  über die Oberfläche setzt und gleich danach die Datei liest, misst den alten
  Stand.

**Ausserdem korrigiert:** T167 nannte `nippprov show`. Den Befehl gibt es
nicht — gemeint ist `pruefen`. Eine Zeile, die so stehen bleibt, produziert
einen Fehlschlag, der keiner ist.

**Der Rückweg wurde ganz gegangen:** `-Aktion Wiederherstellen` hat den
Ausgangszustand vollständig hergestellt, nachgesehen und nicht angenommen —
Keep-Alive 30, alle sechs Einträge in `UserOverrides`, zehn Nebenstellen, keine
graue Gruppe, keine Leiste, Werksdatei gelöscht.

## Neun Befunde behoben (21.09.2026) — zuletzt A1-5

**Zwei bleiben offen: A1-2 und A1-4.**

**A1-5:** `RebuildTeamGroups` baute für jeden Gruppennamen eine Zeile, auch
ohne Treffer. Der Kommentar daneben begründete das richtig — eine Gruppe, die
nur existiert, solange jemand darin steht, liesse sich nicht befüllen —, **nur
gilt das beim Suchen nicht: wer sucht, will finden, nicht zuordnen.** Die
Namensliste wird jetzt gefiltert, wenn eine Suche läuft.

Der subtile Teil war die Liste daneben: `TeamGroupNames` speist das
Kontextmenü «In Gruppe verschieben» und bleibt vollständig, sonst könnte man
beim Suchen nicht mehr in eine unsichtbare Gruppe verschieben. Beides
gemessen. **T248 ist damit ganz bestanden.**

## Acht Befunde behoben (21.09.2026) — A1-9 anders als geplant

**Drei bleiben offen: A1-2, A1-4, A1-5.**

**Bei A1-9 war ein Teil des Befundes falsch, und das ist die eigentliche
Geschichte.** «Es steht nirgends» stimmte nicht: nipp schreibt beim Entfernen
eines Geheimnisses eine Zeile auf **Information**, mit Grund und Kontokennung,
und sie stand bei beiden Vorfällen im Protokoll. Gefehlt hat nicht die
Meldung, sondern dass jemand sie liest — niemand liest das Protokoll in dem
Moment, in dem er ein Profil einspielt.

**Deshalb ist die Massnahme eine andere geworden** als die, über die
entschieden wurde: eine zweite Protokollzeile hätte dasselbe Schicksal gehabt.
Sichtbar wird der Fall dort, wo der Schaden auftritt — in der Anmeldemeldung.
Sie schickte auf die Suche nach einem Tippfehler in drei Angaben, die alle
stimmten, während die vierte gar nicht da war. Jetzt bekommt
`SipErrorCatalog.DescribeRegistrationFailure` mit, ob überhaupt ein Passwort
hinterlegt ist, und sagt es. **Das trifft auch den Fall ohne Profil.**

**Und ein Vorfall gehört dazu:** beim Nachmessen von A1-6 ist derselbe Fehler
noch einmal passiert — Profil eingespielt, `settings.json` zurückgespielt, das
Telefon zwanzig Minuten nicht angemeldet. Die Warnung stand seit vier Tagen in
`CLAUDE.md`, geschrieben von dem, der sie dann übersah. Seither sichert
`Sichern-Und-Zuruecksetzen.ps1` die Geheimnisdatei mit.

## Sieben Befunde behoben (21.09.2026) — zuletzt A1-8

**Vier bleiben offen: A1-2, A1-4, A1-5, A1-9.**

**A1-8 ist der inhaltlich schwerste gewesen**, und die verworfene Hälfte der
Reparatur ist die interessantere. Geplant waren zwei Handgriffe:

Der **erste blieb** — `IntegrationHttpClient` prüft beim Abbruch jetzt, ob
**seine eigene** Zeitgrenze ausgelöst hat, und nicht nur, ob von aussen
abgebrochen wurde. Sind beide gesetzt, ist es ein Timeout: die genauere
Aussage gewinnt.

Der **zweite wurde verworfen** — der äusseren Zeitgrenze eine Reserve
aufzuschlagen, damit die innere zuerst zieht. Naheliegend, und falsch: für
einen Provider, der seine Zeitgrenze nicht selbst durchsetzt, ist die äussere
die einzige, und ein Aufschlag verlängert nur die Wartezeit. **Ein bestehender
Test hat es sofort gefangen.** Die Begründung steht jetzt als Kommentar dort.

**Gemessen mit der Attrappe im Modus `tot`:** die Meldung heisst jetzt
«antwortet nicht» statt «übersprungen», im Protokoll steht «Quelle
attrappe-tot antwortet nicht innerhalb von 3000 ms» — vorher stand dort
**nichts** —, und der Schutzschalter greift nach **fünf** Anfragen statt gar
nicht. Ab der sechsten Suche heisst es wieder «übersprungen», und das ist dort
richtig: die Quelle wird wirklich übersprungen.

**Neu abgesichert:** `Sind_beide_Zeitgrenzen_abgelaufen_gewinnt_die_eigene`.
Die beiden Randfälle daneben waren längst getestet; genau der Fall dazwischen
fehlte.

## Sechs Befunde behoben (21.09.2026)

**A1-1, A1-6, A1-7, A1-10, A1-11 und A1-12**, dazu der Nebenbefund aus A1-4.
**Fünf bleiben offen: A1-2, A1-4, A1-5, A1-8, A1-9.**

**Die letzten beiden waren zweimal derselbe Denkfehler — ein falsch gewählter
Zeitpunkt.**

**A1-6:** Das Schloss an einem gesperrten Feld fehlte beim ersten Aufklappen,
weil der Code die Frage mit `DispatcherQueue.TryEnqueue` um einen Durchlauf
schob und das nicht reichte. Jetzt hängt er sich an das `Loaded` des
Expander-Inhalts. Gemessen: frischer Start, einmal aufgeklappt, Schloss da.

**A1-10:** Nach Strg+Z baut `Refresh()` den Aufbau neu, das fokussierte
Element verschwindet, und WinUI vergab den Fokus ins Vorschau-Textfeld — wo
die `TextBox` ihr eigenes Strg+Z hat. Jetzt holt `FokusInDenAufbau()` ihn
zurück. Gemessen: dreimal Strg+Z nimmt drei Schritte zurück, dreimal Strg+Y
bringt sie wieder.

**Daraus die Regel in `CLAUDE.md`:** wer in WinUI auf einen Zustand wartet,
hängt sich an das Ereignis, das ihn meldet — nicht an den nächsten Tick. Und
wer eine Ansicht neu baut, unter der der Fokus lag, setzt ihn danach selbst.

## Vier Befunde behoben (21.09.2026)

**A1-1, A1-7, A1-11 und A1-12**, dazu der Nebenbefund aus A1-4. **Sechs
bleiben offen.**

**A1-1** — die beiden Schieberegler der Audio-Gruppe sind über
`AutomationProperties.LabeledBy` mit ihrer Beschriftung verbunden; auf der
Seite steht kein bedienbares Element mehr ohne Namen.

**A1-11 brauchte drei Anläufe, und der Umweg ist die Lehre.** Erst
`TextOnAccentFillColorSecondaryBrush` (3,38:1, zu wenig), dann Primary
(10,47:1 dunkel — aber im Hellen **3,70:1 mit schwarzer Schrift auf
Dunkelblau**). Der Grund: `Resource(...)` löst **einmal** auf und liefert einen
festen Pinsel, der dem Themenwechsel nicht folgt. Die Lösung ist, gar keinen zu
setzen: dann erbt der Text vom Knopf, und der Stil führt die Farbe nach.
**Gemessen: 10,47:1 dunkel, 5,67:1 hell**, vorher beide 1,16:1.

**Der Nebenbefund aus A1-4** ist erledigt: `AppLog.ExitForced` meldete «acht
Sekunden», der Wächter wartet drei. Der Hauptbefund bleibt offen.

## Die ersten zwei Befunde sind behoben (21.09.2026)

**A1-7 und A1-12**, beide am laufenden Programm nachgemessen mit derselben
Messung, die sie gefunden hat. **Neun Befunde bleiben offen.**

**A1-7 war eine Zeile, und die Ursachenkette ist die eigentliche Ausbeute.**
`OnPropertyChanged` nimmt die neun Felder aus `ErstBeimVerlassen` vom
automatischen Speichern aus — für sie ist `ApplyEdits()` aus der Oberfläche der
**einzige** Weg auf die Platte. Und der hing an `FocusManager.LosingFocus`,
also an dem Moment, in dem der Fokus noch wechselt: `TextBox` und `NumberBox`
übertragen ihren Inhalt erst mit `LostFocus`. `ApplyEdits` las den alten Stand
und schrieb ihn zurück; feuerte die Bindung danach, sprang `OnPropertyChanged`
wegen `ErstBeimVerlassen` sofort wieder heraus. **Kein zweiter Weg, kein
Hinweis.** Jetzt hängt es an `FocusManager.LostFocus` — das Ereignis gibt es,
der Compiler hat es bestätigt —, und der Wert steht nach einer Sekunde in der
Datei, samt Benutzermarkierung.

**A1-12 dreht die Frage um.** Statt «wo liegt der Fokus» fragt die Wähltastatur
jetzt «womit wurde gedrückt»: das Keypad meldet einen `KeypadPress` mit `Key`
und `VonTastatur`, und die Herkunft kommt aus `Button.FocusState`. Die alte
Fokusabfrage konnte den Mausklick nicht erkennen, weil Windows den Fokus auf
den Knopf setzt, bevor `Click` feuert. Gemessen nach der Reparatur: Klick auf
die «5», dann «7» getippt — im Feld steht «57». Die Tastaturseite ist
unverändert.

**T190 und T233 sind neu gemessen und jetzt beide bestanden** (vorher
«teilweise»). Beide ADRs haben einen Nachtrag bekommen: ADR-045 sagt jetzt,
dass «wirkt beim Verlassen des Feldes» vier Tage lang nicht zutraf, ADR-044,
dass die Fokusbedingung die falsche Frage stellte.

**Und die CI ist auch mit Code grün** — `eca6987` war der erste Commit mit
Quelltextänderungen seit der SDK-Umstellung, und er ist durchgelaufen. Damit
ist belegt, dass der neue Bezugsweg nicht nur für Dokumentation trägt.

## Zwei Entscheidungen vom 17.09.2026, abends

**Dominic hat beide offenen Fragen beantwortet.**

**Das geprüfte SDK-ZIP liegt jetzt unter eigener Kontrolle** (ADR-069). Es
liegt unverändert als Release-Asset in `bv2-GmbH/nipp-build-deps` — einem
öffentlichen Repo ohne Code —, und `ci.yml`, `release.yml` und
`docs/sdk-setup.md` zeigen dorthin. Die Prüfsumme bleibt dieselbe, es ist
dieselbe Datei. **Ein eigenes Repo und nicht ein Release im Hauptrepo**, weil
Velopack dort nach dem neuesten Release sucht und ein SDK-Release den
Auslieferungspfad der installierten Arbeitsplätze hätte treffen können.
**Und die CI ist grün** — Lauf `35265182712`, am 17.09.2026 um 19:32, der
**erste grüne Lauf des öffentlichen Repos überhaupt**. `PublicRepositoryTests`
ist dort zum ersten Mal gelaufen und hat bestanden: kein Systemname, kein
interner Hostname, keine Antwort, die sich als echt mitgeschnitten ausgibt.
Die Kontrolle vor der Öffentlichkeit greift damit nicht mehr nur lokal.

**«Präsenz setzen» ist aus §10 gestrichen.** ADR-055 hatte die Sache am
13.09.2026 entschieden — «Nicht stören» ist ein stummer Klingelton auf Zeit,
kein Präsenzzustand —, liess aber §10 im alten Wortlaut stehen. Genau deshalb
hat die Runde A1 vier Tage später das Menü durchgegangen, die Präsenz nicht
gefunden und sie als Befund A1-3 eingetragen. Jetzt steht in §10, was das Menü
wirklich trägt; **C8 und A1-3 sind geschlossen**, ohne eine Zeile Code. Die
Lehre steht im Nachtrag zu ADR-055: wer eine Fähigkeit streicht, streicht sie
in der Spezifikation, nicht nur im ADR.

## Die CI im öffentlichen Repo hatte nie grün gebaut — bis zum 17.09.2026 abends

**Alle 17 Läufe seit dem ersten Commit am 14.09.2026 sind rot**, und jeder
scheitert an derselben Stelle: der Prüfsumme des Linphone-SDK.

**Gemessen am 17.09.2026:** Unter der URL aus `docs/sdk-setup.md` liegt eine
**andere Datei** als die, mit der hier gebaut wird — dieselbe Adresse,
dieselbe Versionsnummer **5.5.18**, aber `Last-Modified: 07.09.2026 20:19
GMT` und **313 666 099 Bytes** gegen die 313 467 757 der Fassung vom
04.09.2026. **198 342 Bytes Unterschied**, `Content-Type: application/zip` —
also **keine Fehlerseite, sondern ein stilles Neuablegen desselben Release.**
Das lokale ZIP trägt weiterhin genau den Wert, der in `ci.yml`, `release.yml`
und `docs/sdk-setup.md` steht.

**Was daran zählt, ist nicht der rote Haken.** Erstens: `PublicRepositoryTests`
ist laut `CLAUDE.md` «ab jetzt kein Formalismus mehr, sondern die letzte
Kontrolle vor der Öffentlichkeit» — und sie **läuft in der CI nie**, weil der
Build vorher abbricht. Seit dem Repo-Wechsel hat sie nur noch lokal gegriffen.
Zweitens: `release.yml` trägt dieselbe Prüfsumme, **ein Release über GitHub
Actions würde genauso scheitern.**

**Die Prüfsumme einfach auf den neuen Wert zu setzen, wäre die falsche
Antwort** — sie ist die Kontrolle, die verhindert, dass ein unbesehen
verändertes SDK in den Build kommt, und dieselbe Überlegung wie in ADR-040
(«Was in einer fremden Datei steht, hat niemand geprüft»). Zwei Wege stehen
zur Wahl, und die Entscheidung ist offen: **(a)** das neue ZIP holen, den
Unterschied zur Fassung vom 04.09. ansehen und ihn bewusst übernehmen — dann
bauen CI und Arbeitsplatz wieder dasselbe; **(b)** das geprüfte ZIP unter
eigene Kontrolle bringen (eigenes Release-Asset), womit ADR-005 zu Ende
gedacht wäre: dort steht schon, dass der Feed des Herstellers unzuverlässig
ist.

## Die Schreibtisch-Runde A1 hat angefangen

Der Teil des Gerätetags, der weder Anlage noch Headset noch einen zweiten
Rechner braucht.

**Zuerst hat die Runde einen Befund über sich selbst hervorgebracht.**
**Vierzehn Zeilen — T304 bis T317 — waren in zwei Plänen ausformuliert und nie
in `docs/test-matrix.md` übertragen** (`AUDIOQUALITAET-PLAN.md` und
`SHELL-IM-GESPRAECH-PLAN.md`). **T312 galt derweil als bestanden** — im Plan,
nicht in der Matrix. Wer die Matrix abgearbeitet hätte, hätte das Gespräch im
breiten Fenster nie geprüft. Sie stehen jetzt drin; die Zählung geht damit von
252 von 282 auf **222 offen von 303**. Eine Zeile, die nur im Plan steht, ist
keine Zeile.

**Zehn Zeilen haben ein Ergebnis**, sieben davon heute gemessen: T134, T209,
T215, T243, T247, T248, T250. **Fünf Befunde**, alle eingetragen und liegen
gelassen, wie die Regel der Runde es verlangt — vollständig in
`docs/plans/BEWEIS-PLAN.md` unter «Befunde aus A1».

**Der unangenehme ist A1-4.** In `%LOCALAPPDATA%\nipp\logs\beenden.txt` steht
bei **jedem** Beenden «Exit() ist zurückgekehrt, ohne den Prozess zu beenden»
— fünfmal, vom 14. bis zum 17.09.2026. Der Kommentar über der Zeile sagt:
*«Hierher kommt niemand, und genau das ist die Aussage.»* `Application.Exit()`
kehrt nicht zurück, wenn es wirkt. **Der Fehler vom 07. bis 12.09.2026, der
als behoben gilt, erfüllt weiter seine eigene Anzeigebedingung** — und
niemand hat die Datei je angesehen, obwohl sie genau dafür geschrieben wurde.
Was den Prozess dann beendet, ist **nicht gemessen**: der Drei-Sekunden-
Wächter käme in Frage, aber seine Meldung fehlt in derselben Datei, und einmal
war der Prozess schon nach 1,5 Sekunden weg.

Die übrigen vier: die beiden Schieberegler der Audio-Gruppe haben **keinen
vorlesbaren Namen** (die einzigen zwei bedienbaren Elemente ohne, über alle
Reiter und alle sieben Gruppen geprüft); das Infobereich-Symbol wird als
«nipp nipp — angemeldet» **doppelt angesagt**; **«Präsenz setzen» fehlt** im
Infobereich-Menü, womit Befund C8 des Reviews am laufenden Programm bestätigt
ist; und **leere Gruppen verschwinden beim Filtern nicht** — bei einem Treffer
in einer von vier Gruppen stehen die anderen drei als «(0)» da, je 48 Pixel
hoch, obwohl T248 das Gegenteil verlangt.

**Und das lange Gespräch aus dem Audioplan stand längst im Protokoll.** Am
17.09.2026 liegen elf Gespräche im Tagesprotokoll, darunter eines über **30
Minuten**, alle ohne Rauschfilter — der Alltagsbeleg, an dem A6 zweimal
gescheitert war (13 und 23 Sekunden). **Kein Filter überzieht den 10-ms-Tick**
(teuerster `MSRtpSend`, max 9,37 ms gegen 77,24 mit Filter). **Aber fünf
Ticker-Verspätungen stehen trotzdem drin**, 53 bis 79 ms über die halbe
Stunde: der Rauschfilter war die **häufige** Ursache, nicht die einzige. Eine
Verspätung von 79 ms in einer Kette, deren teuerster Schritt 9,37 ms braucht,
kommt nicht aus der Rechenarbeit — **derselbe Verdacht wie bei T38**, und
dieselbe Zeile auf echter x64-Hardware beantwortet beide. A7 steht damit bei
13 / 2 / 14 / **15**, immer im Early Media, danach 30 Minuten sauber.

**Was die Runde sich selbst beigebracht hat:** wer `settings.json` bei
laufendem nipp ändert, verliert die Änderung — beim Beenden schreibt nipp
seinen Stand vollständig zurück. Vierzig von Hand eingetragene Nebenstellen
waren nach dem Neustart wieder zehn.

**Was am Gerät aussteht:** rund 95 der S-Zeilen. Zwei Aufbauten liegen fertig
bereit und sind noch ungenutzt — eine lokale REST-Attrappe (T43, T44, T45) und
ein Provisionierungs-Server samt Profilen (T29, T30, T31, T167, T245, T257,
T258). Dabei sind schon zwei Befunde abgefallen, ohne dass eine Zeile lief:
**`nippprov show` aus T167 gibt es nicht** (das Werkzeug kennt `neu`,
`pruefen`, `schema`), und das Keep-Alive-Feld trägt kein Schloss, weil ihm der
`SettingPath` fehlt.

---


**Stand 16.09.2026, nachts.** Build ohne Warnungen, **1252 Komponententests**
und **34 Architekturtests** grün. Zweig `main`, gepusht. **Release 0.9.12
veröffentlicht** (Kanal `win-stable`, Delta von 0.9.11, unsigniert wie
bisher).

**Der Tag hatte drei Stränge, und zwei davon fingen mit einer Meldung aus dem
Alltag an.**

**1 — «Teilweise starkes Rauschen» (`docs/plans/AUDIOQUALITAET-PLAN.md`).**
Gefunden wurde die Ursache nicht am Gerät, sondern in den Zeilen
`FILTER USAGE STATISTICS`, die auf Debug ohnehin im Protokoll stehen und in
die noch nie jemand gesehen hat.

- **`MSNoiseSuppressor` überzieht sporadisch den Tick**: max **77,24 ms** bei
  einem Ticker, der alle **10 ms** läuft. Die Folge steht im selben
  Protokoll — `Ticker: We are late of 136 miliseconds`, dann
  `Could not get buffer`, dann ein Jitterpuffer, der von 40 auf 154 ms
  springt. Jeder Sprung folgt drei bis sechs Sekunden auf eine Verspätung.
- **Die Gegenprobe (T304) hat es belegt:** ohne den Filter keine
  Ticker-Verspätung, Spitzenlast 11,99 statt 77,24 ms, Jitterpuffer stabil —
  und das Geräusch weg.
- **Der Filter steht nur in der Senderichtung.** Der Schalter in den
  Einstellungen konnte gegen ein Rauschen im eigenen Hörer nie etwas
  ausrichten; seit heute sagt die Beschreibung das auch.
- **Der Standard bleibt trotzdem «ein».** In vier von sechs Gesprächen blieb
  das Maximum unter 6,2 ms, in einem davon über acht Minuten — der Filter ist
  normalerweise unauffällig, und was hier passiert, sieht nach der Emulation
  aus. Ihn überall abzuschalten verschlechterte jeden Arbeitsplatz wegen
  eines Problems, das es dort womöglich gar nicht gibt. **T38 hat damit
  endlich eine konkrete Zeile**: auf echter x64-Hardware den `max`-Wert
  ablesen, unter 6 ms heisst «es war die Emulation».

**2 — Eine ergänzte Mobilnummer erschien erst nach einem Neustart.**
`SameContacts` verglich nur Kennung und Gruppe, und die Kennung einer
Nebenstelle ist `team:{zähler}:{kurzwahl}` — eine Mobilnummer ändert sie
nicht. Dieselbe Lücke verschluckte einen geänderten **Namen** und eine
korrigierte **SIP-Adresse** (dort blieb zusätzlich die Lampe aus). Es war der
dritte Anlauf an der Stelle, deshalb vergleicht sie jetzt den **ganzen**
Datensatz statt eines weiteren Feldes. Sieben Tests, davon vier Gegenproben.

**3 — Das Gespräch im breiten Layout** (`docs/plans/SHELL-IM-GESPRAECH-PLAN.md`).
Bisher ersetzte ein Gespräch die ganze Seite; jetzt steht es breit in der
linken Spalte, und rechts bleiben die Nebenstellen mit ihren Lampen sichtbar
und anklickbar. **T312 ist am Gerät bestanden** (23:28, 13,6 Sekunden
verbunden, keine Ausnahme). **Zwei Dinge brauchten keinen Code:** der zweite
Anruf per Kachel steht schon im `SipService`, und die DTMF-Regel fällt
vermutlich aus dem Ereignisbaum — *vermutlich*, denn gemessen ist sie nicht.

**Was am Gerät aussteht:** T313 bis T317 (Kachelklick im Gespräch,
Layoutwechsel **während** eines Gesprächs, DTMF, Anruf vor dem ersten Messen,
zwei Gespräche breit) — und aus dem Audioplan **ein langes Gespräch** ohne
Rauschunterdrückung: zweimal versucht, zweimal waren es 13 und 23 Sekunden.

**Ein Befund, der sich dreimal bestätigt hat und offen bleibt (A7):** zwischen
`Ringing` und `Connected` wirft WASAPI jedes Mal einen Schwung
`Could not get buffer` — 13, 2 und 14 Mal, **unabhängig vom Rauschfilter**.
Immer im Rufton, nie im Gespräch.

**Und eine Zeile in `CLAUDE.md` hat eine halbe Stunde gekostet:** unter
«Befehle» stand `build.ps1 build Nipp.sln -c Debug` als *der* Bauen-Befehl.
Das prüfen Compiler und Tests, erzeugt aber **keine startbare App** — nipp
stirbt dann sofort mit `REGDB_E_CLASSNOTREG`, ohne eine einzige Zeile im
eigenen Protokoll. Gesucht wurde der Fehler im frisch umgebauten XAML;
gefunden hat ihn die Gegenprobe mit der Release-Exe vom 14.09. Die Zeile
nennt jetzt beides.

---


**Stand 14.09.2026, nachmittags.** Build ohne Warnungen, **1237
Komponententests** und **34 Architekturtests** grün.

**Zuletzt behoben: nipp stürzte im Gespräch ab** (**ADR-067**). Gemeldet als
«während dem Call abgeschmiert», sieben Mal reproduziert — und der Auslöser war
am Ende das **blosse Überfahren des Auflegen-Knopfes** mit der Maus.

- **Der Fehler hinterliess nichts:** keine verwaltete Ausnahme, kein
  `crash.txt`, keine Protokollzeile. Im Ereignisprotokoll stand `0xc000027b` in
  `combase.dll` — eine WinRT-«stowed exception», genau die Sorte, für die
  ADR-053 die drei Wälle aufgestellt hat. Sie greifen nicht, weil nie eine
  verwaltete Ausnahme entsteht.
- **Die Ursache war das Lightweight-Styling am Knopf**: sechs überschriebene
  Zustands-Schlüssel in `Button.Resources`, eingeführt am 13.09.2026 mit
  Befund D14, damit das Rot unter der Maus nicht verschwindet. Aufgelöst
  werden sie genau beim Überfahren. **Alle drei Formen stürzten ab** — flach,
  als Verweis und in `ThemeDictionaries`.
- **Ausgeschlossen wurde der Reihe nach:** die Ziehvorschau vom selben Tag
  (derselbe Absturz auf dem Stand davor), eine verwaltete Ausnahme
  (`FirstChanceException` über alle Threads: kein Eintrag), die
  Qualitätsanzeige im Sekundentakt (Sonde je Teilschritt: Runde vollständig
  durchgelaufen), die Pinselform und der ToolTip.
- **Die rote Rückmeldung ist erhalten**, nur anders gebaut: der Knopf ist
  durchsichtig, die Farbe trägt ein Rahmen darin, die Abstufung macht dessen
  Deckkraft.
- **Zwei Lehren, die teurer waren als der Fix:** «funktioniert nicht» und
  «funktioniert meistens» sehen am Fenster gleich aus — die erste Quote in
  diesem Befund («jeder dritte Zug») stammte aus zwei Stichproben von sieben
  und sechs und war zu hoch gegriffen. Und: *das einzige Ende mitten im
  Gespräch in zwei Tagen* war wertlos als Aussage, weil es **auch das einzige
  Gespräch** dieser beiden Tage war.

**Ebenfalls am 14.09.2026: nipp beendete fremde Teams-Meetings — behoben
(ADR-068).** Nicht der Ring war es, sondern der **Abschlussbericht beim
Ablehnen**; die erste Diagnose hier behauptete den Ring und war falsch.

- **Der gebaute Schutz versagte dreifach:** er prüfte die Fremdbelegung nur
  beim Klingeln, `HookWatch.Fremdbelegung` erkennt ein Meeting gar nicht (das
  Jabra meldet darin durchgehend «aufgelegt»), und `calls.Count > 0` machte
  die Prüfung beim Klingeln ohnehin immer falsch.
- **Vier Messungen vor dem ersten Codezeichen.** Die teuerste Antwort: ohne
  Abschlussbericht **klingelt das Gerät weiter**, auch wenn der Anrufer
  auflegt — der einfache Weg war damit tot. Die Audio-Sitzung dagegen zeigt
  ein Meeting eindeutig, in Wiedergabe **und** Aufnahme, und kostet 3,2 bis
  10,8 ms.
- **Gebaut:** `AudioSessionWatch` über Core Audio (im Zweifel «frei», damit
  ein Fehler an der Audio-Schnittstelle kein Headset ohne Lampen bedeutet),
  das Gate prüft jetzt **jeden** Bericht, und das eigene Gespräch schlägt die
  Fremdbelegung — wer annimmt, hat entschieden. Verschwiegene Berichte werden
  auch nicht zurückgenommen; daran hängt `jeGemeldet`.
- **Am Gerät geprüft:** T298, T299, T301. **Offen:** T300 — gefragt werden die
  Standardgeräte, nicht das Headset. Solange beides dasselbe ist, stimmt es;
  das ist eine Annahme und keine Messung. Und der Notausgang (eine Einstellung
  «Signale ans Headset senden») ist noch nicht gebaut.

---

**Stand 14.09.2026.** Build ohne Warnungen, **1237 Komponententests** und
**34 Architekturtests** grün. Zweig `review-umsetzung`.

**Zuletzt gebaut: die Ziehvorschau** (`docs/plans/ZIEHVORSCHAU-PLAN.md`,
**ADR-066**). Gewünscht war, beim Ziehen zu sehen, wo die Zeile landet — die
anderen sollen ausweichen. Gemeldet war ausserdem, ein Gruppenwechsel sei nicht
möglich.

- **Der Gruppenwechsel war nicht kaputt.** 16 Züge, 15 mit Ergebnis, 14
  Schreibvorgänge — ADR-065 trägt. Aber einer ging **stumm** verloren, und ein
  Fehlschlag, der schweigt, macht aus «geht meistens» in der Wahrnehmung «geht
  nicht». **Die erste Quote, die hier stand, war zu hoch gegriffen** («jeder
  dritte Zug») — sie stammte aus zwei Stichproben von sieben und sechs Zügen,
  in denen gewollte Abbrüche mitzählten.
- **Die Sonde nannte den Grund:** *letztes Ziel Zeile, vor 703 ms*. Beim
  Loslassen stand kein Ablegeziel unter dem Zeiger — zwischen den Zeilen lag
  totes Gebiet. **Das Ablegeziel ist seit ADR-066 die Liste**, die die Stelle
  aus der Zeigerposition rechnet. Damit erledigt sich auch der zweite Befund:
  vier von fünf Drops kamen auf der *gezogenen* Zeile an, weil sie nach dem
  ersten Vorschauschritt unter dem Zeiger liegt.
- **Die Vorschau ist der Auftrag**, kein Bild davon: beim Loslassen wird nicht
  mehr gerechnet, sondern geschrieben, was dasteht. Der Zustand liegt im Kern
  (`TeamDragPreview`, 14 Tests) — der teuerste Fall ist der Abbruch, und der
  muss die Ordnung **exakt** zurückstellen.
- **Gegen das Zittern braucht ein Vorschauschritt eine Zeigerbewegung.** An der
  Gruppengrenze sprang die Vorschau hin und her, in vier von sieben Zügen, 25
  bis 50 ms zwischen Hin und Zurück — nicht wegen des Zeigers, der steht still,
  sondern weil das Layout unter ihm wandert. Die Schwelle (6 Pixel) ist
  **gewählt und nicht gemessen**: T293.
- **Eine Regel, die es nur im Kommentar gab, gibt es jetzt:** im Sortiermodus
  bleibt der Detailbereich zu. Der Kommentar an `IsTeamReorderMode` führte sie
  seit ADR-042 als eine von dreien auf; geschlossen wurde er aber nur beim
  Einschalten, ein Klick öffnete ihn wieder. **Zum vierten Mal ein Satz, den
  niemand geprüft hatte.**
- **Am Gerät geprüft (14.09.2026):** T292 bis T297 bestanden — schmal und
  breit, Gruppenkopf, Abbruch mit Escape, dunkles Erscheinungsbild, und der
  Auflegen-Knopf mehrfach überfahren. **Die Schwelle von 6 Pixeln gegen das
  Zittern ist damit bewährt, nicht gemessen** (T293).

---

**Stand 13.09.2026.** Build ohne Warnungen, **1213 Komponententests** und
**34 Architekturtests** grün. Zweig `review-umsetzung`, **gepusht und von der
CI auf echter x64-Hardware bestätigt**, aber **nicht nach `main` gemergt** —
`main` steht auf dem 06.09.2026. Was den Merge noch trennt, ist kein Code,
sondern der Gerätetag: **252 der 282 Zeilen der Testmatrix haben kein
Ergebnis.**

**Zuletzt gebaut: der Plan für den Rest, und sein erster Schritt**
(`docs/plans/BEWEIS-PLAN.md`). Der Plan deckt die beiden Posten ab, die den
Merge trennen: **W2.8** (der Tag am Gerät) und **W2.1** (Tests für die
SDK-Schicht). **A0 daraus ist umgesetzt.**

- **Die Testmatrix trägt jetzt eine Spalte `Rüstzeug`** — S (Schreibtisch), P
  (Anlage), H (Headset), F (frischer Rechner), W (Windows 10), X (echte
  x64-Hardware). Der Aufwand dieser Matrix war nie die Zahl der Zeilen, sondern
  das Umrüsten; wer sie von oben nach unten abarbeitet, steckt dreimal dasselbe
  Headset um. **288 Zeilen gestempelt.**
- **Und es zählte mehr, als es zählte.** **139 der 252 offenen Zeilen brauchen
  gar kein Gerät** — geschätzt waren 110. Mehr als die Hälfte des Rückstands
  ist am Schreibtisch abzuarbeiten, **bevor** jemand ein Gerät anfasst.
- **Drei Zeilen waren überholt, und niemand hatte sie gestrichen:** T17, T205
  und T232 — alle drei von ADR-062 und ADR-048 erledigt. Bei **T205** stand die
  Streichung seit dem 12.09.2026 im Fliesstext über der Tabelle („T205 und
  T206: beide sind überholt"); T206 war gestrichen, T205 nicht. **Vier weitere
  Zeilen verlangten die Mailbox**, die es seit ADR-062 nicht mehr gibt (T154,
  T193, T210, T213) — wer sie am Gerät geprüft hätte, hätte vier Fehlschläge
  gemeldet, die keine sind.
- **Zwei Zeilen waren als Tabelle kaputt** (T197 und die Testumgebung ganz oben:
  aus einem Pfad war ein echter Zeilenumbruch geworden; T79 hatte eine Spalte zu
  viel) — seit jeher, und niemandem aufgefallen.

**Für Teil B (W2.1) weicht der Plan von der Massnahme ab und braucht dafür
ADR-066:** keine `ISdkCore`-Fassade über achtzig Member, weil die Wrapper-Typen
weder virtuelle Member noch Schnittstellen haben und die Fassade selbst
ungeprüft bliebe. Stattdessen der Weg, den `RingbackWatch`, `HeadsetPolicy` und
sieben weitere schon gegangen sind: die Entscheidung heraustrennen, das
Ausführen am Gerät prüfen.

**Davor gebaut: drei Meldungen aus dem Alltag** (`docs/plans/ALLTAG-PLAN-2.md`,
**ADR-061** bis **ADR-063**). **T287 bis T289.**

- **Die Vorschau im Karten-Designer zeigte nach einem echten Abruf weiter die
  erfundenen Beispieldaten** (ADR-061). Die Ursache lag **nicht** im Zeichnen:
  der Designer bekam die *Anzeigefassung* der Antwort — bei 8192 Zeichen
  abgeschnitten —, der Leser warf, und der Fänger kehrte **still** zurück.
  Weil der Zähler vor der Prüfung stand, meldete die Statuszeile trotzdem
  Erfolg. **Derselbe Fehler stand ein zweites Mal auf der Einstellungsseite**,
  und der Name `RawResponse` war die halbe Ursache.
- **Der Mailbox-Reiter ist weg, und zwar ganz** (ADR-062). Drei Reiter statt
  vier. Entfernt ist die ganze Kette bis in die SDK-Schicht und die
  Provisionierung — der Reiter war der **einzige** Weg zu «Mailbox anrufen»
  und die **einzige** Anzeige für wartende Nachrichten. **Eine Abweichung von
  der Spezifikation**, an acht Stellen dort vermerkt.
- **Eine Gliederungsebene weniger bei den Nebenstellen** (ADR-063). Der
  äussere Aufklapper «Nebenstellen (10)» ist weg; «Team» und «Dienste» stehen
  direkt da. Die Gruppen konnten das Klappen und Merken schon — zu tun war,
  eine Ebene zu entfernen. **Die Falle dabei:** bei genau einer Gruppe wurde
  gar kein Kopf gezeichnet.

**Davor gebaut: Welle 2 der Standortbestimmung** — der Weg auf den
Arbeitsplatz, zwei Absturzpfade und die Tokens. **ADR-055 bis ADR-059.**

- **Ein Arbeitsplatz war vier Handgriffe weit weg, und einer stand nur in der
  Dokumentation** (W2.3). `build\Install-Nipp.ps1` legt den
  Auslieferungszustand ab und startet das Setup; ohne die Factory-Datei nimmt
  ein Arbeitsplatz kein Kundenprofil entgegen, weil die Provisioning-Adresse
  darin steht. **Wer das nicht wusste, suchte den Fehler beim Server.** Das
  Skript prüft die Datei **vor** dem Kopieren und läuft mit `-Silent` als
  Startskript.
- **nipp läuft jetzt einmal, auch ohne Paketidentität** (W2.4). §10 verlangt
  das, und `AppInstance` liefert es nur *mit* — ausgeliefert wird unpackaged,
  der Normalfall im Feld war also der Fall ohne Schutz. Zwei Instanzen
  verdrängen einander an der Anlage, schreiben `settings.json` gegeneinander
  und melden dasselbe Kürzel zweimal an. **Am laufenden Programm bestätigt:**
  ein zweiter Start mit `tel:999` reichte die Nummer weiter.
- **«Nicht stören» ist ein stummer Klingelton auf Zeit** (W2.5, ADR-055) —
  kein SIP-Zustand, keine Mitteilung an die Anlage, und **er überlebt keinen
  Neustart**: ein Schalter, den man einschaltet und vergisst, nimmt Anrufe
  entgegen, die niemand hört.
- **Ein Schreibfehler ist eine Meldung, kein Absturz** (W2.2, ADR-057). Seit
  ADR-045 schreibt jede Feldänderung sofort, und «Konto entfernen» lief aus
  einem `async void` — ein Virenscanner, der die Datei kurz hielt, nahm den
  Prozess mit. Dazu meldete der `ContactStore` aus dem Hintergrund an gebundene
  Oberflächen, **derselbe Absturz wie beim `UpdateService` am 08.09.2026.**
- **Die Tokens beschreiben, was gezeichnet wird** (W2.6, ADR-058). Die Skala
  sagte 3/6/10/14, gezeichnet wurde ein zweites Raster aus Literalen —
  einundfünfzig mal die 8 gegen **zwei** Verwendungen von `NippGapLarge`. Zwei
  Eckradien waren von Fluent abgeschrieben, achtunddreissig Symbolgrössen
  standen als Zahl da, und der Auflegen-Knopf hatte alle drei Zustände auf
  demselben Pinsel. **Am gebauten Fenster nachgeprüft, nicht angenommen.**
- **Die Dokumentation ist umgezogen** (W2.7). Dreizehn Pläne und Reviews
  liegen unter `docs/plans/`, `CLAUDE.md` ist von **1554 auf unter 400 Zeilen**
  zusammengezogen, und was Erfahrung ist statt Regel, steht in dieser Datei und
  in `docs/lehren.md`.

**Davor gebaut: Welle 1 der Standortbestimmung** — sieben Massnahmen,
alle mit CI auf echter x64-Hardware bestätigt. **T263 bis T273.**

- **Der Fokusklau kehrte im Rückfallpfad zurück.** Kommt keine
  Benachrichtigung durch, holte das Fenster sich den Vordergrund, während der
  Fokus auf «Annehmen» lag — **ADR-049 war damit auf genau den Arbeitsplätzen
  neu gebaut, auf denen der Rückfall überhaupt greift.** Es erscheint jetzt
  ohne Vordergrundwechsel; der Fokus in einem nicht aktiven Fenster bekommt
  keine Tastendrücke, und beide Regeln stehen unverändert nebeneinander.
- **Das Ergebnis einer Weiterleitung war unsichtbar.**
  `OnTransferStateChanged` war nicht abonniert, und «übergeben» stand im
  Protokoll, **bevor die Anlage geantwortet hatte**. Ein 403 auf den REFER
  liess das Gespräch je nach Anlage gehalten stehen, ohne ein Wort.
- **Acht stille Lesezugriffe aufs SDK** gaben einen Ersatzwert zurück, ohne
  eine Zeile. Jede bei jedem Pump-Durchlauf zu protokollieren wäre Rauschen —
  `QuietFailures` meldet **einmal je Stelle und Sitzung**.
- **Meldungen, die sagen was zu tun ist:** ein Klick auf den Toast-Körper tat
  nichts, der Weg von der gescheiterten Anmeldung zum Passwortfeld kostete
  vier Schritte, dreizehn Stellen zeigten einen rohen Ausnahmetext, und das
  erste Schliessen in den Infobereich geschah wortlos.
- **`TrayDark.ico` und `TrayLight.ico` hatten dieselbe Prüfsumme**,
  `nipp-dark` und `nipp-light` auch. Die Wahl nach Erscheinungsbild hat seit
  dem Logowechsel nie etwas bewirkt. Dazu vier WinUI-Pinsel, die dem
  **System**thema folgten statt der Wahl des Benutzers, und ein Warnton mit
  4,14:1 gegen Mica.
- **Der Rückbau beim Deinstallieren stand im Plan als erledigt und war nicht
  angemeldet** — das Muster «gebaut, nicht angeschlossen», **zum sechsten
  Mal**. Und die Update-Prüfung lief nur beim Start, obwohl nipp im
  Infobereich wochenlang durchläuft.
- **Zwei Anführungszeichenpaare, «Unverschluesselt zulassen», drei
  Dauerformate.** `UserTextTests` hält die Regeln jetzt fest — über den
  sichtbaren Text, nicht über ganze Zeilen: der erste Anlauf meldete zwei
  Dutzend Kommentare und Wörter wie «zuerst».

**Davor gebaut: Welle 0 der Standortbestimmung** (`docs/plans/REVIEW-2026-09-12.md`,
`docs/plans/WELLE-0-PLAN.md`, **ADR-053** und **ADR-054**, dazu Nachträge zu **ADR-019**
und **ADR-022**). Fünf Schritte, die vor jeder weiteren Entwicklung stehen
mussten:

- **Die Ausnahmegrenze** (ADR-053). Drei Wege, auf denen eine Ausnahme nipp
  beendete, und keiner war Absicht: die SDK-Callbacks liefen ungeschützt an
  acht Abonnenten, **`LinphoneException` wurde im ganzen `src/` nirgends
  gefangen**, und fünf `async void`-Behandler hatten kein `try`. «Stumm»
  drücken, während die Gegenseite auflegt, war ein Absturz. Dazu
  `CallHistoryStore` **ohne einen einzigen `catch`** — eine kaputte
  `history.db` hiess: nipp startet nicht.
- **Der SIP-Trace wird maskiert** (ADR-022, Nachtrag). Auf Debug standen im
  Protokoll dieser Maschine **714 Zeilen mit Digest-Kopfzeilen und 4 282 mit
  Rufnummern**. Entschieden: der Support darf Debug einschalten lassen, also
  wird maskiert. Der Preis steht in der ADR — eine dreistellige Nebenstelle
  verschwindet ganz.
- **Der Benutzer gewinnt** (ADR-054). Das Profil überschrieb bei **jedem
  Start** alles, was es nannte; `IsLocked` kam in `ProvisioningService` nicht
  vor, während die Dokumentation das Gegenteil zusagte.
  `NippSettings.UserOverrides` schliesst das, eingetragen an **einer** Stelle.
- **Der SIP-Port kommt an** (ADR-019, Nachtrag). Feld, Eingabe,
  Profilschlüssel, Validator und Neustart-Hinweis waren da —
  **`core.Transports` kam im ganzen Telefonie-Ordner nicht vor.** ADR-019 hatte
  genau diese Einstellung ins Profil verwiesen und behauptet, sie sei
  umgesetzt. **Das Muster «gebaut, nicht angeschlossen» zum fünften Mal.**
- **Die Klammer-Null.** `+41 (0)79 123 45 67` — die Schreibweise aus Outlook
  und jeder zweiten Signatur — wurde als `+410791234567` gewählt, ohne
  Fehlermeldung.

**Zwei Befunde kamen erst durch die Tests heraus**, nicht durchs Nachdenken:
der Verbindungspool von SQLite hielt die kaputte `history.db` offen, sodass der
Zug zur Seite zeitabhängig scheiterte (der Test lief einzeln grün und im vollen
Lauf rot); und im Maskierungsmuster stand ein **Backspace-Zeichen statt einer
Wortgrenze** — das Muster passte auf nichts, und nur die Ausgabe im Testlauf
hat es gezeigt.

**Davor gebaut: die Breite gehört dem Fenster** (**ADR-052**, **T251 bis
T254**). Drei Meldungen aus dem Alltag, und zwei davon waren dieselbe Ursache:

- **Die Deckelung ist weg.** Zwischen 480 und 960 Pixeln stand der Inhalt als
  schmaler Block in der Mitte, links und rechts leerer Rand — das war ADR-046
  und ist zurückgenommen. **Die zerfallende Zeile ist damit ein bewusst
  getragener Preis**, kein übersehener Fehler; die Milderung steht in ADR-052.
- **Die Kacheln standen untereinander, nicht zu wenige nebeneinander.** Das
  `ItemsWrapGrid` lag im `GroupStyle.Panel` — **und das lesen die
  virtualisierenden Panels gar nicht.** Gezeichnet hat das `ItemsStackPanel`
  daneben: eine Kachel je Reihe, über die volle Breite gestreckt. Der Kommentar
  daneben behauptete seit ADR-047 das Gegenteil und war die Begründung, die
  Stelle nicht anzufassen. **Am gebauten Fenster gesehen, nicht im Code.**
- **Und 468 Pixel blieben auf einem 1920er Fenster tot:** zwei Sternspalten,
  beide mit Höchstbreite, geben den gekürzten Anteil nicht weiter. Die linke
  Spalte ist jetzt **absolut** 480 breit, die rechte nimmt den Rest. Die
  Umschaltleiste steht breit nur noch unter der linken Spalte, der
  Kachelbereich reicht daneben bis an den unteren Rand.

**Davor gebaut: das zweite UX-Review** (`docs/plans/UX-REVIEW-2.md`, **ADR-049** bis
**ADR-051**). Zwanzig Befunde, **achtzehn umgesetzt**, zwei begründet offen.
**T222 bis T250.**

- **Phase 1 — was ein Fehlgriff kosten darf** (ADR-049). Der Karten-Designer
  verlor die Arbeit beim Schliessen über das Kreuz, **lautlos**: er führte
  `HasUnsavedChanges` und las es an einer einzigen Stelle — während der
  Kommentar in `Show` seit jeher das Gegenteil behauptete. **Das Fenster sprang
  beim Klingeln nach vorn, mit dem Fokus auf «Annehmen»** — zwei je für sich
  richtige Entscheidungen, zusammen ein Anruf, den die nächste Leertaste
  entgegennahm. Die Aufnahme startete mit einem Klick, zwei Spalten neben
  «Stumm». **Enter auf einem Namen wählte den Namen.** Drei Knöpfe taten
  stillschweigend nichts. **T222 bis T230.**
- **Phase 2 — der Alltag** (ADR-050). Die Wähltastatur ist **standardmässig
  zu**: sie kostete auf 400 × 660 rund 200 der 450 verfügbaren Pixel, und die
  Kontaktliste zeigt jetzt zehn Zeilen statt fünf. Im Gespräch gibt es
  Tastenkürzel, und **ein zweites systemweites Kürzel schaltet stumm, ohne nipp
  nach vorn zu holen** (weitet §22.5). «Mailboxnummer eintragen» landet im
  Feld statt in der Gruppe. Escape und Alt+Links gehen zurück. **T231 bis
  T240.**
- **Phase 3 — die Struktur** (ADR-051). **Eine Liste je Eingabeart:** eine
  Nummer zeigt die Vorschlagsliste, ein Name die Trefferliste — dieselbe Regel,
  die seit Phase 1 die Eingabetaste trägt. Die **Anruferkarte** ist eine Gruppe
  erster Ebene und per Rechtsklick aus dem Gespräch erreichbar; die Quellen
  stehen unter «Für Administratoren». Das **Kachelraster filtert mit**. **T241
  bis T250.**

**Offen, beide mit Grund:** **C16** (Mailbox in der Navigation) trägt einen
Widerspruch — Fassung A versteckt die Fläche, auf der der Knopf steht, den C8
gerade repariert hat. **C15** («Nicht stören») ist die einzige neue Fähigkeit
der Runde und wartet auf einen Entscheid.

**Und ein Alltagsbefund vom Start der gebauten Fassung, den keine Testzeile
abdeckt:** `Ctrl+Shift+A` — das vorgegebene systemweite Kürzel — ist **auf
dieser Maschine von einer anderen Anwendung belegt** (Windows-Fehler 1408). Der
globale Hotkey wirkt hier also nicht, bis jemand in den Einstellungen ein
anderes einträgt. Die Meldung dazu ist richtig und sagt, was zu tun ist; sie
steht nur im Protokoll und in den Einstellungen, nicht im Blickfeld.

**Davor gebaut: das breite Fenster** (`docs/plans/BREITBILD-PLAN.md`, **§23**,
**ADR-047** und **ADR-048**). Ab **960 logischen Pixeln** stehen zwei Spalten:
links Konto, Nummernfeld, Wähltastatur und der gewählte Bereich, rechts die
**Nebenstellen als Kacheln**, nach Gruppen gegliedert und mit allem darauf, was
die Zeile erst nach einem Klick zeigt. Die Umschaltleiste steuert die linke
Spalte; die Kacheln stehen unabhängig davon. **Und ein angeklickter Kontakt
klappt wieder in der Zeile auf** — in allen drei Listen, unabhängig von der
Breite. **T211 bis T221.**

- **Die Deckelung aus ADR-046 wandert, sie verschwindet nicht:** breit gilt sie
  für die linke Spalte. Eine Kontaktzeile ist nie breiter als im schmalen
  Fenster. — **Einen Tag später mit ADR-052 zurückgenommen:** sie verschwindet
  doch, und die linke Spalte ist fest statt gedeckelt.
- **Das Umsortieren zwischen Gruppen übersteht den Umbau ohne eine Zeile
  Änderung.** `TeamLayout.From` liest die Ordnung aus dem Zustand der
  Sammlungen und nicht aus dem Ziehereignis (ADR-042) — das gilt für ein
  `GridView` wörtlich gleich. Dass das so ist, war beim Schreiben von ADR-042
  kein Ziel; es fällt ab, weil die Antwort dort nicht am Steuerelement hing.
- **Der Einwand, wegen dem ADR-042 den Detailbereich aus der Zeile genommen
  hatte, ist ausgeräumt statt umgangen:** `x:Load` erzeugt den Teilbaum erst
  beim Aufklappen. Geprüft ist der erzeugte Code (`FindName` / `UnloadObject`),
  **die Messung am Gerät steht aus: T220.**

**Der Nebenbefund, und er wäre sonst erst im Alltag aufgefallen: ein
maximiertes Fenster kam nicht maximiert zurück.** `WindowPlacement.Capture`
schrieb Position und Grösse, nicht den Zustand des Presenters, und
`TryApplyRemembered` klemmt zusätzlich auf 92 Prozent des Arbeitsbereichs — wer
nipp maximiert schloss, fand es als beinahe volles Fenster mit Rand ringsum
wieder. Solange das schmale Fenster der Normalfall war, war das eine
Kleinigkeit; wenn Vollbild der Anlass für ein ganzes Layout ist, ist es einer.
**T221.**

**Davor gebaut: die Bedienbarkeit, in drei Phasen** (`docs/plans/UX-REVIEW.md`,
**ADR-044 bis ADR-046**). Ein UX-Review der ganzen Oberfläche hat
fünfundzwanzig Befunde ergeben; umgesetzt sind vierundzwanzig, einer ist
begründet zurückgestellt.

- **Phase 1 — die scharfen Kanten** (ADR-044). Ein Wort je Zustand statt drei
  beziehungsweise vier. Die **Kontaktliste ist ohne Maus bedienbar** — die
  Kontextmenüs hingen am Zelleninhalt, und Menütaste und Umschalt+F10 griffen
  ins Leere, während der Kommentar daneben seit ADR-042 das Gegenteil
  behauptete. Ein Statuston färbt Schrift statt Fläche (im Dunkeln stand fast
  weisser Text auf Gelb, **1,4:1**). **Enter im Weiterleitungsfeld gab das
  Gespräch bis dahin sofort und unwiderruflich ab.** Der Fokus liegt beim
  Klingeln auf „Annehmen". **T185 bis T193.**
- **Phase 2 — der Weg hinein** (ADR-045). **Fünf Speicherregeln werden eine:**
  ein Konto wurde beim Klick geschrieben, eine Nebenstelle nicht — sie erschien
  in der Liste und war beim Verlassen der Seite lautlos weg. Der
  „Speichern"-Knopf ist entfallen. Jeder Hinweis führt jetzt dorthin, wohin er
  verweist (es gab **null** `InfoBar.ActionButton` im ganzen Programm), der
  Erststart klappt die Kontogruppe von selbst auf, und eine SIP-Adresse im
  Domainfeld fällt sofort auf statt nach zwölf Sekunden. **T194 bis T201.**
- **Phase 3 — die Struktur** (ADR-046). Zwei Ebenen in den Einstellungen (114
  Eingabeelemente standen auf einer), **ein Suchfeld statt zwei**, **ein
  Detailbereich statt zwei**, die Inhaltsbreite gedeckelt (gemessen stand das
  Fenster auf **1023 Pixeln**), das Wiedergabegerät im Gespräch und im
  Infobereich, Präsenz und Anrufergebnis in der Sprachausgabe, Tastenkürzel.
  **T202 bis T210.** — **Zwei Punkte davon sind einen Tag später anders
  entschieden worden:** der Detailbereich steht wieder in der Zeile (ADR-048),
  und die Deckelung gilt je Spalte statt fürs Fenster (ADR-047). Die Regel
  dahinter — *ein* Ort für alle Listen, *eine* Zeilenbreite — ist in beiden
  Fällen dieselbe geblieben. **Die gedeckelte Breite ist inzwischen ganz
  weg** (ADR-052); der Befund dahinter bleibt richtig und ist ein bewusst
  getragener Preis.

**Der Befund, der über die drei hinausgeht:** `App.SdkStatus` wurde beim Start
gesetzt, als öffentliche Eigenschaft angeboten — und von **keiner Ansicht
gelesen**. Eine unvollständige Installation sah dadurch aus wie „noch kein
Konto eingerichtet". **Dasselbe Muster wie `CardKind.History`,
`IntegrationConfig.cards` und `ClipResolver.DescribeCaller` — zum vierten
Mal.**

**Bewusst zurückgestellt: B20** — eine geführte Feldzuordnung statt des
JSON-Textfelds. Sie lohnt erst, wenn jemand eine Quelle **ohne** Vorlage
anbinden soll; solange Vorlagen importiert werden, trägt die kurze Fassung.

**Davor gebaut: fünf Meldungen aus dem ersten Tag mit den Gruppen, und eine
davon war T134.**

- **„Beenden" beendet jetzt wirklich** (Etappe E). `Application.Exit()` lief
  auf dem Thread des Infobereich-Symbols und ist dort wirkungslos. Vollständig
  in `CLAUDE.md` unter „Bauen auf dieser Maschine"; **T134**.
- **Ziehen zwischen Gruppen** (Etappen F und G, **ADR-042**). Dahinter lagen
  **zwei Fehler übereinander**: der Ziehvorgang schrieb die Gruppe nirgends,
  und die Kennung einer Nebenstelle enthielt ihren **Index** — deshalb schlug
  schon der zweite Zug fehl, **ohne Fehlermeldung und ohne Protokollzeile**,
  während die Anzeige ihn zeigte. Dazu der Detailbereich **in der Zeile** (nur
  im Team), ein Kontextmenü „In Gruppe verschieben" für den Weg ohne Maus, und
  eine neue Gruppe erscheint **ohne Neustart**. **T175 bis T180.**
- **Der Name bei ausgehenden Anrufen** (Etappe H, **ADR-043**). Der Befund
  steckte in einem Namen: `CallInfo.DisplayLabel` war der einzige
  richtungsneutrale „Name" im System und schaute **nie auf Kontakte**. Bei
  eingehenden Anrufen kaschierte das der Anzeigename der Anlage. Es gibt jetzt
  **einen** `CallPartyResolver` mit zwei Fragen — `NameOf` (Name oder nichts)
  und `Describe` (nie leer) —, und `DisplayLabel` ist gelöscht. **T181 bis
  T184.**

**Der Befund, der über diese vier hinausgeht:** `ClipResolver.DescribeCaller`
war als „die eine Stelle für alle" gebaut und hatte im ganzen `src/`
**keinen einzigen Aufrufer**, während dieselbe Regel dreimal ausgeschrieben
danebenstand. **Eine Fähigkeit zu bauen heisst nicht, sie anzuschliessen** —
dasselbe Muster wie `CardKind.History` und wie `IntegrationConfig.cards`, zum
dritten Mal.

**Davor gebaut: der Quelltext wird offengelegt** (Etappe B, **ADR-040**).
nipp steht unter der **AGPLv3** — `LICENSE` und `NOTICE` liegen im Repo, und
damit ist die Frage beantwortet, die seit dem 04.09.2026 jede Abgabe ausser
Haus sperrte. Möglich wurde das, weil die beiden bv2-eigenen Systeme den
Quelltext verlassen haben: eine **Anbietervorlage** ist jetzt **eine** Datei,
die importiert wird (`ConnectorLibrary`, `ConnectorTemplateReader`), und die
beiden liegen in einem privaten Vorlagen-Repo. **Mitgeliefert wird nur noch
„Eigene REST-API".** Die beiden Zusagen des Katalogs sind dabei vom Test in den
Leser gewandert: eine Vorlage kommt nie eingeschaltet herein, und eine mit
einem Zugangsschlüssel darin wird **abgelehnt** — System.Text.Json schluckt ein
unbekanntes `token` sonst still. **Nichts davon ist am Gerät abgenommen: T169
bis T174.**

**Und ein Befund, der nichts mit den Systemnamen zu tun hatte:** im Repo standen
**echte Kundendaten** — eine als „echte Antwort" deklarierte
Gesprächszusammenfassung mit Klarnamen, Firma und Rufnummer, dazu Klarnamen in
der Testmatrix und in zwei Toast-Testdateien. Alles durch Musternamen ersetzt.
`PublicRepositoryTests` hält es fest.

**Davor gebaut: die Team-Kontakte** (Etappe C, **ADR-041**) — eine
**Handynummer** am Kollegen, ein **Detailbereich** unter der Liste mit Präsenz
und jeder Nummer einzeln wählbar, und **eigene Gruppen** neben „Team". Das
meiste davon war gebaut und nicht angeschlossen: `ContactNumberKind.Mobile`,
`ContactRow.Choices` und `CallNumberCommand` gibt es längst, und `ClipResolver`
läuft beim eingehenden Anruf über **alle** Nummern — die Lücke war eine Zeile in
`TeamContactSource`, die genau eine Nummer baute. **Und sie wog schwerer, als
sie klang:** auf dem neuen Outlook gibt es kein COM (ADR-018), also war eine
Handynummer eines Kollegen bisher nirgends auflösbar. Gruppen stehen als eigene
Liste in den Einstellungen, gezeichnet wird **eine** gruppierte ListView statt N
Expander, und der Detailbereich hängt an der Auswahl. **Nichts davon ist am
Gerät abgenommen: T160 bis T168.**

**Davor gebaut: der Toast bekennt Farbe, und der Karten-Designer stolpert
nicht mehr** (Etappen A und D des Plans vom 10.09.2026). Der Toast setzt
„Annehmen" grün und „Ablehnen" rot über `AppNotificationButtonStyle`, mit
Fähigkeitsprüfung, weil im Feld Windows-10-Arbeitsplätze stehen. Im Designer
lagen **zwei Knopfgruppen über beziehungsweise ausserhalb ihrer Fläche** —
abgeschnitten wurden ausgerechnet „Linie" und „Abstand", und genau daraus wurde
die Meldung, die beiden liessen sich nicht löschen. **Löschen konnte der Kern
immer** (`RemoveSelected` prüft keinen Feldtyp, ein Test belegt es seit K4); es
war ein Auffindbarkeitsfehler, und es gibt jetzt vier Wege dorthin. Dazu rechnet
die Vorschau mit einer **eingegebenen Nummer samt echtem Abruf** — den Parameter
nahm `BuildSnapshot` von Anfang an, nur verdrahtete der Designer ihn fest.
**Nichts davon ist am Gerät abgenommen: T154 bis T159.**

**Davor gebaut: zwei Meldungen aus dem Alltag, beide am Protokoll
aufgeklärt.** Erstens „ein Anruf auf nipp wirft mich aus dem Teams-Meeting" —
Ursache ist der Ring-Report an ein HID-Gerät, das sich nipp mit Teams teilt;
Reports gehen jetzt nur bei eigenem Anlass hinaus (`HeadsetSignalGate`, ADR-028
Nachtrag 4). Zweitens „beim Wählen höre ich einen nipp-eigenen Rufton statt den
der Anlage" — nipp entschied nach 800 ms auf einem Sekundenmittel, das noch auf
0 stand, und legte sich 168 ms über den Anfang des Anlagentons (Nachtrag zu
ADR-029). **Beides ist am Gerät noch nicht abgenommen: T143 bis T153.**

**Am Gerät abgenommen (Engage 75, 09.09.2026):** Annehmen mit der Taste
(**T80**), Annehmen durch Herausnehmen aus der Ladeschale (**T80b**), Auflegen
mit der Taste (**T79**) und ein Gespräch, das stehen bleibt (**T141**).
**Für Link 400 und PRO 9470 offen** — der Engage 75 hat gerade gezeigt, wie
unterschiedlich sich diese Geräte verhalten.

**Am 09.09.2026 repariert: eingehende Anrufe liessen sich nicht annehmen.** Der
Fehler war so gross wie einfach: nipp legte 10 ms nach jedem Annehmen wieder
auf, und bei ausgehenden Anrufen 331 ms nach dem Verbinden. Ursache war das
Nachziehen des Gabelzustands am Headset — es schrieb in dasselbe Feld, aus dem
der Lese-Thread seine Flanken ableitet, und erfand damit Tastendrücke. Die
Bedeutung eines Drucks hängt jetzt am Anrufzustand und nicht mehr am Gerät;
damit ist auch **T82** strukturell gelöst statt geflickt. Vollständig im
**Nachtrag zu ADR-028**; `docs/lehren.md` unter „Windows-Integration".

**Es brauchte drei Anläufe, und jeder wurde erst durch die Messung möglich.**
`HookSwitch` ist an diesem Gerät kein Tastendruck, sondern ein Zustand, den das
Gerät mitverhandelt — solange ein eigener Report unterwegs ist, gilt jede
Gabelmeldung deshalb als Antwort. Und die Reports brauchten **10,9 bis 83,2
Sekunden**, weil `HidD_SetOutputReport` über die Control-Pipe hängt; über
`WriteFile` sind es **3 ms**. Daran hingen drei Alltagsmeldungen auf einmal:
„das Headset läutet weiter, obwohl ich den anderen höre", „extrem verzögert"
und „aus der Ladeschale nehmen tut nichts" — Letzteres, weil das Gerät nie
rechtzeitig erfuhr, dass es klingelt.

**Der Befund daran, und er ist der teuerste bisher:** über empfangene
HID-Reports stand **nie eine Zeile im Protokoll**. Zwei Tage lag der Fehler im
Log ohne Spur, und „die Taste tut nichts" war nicht von „hier kommt gar nichts
an" zu unterscheiden — **dieselbe Lücke wie beim Symbol im Infobereich, zum
zweiten Mal.** Dazu: **T79 (Auflegen) war bestanden und belegte nichts.** In
keinem Protokoll dieses Projekts steht je „Annehmen am Headset gedrueckt"; die
Taste traf zufällig eine der beiden Richtungen. T79 ist zurückgenommen, T80 bis
T84 sind je Gerät neu zu prüfen (drei sind im Alltag), und
`tools\Test-Headset.ps1` wertet aus, was das Gerät wirklich schickt.

**Davor gebaut: Installer und Update-Verteilung** (R0 bis R8) — ein
Velopack-Setup statt MSIX (**ADR-038**), Updates über GitHub Releases mit den
Kanälen stable und beta, beim Start wird **gefragt und nicht geladen**
(**ADR-039**). Vollständig in **`docs/plans/RELEASE-PLAN.md`**, Bedienung in
`docs/updates.md`.

**Der Befund daran, und er ist wieder derselbe:** beim ersten
`dotnet publish` lagen 229 MB Laufzeit im Ausgabeverzeichnis und **keine
einzige Linphone-DLL**. `Linphone.Sdk.targets` kopierte die native Kette an
`Build` (nach `$(OutDir)`) und für MSIX ins Paketlayout — publish sammelt aber
seine eigene Dateiliste und kannte beides nicht. Zwei Wege waren gebaut, der
dritte fehlte, und **nichts hat es gemeldet**: der Build war grün, das Ergebnis
startklar aussehend. Aufgefallen wäre es erst auf dem Zielrechner, mit der
Meldung aus §14.2, die nicht sagt, welche Datei fehlt. Deshalb prüft
`Release-Nipp.ps1` das fertige Verzeichnis noch einmal nach — **ein Target, das
lautlos nichts tut, sieht wie ein Erfolg aus.**

**Die Lizenzfrage ist beantwortet: AGPLv3** (11.09.2026, **ADR-040**).
`LICENSE` und `NOTICE` liegen im Repo, `docs/licensing.md` ist umgeschrieben.
Damit ist der Posten weg, der seit dem 04.09.2026 jede Abgabe ausser Haus
gesperrt hat.

**Das Repo selbst ist aber noch privat** — vollzogen ist die Wahl, nicht der
Schritt. Solange es privat ist, braucht die Update-Prüfung weiterhin ein Token
aus dem Provisioning. Was bis zum Öffentlichmachen fehlt, steht als **R10** in
`docs/plans/RELEASE-PLAN.md`: der Scan über **alle** Commits, der SDK-Quelltext als
Spiegel und der Repo-Wechsel selbst. **Achtung beim Wechsel:**
`VelopackUpdateGateway.RepositoryUrl` ist eine Konstante im Code (dazu
`build/Release-Nipp.ps1`) — ein Repo unter neuem Namen heisst, dass jeder
installierte Arbeitsplatz weiter im alten sucht.

**Davor gebaut: Anrufliste und Designer** (L0 bis L6) — ein verpasster Anruf
gilt als gesehen, sobald er angeklickt wurde (**ADR-035**, Schemafassung 2 der
`history.db`), der Kontextbereich unter der Liste ist die **vierte Kartenart**
und klappt auf (**ADR-036**), und der Designer kennt Abstand, ausblendbare
Beschriftung und „von anderer Karte übernehmen" (**ADR-037**). Vollständig in
**`docs/plans/ANRUFLISTE-PLAN.md`**.

**Der Befund daran, und es ist derselbe wie im Plan davor:** `CardKind.History`
stand seit I4 im Modell, mit dem Kommentar „noch nicht verwendet" — der
Kontextbereich baute seine Zeilen selbst und beschriftete sie maschinell aus dem
Feldnamen. Auf der Gesprächskarte war „Letzte arbeit zeile" ein Fehler, hier war
es der Normalfall. **Ein Test hätte es finden können und zählte stattdessen mit:**
`Die_Kartenuebersicht_nennt_alle_drei_Arten` war grün, weil er drei erwartete.

**Davor gebaut: Einrichtung und Karten** (§21.6, K0 bis K6) — der Katalog
„Quelle hinzufügen" mit bv2-Einträgen als *intern bv2* gekennzeichnet
(**ADR-033**), die Quellen-Oberfläche als ein Ort statt drei Aufklapper, ein
Feldkatalog, der Karten-Designer als eigenes Fenster (**ADR-032**) und der
Toast als Kartenart (**ADR-034**). Vollständig in **`docs/plans/EINRICHTUNG-PLAN.md`**, mit
Umsetzungsstand ganz vorn.

**Der wichtigste Befund daran:** der Wunsch „ich möchte die Werte selber
anordnen" traf keine schlechte Bedienung, sondern eine **Lücke**.
`IntegrationConfig` hatte kein `cards`, obwohl `docs/plans/INTEGRATION-PLAN.md` D.2 die
Datei genau so beschreibt und `CardLayoutEngine` jede Karte übersetzen konnte.
Eine eigene Karte war nicht schwer einzurichten, sondern unmöglich. **Wer hier
eine Fähigkeit als „gebaut" liest, prüft, ob sie auch angeschlossen ist.**

### Am Gerät ist nichts davon abgenommen

Und nicht nur davon: **261 von 284 Zeilen der Testmatrix haben kein Ergebnis.**
Das ist der grösste offene Posten des Projekts, nicht der Code — und **die Zahl
wächst schneller als sie schrumpft**: seit dem 09.09.2026 sind 133 Zeilen
dazugekommen und zwei abgehakt worden. (Zwei sind überholt und gestrichen:
T206 und T242.)

| Gruppe | Was |
|---|---|
| **T274–T283** | **Welle 2 der Standortbestimmung** (13.09.2026, ADR-055 bis ADR-059). Wichtigste: **T275** (nipp zweimal starten, dann mit einer Nummer in der Kommandozeile — die zweite Instanz muss sich beenden und die Nummer weiterreichen), **T278/T279** (ein Arbeitsplatz in einem Befehl, einmal von Hand und einmal als Startskript — der Weg, der beim Kunden zählt), **T280** (die `settings.json` sperren und ein Konto entfernen: bis zum 13.09. nahm dieser Klick den Prozess mit), **T274** («Nicht stören» — es darf nicht klingeln, aber der Toast muss kommen) und **T282** (die Optik nach der Token-Umstellung, in beiden Themen und bei 150 Prozent Textskalierung) |
| **T263–T273** | **Welle 1 der Standortbestimmung** (13.09.2026). Wichtigste: **T263** (in Word tippen, anrufen lassen — mit **abgeschalteten** Benachrichtigungen: das Fenster darf den Fokus nicht nehmen), **T264** (blind an eine Nummer weiterleiten, die es nicht gibt), **T269** (das Infobereich-Symbol auf heller **und** dunkler Taskleiste — die offene Frage: es gibt nur noch eine Datei), **T270/T271** (dunkles Thema bei hellem Windows, Kontrastmodus im Betrieb) und **T272** (deinstallieren und die Registrierung prüfen) |
| **T255–T262** | **Welle 0 der Standortbestimmung** (13.09.2026, ADR-053, ADR-054, Nachträge zu ADR-019 und ADR-022). Wichtigste: **T261** (im Gespräch «Halten» drücken, während die Gegenseite auflegt — bis zum 13.09. ein Absturz), **T255** (Debug einschalten und das Protokoll durchsuchen: keine Rufnummer, keine Digest-Antwort), **T257/T258** (der Handwert überlebt das Profil, und die alte Datei wandert richtig), **T259** (der SIP-Port kommt endlich an) und **T262** (kaputte `history.db` — nipp muss starten). **Vorher `history.db` und `settings.json` kopieren** |
| **T251–T254** | **Die Breite gehört dem Fenster** (12.09.2026, ADR-052). Wichtigste: **T252** (auf einem maximierten Fenster darf rechts kein toter Streifen bleiben, und es stehen sechs bis sieben Kacheln je Reihe) und **T254** (vierzig Nebenstellen, Tab-Wechsel — die Virtualisierung nach dem Panelwechsel; der Fehler wäre ein Ruckeln). **T206 und T242 sind überholt** — die Deckelung ist weg (T251), die Kachelspalte ungedeckelt (T252) |
| **T222–T250** | **Das zweite UX-Review** (12.09.2026, ADR-049 bis ADR-051). Wichtigste: **T223** (in Word tippen, anrufen lassen — das Fenster darf den Fokus nicht mehr nehmen, und die Leertaste darf den Anruf nicht annehmen), **T222** (Designer über das Kreuz schliessen — bis dahin ging die Arbeit lautlos verloren), **T225** (die Aufnahme fragt), **T227** (Enter auf einem Namen wählt nicht mehr), **T246/T247** (eine Liste je Eingabeart) und **T249** (im Sortiermodus tippen: der Modus muss ausgehen, sonst verlöre die gespeicherte Reihenfolge die ausgefilterten Einträge) |
| **T211–T221** | **Das breite Fenster** (12.09.2026, ADR-047, ADR-048). Wichtigste: **T214** (zweimal hintereinander eine Kachel in eine andere Gruppe ziehen — die Gegenprobe zu T176, jetzt im Raster), **T215** (vierzig Nebenstellen, Tab-Wechsel: die Virtualisierung des gruppierten Rasters, und der Fehler wäre ein Ruckeln), **T220** (137 Outlook-Kontakte auf- und zuklappen — die Messung zu `x:Load`) und **T211/T212** (der Umbau passiert einmal, nicht bei jedem Pixel). **T205 und T206 sind überholt** — der Detailbereich steht jetzt in der Zeile (T219), und die Deckelung ist seit ADR-052 ganz weg (T251) |
| **T202–T210** | **Struktur** (12.09.2026, ADR-046). Wichtigste: **T203/T204** (die zusammengeführte Suche, und die Gegenprobe mit getrenntem Netz: die lokalen Treffer müssen sofort kommen) und **T207/T208** (das Wiedergabegerät im Gespräch und im Infobereich). **T205 und T206 nicht mehr prüfen** — sie verlangen den Detailbereich unter der Liste und 480 Pixel fürs ganze Fenster; beides gilt seit ADR-047/ADR-048 anders (T219, T211) |
| **T194–T201** | **Speichermodell und Erststart** (12.09.2026, ADR-045). Wichtigste: **T194** (eine Nebenstelle anlegen und sofort zurück — bis zum 12.09.2026 war sie **lautlos weg**; denselben Test für Gruppe, Audiogerät und Codec-Reihenfolge), **T197** (der Weg vom ersten Start zum ersten Konto) und **T200** (eine DLL entfernen: es muss nach kaputter Installation aussehen, nicht nach „kein Konto") |
| **T185–T193** | **Bedienbarkeit** (12.09.2026, ADR-044). Wichtigste: **T188** (Enter im Weiterleitungsfeld darf das Gespräch **nicht mehr** abgeben — der gefährlichste Fall der ganzen Liste), **T185** (Kontextmenü mit der Menütaste; ohne Maus war die Kontaktliste nicht bedienbar) und **T187** (der Chip „unverschlüsselt" im dunklen Erscheinungsbild) |
| **T175–T184** | **Kontaktliste und der Name beim Hinauswählen** (12.09.2026, ADR-042, ADR-043). Wichtigste: **T176** (zweimal hintereinander ziehen — der Zug, der bis dahin **stumm** verlorenging), **T182** (der Name eines Fremdsystems trifft nach dem letzten Zustandswechsel ein und muss an allen vier Stellen nachkommen, ohne die Ansicht viermal neu zu zeichnen), **T178** (eine neue Gruppe ohne Neustart) und **T180** (kein Ruckeln — die Team-Vorlage ist um den Detailbereich gewachsen). Dazu **T134**: „Beenden" beendet wirklich, **und `beenden.txt` ansehen** — keine Wächterzeile mehr |
| **T169–T174** | **Anbietervorlagen und der Weg danach** (11.09.2026, ADR-040). Wichtigste: **T172** (eine Vorlage mit einem Token darin wird abgelehnt — die Zusage, auf der der ganze Weg steht), **T169** (importieren, Quelle anlegen, Token, Testabruf, einschalten) und **T174** (frischer Klon ohne SDK baut durch — die Gegenprobe zum Öffentlichmachen) |
| **T160–T168** | **Team-Kontakte: Mobilnummer, Detailbereich, Gruppen** (11.09.2026, ADR-041). Wichtigste: **T163** (vier Gruppen und 137 Outlook-Kontakte, Tab-Wechsel — **kein Ruckeln**; die Gegenprobe zur Virtualisierung, und der Fehler wäre kein Absturz), **T162** (eine zweite Nummer darf kein zweites SUBSCRIBE kosten, §14.8), **T161** (ein Anruf von der Handynummer zeigt den Kollegen) und **T168** (eine `settings.json` von vorher — **vorher kopieren**) |
| **T154–T159** | **Toast-Farben und Karten-Designer** (11.09.2026). Wichtigste: **T158** (Vorschau mit echtem Abruf — dabei prüfen, dass **keine Rufnummer im Protokoll** landet), **T159** (Designer offen während eines Gesprächs, dabei abrufen — er teilt sich den Thread mit `Core.Iterate()`; das ist T99 in neuer Lage) und **T155** (der Toast auf einem Windows-10-Arbeitsplatz: die Farben dürfen fehlen, ein Absturz nicht) |
| **T143–T153** | **Der Rufton beim Wählen und die Koexistenz am Headset** (10.09.2026). Wichtigste: **T147** (ein Teams-Meeting endet, sobald es auf nipp klingelt — die gemeldete Störung; **auch mit Notebook-Audio prüfen**), **T150** (ob die Erkennung einer Fremdbelegung überhaupt etwas sieht — sie liest einen gehaltenen Gabelzustand, und ob ein Gerät einen führt, steht in seinem Report-Deskriptor), **T143** (kein Fremdton am Anfang des Anlagentons) und **T153** (die Gegenprobe: die Reparatur darf T80b nicht kosten) |
| **T120–T134** | **Installer und Updates.** Wichtigste: **T120** (Installation auf einem Rechner ohne .NET und ohne App SDK — die Zusage von self-contained), **T122** (`tel:`-Link nach einem Update; zeigt der Registrierungseintrag in `current\`, ist ab dem ersten Update Schluss), **T131** (Toasts — die Voraussetzung, wegen der überhaupt unpackaged ausgeliefert wird), **T134** („Beenden“ beendet wirklich; hängt der Prozess, scheitert jedes Update). **Vor T124 `history.db` kopieren** |
| **T111–T119** | Anrufliste und Designer. Wichtigste: **T112** (der „gesehen"-Zustand überlebt einen Neustart — im Arbeitsspeicher hätte alles davor auch funktioniert), **T113** (der Bereich wächst, und die Liste behält ihre Virtualisierung — **mit Tab-Wechsel** prüfen), **T116** (kaputte Karte kostet nur sich selbst). **Vorher `history.db` kopieren**: Fassung 2 ist ihre erste Wanderung |
| **T98–T110** | Katalog, Designer, Toast-Karte, packaged. Wichtigste: **T99** (Designer während eines Gesprächs — er läuft auf dem Thread, der alle 20 ms `Core.Iterate()` bedient), **T104** (kaputte Karte darf nur sich selbst kosten), **T105** (Toast-Karte, dabei T90–T92 mitfahren) |
| **T90–T97** | Toast mit Kontext, Klingelton, Info-Bereich, Anpinnen, Infobereich-Symbol. T96 bestanden |
| **T78a–c, T80–T86** | Headset. Wichtigster: **T82** — Gespräch in der Oberfläche beenden, dann die Taste beim nächsten Anruf. Ist der Gabelzustand nicht nachgezogen, ist ab da jeder zweite Druck falsch |
| **T40–T59** | Die Integrationen. Besonders T46 (Virtualisierung im Kontakte-Tab) und T51/T52/T57 (beide Quellen aus, Netz getrennt, Protokoll ohne Rufnummern) |
| **T02–T37, T67–T76** | Netz, Registrierung, Oberfläche. Substanziell: **T03 (TLS)** — §9.2 gibt TLS als Standard vor, das Konto läuft über UDP, die Prüfung ist eingebaut und ungeprüft |

**Nicht gebaut, mit Absicht:** **T101** — Ziehen aus der Palette im Designer.
Eingefügt wird über Doppelklick und Knopf.

**Offen mit Befund:** **T110** — packaged bekommt keine Toasts. Die beiden
Manifest-Erweiterungen fehlten und sind nachgetragen; der Fehler wanderte von
`0x80004005` auf `0x80070490`, und die Benachrichtigungsplattform registriert
nipp jetzt erfolgreich. Nötig war es also, ausreichend nicht. Details im
Nachtrag zu ADR-008. **Bis das geht, wird unpackaged ausgeliefert.**

### Von aussen abhängig

- **AP0.5 / AP9.2** Zertifikat — Beschaffung dauert Wochen, früh anstossen.
  **Seit dem 07.09.2026 abends der einzige echte Blocker vor einer Abgabe
  ausser Haus**: das Setup ist unsigniert, SmartScreen hält den ersten Start
  auf (T130). Intern einmal wegzuklicken, beim Kunden nicht
- ~~**AP9.3** Update-Prüfung~~ **erledigt am 07.09.2026** (ADR-039): GitHub
  Releases, Kanäle stable/beta, Prüfung beim Start. `docs/updates.md`
- ~~**AP9.6** Lizenz~~ **erledigt am 11.09.2026** (ADR-040): AGPLv3, der
  Quelltext wird offengelegt. Offen bleibt nur der Repo-Wechsel selbst —
  **das ist keine Lizenzfrage mehr, sondern Handarbeit** (`docs/plans/RELEASE-PLAN.md` R10)
- **AP7.8, T38, AP9.5** — brauchen x64-Hardware. **Es gibt keine** (ADR-001,
  entschieden am 07.09.2026): damit sind die vier nichtfunktionalen Ziele aus
  §2 ungemessen, das M1-Gate unter Emulation abgenommen und der Jitter-Befund
  aus ADR-006 ein unbestätigter Verdacht. Bewusst getragen, **fällig vor der
  ersten Kundenabgabe** — und es braucht kein eigenes Gerät, nur einmal Zugang
  zu einem

Dazu ein Test, den nur Dominic machen kann: eine Team-Nebenstelle anrufen
lassen und schauen, ob die Lampe auf „im Gespräch" springt („klingelt" kennt
das SDK über `presence` nicht, siehe docs/blf-pruefung.md).

### Vorgeschichte

Was wann passiert ist, und wo es vollständig steht. **Von unten nach oben
lesen, wenn man den Faden sucht.**

| Wann | Was | Wo |
|---|---|---|
| 13.09. | **Welle 1** — Fokus beim Klingeln, Ergebnis der Weiterleitung, Meldungen mit Handlung, Symbole und Farben, der Betriebsrand, die Textregeln | **docs/plans/REVIEW-2026-09-12.md** Welle 1, `docs/test-matrix.md` T263–T273 |
| 13.09. | **Welle 0 der Standortbestimmung** — die Ausnahmegrenze, der maskierte SIP-Trace, «der Benutzer gewinnt», der SIP-Port, die Klammer-Null | **docs/plans/REVIEW-2026-09-12.md**, **docs/plans/WELLE-0-PLAN.md**, **ADR-053**, **ADR-054**, `docs/test-matrix.md` T255–T262 |
| 12.09. | **Standortbestimmung** — fünf Achsen, 83 Befunde mit Datei und Zeile, 16 davon hoch, keiner blockierend | **docs/plans/REVIEW-2026-09-12.md** |
| 12.09. | **Die Breite gehört dem Fenster** — die Deckelung fällt, die linke Spalte wird fest, die Kacheln stehen endlich nebeneinander (`GroupStyle.Panel` war wirkungslos) | **ADR-052**, `docs/test-matrix.md` T251–T254 |
| 13.09. abends | **Zweites UX-Review** — was ein Fehlgriff kosten darf, der Alltag, die Struktur. Achtzehn von zwanzig Befunden umgesetzt | **docs/plans/UX-REVIEW-2.md**, **ADR-049** bis **ADR-051**, `docs/test-matrix.md` T222–T250 |
| 13.09. | **Das breite Fenster** — zwei Spalten ab 960 Pixeln, die Nebenstellen als Kacheln, der Detailbereich wieder in der Zeile; dazu ein maximiertes Fenster, das maximiert zurückkommt | **docs/plans/BREITBILD-PLAN.md**, **§23**, **ADR-047**, **ADR-048**, `docs/test-matrix.md` T211–T221 |
| 12.09. abends | **Die Bedienbarkeit, in drei Phasen** — ein Wort je Zustand und die Liste ohne Maus; ein Speichermodell statt fünf und ein Erststart mit Einstieg; zwei Ebenen in den Einstellungen, ein Suchfeld, ein Detailbereich, eine gedeckelte Breite | **docs/plans/UX-REVIEW.md**, **ADR-044** bis **ADR-046**, `docs/test-matrix.md` T185–T210 |
| 12.09. | **„Beenden" beendete nicht** (T134, Ursache gefunden), **Ziehen zwischen Gruppen** und der Detailbereich in der Zeile, **der Name bei ausgehenden Anrufen** | **ADR-042**, **ADR-043**, `docs/test-matrix.md` T175–T184 |
| 11.09. | **Der Quelltext wird offengelegt (B)** — AGPLv3, `LICENSE` und `NOTICE`; die beiden bv2-eigenen Anbieter verlassen den Quelltext und kommen als importierbare Vorlagendatei zurück. Echte Kundendaten aus Tests und Doku entfernt | **ADR-040**, `docs/licensing.md`, `docs/test-matrix.md` T169–T174 |
| 11.09. | **Team-Kontakte (C)** — Handynummer, Detailbereich unter der Liste, eigene Gruppen. Das meiste war gebaut und nicht angeschlossen; die Gruppen brachten die Invariante mit, ohne die ein Ziehvorgang eine Reihenfolge zurückschreibt, die niemand hergestellt hat | **ADR-041**, `docs/test-matrix.md` T160–T168 |
| 11.09. | **Toast-Farben (A) und der Karten-Designer (D)** — grün/rot mit Fähigkeitsprüfung; im Designer lag ein Knopf über der Palette und vier Knöpfe ausserhalb ihrer Spalte, das Löschen war unauffindbar statt kaputt, und die Vorschau rechnet jetzt mit einer eingegebenen Nummer | `docs/test-matrix.md` T154–T159, Plan vom 10.09.2026 |
| 10.09. | **Ein Anruf auf nipp warf den Benutzer aus dem Teams-Meeting** (Reports nur noch bei eigenem Anlass) und **der Rufton beim Wählen war der eigene statt der der Anlage** (blind entschieden auf einem Sekundenmittel) | ADR-028 **Nachtrag 4**, Nachtrag zu **ADR-029**, `docs/test-matrix.md` T143–T153 |
| 09.09. | **Eingehende Anrufe waren nicht annehmbar** — das Nachziehen des Gabelzustands erfand Tastendrücke und legte auf. Bedeutung der Taste jetzt am Anrufzustand, HID-Reports im Protokoll, T82 mit gelöst | Nachtrag zu **ADR-028**, `tools\Test-Headset.ps1` |
| 08.09. | Netzwechsel (entprellt und doch veraltet), der eigene Rufton am Gerät bestätigt, Absturz bei der Update-Prüfung (Ereignis vom falschen Thread) | `docs/lehren.md`, `docs/test-matrix.md` T135–T140 |
| 07.09. abends | **Installer und Updates** (R0–R8): Velopack statt MSIX, GitHub-Kanäle stable/beta, Prüfung beim Start | `docs/plans/RELEASE-PLAN.md`, `docs/updates.md` |
| 07.09. abends | **Anrufliste und Designer** (L0–L6): gesehen/ungesehen, die Karte in der Anrufliste, Abstand und Beschriftung | `docs/plans/ANRUFLISTE-PLAN.md` |
| 07.09. abends | ADR-001, ADR-004 und ADR-006 entschieden beziehungsweise nachgetragen; zwei standen drei Tage falsch auf „offen" | `docs/decisions.md` |
| 07.09. nachmittags | **Einrichtung und Karten** (K0–K6) | `docs/plans/EINRICHTUNG-PLAN.md` |
| 07.09. 14:10 | Der Weg zurück ins Fenster war zu — Doppelklick auf das Infobereich-Symbol öffnete nicht mehr. Zwei Ursachen, beide gemessen; **T96 bestanden** | `docs/lehren.md`, „Windows-Integration" |
| 07.09. mittags | Fünf Wünsche aus dem Alltag: Toast mit Anruferkontext, Rufton beim Wählen, wählbarer Klingelton, Info-Bereich, Logo beim Anheften | ADR-029 bis ADR-031, `docs/packaging.md` |
| 07.09. vormittags | Headset-Tasten und das Freizeichen. Auflegen bestätigt (T79) | §22.5, ADR-028 |
| 07.09. morgens | **T06 bestanden** — ein eingehender Anruf holt die Gesprächsansicht auch bei verstecktem Fenster. Damit ist der Blocker widerlegt, an dem seit dem 05.09. alles hing | `docs/test-matrix.md` |
| 06.09. abends | Beide Quellen angebunden und am Gerät bestätigt: Kontaktsuche, Anruferkontext, Karte | `docs/integrations/` |
| 06.09. | Gesamtprüfung der Anwendung. Der Befund: ein Blocker in acht Zeilen — `MainWindow` meldete bei jedem Klick aufs Fensterkreuz alle Abonnements ab, obwohl `App` das Fenster nur versteckt | **`docs/plans/GESAMT-REVIEW.md`** |
| 06.09. | Integrationsplattform **I0 bis I8**, dazu **I9 und I12 vorgezogen** — 50 Dateien unter `Services/Integrations/`: eigene Konfigurationsdatei mit Store und Validator, Geheimnisse über den bestehenden `SecretStore` mit Präfix `integration:`, HTTP mit Zeit- und Grössengrenze und Schutzschalter, JSONPath-Mapping, eine eingeschränkte Ausdruckssprache, Kontaktsuche über mehrere Quellen, Anruferkontext, Karten samt Feldkatalog, Anbietervorlagen samt Import. Verwaltung, Designer und Verteilung stehen in den Einstellungen. **Zwei Quellen laufen seit dem 06.09.2026 am Gerät** — ein CRM und ein Gesprächsjournal, mit echten Daten bestätigt (aus `CLAUDE.md` hierher gezogen am 13.09.2026: eine Regeldatei führt keinen Umsetzungsstand) | `docs/plans/INTEGRATION-PLAN.md` |
| 05.09. | Erster Tag im Alltag, sechs Befunde | `docs/plans/ALLTAG-PLAN.md` |
| 05.09. nachts | Review der Umsetzung, §7 fand noch acht Fehler | `docs/plans/REVIEW.md` |
| 04.09. | P0 bis P9, M1-Gate, SDK-Beschaffung | `docs/plans/IMPLEMENTATION-PLAN.md` |

**`ABNAHME-ALLTAG.md`** ist der Einstieg für einen Tag mit dem Gerät: was schon
bestanden hat, woran im Alltag zu denken ist, und was heute nicht funktioniert.
