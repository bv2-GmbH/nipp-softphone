"""Erzeugt alle Symbole von nipp aus einer einzigen Quelldatei.

Aufruf:  python tools/Build-Icons.py nipp-v2-logo.png

Warum ein Skript und nicht sechzehn Dateien von Hand: nipp braucht sein Symbol
in vier verschiedenen Rollen (EXE, Infobereich, Titelleiste, Paketmanifest) und
in Grössen von 16 bis 620 Pixeln. Wer das einmal von Hand macht, macht es beim
nächsten Logo wieder — und vergisst dabei eine.

Die Quelle sollte quadratisch, gross (mindestens 512 Pixel) und mit
transparentem Hintergrund sein.
"""

import os
import sys
from PIL import Image

WURZEL = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(WURZEL, "src", "Nipp.App", "Assets")
ICONS = os.path.join(WURZEL, "Icons")


def beschnitten(bild):
    """Entfernt den durchsichtigen Rand.

    Der Grund ist die Lesbarkeit im Kleinen: ein Motiv mit zehn Prozent Luft
    ringsum verliert bei 16 Pixeln zehn Prozent seiner ohnehin knappen Fläche.
    Im Infobereich ist das der Unterschied zwischen einem erkennbaren Symbol
    und einem farbigen Fleck.
    """
    if bild.mode != "RGBA":
        bild = bild.convert("RGBA")

    rand = bild.getbbox()
    return bild.crop(rand) if rand else bild


def quadrat(bild, kante):
    """Skaliert auf ein Quadrat, mittig, ohne zu verzerren."""
    breite, hoehe = bild.size
    faktor = kante / max(breite, hoehe)
    neu = bild.resize(
        (max(1, round(breite * faktor)), max(1, round(hoehe * faktor))),
        Image.LANCZOS,
    )

    flaeche = Image.new("RGBA", (kante, kante), (0, 0, 0, 0))
    flaeche.paste(neu, ((kante - neu.width) // 2, (kante - neu.height) // 2), neu)
    return flaeche


def rechteck(bild, breite, hoehe):
    """Legt das Motiv mittig auf eine durchsichtige Fläche der gewünschten Form.

    Für die Splash- und Wide-Kacheln des Paketmanifests: sie sind breiter als
    hoch, und ein quadratisches Motiv darauf zu strecken würde die Tasse
    verbeulen.
    """
    hoch = round(hoehe * 0.86)
    motiv = quadrat(bild, hoch)

    flaeche = Image.new("RGBA", (breite, hoehe), (0, 0, 0, 0))
    flaeche.paste(motiv, ((breite - hoch) // 2, (hoehe - hoch) // 2), motiv)
    return flaeche


def schreibe_png(bild, pfad):
    bild.save(pfad, "PNG", optimize=True)
    print(f"  {os.path.relpath(pfad, WURZEL)}  {bild.size[0]}x{bild.size[1]}")


def schreibe_ico(bild, pfad, kanten):
    """Ein ICO mit mehreren Auflösungen.

    Windows greift je Stelle die passende heraus: 16 für den Infobereich und
    Menüs, 32 für die Taskleiste, 256 für die grosse Kachelansicht im Explorer.
    Fehlt eine Grösse, skaliert Windows selbst — und das sieht sichtbar
    schlechter aus als eine eigens gerechnete Fassung.
    """
    # <b>Ein quadratisches Bild in der groessten Kantenlaenge uebergeben und
    # PIL die Stufen rechnen lassen.</b>
    #
    # Der naheliegende Weg — jede Stufe selbst rechnen und per append_images
    # anhaengen — sieht richtig aus und schreibt eine Datei mit genau einer
    # Auflaesung: append_images gilt fuer GIF und TIFF, nicht fuer ICO. Der
    # Fehler ist still, und die Datei laedt anschliessend ueberall, nur
    # unscharf. Aufgefallen erst beim Nachzaehlen der enthaltenen Groessen.
    #
    # PIL skaliert intern mit LANCZOS, also demselben Filter, den quadrat()
    # nimmt. Deshalb kostet der Umweg keine Qualitaet.
    gross = quadrat(bild, max(kanten))
    gross.save(pfad, format="ICO", sizes=[(k, k) for k in kanten])

    kontrolle = Image.open(pfad)
    enthalten = sorted(k for k, _ in kontrolle.info.get("sizes", []))

    if enthalten != sorted(kanten):
        raise RuntimeError(
            f"{pfad} enthaelt {enthalten}, erwartet waren {sorted(kanten)}"
        )

    print(f"  {os.path.relpath(pfad, WURZEL)}  {enthalten}")


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1

    quelle = sys.argv[1]

    if not os.path.isabs(quelle):
        quelle = os.path.join(WURZEL, quelle)

    if not os.path.exists(quelle):
        print(f"Quelle nicht gefunden: {quelle}")
        return 1

    roh = Image.open(quelle)
    print(f"Quelle: {os.path.relpath(quelle, WURZEL)}  {roh.size[0]}x{roh.size[1]}  {roh.mode}")

    logo = beschnitten(roh)
    print(f"Nach dem Beschneiden: {logo.size[0]}x{logo.size[1]}")
    print()

    print("EXE-Symbol (Taskleiste, Alt+Tab, Task-Manager, angepinnte Verknuepfung):")
    schreibe_ico(logo, os.path.join(ASSETS, "AppIcon.ico"), [16, 24, 32, 48, 64, 128, 256])

    print()
    print("Infobereich — zwei Dateien, gleicher Inhalt:")
    print("  Der Code waehlt nach Erscheinungsbild (TrayIconHost). Das frueher")
    print("  einfarbige Symbol brauchte eine helle und eine dunkle Fassung; ein")
    print("  farbiges Logo traegt sich auf beiden Untergruenden selbst. Die")
    print("  zwei Dateien bleiben, damit der Code unveraendert bleibt — und")
    print("  damit ein spaeteres Logo wieder zwei Fassungen haben darf.")
    for name in ("TrayLight.ico", "TrayDark.ico"):
        schreibe_ico(logo, os.path.join(ASSETS, name), [16, 20, 24, 32, 48])

    print()
    print("Titelleiste (nach Erscheinungsbild, ueber ms-appx geladen):")
    for ziel in (ASSETS, ICONS):
        for name in ("nipp-dark.png", "nipp-light.png"):
            schreibe_png(quadrat(logo, 256), os.path.join(ziel, name))

    print()
    print("Paketmanifest (Startmenue, Store, Sperrbildschirm):")
    quadrate = {
        "Square44x44Logo.png": 44,
        "Square44x44Logo.scale-200.png": 88,
        "Square150x150Logo.png": 150,
        "Square150x150Logo.scale-200.png": 300,
        "Square71x71Logo.png": 71,
        "Square71x71Logo.scale-200.png": 142,
        "Square310x310Logo.png": 310,
        "StoreLogo.png": 50,
        "LockScreenLogo.scale-200.png": 24,
    }

    # Die Zielgroessen — und der Grund, warum es sie in dieser Zahl braucht.
    #
    # Fuer eine ANGEPINNTE packaged App liest Windows nicht Square44x44Logo,
    # sondern "Square44x44Logo.targetsize-<N>_altform-unplated.png": ohne den
    # Kachelhintergrund, in der Groesse des Platzes. Vorhanden war davon lange
    # nur die 24er, weshalb Windows fuer einen 32-Pixel-Platz in der
    # Taskleiste die 24er hochskalierte — unscharf, und beim Anpinnen fiel es
    # zuerst auf.
    #
    # "lightunplated" ist dieselbe Groesse fuer helle Untergruende (helle
    # Taskleiste, hoher Kontrast). Ein farbiges Logo braucht keine zweite
    # Fassung, aber ohne die Datei nimmt Windows in diesem Fall wieder die
    # naechstbeste Groesse.
    for kante in (16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256):
        quadrate[f"Square44x44Logo.targetsize-{kante}.png"] = kante
        quadrate[f"Square44x44Logo.targetsize-{kante}_altform-unplated.png"] = kante
        quadrate[f"Square44x44Logo.targetsize-{kante}_altform-lightunplated.png"] = kante

    for name, kante in quadrate.items():
        schreibe_png(quadrat(logo, kante), os.path.join(ASSETS, name))

    rechtecke = {
        "Wide310x150Logo.png": (310, 150),
        "Wide310x150Logo.scale-200.png": (620, 300),

        # Beide Fassungen des Startbilds: das Manifest verweist auf
        # "SplashScreen.png", und ohne die scale-100-Datei haengt die Anzeige
        # daran, dass die Ressourcenverwaltung die 200er findet.
        "SplashScreen.png": (620, 300),
        "SplashScreen.scale-200.png": (1240, 600),
    }
    for name, (breite, hoehe) in rechtecke.items():
        schreibe_png(rechteck(logo, breite, hoehe), os.path.join(ASSETS, name))

    print()
    print("Fertig. Danach unpackaged neu bauen (-t:Rebuild) — das EXE-Symbol")
    print("wird beim Kompilieren eingebettet, ein inkrementeller Build nimmt es")
    print("nicht mit.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
