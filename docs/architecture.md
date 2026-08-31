# Technische Architektur

## Fester Modus beim Start

Die Firmware liest GP12 bis GP14, bevor TinyUSB initialisiert wird. Der gewählte Modus
bleibt bis zum Abziehen unverändert. Unterschiedliche Produkt-IDs verhindern,
dass Windows zwischengespeicherte USB-Deskriptoren des anderen Modus verwendet.

| Start | USB-Schnittstellen | Produkt-ID | LED |
|---|---|---:|---|
| keine Taste | genau ein Boot-Keyboard-HID | `CAFE:4030` | grün |
| nur linke Taste gehalten | genau ein Boot-Keyboard-HID | `CAFE:4030` | violett |
| mittlere Taste gehalten | genau ein Generic-IN/OUT-HID | `CAFE:4031` | blau |
| alle drei Tasten gehalten | RP2040-ROM-Update-Laufwerk | ROM | weiß |

`0xCAFE` ist eine Prototyp-VID und nicht für eine öffentliche Produktverteilung
vorgesehen. Für ein vertriebenes Produkt ist eine eigene, rechtmäßig
zugewiesene VID/PID erforderlich.

TARGET und AFK enthalten absichtlich kein CDC, Mass Storage, MIDI, Netzwerk und
keine Vendor-Konfiguration. LOAD enthält absichtlich kein Keyboard. Deshalb
kann der Lade-PC während einer Übertragung keine Tastatureingaben vom Gerät
erhalten.

TARGET und AFK staffeln echtes HID Right-Alt automatisch: Modifier-only, 35 ms
Einschwingzeit, Modifier plus Taste, 15 ms Haltezeit, Taste loslassen, 35 ms und
erst dann den Modifier loslassen. Das entspricht einem physischen AltGr-
Tastendruck besser als ein gemeinsamer Ein-Millisekunden-Bericht. Andere
Modifier behalten den schnellen normalen Ablauf.

## Datenfluss

```text
System-Zwischenablage (Windows, macOS oder Linux)
    -> CopyCop-GUI oder copycop-cli
    -> gemeinsamer CopyCop.Core
    -> 64-Byte-HID-Protokoll im LOAD-Modus
    -> CRC-geprüfter A/B-Flash-Datensatz
    -> Abziehen
    -> Keyboard-HID im TARGET-Modus
    -> deutsches QWERTZ am Ziel-PC
```

Die GUI basiert auf Avalonia und läuft ohne Konsolenfenster. Die CLI bleibt als
separates Programm verfügbar. Beide verwenden HidSharp für den
plattformübergreifenden HID-Zugriff und exakt dieselbe Textanalyse. Die GUI
bewertet Zeichen, UTF-8-Bytes und Auslastung live. `TextSplitter` zerlegt zu
großen Text ohne Datenverlust, ohne Surrogate zu trennen, bevorzugt an
Zeilen- und Wortgrenzen.

Ein physisches C-Ereignis liest Text aus der System-Zwischenablage; die GUI
erlaubt zusätzlich einen expliziten Senden-Knopf. Der Core normalisiert
unterstützte Zeichen, berechnet CRC32 und überträgt in 40-Byte-Nutzdatenblöcken.
Das Protokoll verwendet `HELLO`,
`GET_INFO`, `BEGIN_TRANSFER`, `DATA`, `END_TRANSFER`, `GET_STATUS`, `CLEAR` und
ein asynchrones `COPY_EVENT`. Jeder 64-Byte-Frame enthält Version,
Sequenznummer, Längenfelder und eine eigene CRC32.

Erst `END_TRANSFER` löst das Schreiben in die inaktive Flash-Bank aus. Der
Commit-Marker wird nach Rücklesen und Prüfen des Inhalts zuletzt geschrieben.

## Ausgabe und Zustimmung

Der Typer ist eine nicht blockierende Zustandsmaschine. Er sendet pro Zeichen
Modifier und Taste, danach einen leeren Release-Report und wartet die gewählte
Zeit. AltGr wird mit getrennten Modifier-, Tasten- und Release-Berichten
gestaffelt. Dead Keys wie `~`, Akut und Backtick erhalten anschließend ein
Leerzeichen. Ein kurzer V-Druck pausiert am nächsten sicheren Release-Punkt;
ein weiterer kurzer Druck setzt denselben Text fort. Nach 800 ms Haltedauer
wechselt V in den Release-All-Abbruchpfad.

Beim Anschluss, bei der USB-Anmeldung und nach einem Reset wird nie gespeicherter
Text ausgegeben. Im TARGET-Modus startet nur eine entprellte physische
V-Flanke das Tippen; im bewusst gewählten AFK-Modus starten C oder Strg+C.

Im normalen TARGET-Modus wählt die linke Taste die nächstlangsamere und die
mittlere Taste die nächstschnellere der acht persistenten Stufen. Das gilt auch
während einer laufenden Ausgabe; die Zustandsmaschine übernimmt die neue Pause
ab dem nächsten Zeichenabstand und schreibt die letzte Auswahl erst nach Ende
oder Abbruch in das Flash-Journal. Im AFK-Modus startet die mittlere Taste eine
Endloswiederholung, die rechte stoppt sie und links plus Mitte startet einen
einzelnen Durchlauf. Jeder Abstand zwischen HID-Tastenberichten und jede Pause
zwischen AFK-Wiederholungen wird unabhängig aus
`10, 25, 50, 100, 250, 500, 750, 1000 ms` gezogen. Die GPIO-Tasten selbst werden
dabei nie als die Zeichen C oder V an USB weitergegeben.

## Dienste

- `board_config`: alle GPIO-, Flash-, USB- und Zeitkonstanten
- `buttons`: aktive Low-Eingänge, Boot-Abtastung und Entprellung
- `status_led`: WS2812-PIO-Ausgabe
- `usb_descriptors` / `usb_device`: getrennte LOAD- und Keyboard-Persönlichkeiten
- `protocol`: gerahmter LOAD-Transport
- `storage`: Flash-Journal und atomarer A/B-Textdatensatz
- `keyboard_layout_de`: striktes UTF-8 und deutsche HID-Zuordnung
- `typer`: abbrechbare, zeitgesteuerte Tastaturausgabe

Die Firmware läuft einkernig und ereignisgesteuert. Flash-Zugriffe verwenden
`flash_safe_execute`; ein Watchdog überwacht den Hauptloop.

Die Hostseite ist in drei Projekte getrennt:

- `CopyCop.Core`: HID, Protokoll, CRC, Layout, Bewertung, Aufteilung und Tippdauer
- `CopyCop.Gui`: Avalonia-Desktopoberfläche für Windows, macOS und Linux
- `copycop-cli`: plattformübergreifende Kommandozeile
