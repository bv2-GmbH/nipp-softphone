# Der Weg ins öffentliche Repo — mit frischer Historie

Stand 14.09.2026. Entschieden von Dominic: **das Repo wird öffentlich, und die
Historie bleibt zurück.** Der Grund ist nicht Kosmetik — in 60 der 181 Commits
stehen die Namen der beiden bv2-eigenen Systeme und der Hostname der internen
Anlage.

Vorgeschichte: ADR-040 (die Vorlagen der beiden Quellen sind nicht mehr im
Repo), `docs/licensing.md` (AGPLv3, was bis zum Öffentlichmachen fehlt),
`docs/plans/RELEASE-PLAN.md` R10.

---

## 1. Was gemessen ist

Über alle 181 Commits gesucht (14.09.2026):

| Muster | Commits | Was es ist |
|---|---|---|
| der Name des CRM | 24 | eines der beiden bv2-eigenen Systeme |
| der Name des Gesprächsjournals | 16 | das zweite |
| der Hostname der Anlage | 20 | die interne Telefonanlage |
| ein GitHub-Token-Präfix | 1 | **Platzhalter**, kein echtes Token (geprüft) |
| Schlüssel, Zertifikate, `client_secret` | 0 | — |

**Kein echtes Geheimnis in der Historie.** Was dort steht, sind Namen — aber
genau die verbietet `PublicRepositoryTests`, und der prüft **nur den
Arbeitsbaum**. `docs/licensing.md` nennt das als ersten offenen Punkt: «über
alle Commits prüfen, nicht nur über den Arbeitsbaum».

## 2. Der Preis, und wie er zu vermeiden ist

**Ein neues Repo bricht den Update-Pfad.** Die Adresse steht an zwei Stellen
fest im Produkt:

- `VelopackUpdateGateway.RepositoryUrl` (Konstante im Code)
- `build/Release-Nipp.ps1` (`--repoUrl` beim Hochladen)

Jeder installierte Arbeitsplatz sucht dort weiter, wo er installiert wurde.
Nach dem Wechsel bekäme er **nie wieder ein Update** — und niemand merkt es,
weil die Prüfung still ist.

**Die Brücke:** Bevor das alte Repo stillgelegt wird, geht dort **eine letzte
Fassung** hinaus, in der die Konstante bereits auf das neue Repo zeigt. Wer
sie bekommt, wandert von selbst. Erst danach wird das alte Repo archiviert.

    altes Repo    nipp             0.9.4  →  0.9.5 (zeigt aufs neue)  →  archiviert
    neues Repo    nipp-softphone                            0.9.6, 0.9.7, …

**Wer die Brücke verpasst, muss von Hand neu installieren** — deshalb steht sie
vor allem anderen, und deshalb ist sie eine Fassung für sich und nicht mit
anderen Änderungen vermischt.

## 3. Was vor dem ersten Push fertig sein muss

Aus `docs/licensing.md`, hier mit Stand:

1. **Geheimnisse über alle Commits** — ✔ erledigt (Abschnitt 1). Mit der
   frischen Historie entfällt der Punkt ohnehin.
2. **README für Fremde** — ✔ **erledigt am 14.09.2026** (Ö2).
3. **SDK-Quelltext** — ✔ **erledigt am 14.09.2026** (Ö3): Verweis statt
   Spiegel, weil das SDK unverändert eingebunden wird.
4. **Der Repo-Wechsel selbst** — Abschnitt 2.

Dazu, aus diesem Plan:

5. **Die Testvorlagen der beiden Quellen** liegen seit ADR-040 ausserhalb des
   Repos und werden importiert. Vor dem Push prüfen, dass unter
   `Catalog/templates/` **nur** `custom-rest.json` liegt.
6. **`PublicRepositoryTests` muss grün sein** — er ist der Wächter, und mit dem
   öffentlichen Repo wird er zur wichtigsten Zeile der Testmatrix.

## 4. Die Runden

**Ö1 — Bestandsaufnahme im Arbeitsbaum.** `PublicRepositoryTests` laufen
lassen, `Catalog/templates/` prüfen, `.gitignore` gegen das lesen, was heute
nicht eingecheckt ist (SDK, `dist/`, Profile). Ergebnis: eine Liste dessen, was
im ersten Commit landet.

**Ö2 — Das README für Fremde. ✔ erledigt am 14.09.2026.** Der Kopf sagt jetzt
in den ersten dreissig Zeilen, was ein Fremder als Erstes braucht: **was nipp
ist** (SIP-Softphone, nicht «für die Anlagen von bv2»), **unter welcher
Lizenz** (AGPLv3, mit der Folge für Weitergabe), **was es nicht ist** (keine
Software von der Stange), **wie der Zustand wirklich ist** (unsigniert,
Testmatrix offen, ARM64-Emulation) und **wie man es baut** — samt der
Warnung, dass das SDK nicht im Repo liegt und `build.ps1` Pflicht ist.

Lizenz und SDK-Beschaffung standen vorher in Zeile 1258 und 460; sie stehen
dort weiterhin ausführlich, aber nicht mehr nur dort.

**Ö3 — Der SDK-Spiegel. ✔ erledigt am 14.09.2026 — es braucht keinen.**
Entschieden von Dominic: der Verweis genügt. Er trägt, weil nipp das SDK
**nicht verändert** — eingebunden wird die unveränderte Fassung von
Belledonne, und damit ist deren Veröffentlichung der entsprechende Quelltext
(GPLv3 §6(d), «an einem benannten Ort»). Version, Quelltext-Ort und
Binärpaket stehen jetzt in `NOTICE`; beide Orte wurden am 14.09.2026 mit
HTTP 200 geprüft.

**Der Vorbehalt steht dabei:** wer das SDK eines Tages selbst baut (Weg B),
muss den veränderten Quelltext mitliefern — dann reicht der Verweis nicht
mehr.

**Ö4 — Die Brücke. ✔ vorbereitet am 14.09.2026.** Das neue Repo heisst
`bv2-GmbH/nipp-softphone`. Im Code zeigt `RepositoryUrl` bereits dorthin.

**Der heikle Teil, den man leicht falsch macht:** Upload-Ziel und Suchziel
sind **nicht dasselbe**. Die Brücke muss ins **alte** Repo hochgeladen werden
— dort suchen die installierten Arbeitsplätze —, während das Paket selbst
schon auf das neue zeigt. `Release-Nipp.ps1` hat dafür jetzt `-UploadRepo`;
der Standard ist das neue Repo, für die Brücke wird einmalig das alte
angegeben:

```powershell
$env:GITHUB_TOKEN = gh auth token
.uild\Release-Nipp.ps1 -Version 0.9.5 -Channel stable -Publish `
  -UploadRepo 'https://github.com/bv2-GmbH/nipp'
```

Danach an einem Arbeitsplatz prüfen: Er zieht 0.9.5 aus dem alten Repo, und
die **nächste** Prüfung landet beim neuen. Erst wenn das steht, geht es
weiter.

**Ö5 — Das neue Repo. ✔ erledigt am 14.09.2026.**
`https://github.com/bv2-GmbH/nipp-softphone`, öffentlich, ein Commit
(`c4da665`), 466 Dateien, 11,9 MB.

**Der Weg dorthin ohne Kopiererei:** ein `--orphan`-Branch im bestehenden
Repo, dort alles committen, und ihn als `main` ins neue Repo pushen. Das
lokale Repo behält dabei seine ganze Historie; nur der eine Commit geht
hinaus.

**Was vorher geprüft wurde:** `PublicRepositoryTests` grün, unter
`Catalog/templates/` nur `custom-rest.json`, `.gitignore` deckt SDK, `dist/`,
`bin/` und `obj/`, Arbeitsbaum sauber — und ein Scan über alle 466 Dateien
nach IP- und Mailadressen. Die Treffer waren Versionsnummern, OIDs, der
Platzhalter `pbx.example.ch` und eine öffentliche Kontaktadresse.

**Ö6 — Umschalten.** Die Release-Kette auf das neue Repo zeigen lassen, ein
Release 0.9.6 dort veröffentlichen, an einem Arbeitsplatz die
Aktualisierung prüfen. **Das Token fällt hier weg** — öffentliche
Release-Assets brauchen keines mehr, und `update.token` kann aus der
Provisionierung verschwinden.

**Ö7 — Das alte Repo archivieren**, nicht löschen. Es ist die Geschichte des
Projekts, und es enthält nichts, was gelöscht gehört — nur nichts, was
öffentlich gehört.

## 5. Was dieser Plan nicht tut

- **Er macht nichts öffentlich, bevor Ö2 und Ö3 stehen.** Öffentlich ist
  unumkehrbar: was einmal geklont wurde, ist draussen.
- **Er löscht nichts.** Das alte Repo bleibt, archiviert.
- **Er ändert die Lizenz nicht.** AGPLv3 steht seit ADR-040.

## 6. Eine Lehre aus dem Schreiben dieses Plans

**Der Wächter hat diesen Plan erwischt**, bei seinem ersten Testlauf: Die
ursprüngliche Fassung führte die drei Muster ausgeschrieben in einer Tabelle
auf — in genau dem Dokument, das erklärt, warum sie nicht ins Repo gehören.

`PublicRepositoryTests` setzt seine eigenen Muster deshalb aus Teilstücken
zusammen, mit dem Kommentar «damit dieser Test nicht selbst der einzige
Treffer ist». **Diese Vorsichtsmassnahme gilt für jedes Dokument, das über die
Muster spricht** — auch für einen Plan, auch für ein ADR, auch für eine
Protokollzeile, die man zur Veranschaulichung zitiert.

## 7. Protokoll

| Datum | Was | Ergebnis |
|---|---|---|
| 14.09.2026 | Historie über alle 181 Commits geprüft | 24, 16 und 20 Commits für die drei Muster; **kein echtes Geheimnis**, der Token-Treffer ist ein Platzhalter |
| 14.09.2026 | **Ö2** — README für Fremde | Kopf mit Lizenz, Zustand, Bauanleitung und SDK-Beschaffung vorangestellt |
| 14.09.2026 | **Ö3** — SDK-Quelltext | **Kein Spiegel nötig**: Verweis auf Belledonne in `NOTICE`, beide Orte mit HTTP 200 geprüft |
| 14.09.2026 | **Ö4** — Brücke vorbereitet | `RepositoryUrl` zeigt auf `nipp-softphone`; `Release-Nipp.ps1` trennt Upload-Ziel (`-UploadRepo`) vom Suchziel |
| 14.09.2026 | **Ö5** — neues Repo | `bv2-GmbH/nipp-softphone`, öffentlich, ein Commit `c4da665`; vorher Wächter, Vorlagen, `.gitignore` und ein Adressen-Scan über alle 466 Dateien |
| — | Ö1, Ö6, Ö7 | stehen aus |
