# CopyCop Android APK

`CopyCop-Android.apk` ist die vorgebaute Android-App für direktes Sideloading.
Version 1.2.0 ergänzt `.copycop`-Pakete: Dateien und Ordner lassen sich schrittweise
hinzufügen, einzelne Einträge entfernen und die Auswahl optional komprimieren.
Gespeicherte Pakete können samt Dateiliste wieder geöffnet und ergänzt werden.
„Datei öffnen …“ unterstützt weiterhin Text- und Quellcodedateien inklusive
Zeichenprüfung und Aufteilung großer Texte. Die HTML-Datei im Repository dient
auf dem Zielrechner ausschließlich zum Empfang und Entpacken.
Sie ist mit dem lokalen Android-Debugschlüssel signiert und daher für Tests und
die direkte Installation gedacht, nicht als Google-Play-Release.

Die APK unterstützt Android 8.0 oder neuer auf ARM64- und x86_64-Geräten.
Ihr Signaturschlüssel unterscheidet sich von der bisherigen Repo-APK 1.1.0:
Diese alte Installation kann deshalb nicht direkt aktualisiert werden.
Benötigte Inhalte vorab als Datei sichern, dann die alte App deinstallieren
und Version 1.2.0 installieren. Die zuletzt lokal bereitgestellte 1.2.0-APK
verwendet bereits denselben Schlüssel wie dieses Release.

SHA-256 des Signaturzertifikats:
`81ef0c48e32c988ba8259ca135acbc8c812bd9798b8d6b5f1d3750e119459dfb`.

Zum reproduzierbaren Neubauen dient
`host/packaging/publish-android.ps1`.
