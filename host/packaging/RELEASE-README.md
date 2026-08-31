# CopyCop Hostprogramme

- `CopyCop` / `CopyCop.exe`: grafische Oberfläche ohne Konsolenfenster
- `copycop-cli` / `copycop-cli.exe`: optionale Kommandozeile

Das Quellgerät muss im blauen LOAD-Modus angeschlossen sein: CopyCop abziehen,
mittlere C-Taste halten, USB einstecken und bei blauem Licht loslassen.

Linux: Beide ausführbaren Dateien gegebenenfalls mit `chmod +x` ausführbar
machen. Für HID-Zugriff `99-copycop.rules` als root nach
`/etc/udev/rules.d/` kopieren, die udev-Regeln neu laden und CopyCop neu
einstecken. Die CLI benötigt zusätzlich `xsel`; die GUI nicht.

macOS: Beim ersten Start kann für den nicht signierten lokalen Build
Rechtsklick → Öffnen nötig sein. Die CLI liegt neben `CopyCop.app`.

Jeder gespeicherte Teil darf maximal 126.464 UTF-8-Bytes groß sein. Die GUI
zeigt die genaue Belegung und kann längere Texte verlustfrei aufteilen.

Der Reiter `Bedienung & LEDs` erklärt alle physischen Tastenkombinationen,
Startmodi, Geschwindigkeitsfarben und weiteren LED-Signale direkt in der GUI.
