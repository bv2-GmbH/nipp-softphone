# Eine Integration in nipp einrichten

**Stand:** 11.09.2026 · Bezug: `NIPP-BUILD.md` §21 und §21.6, ADR-033, ADR-040

Wie eine externe Quelle angebunden und getestet wird. Der Weg ist derselbe für CRM, ERP, Ticketing oder eine beliebige REST-Schnittstelle.

---

## Voraussetzung

**Ohne eingerichtete Quelle ändert sich an nipp nichts.** Das Suchfeld im Kontakte-Tab erscheint nicht, die Anruferkarte bleibt leer, es geht keine Anfrage hinaus. Das ist Absicht: Telefonieren hängt an keiner Integration (§21.2).

---

## Ein Token besorgen

Das ist der Schritt vor allen anderen, und er passiert **im Zielsystem**, nicht
in nipp.

**Ein Token gehört an den Arbeitsplatz, nicht an eine Person.** Es soll einen
Passwortwechsel überstehen und nicht mit einem Konto verschwinden, das jemand
beim Austritt schliesst. Und es braucht **nur Lesezugriff** — nipp schreibt in
kein Fremdsystem, weder heute noch geplant. Ein Token, das schreiben darf, ist
hier kein Komfort, sondern ein unnötiges Risiko: die Datei, die auf jeden
Arbeitsplatz verteilt wird, sagt jedem Gerät, welche Adresse mit Rufnummern
beliefert wird.

### Wo das Token herkommt

**Das steht in der Anbietervorlage**, nicht hier: jede Vorlage bringt zu jedem
Geheimnis eine Beschriftung und einen Herkunftshinweis mit, und der erscheint
im Formular direkt über dem Eingabefeld. Das ist der Schritt, der ausserhalb
von nipp passiert, und der einzige, bei dem eine Anleitung wirklich hilft —
deshalb steht sie dort, wo sie zum System gehört.

Typische Wege, in dieser Reihenfolge:

1. **Im Profil des Zielsystems**, wo es meist „API-Token" oder „Persönlicher
   Zugriffsschlüssel" heisst.
2. **In der Administration**, wenn Token dort zentral vergeben werden.
3. **Über die API selbst**, wenn eine Anmeldung mit Benutzername und Passwort
   einen Schlüssel ausstellt.

**Ein Wert erscheint oft nur einmal, beim Anlegen.** Wer ihn dann nicht
einträgt, legt einen neuen an.

---

## Der Weg in sechs Schritten

**Er fängt im Katalog an und nicht mit einer Datei** (§21.6, ADR-033). Wer eine
ganze Konfiguration einliest: das geht weiter, ist aber der Weg für die
**Verteilung** — es ersetzt alle Quellen.

### 0. Eine Anbietervorlage importieren — wenn es eine gibt

**Einstellungen → Integrationen → API-Anbieter importieren …**

Eine Anbietervorlage ist **eine** JSON-Datei, die eine Schnittstelle
beschreibt: Adresse, Anmeldeart, Endpunkte, Feldpfade, die Beschriftung der
Zugangsdaten und eine erfundene Beispielantwort für die Kartenvorschau.
**Einen Zugangsschlüssel enthält sie nicht** — der wird danach in nipp
eingetragen, und eine Datei, die einen enthält, wird abgelehnt (ADR-040).

Nach dem Import steht der Anbieter unter *Quelle hinzufügen*. Die Vorlage
bleibt dabei liegen: dieselbe Vorlage lässt sich für zwei Mandanten zweimal
anwenden, und die Beschriftung der Zugangsdaten kommt weiterhin aus ihr.

nipp bringt **eine** Vorlage mit: *Eigene REST-API*, das Grundgerüst für eine
beliebige JSON-Schnittstelle. Alles Weitere kommt über den Import.

### 1. Quelle hinzufügen

**Einstellungen → Integrationen → Quelle hinzufügen.**

Dort steht die mitgelieferte Vorlage und alles, was importiert wurde —
Importiertes ist gekennzeichnet, mit dem Hersteller daneben.

| Eintrag | Wofür |
|---|---|
| **Eigene REST-API** | ein Grundgerüst für eine beliebige JSON-Schnittstelle. Adresse, Anmeldung und Feldpfade werden danach eingetragen |
| *importierte Anbieter* | was die jeweilige Vorlage beschreibt — die Zeile darunter sagt es |

**Die Quelle kommt abgeschaltet herein.** Eingeschaltet wird nach einem
erfolgreichen Testabruf — sonst fragt nipp beim ersten Anruf eine Adresse, die
noch niemand gesehen hat.

**Zweimal derselbe Eintrag ist erlaubt** und ergibt `crm` und `crm-2`.
Das ist der Fall „zwei Mandanten desselben Systems".

**Hinzufügen nimmt nichts weg.** Weder eine andere Quelle noch die
Einstellungen darunter — eine Vorlage bringt Wartezeiten und
Zusammenführungsregeln mit, aber das sind Vorschläge für eine leere
Konfiguration, keine Anweisung.

### 2. Token eintragen

Im Detail der Quelle, gleich oben. Das Feld trägt die **Beschriftung**, die
sagt, was gebraucht wird, und darunter steht, **woher es kommt** — für die
beiden bv2-Quellen der Weg aus dem Abschnitt oben.

Der Wert bleibt auf diesem Gerät: verschlüsselt und an das
Windows-Benutzerkonto gebunden (DPAPI, §10). In der Konfigurationsdatei steht
nie ein Schlüssel, sondern nur sein Verweis.

**Das Schema gehört nicht in den Wert.** Es hat sein eigenes Feld unter
*Verbindung und Anmeldung*: leer heisst `Bearer`, sonst steht der angegebene
Wert davor. Beides ist gebräuchlich — **das Django REST Framework verlangt
`Token`**, viele andere APIs `Bearer`. Welches gilt, sagt die Vorlage — und
wenn nicht, sagt es der Testabruf.

Wer das Schema in den Geheimniswert schreibt, versteckt einen Teil des
Protokolls in der verschlüsselten Ablage — dort sieht es niemand mehr und
niemand kann es korrigieren.

**Nach einem Gerätewechsel ist er neu einzutragen.** Das gilt genauso für das
SIP-Passwort und ist keine Eigenart der Integrationen.

### 3. Testabruf

Im Detail der Quelle: **Verbindung testen**. Eine Testnummer eintragen, die im
Zielsystem existiert, und *Abruf ausführen*.

Es werden **beide Fähigkeiten** geprüft, wenn die Quelle beide hat —
Anruferkontext und Kontaktsuche. Danach steht dort:

- **die Antwort im Rohzustand** — daraus entstehen die Feldpfade,
- **die gemappten Felder** — daran zeigt sich, ob die Pfade stimmen,
- Status und Dauer.

Ein Feld, das leer bleibt, obwohl in der Antwort etwas steht, hat einen
falschen Pfad. Genau dafür stehen beide nebeneinander.

**Die Antwort bleibt im Arbeitsspeicher** (§21.2) und dient dort zugleich als
Vorschaudaten für den Karten-Designer. Nach einem Neustart von nipp ist sie
weg.

### 4. Pfade nachziehen

Im Detail der Quelle: **Endpunkte und Felder (JSON)**. Dort steht das JSON
**dieser einen** Quelle — nicht die ganze Datei. Ändern, *Übernehmen*, erneut
testen. So oft, bis die Felder stimmen.

Eine kaputte Bearbeitung geht damit auf Kosten dieser Quelle; die anderen
bleiben stehen.

Der Aufbau eines Mappings:

```jsonc
"mapping": {
  "contactName": { "path": "$.contact.fullName" },
  "openOrders":  { "path": "$.orders.open", "as": "number" },
  "label":       { "expr": "concat(contactName, ' · ', company)" }
}
```

`path` zieht einen Wert aus der Antwort (JSONPath), `expr` rechnet aus dem, was
schon gemappt ist. `as` sagt, was der Wert sein soll — ohne Angabe gilt, was im
JSON steht. **Eine Zeichenfolge bleibt Text**, auch wenn Ziffern darin stehen:
eine Rufnummer ist keine Zahl.

**Die Feldnamen sind frei wählbar, aber nicht folgenlos.** Nur ein
katalogisierter Name bekommt eine Beschriftung, eine Bedeutung — und damit den
Toast. Die Tabelle steht in [feldnamen.md](feldnamen.md); wer seine Felder frei
benennt, hat eine funktionierende Karte und einen Toast, der nur die Nummer
zeigt.

### 5. Einschalten

Erst jetzt. Der Schalter steht in der Liste rechts neben der Quelle.

Darunter, unter **Wann nachgeschlagen wird**, stehen drei Schalter, die vorher
nur in der Datei standen: bei eingehenden Anrufen, bei ausgehenden, und ob
interne Nummern nach aussen gehen (ab Werk **nicht** — für eine Nebenstelle hat
kein Fremdsystem eine Antwort, und Notrufnummern gelten als intern).

### 6. Am Telefon prüfen

- **Anruferkontext:** anrufen lassen. Der Name steht sofort da (aus den eigenen
  Kontakten), die Angaben aus dem Fremdsystem kommen nach. Unter der Karte
  steht, was jede Quelle gerade tut.
- **Kontaktsuche:** im Kontakte-Tab erscheint jetzt ein Suchfeld. Lokale
  Treffer sind sofort da, die fremden kommen dazu.

Was auf der Karte **wo** steht, bestimmen Sie selbst:
[karten.md](karten.md).

---

## Verteilen

Für mehrere Arbeitsplätze: **Einstellungen → Integrationen → Ausgeben**, die
Datei auf einen https-Server legen und im Provisionierungsprofil eintragen.

```xml
<integrations src="https://prov.example.ch/integrations.json"/>
```

**Nur https**, ohne Ausnahme — diese Datei bestimmt, welche fremden Adressen
nipp mit Rufnummern beliefert.

Die ausgegebene Datei enthält **Quellen und Karten**, aber keine Geheimnisse:
sie sind an Gerät und Benutzerkonto gebunden und auf jedem Arbeitsplatz einmal
einzutragen.

**„Einlesen" ersetzt alle Quellen.** Das ist für die Verteilung richtig und
beim Hinzufügen einer zweiten Quelle falsch — dafür ist der Katalog da. nipp
fragt vorher und sagt, wie viele Quellen es kostet.

---

## Wenn etwas nicht geht

| Was man sieht | Woran es liegt |
|---|---|
| Kein Suchfeld im Kontakte-Tab | Keine Quelle mit `searchContacts` eingeschaltet |
| Karte bleibt leer | Keine Quelle mit `lookupByPhone`, oder `callerLookup.enabled` steht auf `false` |
| „Zugangsdaten fehlen" | Schritt 2. In der Quellenliste steht dann ein Warnzeichen davor |
| Nach dem Einlesen einer Datei ist eine Quelle weg | „Einlesen" ersetzt **alle** Quellen. Eine einzelne kommt über „Quelle hinzufügen" |
| Ein Feld erscheint auf der Karte nicht | siehe `karten.md`, letzter Abschnitt |
| „antwortet nicht" | Zeitgrenze zu knapp, oder der Server ist langsam. `timeoutMs` erhöhen — aber beim Anruferkontext zählt jede Sekunde |
| „lehnt die Anmeldung ab (401)" | Falscher Schlüssel — oder das falsche `scheme`. **Der Antwort ist nicht zu glauben:** es gibt Server, die `WWW-Authenticate: Bearer` melden und ausschliesslich `Token` akzeptieren — das Django REST Framework tut das in seiner verbreitetsten Anmeldung. Ausprobieren: `scheme` setzen, weglassen, Testabruf |
| „hat kein JSON geliefert" | Meist eine Anmeldeseite in HTML. Adresse und Pfad prüfen |
| „wird gerade nicht gefragt" | Der Schutzschalter. Nach fünf Fehlern in Folge pausiert die Quelle eine Minute |
| Interne Anrufe zeigen nichts | Absicht. Nebenstellen und Notrufnummern gehen ab Werk nicht nach aussen (`lookupInternalNumbers`) |
| „Das ist eine nipp-Konfiguration, keine Anbietervorlage" | Beim Import wurde eine `integrations.json` ausgewählt. Dafür ist *Einlesen* zuständig — und das ersetzt alle Quellen |
| „In der Vorlage steht ein Wert, der wie ein Geheimnis aussieht" | Die Datei enthält einen Zugangsschlüssel. Eine Vorlage beschreibt nur, **wo** einer gebraucht wird |
| „Diese Vorlage ist in Fassung N geschrieben" | Die Datei ist für eine neuere Fassung von nipp. Versucht wird sie nicht — eine unbekannte Fassung kann Felder anders meinen |

Das Diagnosepaket (**Einstellungen → Erweitert**) enthält `integrationen.json` mit Quellen, Zuständen und Befunden — **ohne** Schlüssel, ohne Endpunktpfade und ohne Anfrageparameter.

---

## Was im Protokoll steht

Auf Stufe *Debug* protokolliert nipp je Abfrage die Quelle, ihren Zustand, den HTTP-Status und die Dauer.

**Nicht protokolliert werden Rufnummern, Suchtexte, Namen, Adressen mit Parametern und Antwortinhalte** (§21.2). Ein Softphone, das mitschreibt, wer angerufen hat und was ein CRM dazu wusste, führt ein Bewegungsprofil mit Kundendaten.
