# Dateien als .copycop-Paket übertragen

Ein `.copycop`-Paket bündelt mehrere Dateien in einem ZIP-Archiv und stellt es
als normale, auf einer deutschen Tastatur tippbare Zeichen dar. Dateinamen,
relative Unterordner und Dateiinhalt bleiben erhalten. Auch Bilder, Binärdateien,
Emojis im Dateiinhalt, UTF-8-BOMs, Tabulatoren und CRLF-Zeilenenden werden
bytegenau wiederhergestellt. ZIP-Zeitstempel sind fest; Dateirechte und andere
Dateisystem-Metadaten werden nicht übernommen.

## Benutzung

1. In der Desktop- oder Android-App **Dateien hinzufügen …** oder **Ordner
   hinzufügen …** wählen. Jede weitere Auswahl ergänzt die bestehende Liste,
   auch wenn der Dateidialog nur eine Datei auf einmal anbietet. Einzelne
   Einträge können entfernt und die Auswahl ausdrücklich geleert werden.
   Identische Dateien werden übersprungen; gleicher Name mit anderem Inhalt
   wird gemeldet und überschreibt nichts. Abbruch und Lesefehler erhalten die Liste.
2. Optional komprimieren, **Paket erstellen** und **Als .copycop speichern …**
   wählen. **Datei öffnen …** lädt das vollständige Paket samt Dateiliste
   zum späteren Ergänzen wieder. Eine Änderung der Liste macht das vorherige
   Paket ungültig, bis es neu erstellt wurde. Ein normaler Editortext
   wird beim Speichern als `.copycop` in eine enthaltene `text.txt` verpackt.
3. CopyCop im blauen LOAD-Modus anschließen. Bei mehreren Teilen **Automatisch
   aufteilen** verwenden und den gewünschten Teil auf das Gerät speichern.
   Beim Erstellen eines Pakets geschieht die Aufteilung bereits automatisch.
4. Auf dem Zielrechner die mitgelieferte **copycop.html** in einem aktuellen
   Browser öffnen. Den Cursor in **Paket empfangen** setzen und mit V tippen
   lassen. Das Empfangsfeld zwischen den Teilen nicht leeren. Für jeden weiteren
   Teil wieder am Quellrechner laden und am Ziel ausgeben.
5. **Prüfen & entpacken** wählen. Einzelne Dateien speichern oder **Alle Dateien
   als ZIP** herunterladen und im Dateimanager entpacken. Der ZIP-Download
   erhält Unterordner; ein einzelner Browserdownload verwendet den Basisnamen.

Die HTML-Datei muss vor der Übertragung auf dem Zielrechner verfügbar sein.
Sie lädt keine Skripte, Schriftarten oder Dienste aus dem Internet. Zum lokalen
Entpacken komprimierter Dateien benötigt sie die Browser-Unterstützung für
[`DecompressionStream("deflate-raw")`](https://developer.mozilla.org/en-US/docs/Web/API/DecompressionStream/DecompressionStream).

Die HTML-Seite dient ausschließlich dem Empfang, Prüfen und Entpacken. Das
Vorbereiten, Ergänzen und Komprimieren der Dateien sowie das Laden auf CopyCop
übernehmen die Desktop- und Android-App. Die Android-App verwendet die
Systemdialoge zum Hinzufügen und Speichern und benötigt keine allgemeine
Speicherberechtigung. Ihre Dateiliste wird bei einer vom System neu erstellten
Activity über einen privaten temporären Zwischenstand wiederhergestellt.

**Ein Gerät speichert weiterhin nur einen Teil gleichzeitig.** Die Firmware
muss nicht geändert werden. Am Ziel wird zuerst der Pakettext getippt; die
HTML-Seite oder CLI stellt daraus die eigentlichen Dateien wieder her.

## Kommandozeile ohne angeschlossenes Gerät

```text
copycop-cli pack paket.copycop datei1.txt datei2.py bild.png
copycop-cli pack paket.copycop datei1.txt datei2.py --no-compress
copycop-cli unpack paket.copycop neuer-zielordner
```

Die CLI verwendet bei der Dateiauswahl die Basisnamen. Gleichnamige Dateien
aus unterschiedlichen Ordnern werden deshalb abgewiesen. Für Ordnerstrukturen
**Ordner hinzufügen …** in der Desktop- oder Android-App verwenden. `pack` überschreibt keine
vorhandene Datei; `unpack` erwartet einen noch nicht vorhandenen Zielordner.

Zum Laden auf das Gerät bleibt die bisherige CLI verfügbar:

```text
copycop-cli --file paket.copycop --part 1 --once
```

Das vollständige Paket wird eingelesen und geprüft. Im LOAD-Modus startet die
physische C-Taste die Übertragung des ausgewählten Teils.

## Komprimierung und Grenzen

- Maximal 256 Dateien und 16 MiB ursprünglicher beziehungsweise entpackter
  Dateiinhalt pro Paket.
- Maximal 4 MiB fertiger Übertragungstext. Unkomprimierbare Dateien erreichen
  diese Grenze früher; Base64 benötigt etwa ein Drittel mehr Zeichen als Bytes.
- Jeder Geräteteil enthält einschließlich Nummer und Prüfsumme höchstens
  126.464 ASCII-Bytes. Für kleinere Kapazitäten kann der Core neu aufteilen.
- ZIP-Deflate wird nur verwendet, wenn es das Archiv verkleinert. Vor allem
  Quellcode und wiederholter Text profitieren. Kleine Dateien oder bereits
  komprimierte Medien können als Paket mehr Tippzeichen benötigen als zuvor.
- Der Empfang akzeptiert eine andere Reihenfolge und identische doppelte
  Teile. Fehlende, vermischte oder widersprüchliche Teile werden gemeldet.
- SHA-256 prüft das komplette empfangene Archiv, die ZIP-CRC32 anschließend
  jede Datei. Die Prüfsummen dienen der Fehlererkennung, nicht der
  Verschlüsselung oder Absenderauthentifizierung.
- Absolute Pfade, `..`, Windows-Gerätenamen, Namenskonflikte und nicht reguläre
  Dateien werden abgewiesen. Der Browser zeigt Namen als Text an und führt
  übertragene Dateien nicht aus.

## Format Version 1

Eine UTF-8-Datei ohne BOM enthält einen oder mehrere Rahmen:

```text
COPYCOP/1 <SHA-256 des vollständigen ZIP-Archivs> <Teilnummer>/<Teilezahl>
<Base64-Abschnitt, Zeilen mit höchstens 120 Zeichen>
ENDCOPYCOP
```

Die SHA-256-Prüfsumme wird als 64 hexadezimale Zeichen geschrieben. Teilnummern
beginnen bei 1. Maximal 4096 Teile sind zulässig. Alle Teile eines Pakets tragen
dasselbe Hashfeld und dieselbe Teilezahl. LF und CRLF werden akzeptiert;
Leerzeichen, Tabs und Zeilenumbrüche innerhalb der Base64-Daten werden ignoriert.
Die Sender teilen ausschließlich an Grenzen von vier Base64-Zeichen.

Nach Sortierung und Entfernung identischer Duplikate werden die Base64-
Abschnitte zusammengesetzt, dekodiert und gegen SHA-256 geprüft. Das Ergebnis
ist ein reguläres ZIP-Archiv mit UTF-8-Dateinamen und den ZIP-Methoden 0
(unkomprimiert) oder 8 (Deflate). ZIP64, Verschlüsselung und mehrere ZIP-Datenträger
sind kein Bestandteil dieses Formats. Unbekannte Rahmenversionen werden
abgewiesen. Der gespeicherte Text kann vollständig an Empfänger weitergegeben
werden; einzelne Teile sind für die Übertragung gedacht.
