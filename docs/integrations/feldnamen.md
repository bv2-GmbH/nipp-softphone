# Feldnamen für Karten und Toast

**Stand:** 07.09.2026 · Bezug: `NIPP-BUILD.md` §21, ADR-032, `FieldCatalog.cs`

Wie ein Mapping seine Felder nennen soll — und warum das etwas ändert.

---

## Warum die Namen zählen

Ein Mapping darf seine Felder **beliebig** nennen. `$.contact.fullName` kann
`contactName`, `wer`, `f1` oder `KundeName` heissen; nipp liest, was dasteht,
und die Karte kann jedes davon anzeigen.

Drei Dinge bekommt aber nur, wer einen **katalogisierten** Namen wählt:

1. **Eine deutsche Beschriftung.** Der Designer schlägt sie vor, statt sie aus
   dem Namen zu bauen. `letzteArbeitZeile` wird zu „Letzte Arbeit, ganze
   Zeile" statt zu „Letzte arbeit zeile".
2. **Eine Rolle.** Damit findet `role('name')` das Feld — und damit greifen
   die **mitgelieferten Karten und der Toast**, ohne dass jemand sie anfasst.
3. **Den Toast.** Er fragt ausschliesslich über Rollen. Ein Feld ohne Rolle
   erscheint in einer Benachrichtigung nie.

Der letzte Punkt ist der wichtigste beim Einrichten einer neuen Quelle: wer
seine Felder frei benennt, bekommt eine funktionierende Karte, aber einen
Toast, der nur die Nummer zeigt.

---

## Rollen

Eine Rolle ist eine Frage, die jede Karte stellt. `role('name')` heisst: **die
erste Quelle nach Priorität, die ein Feld mit der Bedeutung „Name" liefert.**

| Rolle | Die Frage | Wo sie gestellt wird |
|---|---|---|
| `role('name')` | Wer ruft an? | Karte (Titel), Toast (Zeile 1) |
| `role('company')` | Aus welcher Firma? | Karte (Untertitel), Toast (Zeile 1) |
| `role('type')` | Was für ein Kontakt? | Karte, Toast (Zeile 1, in Klammern) |
| `role('work')` | Woran wurde zuletzt gearbeitet? | Karte, Toast (Zeile 2) |
| `role('summary')` | Worum ging es im letzten Gespräch? | Karte, Toast (Zeile 3) |
| `role('colleague')` | Wer hatte intern damit zu tun? | Karte, Toast (Zeile 2, angehängt) |

**Zwei Regeln entscheiden, wer gewinnt**, und in dieser Reihenfolge:

1. **Feld vor Quelle.** Geprüft wird Feldname für Feldname; innerhalb eines
   Feldnamens dann Quelle für Quelle. Sonst gewönne ein unscharfes `name` der
   ersten Quelle über das genaue `contactName` der zweiten.
2. **Kleinere Priorität gewinnt.** Wie bei `dataSources[].priority`. Die
   eigenen Kontakte haben Priorität 0 und liegen damit vor jeder externen
   Quelle — wer einen Kollegen im Adressbuch als „Andi" führt, will nicht den
   Eintrag aus einem CRM sehen.

Wer eine bestimmte Quelle **erzwingen** will, schreibt weiterhin
`crm.contactName`. Das ist erlaubt und manchmal richtig — nur eben nicht
in einer Karte, die bei mehreren Kunden laufen soll.

---

## Der Katalog

`role(...)` in der Spalte „Rolle" heisst: dieses Feld beantwortet diese Frage.
Der **Rang** entscheidet innerhalb einer Rolle, welcher Feldname zuerst
geprüft wird — kleiner zuerst.

| Feldname | Beschriftung | Rolle | Art | Rang |
|---|---|---|---|---|
| `contactName` | Name | `role('name')` | Text | 10 |
| `displayName` | Anzeigename | `role('name')` | Text | 20 |
| `name` | Name | `role('name')` | Text | 30 |
| `fullName` | Vollständiger Name | `role('name')` | Text | 40 |
| `firstName` | Vorname | — | Text | — |
| `lastName` | Nachname | — | Text | — |
| `company` | Firma | `role('company')` | Text | 10 |
| `customerName` | Kundenname | `role('company')` | Text | 20 |
| `primaryCustomer` | Hauptkunde | `role('company')` | Text | 30 |
| `organization` | Organisation | `role('company')` | Text | 40 |
| `customerNames` | Alle Kunden | — | Liste | — |
| `customerCount` | Anzahl Kunden | — | Zahl | — |
| `weitereKunden` | Weitere Kunden | — | Text | — |
| `customerNumber` | Kundennummer | — | Text | — |
| `contactType` | Art | `role('type')` | Text | 10 |
| `category` | Kategorie | `role('type')` | Text | 20 |
| `kind` | Art | `role('type')` | Text | 30 |
| `letzteArbeitZeile` | Letzte Arbeit, ganze Zeile | `role('work')` | Text | 10 |
| `letzteArbeit` | Letzte Arbeit | `role('work')` | Text | 20 |
| `lastWork` | Letzte Arbeit | `role('work')` | Text | 30 |
| `lastActivity` | Letzte Aktivität | `role('work')` | Text | 40 |
| `letzteArbeitDatum` | Letzte Arbeit, Datum | — | Datum | — |
| `letzteArbeitWer` | Letzte Arbeit, wer | `role('colleague')` | Text | 10 |
| `letzteZusammenfassung` | Letztes Gespräch | `role('summary')` | Text | 10 |
| `lastCallSummary` | Letztes Gespräch | `role('summary')` | Text | 20 |
| `summary` | Zusammenfassung | `role('summary')` | Text | 30 |
| `offeneAufgabenZeile` | Offene Punkte | — | Text | — |
| `verlauf` | Verlauf | — | Text | — |
| `accountManager` | Account Manager | `role('colleague')` | Text | 20 |
| `technician` | Techniker | `role('colleague')` | Text | 30 |
| `owner` | Zuständig | `role('colleague')` | Text | 40 |
| `email` | E-Mail | — | Text | — |
| `notiz` | Notiz | — | Text | — |
| `openOrders` | Offene Aufträge | — | Zahl | — |
| `revenue` | Umsatz | — | Zahl | — |
| `vip` | VIP | — | Ja/Nein | — |
| `externalId` | Kennung im Fremdsystem | — | Text | — |

---

## Immer vorhanden

Diese Felder gibt es **ohne jede eingerichtete Quelle**. Sie stehen in der
Palette unter „Anruf" und „Kontakte" und machen die Rückfallebene möglich —
`coalesce(role('name'), contacts.displayName, formatPhone(number.e164))` hat
nie eine leere Überschrift.

**Die `call.*`-Felder sind auf der Karte im Gespräch leer**: Dauer und Ergebnis
gibt es erst, wenn er vorbei ist, und die Zeilen verschwinden dort einfach. Auf
der Karte in der **Anrufliste** sind sie der Grund, warum jemand den Eintrag
geöffnet hat. `call.duration` ist leer, wenn nie verbunden wurde — nicht
„0:00", was ein Gespräch von null Sekunden behauptete.

| Pfad | Beschriftung | Gruppe |
|---|---|---|
| `number.e164` | Nummer international | Anruf |
| `number.national` | Nummer national | Anruf |
| `number.digits` | Nummer, nur Ziffern | Anruf |
| `call.startedAt` | Zeitpunkt des Anrufs | Anruf |
| `call.date` | Datum des Anrufs | Anruf |
| `call.time` | Uhrzeit des Anrufs | Anruf |
| `call.duration` | Gesprächsdauer | Anruf |
| `call.outcome` | Ergebnis | Anruf |
| `call.direction` | Richtung | Anruf |
| `contacts.displayName` | Name aus den eigenen Kontakten | Kontakte |
| `contacts.company` | Firma aus den eigenen Kontakten | Kontakte |
| `contacts.source` | Woher der Kontakt kommt | Kontakte |

---

## `anyOf` für alles ohne Rolle

Nicht jedes Feld braucht eine Rolle. Für ein Feld, das mehrere Quellen unter
verschiedenen Namen liefern, gibt es `anyOf`:

```
anyOf('slaStufe', 'vertragsStufe', 'serviceLevel')
```

Es fragt genau wie `role(...)` über alle Quellen nach Priorität, nur mit einer
Liste, die im Ausdruck steht statt im Katalog. Für ein einzelnes Feld ohne
Quellenbindung genügt `anyOf('customerNumber')`.

**Der Unterschied zu `coalesce`:** `coalesce(a.x, b.x)` nennt die Quellen,
`anyOf('x')` nicht. Wer eine Karte für einen einzigen Betrieb schreibt, kann
beides nehmen; wer sie weitergibt, nimmt `anyOf`.

---

## Ein eigenes Feld hinzufügen

Sinnvoll, sobald ein Feld bei **mehreren** Quellen dieselbe Bedeutung hat.
Solange es nur eine Quelle liefert, genügt `anyOf('name')` in der Karte.

Der Weg: `FieldCatalog.Entries` in
`src/Nipp.Core/Services/Integrations/Cards/FieldCatalog.cs` ergänzen. Diese
Tabelle wird daraus erzeugt, und `FieldCatalogTests` hält beides zusammen.

**Eine Rolle bekommt ein neues Feld nur mit Begründung.** Die Ränge innerhalb
der Rollen sind zeichengleich die Reihenfolge der fünf Feldlisten, die bis zum
07.09.2026 im `ToastComposer` standen — auch dort, wo sich eine andere
begründen liesse. Wer dazwischen etwas einfügt, ändert, was am Gerät im Toast
erscheint, und das ist eine Abnahme wert (T90 bis T92).

---

## Woran es liegt, wenn nichts erscheint

| Beobachtung | Ursache | Abhilfe |
|---|---|---|
| Karte zeigt den Namen, Toast nur die Nummer | Das Namensfeld hat keine Rolle | Feld in `contactName` umbenennen |
| Beschriftung heisst „Letzte arbeit zeile" | Der Name steht nicht im Katalog | Katalogisierten Namen nehmen, oder Beschriftung im Designer setzen |
| Karte bleibt ganz leer | Die Karte nennt Quellen, die es hier nicht gibt | `role(...)` statt `quelle.feld` |
| Falscher Name gewinnt | Zwei Quellen liefern dasselbe Feld | `priority` der Quellen ordnen |
| `role('...')` bleibt leer | Rollenname falsch geschrieben | Nur die sechs oben; sie sind klein und englisch |

Im Zweifel: der Testabruf in den Einstellungen zeigt die **gemappten Felder**
unter ihren Namen. Was dort steht, ist genau das, was `role(...)` sieht.
