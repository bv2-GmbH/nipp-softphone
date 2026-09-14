# Provisionierung

Wie nipp an einem Arbeitsplatz zu seinen Einstellungen kommt (§17).

## Die drei Ebenen

Sie greifen bei jedem Start in dieser Reihenfolge:

| # | Ebene | Wo | Wozu |
|---|---|---|---|
| 1 | **Auslieferungszustand** | `%PROGRAMDATA%\bv2\nipp\nipp-factory.xml` | Was für jede Installation gilt: Codecs, Ports, Verschlüsselung, Standardverhalten |
| 2 | **Kundenprofil** | von der Provisioning-Adresse aus den Einstellungen | Konten, Team-Nebenstellen, kundenspezifische Vorgaben, gesperrte Felder |
| 3 | **Was der Benutzer eingestellt hat** | `%APPDATA%\nipp\settings.json` | Schlägt Ebene 1 und 2 — ausser bei gesperrten Feldern |

### Wer gewinnt (ADR-054)

**Der Benutzer.** Hat er einen Wert selbst eingestellt, lässt ein Profil ihn stehen und schreibt eine Zeile ins Protokoll. Das gilt auch für Konten, Nebenstellen und Gruppen: ein von Hand angelegtes zweites Konto überlebt das nächste Profil.

**Ausser das Feld ist gesperrt.** Dann gilt das Profil, und die Markierung «vom Benutzer eingestellt» wird gelöscht. Eine Sperre ist damit der Weg, einen verstellten Arbeitsplatz zurückzuholen — und der einzige: einen Knopf «Auf Profilwerte zurücksetzen» gibt es nicht.

**Was der Benutzer nie angefasst hat, setzt das Profil bei jedem Start neu.** Ein geänderter Profilwert kommt also an, ohne dass jemand etwas tun muss.

> **Beim Update auf die Fassung vom 13.09.2026:** eine bestehende Einstellungsdatei kennt die Markierung nicht und beginnt mit einer leeren. **Das Profil gewinnt dort beim ersten Start noch einmal**, ab dem zweiten gilt die Regel oben. Wer an einem Arbeitsplatz von Hand eingestellt hat und das behalten will, stellt es nach dem ersten Start dieser Fassung einmal neu ein.

Bis zum 13.09.2026 stand hier das Gegenteil von dem, was der Code tat: das Profil überschrieb bei jedem Start alles, was es nannte, und die Sperre entschied nur darüber, ob ein Feld ausgegraut aussieht.

Ein Profil ist eine **Vorgabe, kein vollständiger Einstellungssatz**. Was es nicht nennt, bleibt unverändert — sonst würde jeder Start Lautstärke, Gerätewahl und Erscheinungsbild zurücksetzen.

**Ein Fehler beim Abruf verhindert den Start nicht.** Ist der Server nicht erreichbar, gelten die zuletzt gespeicherten Einstellungen, und das Protokoll sagt warum. Ein Softphone, das nicht startet, weil ein Webserver hustet, wäre unbrauchbar.

## Ein Profil erstellen

Das Werkzeug ist `nippprov` (Projekt `Nipp.Provisioning`):

```
nippprov neu kunde-muster.xml --profil muster-ag
```

Legt eine kommentierte Vorlage an. Ausfüllen, dann gegenlesen:

```
nippprov pruefen kunde-muster.xml
```

`pruefen` verwendet **denselben Parser wie die App**. Was hier durchgeht, geht auch dort durch — und was hier als unbekannter Pfad gemeldet wird, wird in der App stillschweigend übergangen.

```
nippprov schema
```

listet alle zulässigen Elemente und Einstellungspfade.

## Aufbau

```xml
<nipp-provisioning version="1" profile="muster-ag">

  <accounts>
    <account username="151" domain="pbx.muster.ch"
             display-name="Vorname Nachname"
             transport="tls" expires="600" />
  </accounts>

  <team>
    <extension name="Empfang" number="150" sip="sip:150@pbx.muster.ch" />
    <extension name="Pikett" number="151" mobile="+41791234567" group="Support" />
  </team>

  <settings>
    <set path="advanced.country-prefix" value="+41" />
    <set path="codecs.order">opus,G722,PCMA,PCMU</set>
  </settings>

  <locked>
    <field>accounts</field>
    <field>network</field>
  </locked>

</nipp-provisioning>
```

`<set>` nimmt den Wert als Attribut **oder** als Inhalt — das zweite, wenn der Wert Anführungszeichen enthält.

### Konten

`username` und `domain` sind Pflicht, alles andere hat Standardwerte. Höchstens zehn (§20.2); weitere werden übergangen.

Ein Profil, das Konten nennt, **ersetzt** die Kontoliste. Nennt es keine, bleibt sie unberührt.

### Team-Nebenstellen

Erscheinen in der Kontaktliste. **Nur für sie wird Präsenz abonniert** (§14.8) — jede kostet die Anlage ein dauerhaftes SUBSCRIBE, ein Adressbuch mit tausend Einträgen würde sie überlasten. Ohne `sip="…"` steht die Nebenstelle in der Liste, wird aber nicht beobachtet.

`mobile="…"` ist die Handynummer. Sie ist wählbar und wird bei einem eingehenden Anruf erkannt, **erzeugt aber kein zweites Präsenz-Abonnement** — beobachtet wird ausschliesslich über `sip="…"`. Die Last auf der Anlage ändert sich um null.

`group="…"` legt die Gruppe in der Kontaktliste fest (ADR-041). **Die Gruppen entstehen in der Reihenfolge des Profils**; ohne Angabe steht eine Nebenstelle in der ersten. Ein Profil, das Nebenstellen setzt, setzt damit auch die Gruppen — genauso, wie es die Kontoliste ersetzt.

### Gesperrte Felder

Ein Pfad in `<locked>` sperrt auch alles darunter: `network` sperrt `network.sip-port` mit. Das Bedienelement erscheint ausgegraut mit Schloss und dem Hinweis „Von der Administration festgelegt".

> **Das ist ein Bedienschutz, keine Sicherheitsgrenze.** Die Konfigurationsdatei unter `%APPDATA%` gehört dem angemeldeten Benutzer, und ein Texteditor öffnet sie so gut wie nipp. Wer wirklich verhindern will, dass etwas verstellt wird, braucht Gruppenrichtlinien oder Dateirechte — nicht diese Liste.

## Passwörter

Ein Konto darf `password="…"` mitbringen, **sollte es aber nicht**.

Das Profil liegt auf einem Webserver. Wer es abrufen kann, hat damit die Zugangsdaten — und einen Webserver so abzusichern, dass nur die richtigen Arbeitsplätze ihn erreichen, ist mehr Arbeit als sie aussieht. Der bessere Weg: Benutzername und Domäne vorgeben, das Passwort fragt nipp einmal ab und legt es lokal über DPAPI ab (§11).

Wenn es doch sein muss: **nur über https**, und den Zugriff auf das Verzeichnis einschränken. `nippprov pruefen` weist auf ein Profil mit Passwörtern ausdrücklich hin.

## Ausliefern

**Der Weg in einem Befehl** (seit dem 13.09.2026, Befund E6):

```powershell
# Interaktiv auf dem eigenen Rechner
.\build\Install-Nipp.ps1

# Als Computerstartskript per Gruppenrichtlinie oder Intune
.\build\Install-Nipp.ps1 -Setup \\fileserver\nipp\nipp-win-stable-Setup.exe `
    -Factory \\fileserver\nipp\nipp-factory.xml -Silent
```

Das Skript legt den Auslieferungszustand ab und startet das Setup. Es prüft die Datei **vor** dem Kopieren: eine kaputte Konfiguration nach `%PROGRAMDATA%` zu legen hiesse, dass nipp bei jedem Start warnt und der Arbeitsplatz trotzdem aussieht, als wäre er eingerichtet.

> **Dieser Schritt stand bis zum 13.09.2026 nur hier und nirgends im Code.** Kein Skript und kein Installationsschritt legte die Factory-Datei ab. Ein Arbeitsplatz ohne sie nimmt kein Kundenprofil entgegen — die Provisioning-Adresse steht ja darin —, und wer das nicht wusste, suchte den Fehler beim Server.

**Was das Skript nicht löst:** das Setup ist unsigniert (AP9.2). Klickt ein Mensch doppelt, hält SmartScreen den ersten Start auf. Über ein Startskript läuft es als SYSTEM, und die Frage stellt sich nicht — dafür ist `-Silent` da.

**Von Hand**, wenn es sein muss: `build\nipp-factory.xml` nach `%PROGRAMDATA%\bv2\nipp\nipp-factory.xml` kopieren. Die Datei enthält keine Konten und sperrt nichts — beides gehört ins Kundenprofil.

**Kundenprofil:** unter der Adresse ablegen, die in den Einstellungen als Provisioning-Adresse eingetragen ist, üblicherweise `https://prov.<domäne>/<kennung>.xml`. Die Adresse selbst lässt sich im Auslieferungszustand vorgeben (`advanced.provisioning-uri`).

## Warum ein eigenes Format

§9 nennt `Core.ProvisioningUri` — den Weg des SDK. Der ist hier nicht gangbar: nipp führt seine Einstellungen in `settings.json`, nicht in der `linphonerc` des SDK. Beides nebeneinander hiesse zwei Wahrheiten, die sich gegenseitig überschreiben. Ausserdem kennt das Linphone-Format nichts von Team-Nebenstellen, gesperrten Feldern oder Erscheinungsbild.

Ausführlich in [ADR-010](decisions.md).
