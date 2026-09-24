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

## Dateipakete vorbereiten

Für eingefügten oder eingelesenen Text gibt es außerdem **Text komprimieren –
Empfang über HTML**. Der Haken ist standardmäßig aus. Eingeschaltet wird der
Originaltext als `text.txt` verpackt, während er im Editor erhalten bleibt.
Die Anzeige zeigt Paketgröße und Ersparnis oder Mehrbedarf. Speichern,
Gerätetransfer, Aufteilung und Tippdauer verwenden die vorbereitete Fassung;
auch die physische C-Taste beachtet den Haken. Am Ziel in `copycop.html`
empfangen und entpacken. Für direktes Tippen in andere Apps oder zum Übertragen
der selbstentpackenden Empfangs-HTML den Haken ausschalten. Die Einstellung
bleibt bei einer Wiederherstellung der Activity erhalten.

**Dateien hinzufügen …** und **Ordner hinzufügen …** ergänzen die bestehende
Dateiliste. Mehrfachauswahl ist optional: Mehrere nacheinander ausgewählte
Einzeldateien bleiben gemeinsam in der Liste. Mit der Dateiliste und
**Ausgewählte Datei entfernen** lässt sich gezielt ein Eintrag entfernen;
**Auswahl leeren** beginnt eine neue Auswahl. Identische Dateien werden
übersprungen, gleichnamige Dateien mit anderem Inhalt nicht überschrieben.

**Paket erstellen** bündelt die Liste mit optionaler Kompression und bereitet
nummerierte Geräteteile vor. **Als .copycop speichern …** öffnet den
Android-Speicherdialog; **Datei öffnen …** lädt das Paket samt Dateiliste wieder,
damit es ergänzt werden kann. Text im Editor lässt sich als enthaltene
`text.txt` speichern. Änderungen an der Liste oder Kompression erfordern ein
neues Paket; die vorherige Ausgabe wird dabei ungültig.

Auswahl, Abbruch und Fehler erhalten die bisherigen Dateien. Bei einer vom
System neu erstellten Activity bleibt der Zwischenstand in einem privaten
temporären App-Cache erhalten. Die Systemdialoge benötigen keine pauschale
Speicherberechtigung. Es gelten 256 Dateien, 16 MiB Originalinhalt und 4 MiB
Übertragungstext pro Paket.

Am Zielrechner dient `copycop.html` ausschließlich dem Empfang und Entpacken.
Dateiverwaltung und Übertragung auf das Gerät bleiben in der App.

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

Erforderlich sind das .NET-10-SDK, die Android-Workload und ein Android-SDK:

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
