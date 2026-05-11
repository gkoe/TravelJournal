# TravelJournal

**Deine Reise verdient mehr als einen Instagram-Post.**

TravelJournal verwandelt den Ordner mit deinen Urlaubsfotos in eine vollständige Reisedokumentation — mit automatischen Streckenkarten, Ortsnamen aus GPS-Daten und einer eleganten Präsentation, die du deinen Freunden zeigen oder im Browser teilen kannst. Alles läuft lokal auf deinem Windows-PC, ohne Cloud-Zwang und ohne Abo.

---

## Was TravelJournal kann

### Fotos vorbereiten
- **HEIC automatisch konvertieren** — iPhone-Fotos im HEIC-Format werden mit einem Klick nach JPEG umgewandelt. Die Originale werden danach gelöscht, Speicherplatz gespart.
- **Fotos umbenennen** — Aus `IMG_4711.jpg` wird `2025_07_14_09_23_Salzburg.jpg`. Das Schema aus Datum, Uhrzeit und Ort macht deine Bibliothek für immer sortierbar, auch ohne TravelJournal.
- **Neu scannen** — Fotos nachträglich hinzugefügt? Ein Klick aktualisiert die Galerie. Neue Fotos werden mit dem Badge **NEU** markiert.

### Fotos kuratieren
Jedes Foto bekommt einen von fünf **Status**, den du per Tastendruck oder Kontextmenü setzt:

| Status | Taste | Bedeutung |
|--------|-------|-----------|
| Ausgewählt | `1` | Kommt in die Präsentation |
| Abgewählt | `2` | Bleibt im Ordner, wird übersprungen |
| Offen | `0` | Noch nicht entschieden |
| ▶ Startfolie | `3` | Eröffnungsbild der Präsentation |
| ■ Schlussfolie | `4` | Abschlussbild der Präsentation |

`Leertaste` schaltet zyklisch durch die drei Hauptzustände. Die **Filterschaltflächen** zeigen nur Fotos des gewählten Status — so siehst du mit einem Blick, welche Fotos noch nicht kuratiert sind.

### Fotos bearbeiten
- **Drehen** — `L` dreht nach links, `R` nach rechts, `S` speichert dauerhaft in die Datei. Bis zum Speichern ist die Drehung nur eine Vorschau — du kannst sie jederzeit verwerfen.
- **Titel und Beschreibung** — Jedes Foto kann einen individuellen Titel und eine mehrzeilige Beschreibung bekommen. Beide erscheinen später in der Präsentation. Änderungen werden automatisch gespeichert.
- **Metadaten im Blick** — Datum, GPS-Koordinaten, Höhe über dem Meeresspiegel, Auflösung und Dateigröße sind im Detailbereich immer sichtbar.

### Orte ermitteln
Ein Klick auf **Orte ermitteln** fragt für alle ausgewählten Fotos mit GPS-Daten den Ortsnamen über OpenStreetMap (Nominatim) ab. Die Ergebnisse erscheinen direkt in der Galerie und im Detailbereich. Ein zweiter Klick bricht eine laufende Abfrage ab.

> Die GPS-Koordinaten stecken bereits in deinen Fotos — sie werden von Smartphone-Kameras automatisch eingebettet.

### Streckenkarten generieren
Das Herzstück von TravelJournal. Nach der Ortserkennung erzeugt **Karten generieren** (`Strg+M`) automatisch eine Karte für jeden erkannten Stopp deiner Reise — als hochauflösendes PNG direkt neben deinen Fotos. Die Karten erscheinen in der Galerie mit dem Badge **KARTE** und werden chronologisch zwischen die Fotos einsortiert.

Konfigurierbar über das aufklappbare **Karten-Panel**:

| Einstellung | Beschreibung |
|-------------|--------------|
| Stil | Kartenstil (Outdoor, Straße, Satellit, …) |
| Sprache | Beschriftungssprache auf der Karte |
| Rand | Freier Rand um die Strecke (Schieberegler) |

Für die Kartenerzeugung wird ein kostenloser **MapTiler**-Account benötigt (100.000 Anfragen/Monat im Free-Plan — für private Nutzung mehr als ausreichend).

### Vollbild-Präsentation
`Strg+P` startet eine Vollbild-Diashow aller ausgewählten Fotos und Karten in chronologischer Reihenfolge. Die Fotos wechseln automatisch, Ort, Datum und Beschreibung werden als Einblendung angezeigt. Steuerung über Pfeiltasten oder Leertaste zum Pausieren.

### Web-Export
**Web-Präsentation exportieren** erzeugt einen selbstständig lauffähigen Ordner mit HTML, CSS und JavaScript — keine Internetverbindung, kein Server nötig. Einfach den Ordner an Freunde schicken oder auf einer Website veröffentlichen. Fotos werden auf 1920 px optimiert, EXIF-Daten entfernt.

---

## Installation

### Systemvoraussetzungen
- Windows 10 oder Windows 11 (64-Bit)
- Keine zusätzliche Software notwendig — .NET ist bereits enthalten

### Schritt 1 — Herunterladen

Öffne die Releases-Seite auf GitHub:

**→ [github.com/gkoe/TravelJournal/releases](https://github.com/gkoe/TravelJournal/releases)**

Lade das neueste ZIP herunter, z. B. `TravelJournal-v1.0.0-win-x64.zip`.

### Schritt 2 — Entpacken

Entpacke das ZIP in einen Ordner deiner Wahl, zum Beispiel `C:\Programme\TravelJournal`. Verschiebe anschließend nicht einzelne Dateien aus diesem Ordner heraus — die App benötigt alle Dateien gemeinsam.

> **Hinweis zu Windows Defender:** Da die App keine offizielle Code-Signierung hat, kann Windows beim ersten Start eine SmartScreen-Warnung anzeigen. Klicke auf **Weitere Informationen → Trotzdem ausführen**.

### Schritt 3 — Konfigurieren

Öffne die Datei `appsettings.json` im entpackten Ordner mit einem Texteditor (Rechtsklick → *Mit Editor öffnen*):

```json
{
  "MapTiler": {
    "ApiKey": "DEIN_KEY_HIER"
  },
  "Nominatim": {
    "ContactEmail": "deine@email.at"
  },
  "MapRendering": {
    "StyleId": "outdoor-v2",
    "Language": "de",
    "BoundsPaddingFraction": 0.12,
    "TargetWidthPx": 1600,
    "TargetHeightPx": 1200,
    "StopThresholdMinutes": 30,
    "AddFinalSummaryMap": true,
    "CustomTileUrlTemplate": null
  }
}
```

**MapTiler API Key** (für Kartengenerierung):
1. Kostenlosen Account anlegen auf [cloud.maptiler.com](https://cloud.maptiler.com/auth/widget)
2. Nach dem Login unter *Account → Keys* den Standard-Key kopieren
3. Den Wert `DEIN_KEY_HIER` in der `appsettings.json` ersetzen

**Nominatim Contact Email** (für Ortserkennung):
- Trage deine E-Mail-Adresse ein. Sie wird als Kontaktinformation im HTTP-Header an den OpenStreetMap-Dienst übermittelt — kein Account erforderlich.

> Wenn du keine Karten generieren möchtest, kannst du `MapTiler:ApiKey` leer lassen. Die Ortserkennung funktioniert ohne API-Key.

### Schritt 4 — Starten

Doppelklick auf `TravelJournal.exe` — fertig.

---

## Typischer Ablauf

```
1. Ordner öffnen          →  Foto-Ordner vom Urlaub auswählen
2. HEIC importieren        →  (nur bei iPhone-Fotos nötig)
3. Fotos umbenennen        →  einmalig, für geordnete Dateinamen
4. Fotos kuratieren        →  Auswahl/Abwahl per Tastatur (1, 2, 0)
5. Orte ermitteln          →  GPS → Ortsnamen (benötigt Internetverbindung)
6. Karten generieren       →  Strg+M, dauert je nach Reisegröße 1–3 Min.
7. Titel + Beschreibungen  →  optional, im Detailbereich rechts
8. Präsentation starten    →  Strg+P, Vollbild
9. Web-Export              →  Ordner mit Freunden teilen
```

---

## Tastenkürzel

| Taste | Aktion |
|-------|--------|
| `1` / `2` / `0` | Status Ausgewählt / Abgewählt / Offen |
| `3` / `4` | Status Startfolie / Schlussfolie |
| `Leertaste` | Status zyklisch weiterschalten |
| `L` / `R` | Foto links / rechts drehen (Vorschau) |
| `S` | Drehung dauerhaft speichern |
| `Entf` | Foto aus der Liste entfernen |
| `F5` | Ordner neu scannen |
| `Strg+M` | Karten generieren |
| `Strg+P` | Präsentation starten |

---

## Häufige Fragen

**Meine Fotos haben keine GPS-Daten — was nun?**
Ortserkennung und Kartengenerierung sind dann nicht möglich. Alle anderen Funktionen (Kuratieren, Umbenennen, Präsentation, Web-Export) funktionieren ohne GPS.

**Wie viel kostet MapTiler?**
Der Free-Plan ist kostenlos und beinhaltet 100.000 Tile-Anfragen pro Monat. Eine Reise mit 50 Stopps erzeugt typischerweise unter 5.000 Anfragen.

**Kann ich die App auf mehreren PCs verwenden?**
Ja. Einfach den entpackten Ordner kopieren und die `appsettings.json` auf dem neuen PC anpassen. Die App speichert keine Einstellungen in der Registry.

**Wo werden die Reisedaten gespeichert?**
Im Foto-Ordner selbst, in der Datei `tour.csv`. Diese Datei enthält Status, Titel, Beschreibung und Ort aller Fotos. Sie liegt neben deinen Fotos und kann mit dem Ordner verschoben oder gesichert werden.

**Werden meine Fotos verändert?**
Nur wenn du es explizit anforderst: beim HEIC-Import (Original wird gelöscht, JPEG angelegt), beim Umbenennen und beim dauerhaften Speichern einer Drehung (`S`). Die Kartengenerierung und der Web-Export erzeugen nur neue Dateien.

---

## Über die Anwendung

TravelJournal ist ein Open-Source-Projekt, entwickelt für Windows mit .NET 10 und WPF.

Quellcode und aktuelle Versionen: **[github.com/gkoe/TravelJournal](https://github.com/gkoe/TravelJournal)**
