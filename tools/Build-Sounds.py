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


#: Spitzenpegel des Anklopftons, in dBFS gegen Vollaussteuerung (0 dBFS).
#: Richtwert aus LOGBEFUNDE-PLAN.md R1.0: deutlich leiser als der Gespraechs-
#: pegel, damit er auffaellt, ohne das Gespraech zu uebertoenen. UNGEHOERT —
#: dieser Wert ist nicht an der Anlage geprueft, siehe T341.
ANKLOPFTON_PEGEL_DBFS = -18.0

#: Dauer der beiden Toene des Doppeltons, in Sekunden. Kurz genug, dass er
#: wie ein Hinweis klingt und nicht wie ein zweites Klingeln. UNGEHOERT.
ANKLOPFTON_TONDAUER = 0.12

#: Abstand zwischen den beiden Toenen des Doppeltons, in Sekunden. UNGEHOERT.
ANKLOPFTON_TONABSTAND = 0.16

#: Abklingzeit (e-Funktion) der beiden Toene, in Sekunden — weich statt
#: abgeschnitten, wie bei anschlag() sonst auch. UNGEHOERT.
ANKLOPFTON_ABKLINGEN = 0.09

#: Die beiden Frequenzen des Doppeltons, in Hz. Eine kleine Terz abwaerts
#: (880 -> 740 Hz) statt aufwaerts, damit er sich vom aufsteigenden
#: Klingelton (klingelton()) unterscheidet und nicht wie ein zweiter Anruf
#: klingt, sondern wie ein kurzer Hinweis daneben. UNGEHOERT.
ANKLOPFTON_FREQUENZEN = (880.00, 739.99)


def anklopfton():
    """Ein leiser, kurzer Doppelton fuer den zweiten eingehenden Anruf (R1.0).

    Ersetzt den generierten Ton des SDK (440 Hz, amplitude=1.000000, alle
    zwei Sekunden unbegrenzt, in den Audioweg des laufenden Gespraechs
    gespielt — LOGBEFUNDE-PLAN.md L12). Diese Datei wird ueber
    Core.SetTone(ToneID.CallWaiting, ...) gesetzt und ersetzt nur die
    Lautstaerke und den Klang; ob und wie oft das SDK sie wiederholt, ist
    nicht Teil dieser Datei und beim Build nicht geprueft (siehe T341).

    Die Zahlen hier sind ein Anfang, keine Abnahme: benannt, damit sie sich
    aendern lassen, ohne ein Wellenbild lesen zu muessen — aber UNGEHOERT.
    Was leise genug ist, um nicht zu stoeren, und auffaellig genug, um nicht
    uebergangen zu werden, entscheidet der Hoertest aus T341, nicht diese
    Datei.
    """
    hoch, tief = ANKLOPFTON_FREQUENZEN

    proben = ueberlagert(
        [
            (0.00, anschlag(hoch, ANKLOPFTON_TONDAUER, ANKLOPFTON_ABKLINGEN)),
            (ANKLOPFTON_TONABSTAND, anschlag(tief, ANKLOPFTON_TONDAUER, ANKLOPFTON_ABKLINGEN)),
        ],
        laenge=ANKLOPFTON_TONABSTAND + ANKLOPFTON_TONDAUER + 0.2,
    )

    return normalisiert(proben, spitze_dbfs=ANKLOPFTON_PEGEL_DBFS)


def main():
    os.makedirs(KLAENGE, exist_ok=True)
    print(f"Klaenge nach {KLAENGE}")

    schreibe_wav(os.path.join(KLAENGE, "nipp-ring.wav"), klingelton())
    schreibe_wav(os.path.join(KLAENGE, "nipp-ringback.wav"), freizeichen())
    schreibe_wav(os.path.join(KLAENGE, "nipp-callwaiting.wav"), anklopfton())

    print()
    print("Fertig. Die Dateien gehen ueber den Content-Eintrag im csproj ins")
    print("Ausgabeverzeichnis und ueber AppxPackagePayload ins MSIX.")
    print()
    print("nipp-callwaiting.wav ist UNGEHOERT (R1.0, LOGBEFUNDE-PLAN.md) —")
    print("vor dem Ausliefern an der Anlage abhoeren (T341).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
