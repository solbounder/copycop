# ClipboardBridge CLI

Die CLI läuft unter Windows, macOS und Linux und besitzt dieselbe
Kapazitäts-, Unicode-, Split- und HID-Logik wie die grafische CopyCop-App.

```text
ClipboardBridge [--replace-unsupported] [--part N] [--once]
```

- `--replace-unsupported`: unbekannte Unicode-Zeichen als `?` speichern
- `--part N`: bei übergroßem Text automatisch erzeugten Teil N speichern
- `--once`: nach einem Ladeversuch beenden

Ohne `--part` zeigt die CLI alle Teilgrößen und fragt interaktiv nach der
gewünschten Teilnummer. Jeder Teil ist höchstens 126.464 UTF-8-Bytes groß.

Unter Linux verwendet die Zwischenablagebibliothek `xsel`. Für den Zugriff auf
CopyCop ist außerdem die udev-Regel unter `host/linux/99-copycop.rules` nötig.
Die GUI verwendet unter Linux die native Avalonia-Zwischenablage und benötigt
`xsel` daher nicht.
