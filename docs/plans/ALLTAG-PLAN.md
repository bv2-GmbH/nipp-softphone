# Plan: sechs Befunde aus dem Alltag

**Aufgenommen am 06.09.2026**, nachdem beide Integrationen liefen und nipp
zum ersten Mal einen ganzen Nachmittag im Alltag benutzt wurde.

Alle sechs sind **am Code und an der laufenden Anwendung nachgeprüft**, nicht
aus der Beschreibung abgeleitet. Wo unten „Ursache" steht, ist sie belegt.

**Alle sechs sind abgearbeitet.** Der Stand vom 07.09.2026:

| # | Befund | Zustand |
|---|---|---|
| **1** | Kein Symbol im Infobereich | **behoben** 06.09. (Handle überlebte sein Objekt nicht) |
| **2a** | Kein Hinweis, dass Outlook fehlt | **behoben** 06.09. nachts, am Gerät bestätigt |
| **2b** | Outlook-Kontakte fehlen ganz | **entschieden:** COM bleibt, Graph zurückgestellt (ADR-018) |
| **6** | Nur die geschäftliche Nummer sichtbar | **behoben** 06.09. |
| **3** | Kontaktliste beim Weiterleiten | **gebaut** 06.09. nachts (§22.1, ADR-023) |
| **4** | Fünf zuletzt gewählte Nummern | **gebaut** 06.09. nachts (§22.2, ADR-024), auf Wunsch über einen Pfeil im Feld statt beim Fokus |
| **5** | CRM und Gesprächsjournal im Anrufjournal | **gebaut** 06.09. nachts (§22.3, ADR-027), Weg A — bei Bedarf abrufen, nichts speichern |

Was bleibt, ist die **Abnahme am Gerät**: T60 bis T65 aus diesem Dokument sowie
T66 bis T77 aus der Gesamtprüfung stehen in `docs/test-matrix.md` und sind
ungeprüft. Der Rest dieses Dokuments ist die Vorgeschichte — die Analyse, die zu
den Entscheidungen geführt hat. Sie steht weiterhin hier, weil die Begründungen
in den ADRs auf sie verweisen.

**Ursprünglich empfohlene Reihenfolge:** 1 → 6 → 2a → 4 → 3 → 5, und 2b als
Entscheidung daneben. So ist es auch gelaufen.

---

## 1. Kein Symbol im Infobereich

**Ursache gefunden** — `src/Nipp.App/Windows/TrayIconHost.cs:157`:

```csharp
private nint LoadIcon()
{
    using var icon = new System.Drawing.Icon(path);
    return icon.Handle;          // <- das Handle ist beim Return schon tot
}
```

Das `using` ruft `Dispose()` beim Verlassen der Methode, und
`System.Drawing.Icon.Dispose` gibt das native Handle frei (`DestroyIcon`).
Zurück kommt eine Zahl, die auf nichts mehr zeigt. Der Infobereich bekommt
ein ungültiges Handle und zeichnet einen leeren Platz — **genau das
beobachtete Bild: nipp ist da, ohne Symbol.**

Die Symboldateien sind in Ordnung; `TrayLight.ico`, `TrayDark.ico` und
`AppIcon.ico` liegen im Ausgabeverzeichnis. Das Protokoll meldet
folgerichtig „Symbol im Infobereich angelegt" — aus Sicht von nipp ist alles
gutgegangen.

**Warum es zeitweise trotzdem sichtbar war:** ein freigegebenes Handle wird
von Windows nicht sofort ungültig. Solange der Speicher nicht neu vergeben
ist, zeichnet das Symbol weiter. Das erklärt, warum der Fehler nicht bei
jedem Start gleich aussieht.

### Vorgehen

Das `Icon` am Leben halten, solange das Symbol lebt:

```csharp
private System.Drawing.Icon? _iconResource;

private nint LoadIcon()
{
    var neu = new System.Drawing.Icon(path);
    var alt = _iconResource;
    _iconResource = neu;
    alt?.Dispose();              // erst nach dem Wechsel, nie vorher
    return neu.Handle;
}
```

In `Dispose()` mit freigeben. Die Reihenfolge ist wichtig: beim Themenwechsel
darf das alte Symbol erst weg, wenn `UpdateIcon` das neue übernommen hat,
sonst blitzt derselbe Fehler kurz auf.

**Prüfen:** neuer Testfall **T58** — Symbol im Infobereich sichtbar, nach
einem Themenwechsel immer noch, und nach einer Stunde Laufzeit auch. Der
letzte Teil ist der eigentliche: er unterscheidet ein gültiges Handle von
einem, das noch nicht überschrieben wurde.

---

## 2. Outlook-Kontakte fehlen

Zwei getrennte Sachen, die zusammen auftreten und getrennt gehören.

### Was tatsächlich los ist

Das Protokoll ist eindeutig:

```
[INF] OutlookContactSource  Outlook laeuft nicht — persoenliche Kontakte
                            bleiben aus. nipp startet Outlook absichtlich
                            nicht selbst (Paragraph 8.4).
[INF] ContactStore          Kontakte geladen: 10
```

Die zehn sind die Team-Nebenstellen. Und nipp hat recht: **es läuft kein
klassisches Outlook.** Auf dem Gerät läuft

```
olk.exe   Microsoft.OutlookForWindows_1.2026.818.100_arm64
```

— das **neue Outlook**. Das ist eine gänzlich andere Anwendung: eine
WinUI-App um den Web-Client herum, **ohne COM-Automatisierung**. Es gibt kein
`Outlook.Application`, das man ansprechen könnte, und keinen
ROT-Eintrag. Der Weg aus §8.4 ist hier nicht kaputt, er ist nicht vorhanden.

**Das ist keine Regression.** Die COM-Korrektur aus REVIEW.md R1
(`CLSIDFromProgID` vor `GetActiveObject`) ist richtig und greift — sie kann
nur nichts finden, wo nichts registriert ist.

### 2a. Der fehlende Hinweis — ein Fehler, heute behebbar

§8.4 verlangt ausdrücklich: *„Outlook muss laufen (sonst **verständliche
Anzeige** statt leerer Liste)"*.

Diese Anzeige gibt es **nur im Protokoll**. In der Oberfläche fehlt der
Abschnitt einfach, und es sieht aus, als sei nipp defekt — genau der
Eindruck, den §8.4 verhindern wollte.

**Vorgehen:** im Kontakte-Tab an der Stelle des Outlook-Abschnitts eine
Zeile, wenn die Quelle nichts liefern konnte, mit dem Grund und dem, was zu
tun ist. Der `ContactStore` kennt den Zustand bereits; er muss ihn nur nach
oben geben (heute endet er im Log).

Zwei Gründe sind zu unterscheiden, weil die Abhilfe verschieden ist:

| Zustand | Anzeige |
|---|---|
| Klassisches Outlook läuft nicht | „Outlook ist nicht geöffnet. nipp startet es nicht selbst — Outlook öffnen und den Tab neu betreten." |
| Nur das neue Outlook gefunden | „Das neue Outlook stellt keine Kontakte für andere Programme bereit. Siehe Einstellungen → Kontakte." |

Den zweiten Fall kann nipp erkennen: läuft ein Prozess `olk` und kein
`OUTLOOK`, ist die Lage eindeutig. Ohne diese Unterscheidung schickt der
erste Text jemanden auf die Suche nach einem Outlook, das er offen hat.

### 2b. Der eigentliche Punkt — eine Entscheidung, kein Programmierauftrag

**ADR-009 ist in seiner Begründung überholt.** Er wählte COM statt Graph:

> die Entra-App-Registrierung lohnt den Aufwand nicht, solange nipp intern
> läuft … Graph bleibt nachrüstbar, wird aber nicht vorgebaut.

Die Annahme dahinter war, dass auf jedem Arbeitsplatz ein klassisches
Outlook läuft. Auf diesem Gerät trifft sie nicht zu, und das neue Outlook
wird von Microsoft als Nachfolger ausgerollt. Die Frage ist also nicht *ob*,
sondern *wann* das jeden Arbeitsplatz betrifft.

Drei Wege, mit ehrlichen Kosten:

| | Weg | Aufwand | Was es kostet |
|---|---|---|---|
| **A** | **Microsoft Graph**, nativ in `OutlookContactSource` | 3–5 Tage | Entra-App-Registrierung, Zustimmung eines Administrators, OAuth2 mit Token-Erneuerung. Funktioniert unabhängig davon, welches Outlook läuft — **und ob überhaupt eines läuft** |
| **B** | **Graph über die Integrationsplattform** | 4–6 Tage | Wie A, plus: der Plattform fehlt OAuth2. Sie kennt heute `none`, `apiKey`, `bearer`, `basic` — ein Graph-Token läuft nach einer Stunde ab und braucht einen Erneuerungsfluss. Dafür wäre es danach **für jede OAuth2-Quelle** da |
| **C** | **Klassisches Outlook voraussetzen** | ~0 | Kostet nichts und löst nichts. Als Übergang vertretbar, wenn 2a steht |

**Empfehlung: erst 2a, dann entscheiden.** Mit einem verständlichen Hinweis
ist die Lage nicht mehr peinlich, nur unvollständig — und die Entscheidung
zwischen A und B lässt sich in Ruhe treffen, statt unter dem Druck einer
Oberfläche, die kaputt aussieht.

**Wenn entschieden wird, halte ich B für richtig**, obwohl es teurer ist.
OAuth2 fehlt der Plattform ohnehin, und sie wurde als *generische* Anbindung
beauftragt (§21). Die erste OAuth2-Quelle zahlt den Aufbau, jede weitere
bekommt ihn geschenkt. Wer A nimmt, baut denselben Erneuerungsfluss später
ein zweites Mal.

**In jedem Fall ein ADR** — ADR-018, der ADR-009 ablöst und den Befund zum
neuen Outlook festhält. Sonst wählt der nächste Durchgang wieder COM, mit
derselben Begründung, die heute nicht mehr trägt.

---

**Entschieden am 06.09.2026 abends: Weg C — es bleibt bei COM.** Graph wurde
erwogen und zurückgestellt; die Kosten der Entra-Registrierung wiegen schwerer
als der Nutzen, solange andere Dinge drücken. Auf einem Arbeitsplatz mit dem
neuen Outlook fehlen die persönlichen Kontakte damit weiterhin — das ist der
bewusst in Kauf genommene Preis, und 2a sorgt dafür, dass er benannt wird
statt wie ein Defekt auszusehen. Vollständig in ADR-018; der Auslöser für eine
Neubewertung steht dort.

---

## 6. Nur die geschäftliche Nummer wird angezeigt

**Die Vermutung stimmt, und die Ursache liegt nicht dort, wo man sie sucht.**

Das Mapping ist in Ordnung. `crm.json` bildet beide Nummern ab:

```json
"phoneBusiness": { "path": "$.fixnet_number" },
"phoneMobile":   { "path": "$.mobile_number" }
```

`HttpContactSearchProvider.ReadNumbers` sammelt jedes Feld, das mit `phone`
beginnt — beide kommen an, alphabetisch sortiert, `phoneBusiness` zuerst.
**Im Modell sind beide Nummern vorhanden.**

Verloren gehen sie eine Ebene höher, in `ContactRow`
(`src/Nipp.Core/ViewModels/ContactRow.cs:47,58`):

```csharp
var number = PhoneNumberFormat.ForDisplay(Contact.PrimaryNumber);
public string? Number => Contact.PrimaryNumber;
```

Und `PrimaryNumber` ist definiert als `Numbers[0]` — die erste, also die
geschäftliche. Die Zeile zeigt nur sie, **und ein Klick wählt nur sie.** Die
Mobilnummer ist da und unerreichbar.

### Vorgehen

Die Zeile bleibt einzeilig — mehrere Nummern untereinander sprengen die
Liste, und §20 hält die Oberfläche bewusst schmal. Stattdessen:

1. **`ContactRow` bekommt `Numbers`** und eine Angabe, ob es mehr als eine
   gibt.
2. **Bei mehreren Nummern** trägt die Zeile ein Zeichen dafür (etwa „+1"),
   und ein Klick öffnet eine kleine Auswahl mit Art und Nummer — geschäftlich,
   mobil, privat — statt sofort zu wählen.
3. **Bei genau einer Nummer** bleibt alles wie heute: ein Klick wählt.

Punkt 3 ist die Bedingung dafür, dass die Änderung niemanden ausbremst. Der
häufige Fall darf keinen zusätzlichen Klick bekommen.

**Prüfen:** `0443954016` (Alexander Ruoss) hat Festnetz **und** mobil,
`0713142250` (4net AG) nur Festnetz. Beide Fälle mit einem Testfall **T59**.

---

## 3. Kontaktliste beim Weiterleiten

**Heute** ist das Ziel ein reines Textfeld (`ActiveCallPage.xaml:366`,
`TransferTargetBox`). Wer intern weiterverbindet, muss die Nebenstelle
auswendig wissen — bei zehn Nebenstellen geht das, bei dreissig nicht mehr.

**Nicht in der Spezifikation.** §8.2 beschreibt blinde und begleitete
Übergabe, ohne zu sagen, wie das Ziel hineinkommt. Das ist eine Ergänzung,
kein Fehler — und braucht nach der Projektregel einen Auftrag.

### Vorgehen

Aus dem Textfeld eine `AutoSuggestBox` machen, gespeist aus denselben
Kontakten, die der Kontakte-Tab zeigt:

- **Team-Nebenstellen zuerst**, mit ihrer Präsenzlampe. Beim Weiterverbinden
  ist „ist die Person überhaupt frei" die eigentliche Frage — dieselbe
  Information, die das Besetztlampenfeld schon abonniert hat.
- Danach die übrigen Quellen.
- **Freie Eingabe bleibt möglich.** Eine externe Nummer muss weiterhin ohne
  Umweg eingetippt werden können; die Vorschlagsliste ergänzt das Feld, sie
  ersetzt es nicht.

Der `ContactStore` und die Präsenz liegen bereits vor, es ist im Kern eine
Oberflächenänderung. Was Zeit kostet, ist die Anordnung auf 400 px Breite
neben den beiden Übergabeschaltflächen.

**Prüfen:** T60 — Auswahl per Tastatur, freie Eingabe, blinde und begleitete
Übergabe je einmal über die Liste.

---

## 4. Die fünf zuletzt gewählten Nummern

**Nicht in der Spezifikation** — weder §20 noch §8 kennen eine
Wahlwiederholung.

Die Daten sind vollständig vorhanden: `CallHistoryStore` speichert jeden
Anruf mit Richtung, Nummer und aufgelöstem Namen. Es braucht keine neue
Ablage, nur eine Abfrage.

### Vorgehen

Beim Hineinklicken in das leere Nummernfeld (`ShellPage.xaml:325`,
`NumberBox`) eine Liste unter dem Feld:

- **Die letzten fünf abgehenden Anrufe**, ohne Doppelte. Wer dieselbe Nummer
  dreimal probiert hat, will nicht dreimal dieselbe Zeile sehen.
- Je Zeile der aufgelöste Name, darunter die Nummer, rechts wie lange es her
  ist.
- **Nur bei leerem Feld.** Sobald jemand tippt, ist die Liste im Weg.

Eine Frage gehört entschieden: **nur abgehende, oder auch angenommene
eingehende?** Ich schlage **nur abgehende** vor — „zuletzt gewählt" ist das,
was gefragt war, und wer zurückrufen will, hat dafür das Journal mit seinen
Filtern. Zwei Listen mit ähnlichem Inhalt an zwei Stellen verwirren mehr, als
sie helfen.

**Prüfen:** T61 — Liste erscheint bei leerem Feld, verschwindet beim Tippen,
Auswahl übernimmt die Nummer, keine Doppelten.

---

## 5. CRM und Gesprächsjournal im Anrufjournal

Der grösste der drei Wünsche, und der einzige mit einer Frage, die nicht
technisch ist.

**Heute** zeigt die Anrufliste Nummer, Dauer, Uhrzeit und Ergebnis (§20.3).
Der Anruferkontext existiert nur **während** des Gesprächs.

### Die Frage, die zuerst zu klären ist

Um im Journal zu zeigen, was das CRM und das Gesprächsjournal wissen, gibt es zwei
Wege — und sie unterscheiden sich nicht im Aufwand, sondern in dem, was
danach auf der Festplatte liegt.

| | Weg | Folge |
|---|---|---|
| **A** | **Beim Anklicken neu abrufen** | Nichts wird gespeichert. Der Eintrag zeigt, was heute gilt — bei einem Anruf von vor drei Monaten also den heutigen Stand, nicht den von damals. Ohne Netz oder abgeschaltete Quelle bleibt die Stelle leer |
| **B** | **Beim Anruf mitspeichern** | Sofort da, auch offline, und historisch richtig. **Aber:** Gesprächszusammenfassungen und Stimmungswerte lägen dann dauerhaft in `history.db` auf dem Arbeitsplatz |

**Ich empfehle A, deutlich.** Der Grund steht in den Zusagen, die wir dem
Gesprächsjournal-Team gegeben haben und die in `prompt-journal.md` schriftlich
festgehalten sind:

> Die Antwort bleibt höchstens **fünf Minuten im Arbeitsspeicher** und wird
> nie auf die Platte geschrieben.

Weg B bricht diese Zusage. Eine lokale Datenbank mit den
Gesprächszusammenfassungen und Stimmungsbewertungen aller Anrufe eines Jahres
ist etwas grundlegend anderes als eine Karte, die beim Klingeln kurz
erscheint — auch wenn beide dieselben Daten zeigen. Sie überlebt jedes
Zeitlimit, wandert bei einem Gerätewechsel mit, und niemand hat ihr
zugestimmt.

§21.2 zieht dieselbe Grenze: die Anrufliste *„darf einen extern aufgelösten
**Namen** speichern"* — einen Namen, nicht einen Gesprächsinhalt.

**Wenn B trotzdem gewollt ist**, gehört das ausdrücklich entschieden, in
einem ADR festgehalten und dem Gesprächsjournal-Team gesagt, weil es die Zusage
ändert. Es wäre nicht falsch, aber es wäre eine andere Entscheidung als die
bisher getroffene.

### Vorgehen (Weg A)

1. **Aufklappbereich am Journaleintrag.** Klick klappt auf, statt eine neue
   Seite zu öffnen — die Liste bleibt sichtbar, und der Rückweg entfällt.
2. **Beim Aufklappen** `CallerContextService` mit der Nummer des Eintrags
   fragen. Derselbe Dienst wie beim Anruf, dieselbe Karte, derselbe
   Zwischenspeicher.
3. **Nur eine Anfrage gleichzeitig.** Wer durch die Liste klickt, löst sonst
   je Eintrag einen Abruf aus. Der vorige wird abgebrochen — dasselbe Muster
   wie bei der Suche mit ihrem Generationszähler.
4. **Ohne Quelle oder ohne Netz** bleibt der Bereich leer mit einer
   Begründung. Nie ein leerer Kasten ohne Erklärung.

Der Aufwand liegt fast ganz in Punkt 3. Die Karte selbst ist gebaut,
`CardView` kann sie zeichnen, und der Dienst kennt die Nummer.

**Prüfen:** T62 bis T64 — Eintrag mit Treffer in beiden Quellen, einer ohne
Treffer, und schnelles Durchklicken ohne veraltete Anzeige. Dazu **T65**:
nach dem Benutzen des Journals steht in `history.db` keine
Gesprächszusammenfassung. Das ist der Test, der Weg A von Weg B
unterscheidet, und er gehört geschrieben, bevor der Code entsteht.

---

## Reihenfolge

**Zuerst 1 und 6.** Beide sind Fehler mit gefundener Ursache, beide sind an
einem Vormittag zu haben, und beide fallen jeden Tag auf: ein fehlendes
Symbol im Infobereich und eine Mobilnummer, die da ist und sich nicht wählen
lässt.

**Dann 2a.** Auch ein Fehler, etwas mehr Arbeit, und er nimmt den Druck von
der Entscheidung 2b — mit einem verständlichen Hinweis ist die Lage
unvollständig statt kaputt.

**Dann 4, dann 3.** In dieser Reihenfolge, weil 4 die kleinere Änderung ist
und beide dieselbe Stelle berühren: eine Vorschlagsliste unter einem
Eingabefeld. Was bei 4 an Anordnung und Tastaturbedienung gelernt wird,
trägt 3 mit.

**Zuletzt 5**, und erst nachdem die Frage A/B beantwortet ist.

**2b läuft daneben** und ist keine Programmierarbeit, sondern eine
Entscheidung mit einer Vorbedingung: die Entra-App-Registrierung braucht
einen Administrator und dauert. Wenn Graph kommt, sollte das früh angestossen
werden — wie das Zertifikat.

## Was vorher zu entscheiden ist

Vier Punkte brauchen eine Antwort, bevor Code entsteht:

1. **2b** — Graph nativ (A), Graph über die Plattform (B), oder klassisches
   Outlook voraussetzen (C)? Empfehlung: B, mit ADR-018.
2. **4** — nur abgehende Nummern, oder auch angenommene eingehende?
   Empfehlung: nur abgehende.
3. **5** — bei Bedarf abrufen (A) oder mitspeichern (B)? Empfehlung: A,
   nachdrücklich.
4. **3, 4 und 5 sind Funktionen ohne Auftrag** in NIPP-BUILD.md. Nach der
   Projektregel „kein Feature ohne Auftrag" gehören sie in die
   Spezifikation, bevor sie gebaut werden — als Ergänzung zu §8.2, §20 und
   §21. Das ist kein Formalismus: die drei ändern, wie nipp bedient wird, und
   die nächste Fassung soll nachlesen können, warum.
