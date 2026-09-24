# copycop-cli

## Dateipakete ohne Gerät

```text
copycop-cli pack paket.copycop datei1.txt datei2.py [--no-compress]
copycop-cli unpack paket.copycop neuer-zielordner
```

Mehrere Dateien werden samt Basisnamen bytegenau gebündelt, optional mit
ZIP-Deflate komprimiert und als nummerierter ASCII-Text gespeichert. Die CLI
überschreibt weder vorhandene Paketdateien noch bestehende Zielordner.
Zum Empfangen im Browser liegt `web/copycop.html` bei. Details und Grenzen
stehen in [`docs/copycop-format.md`](../../docs/copycop-format.md).

## Text oder Paket auf CopyCop laden

Die CLI läuft unter Windows, macOS und Linux und besitzt dieselbe
Kapazitäts-, Unicode-, Split- und HID-Logik wie die grafische CopyCop-App.

```text
copycop-cli [--file PATH] [--replace-unsupported] [--part N] [--once]
```

- `--replace-unsupported`: unbekannte Unicode-Zeichen als `?` speichern
- `--part N`: bei übergroßem Text automatisch erzeugten Teil N speichern
- `--once`: nach einem Ladeversuch beenden
- `--file PATH`: eine Textdatei statt der Zwischenablage verwenden

Beispiel: `copycop-cli --file "C:\Texte\beispiel.txt" --once` liest die Datei
beim Start ein. Nach Verbindung im LOAD-Modus startet die physische C-Taste
die Übertragung dieses Inhalts. Änderungen an der Datei erfordern einen
Neustart der CLI. Unterstützt werden UTF-8 sowie UTF-16/UTF-32 mit BOM, bis
zu 4 MiB. Leere, unlesbare oder ungültig kodierte Dateien werden vor der
Gerätesuche gemeldet. Der Ziel-PC erhält den Text als Tastatureingabe.

Ohne `--part` zeigt die CLI alle Teilgrößen und fragt interaktiv nach der
gewünschten Teilnummer. Jeder Teil ist höchstens 126.464 UTF-8-Bytes groß.

Unter Linux verwendet die Zwischenablagebibliothek `xsel`. Für den Zugriff auf
CopyCop ist außerdem die udev-Regel unter `host/linux/99-copycop.rules` nötig.
Die GUI verwendet unter Linux die native Avalonia-Zwischenablage und benötigt
`xsel` daher nicht.
