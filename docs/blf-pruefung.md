# Besetztlampenfeld: funktioniert es mit unserer Anlage?

**Ja — seit dem 05.09.2026 abends, nach drei Korrekturen.** Das Ergebnis steht
unten; der Rest der Seite ist das Messverfahren und die Geschichte dahinter,
weil die Annahme, mit der der Tag begann, falsch war.

## Worum es geht

nipp abonniert die Präsenz beobachteter Nebenstellen über Linphones
`FriendList` mit `SubscribesEnabled`. Das sendet einen `SUBSCRIBE` mit

```
Event: presence
```

— das SIMPLE-Verfahren nach RFC 3856. Ein klassisches Besetztlampenfeld, wie es
ein Tischtelefon zeigt, verwendet dagegen

```
Event: dialog
```

nach RFC 4235. **Das ist nicht dasselbe:**

| | sagt |
|---|---|
| `presence` | was ein Client über sich selbst veröffentlicht („Reto ist beschäftigt") |
| `dialog` | was die Anlage über eine Nebenstelle weiss („an 153 klingelt es") |

Ein Tischtelefon veröffentlicht normalerweise keine Präsenz. Ohne `PUBLISH` von
der Gegenseite kommt über den presence-Weg schlicht nichts an — **so die
Annahme am Morgen.** Sie war für unsere Anlage falsch: `pbx.example.ch` bildet
den Leitungszustand der Nebenstellen selbst auf `presence` ab und beantwortet
den `SUBSCRIBE` mit `Subscription-State: active`. Frei, im Gespräch und offline
kommen an. Gut, dass gemessen wurde, statt umzubauen.

### Eine Folge steht schon fest

Das SDK kennt in `ConsolidatedPresence` nur `Online`, `Busy`, `DoNotDisturb`
und `Offline`. Der für ein Besetztlampenfeld interessanteste Zustand —
**„klingelt"** — kann über diesen Weg also **nie** eintreffen. `PresenceStatus.Ringing`
ist damit vorerst toter Code; er bleibt stehen, weil der dialog-Weg ihn liefern
würde.

## Die Messung

1. In nipp: **Einstellungen → Kontakte → Team-Nebenstellen**. Eine Nebenstelle
   eintragen — Name, Nummer und **die SIP-Adresse** (`sip:153@pbx.example.ch`).

   > Ohne SIP-Adresse erscheint die Nebenstelle in der Liste, wird aber nicht
   > abonniert. Das ist Absicht (§14.8) und in diesem Fall die häufigste
   > Ursache für „es passiert nichts".

2. **Einstellungen → Erweitert → Protokollierung** auf **Debug**. Erst damit
   schreibt das SDK seine SIP-Nachrichten mit ins Protokoll, und die Antwort
   der Anlage auf den `SUBSCRIBE` steht im Klartext da (`SIP/2.0 200 OK`
   oder `SIP/2.0 489 Bad Event`).

   > Bis zum 05.09.2026 war dieser Schalter ohne Wirkung — er wurde
   > gespeichert und nirgends gelesen, und die SDK-Meldungen wurden gar nicht
   > eingesammelt. Beides ist behoben.

3. **Speichern**, nipp neu starten.

4. Auswerten:

   ```powershell
   .\tools\Test-Blf.ps1
   ```

   Das Skript liest den letzten Programmstart aus dem Protokoll und sagt, was
   herausgekommen ist. Mit `-Follow` lässt es sich live mitlesen.

### Erster Testlauf am 05.09.2026, nachmittags

Zehn Nebenstellen abonniert — und **keine einzige Antwortzeile**. Das lag nicht an
der Anlage, sondern an der Diagnose: sie lauschte auf
`OnSubscriptionStateChanged` des Core, und der feuert nur für
`Core.Subscribe()`-Ereignisse, nicht für die internen Abonnements einer
`FriendList`. Der Zustand steht an jedem `Friend` (`Friend.SubscriptionState`)
und wird seither im Pump alle fünf Sekunden geprüft. **Der Test muss deshalb
mit dem neuen Build wiederholt werden.**

## Die möglichen Ergebnisse

| Was im Protokoll steht | Bedeutung | Was daraus folgt |
|---|---|---|
| `Abonnement 'presence' … : Active` **und** `Praesenz geaendert` | Die Anlage kann presence | Es funktioniert — ausser „klingelt", siehe oben |
| `Active`, aber **kein** `NOTIFY` | Die Anlage stimmt zu, hat nichts zu melden | Die Nebenstelle veröffentlicht keine Präsenz → dialog-Weg nötig |
| `abgelehnt: BadEvent (SIP 489 …)` | Die Anlage kennt `presence` nicht | dialog-Weg nötig |
| `abgelehnt: Forbidden` oder `Declined` | Berechtigung fehlt | Anlagenadministration fragen |
| `abgelehnt: NotFound` | Nebenstelle unbekannt | SIP-Adresse in den Einstellungen prüfen |
| gar keine Abonnement-Zeile | Nichts abonniert | SIP-Adresse fehlt bei der Nebenstelle |

## Wenn der dialog-Weg nötig ist

Machbar mit diesem SDK, aber ein Umbau von etwa einem Tag:

- `Core.Subscribe(address, "dialog", expires, null)` statt `FriendList` —
  die Methode gibt es im Wrapper.
- `OnNotifyReceived` liefert den NOTIFY-Body; das XML nach RFC 4235 muss
  **selbst** ausgewertet werden. Das SDK tut das nur für presence.
- Die Zustände wären dann `trying`, `early` (klingelt), `confirmed`
  (im Gespräch), `terminated` (frei) — und damit endlich der vollständige
  Satz aus §8.4.
- `PresenceStatus` und `BlfService.Describe` bleiben unverändert, nur die
  Quelle wechselt. Die Schichtgrenze aus §6 hält das aus.

Vor dem Umbau eine ADR schreiben: die Abweichung von der ursprünglichen
Annahme („Präsenz über `Friend`/`FriendList`", AP6.5) gehört festgehalten.

## Ergebnis

```
Datum:        05.09.2026, 18:48
Anlage:       pbx.example.ch (UDP), Konto 152bv2
Nebenstellen: 10 (152, 153, 154, 901, 902, 904, 905, 906, 907, 920)
Abonnement:   Event: presence, alle zehn Active (Subscription-State: active;expires=600)
NOTIFY:       kommt, Event: presence, 25 innerhalb der ersten Sekunden
Zustände:     7x frei (Online), 2x im Gespräch (Busy), 1x offline
Befund:       Das Besetztlampenfeld funktioniert. Farbe und Text in der Liste.
```

Belege: `docs/review/2026-09-05-blf/` — vorher zehnmal „unbekannt", nachher
frei / offline / im Gespräch.

### Was zwischen „die Anlage antwortet" und „die Lampe leuchtet" lag

Drei Fehler, alle in nipp, keiner in der Anlage:

1. **Die Diagnose lauschte am falschen Haken.** `OnSubscriptionStateChanged`
   am Core feuert nur für `Core.Subscribe()`, nicht für die Abonnements einer
   `FriendList`. Jetzt prüft der Pump alle fünf Sekunden `Friend.SubscriptionState`.

2. **Adressformat.** Das SDK meldet die Adresse als `"152" <sip:152@pbx.example.ch>`
   — mit Anzeigename. Die Einstellungen kennen `sip:152@pbx.example.ch`. Der
   Vergleich fand nie zusammen, der Zustand landete unter dem falschen
   Schlüssel und wurde bei der nächsten Synchronisation als „nicht mehr
   beobachtet" gelöscht. `SipUri.Normalize` bringt beide Seiten auf eine Form;
   die Bridge liefert seither `AsStringUriOnly()`.

3. **Die Liste wurde bei jeder Synchronisation ersetzt.** Das SDK speichert
   Freundeslisten in `linphone.db` mit `UNIQUE (name)`. Das zweite
   `AddFriendList("nipp-blf")` im selben Lauf verletzte den Index —
   `Caught exception in MainDb::insertFriendList`, dann eine SEH-Ausnahme.
   Die Liste wird jetzt einmal angelegt oder aus der Datenbank übernommen
   und danach nur noch abgeglichen.

Nebenbei fiel auf, dass die Einstellung **Protokollierung: Aus/Info/Debug**
nirgends gelesen wurde und die SDK-Meldungen gar nicht ins Protokoll kamen.
Beides behoben; ohne den SIP-Trace wäre keiner der drei Fehler zu finden gewesen.

### Was offen bleibt: „klingelt"

Über `presence` kennt das SDK nur `Online`, `Busy`, `DoNotDisturb`, `Offline`.
Ob die Anlage beim Klingeln einer Nebenstelle etwas meldet — und was — muss
ein Test mit einer klingelnden Nebenstelle zeigen. Meldet sie `Busy`, zeigt
nipp „im Gespräch", was für ein Besetztlampenfeld reicht. Meldet sie nichts,
bleibt der dialog-Weg (RFC 4235) die Option; er ist unten beschrieben.
