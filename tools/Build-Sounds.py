"""Erzeugt die Klaenge von nipp: Klingelton und Freizeichen.

Aufruf:  python tools/Build-Sounds.py

Warum erzeugt und nicht mitgeliefert: das Linphone-Paket bringt sechs sanfte
Klingeltoene mit (soft_as_snow und Verwandte), aber alle sechs sind .mkv, und
die dafuer noetige bcmatroska2.dll fehlt im win64-Prebuilt. Abspielbar bleiben
im SDK genau zwei WAV-Dateien: oldphone-mono.wav — die alte Telefonglocke, aus
dem Alltag als „nervend" gemeldet — und toy-mono.wav.

Ein Skript statt zweier Binaerdateien im Repo, damit nachvollziehbar bleibt,
warum die Toene klingen, wie sie klingen. Wer sie aendern will, aendert Zahlen
mit Namen, nicht ein Wellenbild.

Nur Standardbibliothek: kein numpy, kein ffmpeg. Die Dateien sind klein genug,
dass eine Schleife in Python sie in Sekundenbruchteilen schreibt.
"""

import math
import os
import struct
import wave

WURZEL = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
KLAENGE = os.path.join(WURZEL, "src", "Nipp.App", "Assets", "Sounds")

RATE = 48000  # Hz. Was WASAPI auf dieser Hardware ohnehin fahren will —
              # das SDK meldet sonst bei jedem Ton „changing output rate to
              # 44100 Hz is not supported by the device. Keep 48000 Hz".


def stille(sekunden):
    return [0.0] * int(RATE * sekunden)


def anschlag(frequenz, dauer, abfall, obertoene=(1.0, 0.18, 0.06)):
    """Ein angeschlagener Ton mit weichem Einsatz und exponentiellem Abfall.

    Das ist der ganze Unterschied zwischen einem Klingelton und einem Alarm.
    Eine Rechteckhuellkurve — Ton an, Ton aus — erzeugt am Anfang und am Ende
    ein Knacken und klingt dadurch hart; ein Anstieg ueber zwoelf Millisekunden
    und ein Ausklang wie bei einem Glockenspiel klingt nach Instrument.

    Die Obertoene machen die Waerme: ein reiner Sinus klingt nach Messgeraet,
    zweite und dritte Harmonische in geringer Staerke nach Klangkoerper.
    """
    anstieg = int(RATE * 0.012)
    proben = []

    for i in range(int(RATE * dauer)):
        t = i / RATE
        huelle = math.exp(-t / abfall)

        if i < anstieg:
            huelle *= i / anstieg

        wert = 0.0
        for n, staerke in enumerate(obertoene, start=1):
            wert += staerke * math.sin(2.0 * math.pi * frequenz * n * t)

        proben.append(wert * huelle)

    return proben


def ueberlagert(spuren, laenge):
    """Legt Anschlaege mit Startzeitpunkt uebereinander."""
    misch = [0.0] * int(RATE * laenge)

    for start, proben in spuren:
        versatz = int(RATE * start)
        for i, wert in enumerate(proben):
            ziel = versatz + i
            if ziel < len(misch):
                misch[ziel] += wert

    return misch


def normalisiert(proben, spitze_dbfs):
    """Skaliert auf einen Spitzenpegel in dBFS.

    Kein Ton wird auf Vollaussteuerung geschrieben: der Klingelton kommt in
    einer stillen Umgebung ueberraschend, und die Lautstaerke gehoert dem
    Benutzer und seiner Windows-Mischung, nicht der Datei.
    """
    hoechster = max(abs(wert) for wert in proben) or 1.0
    ziel = 10.0 ** (spitze_dbfs / 20.0)
    faktor = ziel / hoechster

    return [wert * faktor for wert in proben]


def schreibe_wav(pfad, proben):
    daten = b"".join(
        struct.pack("<h", max(-32768, min(32767, int(wert * 32767.0))))
        for wert in proben
    )

    with wave.open(pfad, "wb") as datei:
        datei.setnchannels(1)
        datei.setsampwidth(2)
        datei.setframerate(RATE)
        datei.writeframes(daten)

    print(f"  {os.path.basename(pfad):22} {len(proben) / RATE:5.2f} s  {len(daten) + 44:7d} B")


def klingelton():
    """Ein aufsteigender Dreiklang, dann drei Sekunden Ruhe.

    Die Ruhe ist der eigentliche Punkt. Das SDK spielt den Klingelton in
    Schleife, solange es klingelt; eine Datei, die durchgehend klingt, wird
    damit zu einem Dauerton. Die Pause macht aus dem Ton ein Klingeln, das man
    aussitzen kann, ohne dass es druengt.
    """
    d5 = 587.33
    fis5 = 739.99
    a5 = 880.00

    proben = ueberlagert(
        [
            (0.00, anschlag(d5, 1.4, 0.45)),
            (0.18, anschlag(fis5, 1.4, 0.45)),
            (0.36, anschlag(a5, 1.6, 0.55)),
        ],
        laenge=4.0,
    )

    return normalisiert(proben, spitze_dbfs=-12.0)


def freizeichen():
    """Der Schweizer Rufton: 425 Hz, eine Sekunde an, vier Sekunden aus.

    Bewusst der gewohnte Ton und keine Eigenschoepfung — beim Waehlen will
    niemand ueberrascht werden, sondern hoeren, dass es beim Gegenueber
    klingelt.

    Zwei Zyklen sind zehn Sekunden. Laenger braucht die Datei nicht zu sein:
    der Wachthund in SipService startet sie neu, solange gewaehlt wird. Und
    kuerzer sollte sie nicht sein, damit ein ausgefallener Neustart nicht
    sofort auffaellt — dreissig Sekunden waeren dafuer knapp drei Megabyte im
    Paket, fuer nichts.
    """
    ton = []
    anstieg = int(RATE * 0.010)
    laenge = int(RATE * 1.0)

    for i in range(laenge):
        huelle = 1.0

        if i < anstieg:
            huelle = i / anstieg
        elif i > laenge - anstieg:
            huelle = (laenge - i) / anstieg

        ton.append(math.sin(2.0 * math.pi * 425.0 * i / RATE) * huelle)

    zyklus = ton + stille(4.0)

    return normalisiert(zyklus * 2, spitze_dbfs=-18.0)


def main():
    os.makedirs(KLAENGE, exist_ok=True)
    print(f"Klaenge nach {KLAENGE}")

    schreibe_wav(os.path.join(KLAENGE, "nipp-ring.wav"), klingelton())
    schreibe_wav(os.path.join(KLAENGE, "nipp-ringback.wav"), freizeichen())

    print()
    print("Fertig. Die Dateien gehen ueber den Content-Eintrag im csproj ins")
    print("Ausgabeverzeichnis und ueber AppxPackagePayload ins MSIX.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
