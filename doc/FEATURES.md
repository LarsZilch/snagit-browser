# Snagit Browser – Funktionsübersicht

Der **Snagit Browser** ist eine Windows-Anwendung (WPF, .NET 9) zum schnellen Durchsuchen, Filtern und Verwalten einer großen Snagit-Bibliothek (`.snagx`-Dateien). Er liest die Dateien direkt aus dem Snagit-Ordner, baut einen lokalen Suchindex auf und zeigt alle Captures in einer Galerie.

---

## Inhalt

1. [Bibliothek und Indexierung](#1-bibliothek-und-indexierung)
2. [Galerie](#2-galerie)
3. [Suche und Filter](#3-suche-und-filter)
4. [Auswahl und Aktionen in der Galerie](#4-auswahl-und-aktionen-in-der-galerie)
5. [Detailansicht](#5-detailansicht)
6. [Tags bearbeiten](#6-tags-bearbeiten)
7. [Löschen](#7-löschen)
8. [Export und Zwischenablage](#8-export-und-zwischenablage)
9. [Datensicherheit](#9-datensicherheit)
10. [Einstellungen und Speicherorte](#10-einstellungen-und-speicherorte)
11. [Tastenkürzel](#11-tastenkürzel)

---

## 1. Bibliothek und Indexierung

- **Quellordner wählen** – Über *Quellordner …* wird der Snagit-Bibliotheksordner festgelegt (Standard: `S:\AppData\Snagit`). Alle Unterordner werden rekursiv nach `.snagx`-Dateien durchsucht.
- **Lokaler Suchindex** – Metadaten und Texte werden in einer SQLite-Datenbank mit Volltextsuche (FTS5) gespeichert. Pro Quellordner gibt es eine eigene Index-Datei.
- **Inkrementelles Einlesen** – Beim Start und über *Neu einlesen* werden nur neue oder geänderte Dateien gelesen (Vergleich über Dateigröße und Änderungszeit). Gelöschte Dateien verschwinden aus dem Index.
- **Paralleles Lesen** – Dateien werden in Blöcken zu 200 mit bis zu 4 Threads gelesen.
- **Fortschrittsanzeige** – Die Statuszeile zeigt einen Fortschrittsbalken und am Ende eine Zusammenfassung: Anzahl Captures, neu, geändert, entfernt, nicht lesbar und Dauer.
- **Galerie schon während des ersten Aufbaus** – Beim allerersten Indexieren aktualisiert sich die Galerie etwa alle 5 Sekunden, man muss also nicht auf das Ende warten.
- **Ausgelesene Daten pro Capture**:
  - Aufnahmedatum (aus den Metadaten, sonst aus dem Dateinamen `dd-MM-yyyy_HH-mm-ss`, sonst Dateidatum)
  - Anwendung, Hersteller, Version, Fenstertitel, URL
  - Tags
  - Texte aus Annotationen (RTF wird in Klartext umgewandelt)
  - Bildgröße und Anzahl der Annotationen

## 2. Galerie

- **Zeilen-Layout wie in Foto-Galerien** – Alle Bilder einer Zeile sind gleich hoch, jede Zeile füllt die volle Breite, und das Seitenverhältnis bleibt erhalten.
- **Einstellbare Vorschaugröße** – Über den Schieberegler *Vorschaugröße (Zeilenhöhe)* von 120 bis 1600 px oder mit **Strg + Mausrad** (±10 % pro Raste).
- **Scharfe Vorschaubilder** – Bilder werden in passenden Auflösungsstufen (256 bis 4096 px) geladen, abhängig von Kachelgröße und Monitor-Skalierung (DPI). Bei Captures ohne Annotationen wird über 1024 px das schärfere Originalbild verwendet.
- **Schnell auch bei großen Bibliotheken** – Nur sichtbare Zeilen werden erzeugt (Virtualisierung). Vorschaubilder liegen in einem RAM-Cache (bis 400 MB), es wird nichts auf die Festplatte geschrieben.
- **Scroll-Position bleibt erhalten** – nach Fenstergrößen-Änderung, Zoom, Löschen oder Tag-Änderung.
- **Infos auf der Kachel**:
  - Beim Überfahren mit der Maus: Fenstertitel, Datum, Anwendung und Tags
  - Tooltip mit Dateiname, Anwendung, Fenster, Tags und den ersten 400 Zeichen der Texte
  - Blaues **„T“**-Symbol, wenn das Capture Texte/Notizen enthält
  - Optional: Tags direkt auf dem Bild (*Tags auf den Bildern anzeigen*)
- **Sortierung** – Neueste Captures zuerst.
- **Statuszeile** – Anzahl Treffer, Anzahl ausgewählter Captures und aktuelle Statusmeldung.

## 3. Suche und Filter

Alle Filter lassen sich kombinieren. *Filter zurücksetzen* hebt alle auf einmal auf.

### Volltextsuche
- Durchsucht **Texte, Tags, Fenstertitel, Anwendung, URL und Dateiname**.
- Jedes Wort wird als **Wortanfang** gesucht (`rech` findet `Rechnung`).
- **Alle Wörter** müssen vorkommen (UND-Verknüpfung).
- Akzente/Umlaute werden toleriert.
- Die Suche startet automatisch 300 ms nach der letzten Eingabe.

### Tag-Leiste
- Zeigt alle Tags als Buttons mit Anzahl, alphabetisch sortiert.
- Ein Klick filtert nach diesem Tag, ein erneuter Klick hebt den Filter auf (höchstens ein Tag gleichzeitig).
- Eigenes Suchfeld *Tags filtern …*, um die Leiste einzugrenzen.
- Zusammenklappbar; der Zustand wird gespeichert.

### Seitenleiste
| Filter | Beschreibung |
|---|---|
| **Anwendung** | Auswahl der Anwendung, mit der das Capture erstellt wurde (mit Anzahl, Eingabe zur Schnellsuche möglich) |
| **Anzahl Tags** | beliebig viele · ohne Tags · mindestens 1 Tag · genau *n* Tags (jeweils mit Anzahl) |
| **Nur mit Text/Notizen** | nur Captures, die Texte in Annotationen enthalten |
| **Zeitraum** | Baum *Alle Captures → Jahr → Monat → Tag* mit Anzahl pro Eintrag |

Der Zeitraum-Baum zählt immer passend zu den übrigen Filtern neu. Der Aufklappzustand bleibt erhalten; beim ersten Start ist das neueste Jahr aufgeklappt.

## 4. Auswahl und Aktionen in der Galerie

### Auswahl
- **Klick** – einzelnes Capture auswählen
- **Strg + Klick** – Auswahl umschalten
- **Umschalt + Klick** – Bereich auswählen
- **Strg + A** – alles auswählen
- **Pfeil links/rechts** – Auswahl verschieben
- **Doppelklick / Enter** – Detailansicht öffnen

### Kontextmenü (Rechtsklick)
- **Öffnen** – Detailansicht
- **Im Snagit Editor öffnen** – bei Mehrfachauswahl höchstens 10 Dateien auf einmal. Der Snagit Editor wird über die Windows-Dateizuordnung oder im Ordner `TechSmith` unter *Programme* gefunden.
- **Tags bearbeiten …** (Strg+T)
- **Texte kopieren** – Texte aller ausgewählten Captures in die Zwischenablage
- **Bilder exportieren (PNG) …**
- **.snagx-Dateien kopieren nach …**
- **Im Explorer anzeigen**
- **Löschen (Papierkorb)** (Entf)

Die Buttons *Tags …* und *Löschen* gibt es zusätzlich in der oberen Leiste.

## 5. Detailansicht

Öffnet sich per Doppelklick oder Enter in einem eigenen Fenster.

### Bildanzeige
- **Blättern** – Buttons *Zurück/Weiter*, Pfeiltasten, Bild auf/ab, Mausrad, Pos1/Ende für erstes/letztes Bild. Geblättert wird innerhalb der aktuellen Trefferliste.
- **Zoom** – Strg + Mausrad (zoomt zur Mausposition), Strg+Plus/Minus, Lupen-Buttons, Bereich 5 % bis 800 %.
- **Einpassen / 100 %** – Button *Einpassen*, Strg+0 oder Doppelklick aufs Bild. Beim Einpassen wird nie über 100 % vergrößert, damit das Bild scharf bleibt.
- **Verschieben** – Bei Zoom mit gedrückter Maustaste ziehen.
- **Zoomstufe bleibt beim Blättern erhalten**, wenn sie von 100 % abweicht.
- **Original ohne Annotationen** – Bei bearbeiteten Captures lässt sich das unbearbeitete Originalbild in voller Auflösung anzeigen. Ein Hinweis erklärt, wenn Snagit das Bild mit Annotationen verkleinert gespeichert hat (max. 1024 px).

### Seitenleiste
- **Tags** – anzeigen und bearbeiten (siehe [Tags bearbeiten](#6-tags-bearbeiten))
- **Metadaten** – Datum, Anwendung, Hersteller, Version, Fenster, URL, Bildgröße, Annotationen (z. B. „3× Arrow, 1× Callout“), Snagit-Version, Betriebssystem, Dateipfad. Werte lassen sich markieren und kopieren.
- **Texte / Notizen** – alle Texte aus den Annotationen

### Aktionen
- **In Snagit öffnen** (Strg+E)
- **Bild speichern …** – als PNG (bei Original-Ansicht mit Zusatz `_original`)
- **Bild kopieren** – in die Zwischenablage
- **Texte kopieren**
- **Im Explorer anzeigen**
- **Löschen** (Entf) – danach springt die Ansicht zum nächsten Bild

Der Fenstertitel zeigt Dateiname und Position (z. B. `(12/340)`), bei ungespeicherten Tag-Änderungen mit einem **●** davor.

## 6. Tags bearbeiten

Tags werden **direkt in die `.snagx`-Datei** geschrieben und sind damit auch in Snagit sichtbar.

### Einzelnes Capture (Detailansicht)
- Vergebene Tags erscheinen als Kacheln; **×** entfernt einen Tag.
- Eingabefeld *Neuer Tag oder suchen …* – Enter fügt den Tag hinzu, gleichzeitig filtert es die Liste der vorhandenen Tags.
- Liste **aller vorhandenen Tags** – Klick fügt hinzu bzw. entfernt.
- Bei abweichender Groß-/Kleinschreibung wird die Schreibweise eines vorhandenen Tags übernommen (`backup` → `Backup`).
- Änderungen werden mit **Speichern** (Strg+S) geschrieben oder **verworfen**. Beim Blättern oder Schließen wird automatisch gespeichert.

### Mehrere Captures (Strg+T)
- Fenster *Tags für n ausgewählte Captures*. Die Tags werden frisch aus den Dateien gelesen.
- Zeigt für jeden Tag, bei wie vielen Bildern er vorhanden ist (z. B. `3/10`).
- Klick auf einen Tag setzt ihn bei **allen** ausgewählten Captures; ist er schon überall vorhanden, wird er bei allen entfernt.
- Zusammenfassung der geplanten Änderungen (`+ neu`, `− entfernt`) und wie viele Dateien geändert werden.
- **Übernehmen** (Strg+Enter) schreibt alle Dateien mit Fortschrittsanzeige. Dateien, die nicht geschrieben werden konnten, werden am Ende aufgelistet.
- Bestehende Reihenfolge der Tags bleibt erhalten, neue werden angehängt.

## 7. Löschen

- Captures werden in den **Windows-Papierkorb** verschoben und lassen sich dort wiederherstellen.
- Optionale Rückfrage mit *Nicht mehr fragen*; einstellbar über *Vor dem Löschen nachfragen*.
- Funktioniert für einzelne Captures und für eine Mehrfachauswahl.
- Gelöschte Captures verschwinden sofort aus Galerie, Filtern und Index.
- Es können nur Dateien innerhalb des Snagit-Ordners gelöscht werden.

## 8. Export und Zwischenablage

| Funktion | Ergebnis |
|---|---|
| **Bilder exportieren (PNG)** | Bilder mit Annotationen als PNG in einen Zielordner |
| **.snagx-Dateien kopieren nach** | Originaldateien in einen Zielordner |
| **Bild speichern** (Detailansicht) | ein Bild als PNG, wahlweise das Original |
| **Bild kopieren** | aktuelles Bild in die Zwischenablage |
| **Texte kopieren** (Strg+C) | Texte der Auswahl in die Zwischenablage |

- Vorhandene Dateien im Ziel werden nie überschrieben, stattdessen wird nummeriert (`Datei (2).png`).
- Der zuletzt gewählte Exportordner wird gemerkt.
- Exporte in den Snagit-Quellordner sind gesperrt.

## 9. Datensicherheit

Der Snagit-Ordner wird grundsätzlich nur gelesen. Die einzigen Ausnahmen sind **Tags ändern** und **Löschen in den Papierkorb**.

**Schutz beim Schreiben von Tags:**
1. Prüfung, ob die Datei seit dem Lesen von einem anderen Programm (z. B. Snagit) geändert wurde – wenn ja, wird nichts geschrieben.
2. **Sicherungskopie** nach `%LOCALAPPDATA%\SnagitBrowser\Backups\<Datum>\`, per Hash geprüft.
3. In der Datei wird nur der Tag-Eintrag in `metadata.json` ersetzt. Bilder und Annotationen bleiben Byte für Byte gleich.
4. Die neue Fassung wird geprüft: alle anderen Einträge und Metadatenfelder unverändert, Tags korrekt lesbar.
5. **Atomarer Austausch** der Datei – bei einem Absturz bleibt entweder die alte oder die neue Fassung vollständig erhalten.

Sicherungskopien, die älter als **30 Tage** sind, werden beim Start automatisch gelöscht.

**Weitere Schutzmaßnahmen:**
- Index, Einstellungen und Exporte dürfen nie im Snagit-Ordner liegen.
- Beim Öffnen in Snagit schreibt der Browser nichts; Änderungen entstehen nur, wenn im Snagit Editor gespeichert wird.

## 10. Einstellungen und Speicherorte

Alle Daten liegen unter `%LOCALAPPDATA%\SnagitBrowser\`:

| Datei/Ordner | Inhalt |
|---|---|
| `settings.json` | Einstellungen |
| `index-<hash>.db` | Suchindex pro Quellordner |
| `Backups\` | Sicherungskopien vor Tag-Änderungen |

**Gespeicherte Einstellungen:**
- Quellordner
- Letzter Exportordner
- Vor dem Löschen nachfragen
- Tags auf den Bildern anzeigen
- Tag-Leiste auf- oder zugeklappt

## 11. Tastenkürzel

### Galerie
| Taste | Funktion |
|---|---|
| Enter / Doppelklick | Detailansicht öffnen |
| Pfeil links/rechts | Auswahl verschieben |
| Strg + A | Alles auswählen |
| Strg + C | Texte kopieren |
| Strg + T | Tags bearbeiten |
| Entf | In den Papierkorb |
| Strg + Mausrad | Vorschaugröße ändern |

### Detailansicht
| Taste | Funktion |
|---|---|
| Pfeil links/rechts/oben/unten, Bild auf/ab, Mausrad | Vorheriges/nächstes Bild |
| Pos1 / Ende | Erstes/letztes Bild |
| Strg + Mausrad, Strg + Plus/Minus | Zoomen |
| Strg + 0, Doppelklick | Einpassen / 100 % |
| Strg + S | Tags speichern |
| Strg + E | In Snagit öffnen |
| Entf | In den Papierkorb |
| Esc | Eingabe leeren bzw. Fenster schließen |

### Tags für mehrere Captures
| Taste | Funktion |
|---|---|
| Enter | Tag bei allen setzen |
| Strg + Enter | Übernehmen |
| Esc | Eingabe leeren bzw. abbrechen |
