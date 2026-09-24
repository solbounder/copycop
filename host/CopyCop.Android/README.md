# CopyCop für Android

Die Android-App lädt Text über USB direkt auf CopyCop. Textanalyse,
Unicode-Normalisierung, Aufteilung, CRC und Übertragungsprotokoll stammen aus
demselben `CopyCop.Core`, den auch Desktop-GUI und CLI verwenden.

## Voraussetzungen

- Android 8.0 (API 26) oder neuer
- Smartphone oder Tablet mit USB-Host-/OTG-Unterstützung
- passendes USB-OTG-Kabel oder ein USB-OTG-Adapter
- CopyCop-Firmware mit dem LOAD-Protokoll `CAFE:4031`

## Benutzung

1. App öffnen.
2. CopyCop abziehen.
3. Die mittlere C-Taste am Gerät halten und CopyCop mit dem Android-Gerät
   verbinden.
4. Den Android-Dialog für den USB-Zugriff bestätigen.
5. Über **Datei öffnen …** eine Textdatei auswählen, Text einfügen oder aus der
   Zwischenablage übernehmen und auf
   **Text auf CopyCop speichern** tippen.
6. CopyCop nach der grünen Bestätigung abziehen und im normalen Modus mit dem
   Ziel-PC verbinden.

Solange die App im Vordergrund ist, übernimmt die physische C-Taste im
LOAD-Modus die Android-Zwischenablage und startet den Transfer automatisch,
wenn der Text vollständig übertragbar ist. Android erlaubt Apps im Hintergrund
keinen allgemeinen Zwischenablagezugriff; in diesem Fall fordert die App zum
manuellen Öffnen auf.

## Textdateien einlesen

**Datei öffnen …** öffnet Androids Dateiauswahl, etwa für Dateien aus Downloads
oder einem installierten Dokumentanbieter. Es ist keine allgemeine
Speicherberechtigung erforderlich. Die App liest jeweils eine Datei in den
Editor; erst **Text auf CopyCop speichern** überträgt ihren geprüften Inhalt.
Die physische C-Taste bleibt für die Zwischenablage zuständig.

Unterstützt werden UTF-8 (mit oder ohne BOM) sowie UTF-16/UTF-32 mit BOM, bis
zu 4 MiB pro Datei. Auch Quellcodedateien können ausgewählt werden. Die
vorhandene Zeichenprüfung, Tippdauer und Aufteilung in gerätegerechte Teile
gelten ebenso für importierten Text. Bei Abbruch oder Lesefehlern bleibt der
bisherige Editorinhalt erhalten. Während Auswahl, Einlesen und Übertragung
sind konkurrierende Bearbeitungsaktionen gesperrt.

CopyCop gibt den Textinhalt als Tastatureingabe aus; es legt keine Datei auf
dem Ziel-PC an. PDF-, Office- und Bilddateien werden nicht in Text umgewandelt.

## Bauen

Erforderlich sind das .NET-8-SDK, die Android-Workload und ein Android-SDK:

```powershell
dotnet workload install android
powershell -File host/packaging/publish-android.ps1
```

Das lokal installierbare APK liegt danach unter
`host/release/android/CopyCop-Android.apk`. Ohne eigene Signierparameter nutzt
der lokale .NET-Android-Build den Debug-Schlüssel und ist damit für Tests und
direktes Sideloading gedacht, nicht für Google Play.

Der USB-Transport verwendet Androids `UsbManager`, übernimmt die HID-
Schnittstelle exklusiv, sendet HID-Output-Reports über `SET_REPORT` und liest
den Interrupt-IN-Endpunkt asynchron in einen direkten Java-USB-Puffer. Die
Firmware muss dafür nicht geändert werden.
