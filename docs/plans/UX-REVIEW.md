# UX-Review — was nipp seine Benutzer kostet

**Datum:** 12.09.2026 · **Grundlage:** alle fünf XAML-Ansichten, die ViewModels
in `Nipp.Core`, und eine laufende Instanz · **Befunde:** 25

Die Telefonie ist in gutem Zustand. Ein Kollege ist mit einem Doppelklick
angerufen, ein verpasster Anruf mit einem zweiten zurückgerufen, die
Gesprächsansicht stellt „Auflegen" fest in die Kopfzeile und erklärt den
Unterschied zwischen „Sofort abgeben" und „Erst ankündigen" in der Beschriftung
statt im Jargon. Die Fehlertexte nennen Ursache **und** Abhilfe — das ist
selten und soll so bleiben.

**Die Reibung sitzt fast vollständig in den Einstellungen und an den Rändern
der Tastaturbedienung.**

> **Zum Umfang.** Die Projektregel „Kein Feature ohne Auftrag aus
> NIPP-BUILD.md" gilt hier mit. Die Empfehlungen ordnen um, benennen um, führen
> zusammen und schliessen an, was schon gebaut ist. Wo eine Empfehlung darüber
> hinausgeht, steht es ausdrücklich dabei und wurde als ADR festgehalten.

---

## Umsetzungsstand

| Phase | Inhalt | Stand |
|---|---|---|
| **1 — Die scharfen Kanten** | B4, B5, B9, B10, B11, B13, B22, B23, B25 | **umgesetzt** (ADR-044), T185–T193 |
| **2 — Der Weg hinein** | B1, B2, B3, B14, B17, B19, B24 | **umgesetzt** (ADR-045), T194–T201 |
| **3 — Die Struktur** | B6, B7, B8, B12, B15, B16, B18, B21 | **umgesetzt** (ADR-046), T202–T210 |

**B20 ist bewusst zurückgestellt** (siehe unten). **Am Gerät abgenommen ist
nichts davon** — T185 bis T210.

**Was sich dabei als zusätzlicher Befund ergeben hat:** `App.SdkStatus` wurde
beim Start gesetzt, als öffentliche Eigenschaft angeboten — und von **keiner
Ansicht gelesen**. Eine unvollständige Installation sah dadurch aus wie „noch
kein Konto eingerichtet". Dasselbe Muster wie bei `CardKind.History`,
`IntegrationConfig.cards` und `ClipResolver.DescribeCaller`, **zum vierten
Mal**.

**Und eine Korrektur an diesem Bericht selbst:** unter B24 standen anfänglich
die Präsenzdiagnose-Texte („RFC 4235", „siehe docs/decisions.md") als
Benutzertexte. Sie gehen in Wirklichkeit ins **Protokoll** — dort sind sie
richtig und bleiben unverändert.

---

## Die zehn wichtigsten Erkenntnisse

1. **Auf der Einstellungsseite galten fünf verschiedene Speicherregeln
   nebeneinander.** Ein Konto wurde beim Klick auf „Hinzufügen" sofort
   gespeichert; eine Team-Nebenstelle erschien beim selben Handgriff in der
   Liste und war beim Verlassen der Seite *lautlos weg*. Keine Anzeige für
   „ungespeichert", keine Rückfrage. Der einzige Weg, auf dem in nipp Arbeit
   verlorenging.
2. **Kein Hinweistext war anklickbar.** Null `InfoBar.ActionButton` im ganzen
   Programm. „Noch kein SIP-Konto eingerichtet. Unten auf Einstellungen tippen."
   beschrieb den Weg, statt ihn anzubieten — dasselbe bei Mailbox, Outlook und
   leerer Kontaktliste.
3. **Der Erststart hatte keinen Einstieg.** Neun zugeklappte Gruppen, keine
   Pflichtfeldmarkierung, ein grauer Knopf ohne Grund, und bei einem Tippfehler
   in der Domain zwölf Sekunden Wartezeit.
4. **Die Kontaktliste war ohne Maus nicht bedienbar.** Die Kontextmenüs hingen
   am Inhalt der Zeilenvorlage; Menütaste und Umschalt+F10 griffen ins Leere,
   Enter tat nichts. Der Kommentar im Code behauptete ausdrücklich das
   Gegenteil.
5. **Zwei Anzeigen setzten eine Tonfarbe als Hintergrund und liessen den Text
   erben.** Im dunklen Erscheinungsbild fast weisser Text auf `#FCE100` — rund
   1,4:1. Betroffen: der Verschlüsselungs-Chip und jedes Kartenabzeichen.
6. **Das Fenster wuchs, das Layout nicht.** Gebaut für 400 Pixel, ohne
   Obergrenze. Gemessen stand es auf **1023 Pixeln**: Name links aussen,
   Präsenz neunhundert Pixel weiter rechts.
7. **Die Einstellungen waren ein Administrationswerkzeug mit einem Benutzerteil
   darin** — 114 Eingabeelemente, 38 Schaltflächen, 31 Aufklappbereiche auf
   einer Seite. „Klingelton" stand gleichrangig neben „Keep-Alive (Sekunden,
   0 = aus)".
8. **Das Wechseln des Audiogeräts kostete fünf Klicks und ein Speichern** —
   der häufigste Einstellungswechsel eines Softphone-Benutzers.
9. **Enter im Weiterleitungsfeld gab das Gespräch sofort ab.** Die einzige
   unwiderrufliche Handlung des Programms lag auf der Standardtaste.
10. **Vier Wörter für einen Zustand.** „angemeldet" / „Angemeldet" /
    „registriert" / „Registrierung fehlgeschlagen". Der Gesprächszustand stand
    dreimal im Code, jedes Mal anders formuliert.

---

## Gesamteindruck vor der Umsetzung

| | Wert | Begründung |
|---|---|---|
| Verständlichkeit | 7/10 | Die Texte sind überdurchschnittlich gut. Abzug für uneinheitliche Begriffe und die technischen Brocken |
| Einfachheit | 4/10 | Die Hauptansicht ist knapp. Die Einstellungsseite trägt 114 Eingabeelemente ohne Ebenentrennung |
| Navigation | 7/10 | Vier Bereiche, sichtbar markiert, fester Rückweg ins Gespräch. Die Einstellungen fallen aus dem Muster |
| Visuelle Hierarchie | 6/10 | Bei 400 Pixeln stimmt sie. Breiter gezogen zerfällt jede Zeile in zwei weit auseinanderliegende Hälften |
| Effizienz | 5/10 | Anrufen und Weiterleiten sind kurz. Alles durch die Einstellungen ist lang |
| Konsistenz | 4/10 | Fünf Speicherregeln, vier Wörter für einen Zustand, zwei Suchfelder, zwei Auswahlverhalten |
| Fehlervermeidung | 5/10 | Die Bestätigungsdialoge sind vorbildlich. Daneben stiller Datenverlust und Enter auf der blinden Weiterleitung |
| Barrierefreiheit | 5/10 | Grundlagen sitzen. Gebrochen: der Tastaturweg zum Kontextmenü, der Fokus beim Annehmen, zwei Kontraste |

---

## Die Befunde

Nach Schadenshöhe. Die Kennungen laufen durch ADR-044 bis ADR-046 und durch die
Testmatrix.

| # | Befund | Prio | Aufwand | Phase |
|---|---|---|---|---|
| B1 | Fünf Speicherregeln auf einer Seite, ohne Anzeige und ohne Rückfrage | **P0** | M | 2 |
| B2 | Kein Hinweistext führt dorthin, wohin er verweist | P1 | S | 2 |
| B3 | Der Erststart hat keinen Einstieg | P1 | M | 2 |
| B4 | Kontaktliste ohne Maus nicht bedienbar | P1 | S | 1 |
| B5 | Text erbt seine Farbe auf getöntem Grund — 1,4:1 im Dunkeln | P1 | S | 1 |
| B6 | Das Fenster wächst, das Layout nicht | P1 | M | 3 |
| B7 | Die Einstellungen trennen Benutzer und Administrator nicht | P1 | L | 3 |
| B8 | Audiogerät wechseln: fünf Klicks für den häufigsten Wechsel | P1 | M | 3 |
| B9 | Enter gibt das Gespräch unwiderruflich ab | P1 | S | 1 |
| B10 | Beim Klingeln liegt der Fokus nicht auf „Annehmen" | P1 | S | 1 |
| B11 | Vier Wörter für „angemeldet", drei Wortschätze für einen Gesprächszustand | P2 | S | 1 |
| B12 | Zwei Suchfelder für überlappende Bestände | P2 | M | 3 |
| B13 | „Team" bezeichnet zwei verschiedene Dinge übereinander | P2 | S | 1 |
| B14 | Ausgegraute Schaltflächen ohne Begründung | P2 | S | 2 |
| B15 | Die Sprachausgabe verliert Präsenz, Ergebnis und Uhrzeit | P2 | S | 3 |
| B16 | Keine Tastenkürzel innerhalb der Anwendung | P2 | S | 3 |
| B17 | Eine unvollständige Installation sieht aus wie „kein Konto" | P2 | S | 2 |
| B18 | Zwei gleich aussehende Listen, zwei Auswahlverhalten | P2 | M | 3 |
| B19 | Der Katalog öffnet einen Dialog für eine einzige Wahl | P2 | S | 2 |
| B20 | Endpunkte werden als rohes JSON getippt | P2 | L | — |
| B21 | Klickziele unter 32 Pixeln, Tastaturbuchstaben mit 8 Pixeln | P3 | S | 3 |
| B22 | Der Pfeil im Nummernfeld sieht aus wie ein Aufklappmenü | P3 | S | 1 |
| B23 | Uneinheitliche Standardschaltfläche in destruktiven Dialogen | P3 | S | 1 |
| B24 | Technische Innereien in Benutzertexten | P2 | M | 2 |
| B25 | Ersatzumlaute, drei Anführungszeichenarten, ein falscher Plural | P3 | S | 1 |

**B20 ist bewusst zurückgestellt.** Eine geführte Feldzuordnung statt des
JSON-Textfelds lohnt erst, wenn jemand tatsächlich eine Quelle ohne Vorlage
anbinden soll. Solange Vorlagen importiert werden, trägt die kurze Fassung: das
Feld steht unter „Für Administratoren" und sagt darüber, dass eine
Anbietervorlage diese Angaben normalerweise mitbringt.

---

## Abläufe, vorher und nachher

### Vom ersten Start zum ersten Anruf

**Vorher** — Fenster mit zwei Hinweisen → Einstellungen → richtige Gruppe raten
→ aufklappen → vier Felder, Pflicht unklar → grauer Knopf → 12 s warten →
Zurück → wählen.

**Nachher** — Fenster mit einem Hinweis **samt Schaltfläche** → Formular offen,
Fokus im ersten Feld → drei markierte Pflichtfelder mit Sofortprüfung →
Hinzufügen → wählen.

*Entfallen: das Raten der Gruppe, das Aufklappen, das Zurückgehen. Die
Zwölf-Sekunden-Wartezeit bleibt für echte Netzprobleme, nicht mehr für
Tippfehler.*

### Eine Team-Nebenstelle anlegen

**Vorher** — Einstellungen → Kontakte aufklappen → scrollen → Name und
Nebenstelle → Hinzufügen → **Speichern, sonst verloren** → Zurück.

**Nachher** — Einstellungen → Kontakte → Name und Nebenstelle → Hinzufügen
(wirkt sofort) → Zurück.

*Ein Schritt weniger — und, wichtiger, der Schritt, dessen Vergessen die Arbeit
kostete.*

### Das Audiogerät wechseln

**Vorher** — Einstellungen → Audio aufklappen → drei Listen → Speichern →
Zurück.

**Nachher** — Lautsprechersymbol in der Gesprächsansicht → Gerät wählen. Oder
über das Infobereich-Symbol, ohne das Fenster zu öffnen.

*Der lange Weg bleibt für die Erstkonfiguration, wirkt dort aber sofort.*

### Einen Kunden suchen und anrufen

**Vorher** — ins Nummernfeld tippen → kein Treffer → merken, dass es ein
zweites Feld gibt → unten nochmals tippen → Treffer → Doppelklick.

**Nachher** — ins Nummernfeld tippen → lokale Treffer sofort, Quellentreffer
nach 300 ms darunter → Eingabe.

*Ein Feld statt zwei. Die Zusage „kein Netz im Tippweg" bleibt, weil die
lokalen Treffer unverändert sofort erscheinen.*

### Ein Gespräch weiterverbinden

**Vorher** — Weiterleiten → Namen tippen → Vorschlag mit Präsenzlampe → Sofort
abgeben / Erst ankündigen. **Enter gab sofort ab.**

**Nachher** — derselbe Weg; Enter übernimmt den Vorschlag und setzt den Fokus
auf „Sofort abgeben". Das zweite Enter führt aus.

*Kein Schritt weniger, aber ein irreversibler Fehlgriff weniger. Die
Präsenzlampe in der Vorschlagsliste ist die beste einzelne Entscheidung dieser
Ansicht — sie beantwortet die eigentliche Frage, bevor sie gestellt wird.*

### Eine Datenquelle anbinden

**Vorher** — Einstellungen → Integrationen → Dialog mit einem Eintrag → Token
ablegen → Verbindung übernehmen → **JSON tippen** → Testabruf → einschalten.

**Nachher** — Vorlage importieren → Quelle entsteht direkt → Token ablegen →
Testabruf → einschalten.

*Der JSON-Schritt entfällt für alle, die eine Vorlage haben — also für den
Normalfall, seit ADR-040 die Anbieter aus dem Quelltext genommen hat. Die
erzwungene Reihenfolge (angelegt → aus → getestet → ein) bleibt; sie ist
richtig.*

---

## Was entfernt wurde

- **Die Schaltfläche „Speichern" in den Einstellungen.** Mit einem
  einheitlichen Speichermodell überflüssig — und solange es sie gab, war sie
  die Ursache von B1.
- **Das zweite Suchfeld unter der Kontaktliste.** Aufgelöst in die
  Vorschlagsliste des Nummernfelds.
- **Der Auswahldialog bei genau einer Vorlage.** Ein Dialog, der eine Wahl
  zwischen einer Möglichkeit anbietet, ist ein Zwischenschritt ohne
  Entscheidung.
- **Die Gruppe „Erweitert".** Kein Thema, sondern ein Restehaufen: Länderpräfix
  und Aufnahmeordner gehören zum Benutzer, Protokollierung und Provisioning zum
  Administrator.
- **Der zweite Detailbereich in der Team-Zeile.** Ein Ort für beide Listen
  genügt.
- **Die Kontoauswahl bei genau einem Konto.** Eine Auswahlliste mit einem
  Eintrag ist keine Wahl.

**Ausdrücklich nicht entfernt:** die Bestätigungsdialoge. Sie sind ausführlich,
und genau das ist richtig — jeder nennt die Folge *und* die Gegenprobe. Ebenso
bleibt die erzwungene Reihenfolge beim Einrichten einer Quelle: sie verhindert,
dass eine ungeprüfte Quelle beim ersten Anruf eine fremde Adresse befragt.

---

## Zielstruktur der Einstellungen

| Ebene | Gruppe | Inhalt |
|---|---|---|
| **Benutzer** | Konto | Konten, Anmeldezustand, Mailboxnummer |
| | Audio | Mikrofon, Lautsprecher, Klingelgerät, Klingelton, Echo- und Rauschunterdrückung |
| | Darstellung | hell/dunkel/wie Windows, immer im Vordergrund |
| | Kontakte | Outlook, Besetztlampenfeld, Gruppen, Nebenstellen |
| | Start und Bedienung | Autostart, minimiert starten, automatisch annehmen, Tastenkürzel, Länderpräfix, Aufnahmeordner, Aufbewahrung |
| **Integrationen** | Quellen | Liste, Vorlage importieren, Token, Testabruf, wann nachgeschlagen wird |
| | Anruferkarte | die vier Karten, Designer |
| **Administrator** | Netzwerk und Verschlüsselung | SIP-Port, Zertifikat, Keep-Alive, STUN, ICE, SRTP, Video |
| *ein Aufklapper* | Codecs | Reihenfolge, DTMF-Verfahren |
| | Provisioning und Diagnose | Adresse, Abruf, Protokollierung, Log-Ordner, Diagnosepaket |
| | Sichern und zurücksetzen | Ausgeben, Einlesen, Zurücksetzen, Endpunkte-JSON der Quellen |
| **Fuss** | Über nipp | Fassung, Aktualisierung, Kontakt |

Die Aktualisierung wandert von der Mitte in den Fuss, zum Versionsblock —
dorthin, wo man sie sucht.

---

## Eine Einschränkung

Dieser Bericht beruht auf dem Quelltext, auf einer laufenden Instanz und auf
einer Einzelmessung des Fensters — **nicht auf Beobachtung echter Benutzer.**
Die Rangfolge ist nach Schadenshöhe geschätzt.

Was sie belastbar machen würde: einem Kollegen ohne Vorwissen fünfzehn Minuten
lang zusehen, wie er ein Konto einrichtet, eine Nebenstelle anlegt und sein
Headset wechselt. Die drei Aufgaben decken B1, B2, B3 und B8 gemeinsam ab.

**Und der grösste offene Posten bleibt derselbe wie vorher:** am Gerät
abgenommen ist von alledem nichts. T185 bis T210.
