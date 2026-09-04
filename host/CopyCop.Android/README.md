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
5. Text einfügen oder aus der Zwischenablage übernehmen und auf
   **Text auf CopyCop speichern** tippen.
6. CopyCop nach der grünen Bestätigung abziehen und im normalen Modus mit dem
   Ziel-PC verbinden.

Solange die App im Vordergrund ist, übernimmt die physische C-Taste im
LOAD-Modus die Android-Zwischenablage und startet den Transfer automatisch,
wenn der Text vollständig übertragbar ist. Android erlaubt Apps im Hintergrund
keinen allgemeinen Zwischenablagezugriff; in diesem Fall fordert die App zum
manuellen Öffnen auf.

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
den Interrupt-IN-Endpunkt asynchron. Die Firmware muss dafür nicht geändert
werden.
