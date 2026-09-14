# SDK-Setup

Wie das Linphone SDK in dieses Projekt kommt. Reproduzierbarkeit ist hier wichtiger als Eleganz (§5) — deshalb steht unten die Prüfsumme.

## Gewählter Weg: C — offizielles Prebuilt-ZIP

Begründung: **ADR-005** (löst ADR-002 ab). Der NuGet-Feed ist zwar anonym lesbar und führt die richtige Version, trägt aber keinen Build — der erste echte `dotnet restore` scheiterte nach 1.76 Minuten an Timeouts und einem HTTP 500.

| Angabe | Wert |
|---|---|
| Quelle | `https://download.linphone.org/releases/windows/sdk/linphone-sdk-win64-5.5.18.zip` |
| Version | **5.5.18** (Tag-Datum 03.09.2026) |
| Ablage | `sdk/` im Repo-Wurzelverzeichnis — **gitignoriert, nicht eingecheckt** |
| Bezogen am | 04.09.2026, Download in 42 s (7,1 MB/s) |
| Grösse | 298,9 MB |
| **SHA256** | `4C69440CFFD843DA324C7AEDA9A2EA54C9E699AE199AB0A9F102AC26F55BE023` |
| Wrapper | `share/linphonecs/LinphoneWrapper.cs` — 62.253 Zeilen, 2.726 öffentliche Deklarationen, 105 Klassen, 103 Enums |
| Commit-Hash des SDK | *im Paket nicht ersichtlich* |

**Die Prüfsumme ist nicht Zierde.** Beim NuGet-Weg hätte `packages.lock.json` die Reproduzierbarkeit übernommen; beim ZIP-Weg gibt es das nicht. Ohne Version, Hash und Bezugsdatum weiss in sechs Monaten niemand mehr, welcher SDK-Stand in einem Build war.

## Erwartetes Layout im ZIP

```
linphone-sdk/win64/
├── bin/
│   ├── liblinphone.dll
│   ├── mediastreamer2.dll
│   └── belle-sip.dll
├── lib/mediastreamer/plugins/
│   ├── libmswasapi.dll        <- ohne die kein Audio unter Windows
│   ├── libmswebrtc.dll
│   └── libmsopenh264.dll
└── share/linphonecs/
    └── LinphoneWrapper.cs     <- die maßgebliche API-Quelle (§0 Regel 1)
```

## Rückfallebenen

**Weg A — NuGet.** Bleibt in `nuget.config` eingetragen: Feed `https://gitlab.linphone.org/api/v4/projects/411/packages/nuget/index.json` (projectId 411, anonym lesbar), Paket `LinphoneSDK.Windows`, Version 5.5.18 als stabile Version vorhanden. Nutzbar, wenn der Server gerade mitspielt — aber nichts im Build darf davon abhängen.

**Weg B — Selbstbau.** Nur bei fehlendem Codec. Voraussetzungen und Preset-Namen in §5 der Spezifikation.

## Native DLL-Kette — Inventar vom 04.09.2026

**34 DLLs unter `bin/`**, die grössten: `mediastreamer2.dll` (22,9 MB), `liblinphone.dll` (12,8 MB), `xerces-c.dll` (3,7 MB), `ZXing.dll` (1,8 MB), `turbojpeg.dll` (1,8 MB), `belle-sip.dll` (1,4 MB), `belcard.dll` (1,2 MB), `sqlite3.dll` (1,0 MB).

Vollständig: `bctoolbox`, `bctoolbox-tester`, `bcunit`, `belcard`, `belle-sip`, `belr`, `bv16`, `bzrtp`, `decaf`, `gsm`, `hidapi`, `jpeg62`, `jsoncpp`, `libbelle-sip-tester`, `liblinphone`, `lime`, `mbedcrypto`, `mbedtls`, `mbedx509`, `mediastreamer2`, `opus`, `ortp`, `soci_core_4_0`, `soci_sqlite3_4_0`, `speex`, `speexdsp`, `sqlite3`, `srtp2`, `turbojpeg`, `xerces-c`, `xml2`, `yuv`, `zlib1`, `ZXing`.

**3 Plugins unter `lib/mediastreamer/plugins/`** — hier sitzt der Fallstrick:

| Plugin | Grösse | Zweck |
|---|---|---|
| `libmswasapi.dll` | 106 KB | **WASAPI-Audio. Ohne die kein Ton unter Windows.** |
| `libmswebrtc.dll` | 601 KB | WebRTC-Verarbeitung (AEC3, Rauschunterdrückung) |
| `libmsopenh264.dll` | 60 KB | H.264 — für nipp nicht gebraucht (Video ist ausgeschlossen, §2) |

Vier Beobachtungen fürs Kopierskript in AP2.1:

1. Die Plugins liegen **getrennt** von den Haupt-DLLs, wie §14.2 vorhersagt. MSIX findet DLLs in anderen Ordnern als der EXE nicht von selbst — beides muss zusammen aufgehen.
2. `bctoolbox-tester.dll` und `libbelle-sip-tester.dll` (1,4 MB zusammen) sind **Testbibliotheken** und gehören nicht in ein Auslieferungspaket. Beim Kopierskript ausschliessen.
3. `libmsopenh264.dll` ebenfalls nicht — Video ist ausgeschlossen. Weglassen, nicht „schon mal mitnehmen".
4. Die Kette hat 34 Glieder. `dumpbin /dependents` ist keine Formalität: fehlt eines, ist die Fehlermeldung nutzlos.

*Nach dem ersten erfolgreichen Laden hier ergänzen:* das Post-Build-Kopierskript und das Ergebnis der `dumpbin`-Gegenprüfung.

**Der Fallstrick** (§14.2): die Mediastreamer-Plugins liegen unter `lib/mediastreamer/plugins/`, nicht neben den Haupt-DLLs. Ohne `libmswasapi.dll` gibt es kein Audio, und die Fehlermeldung sagt das nicht. Zugleich findet MSIX DLLs in anderen Ordnern als der EXE nicht von selbst — beides muss zusammen aufgehen, und genau das klärt AP2.1.

## Hinweise für ein späteres CI

Das ZIP gehört in einen kontrollierten Speicher (Artefakt-Cache oder Paketablage von bv2), nicht bei jedem Lauf frisch von `download.linphone.org`. Sonst wird die Fremdverfügbarkeit nur vom Entwicklerrechner in die Pipeline verschoben.
