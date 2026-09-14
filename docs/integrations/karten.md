# Karten zusammenstellen

**Stand:** 07.09.2026 · Bezug: `NIPP-BUILD.md` §21.6, ADR-032, ADR-034,
ADR-036, ADR-037

Welche Werte wo stehen — im Gespräch, beim eingehenden Anruf, im aufgeklappten
Eintrag der Anrufliste und in der Benachrichtigung.

---

## Wo es losgeht

**Einstellungen → Integrationen → Anruferkarte.** Dort stehen vier Zeilen, und
jede öffnet mit „Bearbeiten …" den Designer:

| Art | Wo sie erscheint | Was sie tragen soll |
|---|---|---|
| **Gespräch** | im laufenden Gespräch, unter Name und Dauer | ausführlich — hier wird gelesen, während gesprochen wird |
| **Eingehender Anruf** | im Moment des Klingelns | kurz. Wer klingelt, soll auf einen Blick sehen, wer das ist |
| **Anrufliste** | im aufgeklappten Eintrag, unter der Liste | was zur Nummer bekannt ist. Name, Uhrzeit und Ergebnis stehen schon im Kopf darüber |
| **Benachrichtigung** | der Windows-Toast | genau drei Textzeilen plus die Nummer klein darunter |

**Beim Eintrag der Anrufliste gehört der Kopf nicht zur Karte** (ADR-036): Name,
Uhrzeit, Ergebnis und das Kreuz zum Schliessen stehen fest. Sie sagen, *welcher*
Eintrag offen ist — der Bereich steht unter der Liste und nicht in der Zeile, und
ohne diese Angabe stünden darunter Werte ohne Bezug.

**Der Designer ist ein eigenes Fenster.** Das nipp-Fenster ist rund 400 Pixel
breit (§20.1) und trägt keine Felderpalette. Die **Vorschau** im Designer ist
trotzdem genau 400 Pixel breit — sonst wäre sie keine.

---

## Der Aufbau einer Karte

Eine Karte besteht aus **Zeilen**, eine Zeile aus einer oder zwei **Spalten**,
und eine Spalte aus **Bausteinen** von oben nach unten.

```
┌─ Karte (400 px) ──────────────────────────┐
│ Hans Muster                    ← Zeile 1  │
│ Muster AG                      ← Zeile 2  │
├─────────────────┬─────────────────────────┤
│ Kundennr. 4711  │ Offene Aufträge 3       │  ← Zeile 3, zwei Spalten
├─────────────────┴─────────────────────────┤
│ Letztes Gespräch  Rückruf vereinbart      │  ← Zeile 4
└───────────────────────────────────────────┘
```

**Zwei Spalten und nicht mehr.** Das Raster hat sechs Einheiten; eine dritte
Spalte wäre gut sechzig Pixel breit und zeigte nichts. Im Designer heisst der
Schalter dafür „Nebeneinander" und wirkt auf die Zeile des ausgewählten
Bausteins.

### Die Bausteine

| Baustein | Wofür | Im Toast |
|---|---|---|
| **Text** | eine Zeile ohne Beschriftung — Überschrift, Unterzeile, Kleingedrucktes | ja, das ist dort der einzige |
| **Feld** | Beschriftung und Wert, die häufigste Zeile | nein |
| **Abzeichen** | ein kurzes Merkmal wie „VIP", mit Farbton | nein |
| **Trennlinie** | ein Strich zwischen zwei Bereichen | nein |
| **Abstand** | Luft statt Strich, in drei Grössen | nein |
| **Schaltfläche** | öffnet eine Adresse im Browser | nein |
| **Verweis** | dasselbe, als Link dargestellt | nein |
| **Quellenzustand** | „CRM antwortet nicht" | nein |

Die Menge ist **geschlossen** (§21.2). Was hier nicht steht, lässt sich über
eine Konfigurationsdatei nicht erzeugen — freies Markup aus einer verteilten
Datei wäre eine Ausführungsfläche im Programm.

**Der Abstand hat drei Grössen — klein, mittel, gross — und keine Pixelzahl**
(ADR-037). Eine Karte beschreibt Layout über Struktur und nicht über
Bildschirmmasse; was „mittel" heisst, entscheidet die Oberfläche. Für eine
sichtbare Trennung nimmt man die Linie, für eine ruhige den Abstand.

### Die Beschriftung eines Feldes ausblenden

Im Eigenschaftenbereich rechts steht bei einem Feld der Haken **„Beschriftung
zeigen"**. Abgeschaltet steht nur der Wert da, ohne Einzug:

```
Beschriftung an:   Name          Hans Muster
Beschriftung aus:  Hans Muster
```

**Die Beschriftung bleibt trotzdem gefüllt** und wird auch gebraucht: eine
Sprachausgabe liest weiterhin „Name: Hans Muster". Wer sie leert, statt den
Haken zu lösen, nimmt ihr das (§8.4).

### Von einer anderen Karte übernehmen

Der Knopf links unten holt den **Aufbau** einer anderen Kartenart in die
bearbeitete — etwa die Gesprächskarte in die Anrufliste. Kennung, Name und Art
bleiben die eigenen; übernommen werden die Abschnitte.

**Gekürzt wird dabei nichts.** Wer die Gesprächskarte in die Benachrichtigung
übernimmt, bekommt mehr, als dort erscheinen kann — das steht dann als Befund
unten, und Speichern ist gesperrt, bis die überzähligen Bausteine weg sind.
Welcher fällt, entscheidet nicht nipp.

Ein Übernehmen ist **ein** Schritt: Strg+Z holt den vorigen Stand zurück.

---

## Woher ein Wert kommt

Das ist die eigentliche Arbeit. Der Designer bietet fünf Formen an:

### Feld

Ein bestimmtes Feld einer bestimmten Quelle: `crm.contactName`. Aus der
Palette links auswählen und „Ausgewähltes Feld einfügen" drücken (oder
doppelt anklicken).

**Wann:** wenn die Karte für **diesen** Betrieb ist und die Quelle so heissen
wird.

Zwei Gruppen gibt es **immer**, auch ohne eine einzige eingerichtete Quelle:
`number.*` (die Rufnummer in ihren Formen) und `call.*` — Zeitpunkt, Datum,
Uhrzeit, Dauer, Ergebnis und Richtung des Anrufs. Auf der Karte **im Gespräch**
sind Dauer und Ergebnis leer, weil es sie erst danach gibt; auf der Karte in
der **Anrufliste** sind sie der Grund, warum jemand den Eintrag geöffnet hat.

### Bedeutung

`role('name')` — **die erste Quelle nach Reihenfolge, die ein Feld dieser
Bedeutung liefert.** Sechs Bedeutungen: `name`, `company`, `type`, `work`,
`summary`, `colleague`.

**Wann:** fast immer. Eine Karte mit Bedeutungen trägt bei jedem Kunden, ohne
angefasst zu werden — deshalb benutzen die mitgelieferten Karten
ausschliesslich das. Welcher Feldname welche Bedeutung hat, steht in
[feldnamen.md](feldnamen.md).

### Erster Treffer aus

`coalesce(a, b, c)` — der erste Wert, der etwas enthält. Im Designer eine Liste
von Feldern, mit Komma getrennt.

**Wann:** für eine Rückfallkette. Die Überschrift der mitgelieferten
Gesprächskarte ist genau das:

```
coalesce(role('name'), contacts.displayName, formatPhone(number.e164))
```

Die letzte Stufe greift immer — die Karte hat nie eine leere Überschrift.

### Fester Text

`'VIP'`. Für Abzeichen und für Zeilen, die immer dasselbe sagen.

### Ausdruck

Alles Übrige, in der Ausdruckssprache aus §21.3:
`if(erp.openOrders > 5, 'viel offen', '')`.

**Der Designer verändert einen Ausdruck nicht.** Was er nicht in eine der vier
Formen oben zerlegen kann, zeigt er als Ausdruck und gibt es **unverändert**
weiter. Eine von Hand geschriebene Karte lässt sich damit öffnen und speichern,
ohne dass etwas verlorengeht.

---

## Sichtbarkeit

Je Baustein drei Möglichkeiten:

- **Immer.**
- **Nur wenn ein Wert da ist** — die häufigste Wahl. Erzeugt
  `!isEmpty(<eigener Wert>)`.
- **Nach eigener Bedingung** — ein Ausdruck, etwa `crm.vip == true`.

Dazu bei einem **Feld** die Frage, was bei leerem Wert dasteht:

| Einstellung | Wirkung |
|---|---|
| Häkchen **aus** | die Zeile **verschwindet** |
| Häkchen **an** | die Zeile bleibt und zeigt „—" |

Beides ist sinnvoll, und der Unterschied gehört Ihnen: „Offene Aufträge: —"
sagt, dass gefragt wurde; eine fehlende Zeile hält die Karte kurz.

---

## Die Vorschau

Sie zeigt **Werte, nicht Ausdrücke** — und sie kommen durch dasselbe Mapping
wie im Gespräch. Der Fall, für den das zählt, ist der häufigste beim
Einrichten: der Feldpfad ist falsch, und die Zeile bleibt leer. Das soll **im
Designer** zu sehen sein und nicht erst am Telefon.

Darunter steht, woher die Daten kommen:

| Text | Bedeutung |
|---|---|
| „Vorschau mit erfundenen Beispieldaten" | noch kein Testabruf auf diesem Gerät. Die Zeilen sind erfunden — **richten Sie die Karte nicht darauf aus** |
| „Vorschau mit der Antwort des letzten Testabrufs" | Ihre eigenen Daten. So wird es aussehen |
| „Keine Quelle eingerichtet" | es gibt nur, was aus dem Anruf selbst kommt: die Nummer |

**Für die eigenen Felder:** Einstellungen → Integrationen → Quelle auswählen →
Verbindung testen. Die Antwort bleibt nur im Arbeitsspeicher (§21.2) — nach
einem Neustart von nipp zeigt die Vorschau wieder Beispieldaten.

---

## Die Benachrichtigung ist besonders

**Windows nimmt drei Textzeilen.** Plus die Attributionszeile, die klein und
grau darunter steht. Eine vierte ist nicht abgeschnitten, sondern **weg** — der
Designer meldet sie, bevor gespeichert werden kann.

**Und ohne eigene Karte gilt eine Zusammensetzung, die eine Karte nicht
nachbauen kann** (ADR-034): Zeile 1 ist dort `Name · Firma (Art)`, jeder Teil
einzeln weglassbar und die Firma unterdrückt, wenn sie dasselbe sagt wie der
Name. Wer den Toast selbst zusammenstellt, bekommt drei **einfache** Zeilen und
gibt diese Feinheiten auf.

Der Designer fängt deshalb mit drei lesbaren Zeilen an — `role('name')`,
`role('work')`, `role('summary')` — und nicht mit einer Kopie der
Zusammensetzung, die dort nur als unleserlicher Ausdruck stehen könnte.

**Was in einer Benachrichtigung steht, bleibt im Windows-Benachrichtigungs­center
liegen**, bis jemand es wegklickt. Bei Gesprächsinhalten ist das eine
Entscheidung, keine Anzeige (ADR-027, ADR-030). nipp entfernt den Toast beim
Anrufende.

---

## Speichern, zurücksetzen, verwerfen

| Knopf | Was passiert |
|---|---|
| **Speichern** | die Karte gilt ab sofort und ersetzt die vorhandene ihrer Art. Gesperrt, solange ein Befund offen ist |
| **Verwerfen** | zurück zum Stand beim Öffnen, Fenster schliesst |
| **Auf mitgelieferte Karte zurücksetzen** | **entfernt** die eigene Karte. Danach gilt wieder die von nipp — und die kommt bei einer Verbesserung von nipp mit. Deshalb entfernen und nicht kopieren |

**Rückgängig** (die beiden Knöpfe oben rechts) nimmt die strukturellen Schritte
zurück: Einfügen, Entfernen, Verschieben, Teilen. Fünfzig Schritte tief. Das
Tippen in einem Eigenschaftenfeld nicht — ein Rückgängig je Buchstabe wäre
unbedienbar.

---

## Weitergeben

Karten stehen in `integrations.json` und gehen mit **Ausgeben** und
**Einlesen** mit — Einstellungen → Integrationen, unten. Eine ausgegebene
Datei enthält keine Geheimnisse, nur Verweise darauf (§21.2).

Für die Verteilung an mehrere Arbeitsplätze gilt der Weg aus
[einrichten.md](einrichten.md), Abschnitt „Verteilen": dieselbe Datei über das
Provisionierungsprofil.

---

## Wenn etwas nicht so aussieht wie gedacht

| Beobachtung | Ursache | Abhilfe |
|---|---|---|
| Eine Zeile fehlt ganz | ihr Wert ist leer und das Häkchen für „—" ist aus | Häkchen setzen, oder in der Vorschau prüfen, ob der Wert ankommt |
| Alles leer, nur die Nummer steht da | die Karte nennt Quellen, die es hier nicht gibt | `role(...)` statt `quelle.feld` |
| Beschriftung heisst „Letzte arbeit zeile" | der Feldname steht nicht im Katalog | Beschriftung von Hand setzen, oder einen katalogisierten Namen mappen |
| Der Toast zeigt nur die Nummer | das Namensfeld hat keine Bedeutung | Feld in `contactName` umbenennen — siehe [feldnamen.md](feldnamen.md) |
| Speichern ist grau | ein Befund steht unten im Fenster | er nennt die Stelle und was zu tun ist |
| Nach dem Speichern gilt die alte Karte | die neue hat einen Fehler und wurde abgewiesen | in den Einstellungen unter „Die Konfiguration hat Befunde" nachsehen |
| Zwei Werte nebeneinander werden zu einem | die Zeile hatte schon zwei Spalten; „Nebeneinander" führt sie dann zusammen | noch einmal drücken |
