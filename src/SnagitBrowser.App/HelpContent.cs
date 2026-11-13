namespace SnagitBrowser.App;

/// <summary>
/// Inhalt der Hilfeseite auf Deutsch und Englisch (Auszeichnung siehe <see cref="HelpWindow.Build"/>).
/// Ausführliche Fassung: doc/FEATURES.md.
/// </summary>
public static class HelpContent
{
    public static string Get(bool english) => (english ? English : German).Replace("{version}", AppInfo.VersionText);

    private const string German = """
        Der **Snagit Browser** durchsucht, filtert und verwaltet eine Snagit-Bibliothek (`.snagx`-Dateien). Er liest die Dateien direkt aus dem Snagit-Ordner, baut einen lokalen Suchindex auf und zeigt alle Captures in einer Galerie. Der Snagit-Ordner wird nur gelesen – ausgenommen sind nur das Ändern von Tags und das Löschen in den Papierkorb.

        # 1. Bibliothek und Index
        - **Quellordner …** legt den Snagit-Bibliotheksordner fest (Standard: `Dokumente\Snagit`). Alle Unterordner werden nach `.snagx`-Dateien durchsucht.
        - Metadaten und Texte landen in einem lokalen Suchindex (SQLite mit Volltextsuche). Pro Quellordner gibt es eine eigene Index-Datei.
        - Beim Start und über **Neu einlesen** werden nur neue oder geänderte Dateien gelesen. Gelöschte Dateien verschwinden aus dem Index.
        - Die Statuszeile zeigt den Fortschritt und am Ende eine Zusammenfassung (neu, geändert, entfernt, nicht lesbar, Dauer).
        - Beim allerersten Einlesen füllt sich die Galerie schon währenddessen, etwa alle 5 Sekunden.
        - Pro Capture werden gelesen: Aufnahmedatum, Anwendung, Hersteller, Version, Fenstertitel, URL, Tags, Texte aus Annotationen, Bildgröße und Anzahl der Annotationen.

        # 2. Galerie
        - Alle Bilder einer Zeile sind gleich hoch, jede Zeile füllt die Breite, das Seitenverhältnis bleibt erhalten.
        - **Vorschaugröße** über den Schieberegler (120 bis 1600 px) oder mit **Strg + Mausrad**.
        - Beim Überfahren mit der Maus erscheinen Fenstertitel, Datum, Anwendung und Tags; der Tooltip zeigt zusätzlich den Anfang der Texte.
        - Ein blaues **T** oben rechts bedeutet: Das Capture enthält Texte oder Notizen.
        - **Tags auf den Bildern anzeigen** blendet die Tags direkt auf den Kacheln ein.
        - Die **Seitenleiste** links lässt sich über den schmalen Streifen an ihrem Rand ein- und ausklappen; die Galerie nutzt dann die volle Breite.
        - Die neuesten Captures stehen oben. Die Statuszeile zeigt Trefferzahl, Auswahl und die letzte Meldung.

        # 3. Suche und Filter
        Alle Filter lassen sich kombinieren. **Filter zurücksetzen** hebt alle auf einmal auf.
        ## Volltextsuche
        - Durchsucht Texte, Tags, Fenstertitel, Anwendung, URL und Dateiname.
        - Jedes Wort wird als Wortanfang gesucht (`rech` findet „Rechnung“), alle Wörter müssen vorkommen.
        - Akzente und Umlaute werden toleriert. Die Suche startet kurz nach der letzten Eingabe.
        ## Tag-Leiste
        - Zeigt alle Tags mit Anzahl. Ein Klick filtert nach dem Tag, ein zweiter Klick hebt den Filter auf (höchstens ein Tag gleichzeitig).
        - Das Feld **Tags filtern …** grenzt die Leiste ein. Die Leiste lässt sich zuklappen; der Zustand wird gespeichert.
        ## Seitenleiste
        | Filter | Wirkung |
        | Anwendung | Nur Captures einer bestimmten Anwendung (mit Anzahl; Eintippen sucht in der Liste) |
        | Anzahl Tags | beliebig viele, ohne Tags, mindestens 1 Tag oder genau n Tags |
        | Nur mit Text/Notizen | Nur Captures mit Texten in Annotationen |
        | Zeitraum | Baum Alle Captures → Jahr → Monat → Tag, jeweils mit Anzahl |
        Der Zeitraum-Baum zählt passend zu den übrigen Filtern neu.

        # 4. Auswahl und Aktionen
        - **Klick** wählt ein Capture, **Strg + Klick** schaltet die Auswahl um, **Umschalt + Klick** wählt einen Bereich, **Strg + A** alles.
        - **Doppelklick** oder **Enter** öffnet die Detailansicht.
        - Das **Kontextmenü** (Rechtsklick) bietet: Öffnen, Im Snagit Editor öffnen (höchstens 10 auf einmal), Tags bearbeiten, Texte kopieren, Bilder exportieren, .snagx-Dateien kopieren, Im Explorer anzeigen, Löschen.
        - **Tags …** und **Löschen** gibt es zusätzlich oben in der Leiste.

        # 5. Detailansicht
        - **Blättern** mit Zurück/Weiter, Pfeiltasten, Bild auf/ab oder Mausrad; Pos1/Ende springen zum ersten/letzten Bild der Trefferliste.
        - **Zoomen** mit Strg + Mausrad (zur Mausposition), Strg + Plus/Minus oder den Lupen-Buttons (5 % bis 800 %). Bei Zoom lässt sich das Bild mit gedrückter Maustaste verschieben.
        - **Einpassen** (Strg + 0 oder Doppelklick aufs Bild) wechselt zwischen eingepasst und 100 %. Eingepasst wird nie über 100 % vergrößert.
        - **Original** zeigt bei bearbeiteten Captures das unbearbeitete Bild in voller Auflösung. Ein Hinweis erscheint, wenn Snagit das Bild mit Annotationen verkleinert gespeichert hat.
        - Rechts stehen Tags, Metadaten (markier- und kopierbar) und alle Texte aus den Annotationen.
        - Aktionen: In Snagit öffnen (Strg + E), Bild speichern (PNG), Bild kopieren, Texte kopieren, Im Explorer anzeigen, Löschen (Entf).

        # 6. Tags bearbeiten
        Tags werden direkt in die `.snagx`-Datei geschrieben und sind damit auch in Snagit sichtbar.
        ## Ein Capture (Detailansicht)
        - **×** an einem Tag entfernt ihn. Im Feld **Neuer Tag oder suchen …** fügt Enter einen Tag hinzu; gleichzeitig filtert das Feld die Liste der vorhandenen Tags.
        - Ein Klick in der Liste der vorhandenen Tags fügt hinzu bzw. entfernt. Bei abweichender Groß-/Kleinschreibung wird die vorhandene Schreibweise übernommen.
        - **Speichern** (Strg + S) schreibt die Änderung, **Verwerfen** nimmt sie zurück. Beim Blättern oder Schließen wird automatisch gespeichert.
        ## Mehrere Captures (Strg + T)
        - Zeigt für jeden Tag, bei wie vielen der ausgewählten Bilder er vorhanden ist (z. B. `3/10`).
        - Klick auf einen Tag setzt ihn bei allen; ist er schon überall vorhanden, wird er bei allen entfernt. **×** entfernt ihn bei allen.
        - Eine Zusammenfassung zeigt die geplanten Änderungen. **Übernehmen** (Strg + Enter) schreibt alle Dateien; nicht schreibbare Dateien werden am Ende aufgelistet.

        # 7. Löschen
        - Captures werden in den **Windows-Papierkorb** verschoben und lassen sich dort wiederherstellen.
        - Die Rückfrage lässt sich mit **Nicht mehr nachfragen** oder über **Vor dem Löschen nachfragen** abschalten.
        - Gelöschte Captures verschwinden sofort aus Galerie, Filtern und Index.

        # 8. Export und Zwischenablage
        | Funktion | Ergebnis |
        | Bilder exportieren (PNG) | Bilder mit Annotationen als PNG in einen Zielordner |
        | .snagx-Dateien kopieren nach | Originaldateien in einen Zielordner |
        | Bild speichern (Detailansicht) | Ein Bild als PNG, wahlweise das Original |
        | Bild kopieren | Aktuelles Bild in die Zwischenablage |
        | Texte kopieren (Strg + C) | Texte der Auswahl in die Zwischenablage |
        Vorhandene Dateien im Ziel werden nie überschrieben, sondern nummeriert (`Datei (2).png`). Der letzte Exportordner wird gemerkt. Exporte in den Snagit-Ordner sind gesperrt.

        # 9. Datensicherheit beim Schreiben von Tags
        - Wurde die Datei seit dem Lesen von einem anderen Programm (z. B. Snagit) geändert, wird nichts geschrieben.
        - Vorher wird eine **Sicherungskopie** nach `%LOCALAPPDATA%\SnagitBrowser\Backups\` angelegt und geprüft.
        - In der Datei wird nur der Tag-Eintrag in `metadata.json` ersetzt; Bilder und Annotationen bleiben Byte für Byte gleich. Fehlt der Eintrag, wird er eingefügt.
        - Die neue Fassung wird geprüft und dann **atomar** ausgetauscht – bei einem Absturz bleibt die alte oder die neue Fassung vollständig erhalten.
        - Sicherungskopien, die älter als 30 Tage sind, werden beim Start gelöscht.

        # 10. Sprache, Einstellungen und Speicherorte
        - Die **Sprache** (Deutsch/English) wählst du oben rechts. Sie wirkt sofort und wird für den nächsten Start gespeichert. Standard ist Deutsch.
        - Gespeichert werden außerdem: Quellordner, letzter Exportordner, Vor dem Löschen nachfragen, Tags auf den Bildern anzeigen und der Zustand der Tag-Leiste.
        | Datei/Ordner unter %LOCALAPPDATA%\SnagitBrowser | Inhalt |
        | settings.json | Einstellungen |
        | index-….db | Suchindex pro Quellordner |
        | Backups\ | Sicherungskopien vor Tag-Änderungen |

        # 11. Tastenkürzel
        ## Galerie
        | Taste | Funktion |
        | F1 | Diese Hilfe |
        | Enter / Doppelklick | Detailansicht öffnen |
        | Pfeil links/rechts | Auswahl verschieben |
        | Strg + A | Alles auswählen |
        | Strg + C | Texte kopieren |
        | Strg + T | Tags bearbeiten |
        | Entf | In den Papierkorb |
        | Strg + Mausrad | Vorschaugröße ändern |
        ## Detailansicht
        | Taste | Funktion |
        | Pfeiltasten, Bild auf/ab, Mausrad | Vorheriges/nächstes Bild |
        | Pos1 / Ende | Erstes/letztes Bild |
        | Strg + Mausrad, Strg + Plus/Minus | Zoomen |
        | Strg + 0, Doppelklick | Einpassen / 100 % |
        | Strg + S | Tags speichern |
        | Strg + E | In Snagit öffnen |
        | Entf | In den Papierkorb |
        | Esc | Eingabe leeren bzw. Fenster schließen |
        ## Tags für mehrere Captures
        | Taste | Funktion |
        | Enter | Tag bei allen setzen |
        | Strg + Enter | Übernehmen |
        | Esc | Eingabe leeren bzw. abbrechen |

        > Snagit Browser, Version {version}
        """;

    private const string English = """
        **Snagit Browser** searches, filters and manages a Snagit library (`.snagx` files). It reads the files directly from the Snagit folder, builds a local search index and shows all captures in a gallery. The Snagit folder is only read – the only exceptions are changing tags and deleting to the Recycle Bin.

        # 1. Library and index
        - **Source folder …** sets the Snagit library folder (default: `Documents\Snagit`). All subfolders are searched for `.snagx` files.
        - Metadata and texts are stored in a local search index (SQLite with full-text search). Each source folder has its own index file.
        - At startup and via **Rescan**, only new or changed files are read. Deleted files disappear from the index.
        - The status bar shows the progress and, at the end, a summary (new, changed, removed, unreadable, duration).
        - During the very first scan the gallery already fills up while scanning, about every 5 seconds.
        - For each capture the following is read: capture date, application, publisher, version, window title, URL, tags, texts from annotations, image size and number of annotations.

        # 2. Gallery
        - All images in a row have the same height, every row fills the width, and the aspect ratio is kept.
        - **Thumbnail size** via the slider (120 to 1600 px) or with **Ctrl + mouse wheel**.
        - Hovering shows window title, date, application and tags; the tooltip also shows the beginning of the texts.
        - A blue **T** in the top right corner means the capture contains texts or notes.
        - **Show tags on the images** displays the tags directly on the tiles.
        - The **sidebar** on the left can be collapsed and expanded with the narrow strip at its edge; the gallery then uses the full width.
        - The newest captures are at the top. The status bar shows the number of results, the selection and the last message.

        # 3. Search and filters
        All filters can be combined. **Reset filters** clears all of them at once.
        ## Full-text search
        - Searches texts, tags, window title, application, URL and file name.
        - Every word is matched as a word beginning (`inv` finds “invoice”), and all words must occur.
        - Accents and umlauts are tolerated. The search starts shortly after you stop typing.
        ## Tag bar
        - Shows all tags with their count. A click filters by the tag, a second click clears the filter (at most one tag at a time).
        - The **Filter tags …** field narrows down the bar. The bar can be collapsed; its state is saved.
        ## Sidebar
        | Filter | Effect |
        | Application | Only captures of one application (with count; typing searches the list) |
        | Number of tags | any number, without tags, at least 1 tag or exactly n tags |
        | Only with text/notes | Only captures with texts in annotations |
        | Date range | Tree All captures → year → month → day, each with a count |
        The date tree recounts to match the other filters.

        # 4. Selection and actions
        - **Click** selects a capture, **Ctrl + click** toggles the selection, **Shift + click** selects a range, **Ctrl + A** selects all.
        - **Double-click** or **Enter** opens the detail view.
        - The **context menu** (right-click) offers: Open, Open in Snagit Editor (at most 10 at a time), Edit tags, Copy texts, Export images, Copy .snagx files, Show in Explorer, Delete.
        - **Tags …** and **Delete** are also available in the top bar.

        # 5. Detail view
        - **Browse** with Back/Next, arrow keys, Page Up/Down or the mouse wheel; Home/End jump to the first/last image of the result list.
        - **Zoom** with Ctrl + mouse wheel (towards the mouse position), Ctrl + Plus/Minus or the magnifier buttons (5 % to 800 %). When zoomed, drag the image with the mouse button held down.
        - **Fit** (Ctrl + 0 or double-click on the image) switches between fitted and 100 %. When fitted, the image is never enlarged beyond 100 %.
        - **Original** shows the unedited image of an edited capture in full resolution. A hint appears when Snagit saved the image with annotations at a reduced size.
        - On the right you find tags, metadata (selectable and copyable) and all texts from the annotations.
        - Actions: Open in Snagit (Ctrl + E), Save image (PNG), Copy image, Copy texts, Show in Explorer, Delete (Del).

        # 6. Editing tags
        Tags are written directly into the `.snagx` file, so they are also visible in Snagit.
        ## One capture (detail view)
        - **×** on a tag removes it. In the **New tag or search …** field, Enter adds a tag; at the same time the field filters the list of existing tags.
        - A click in the list of existing tags adds or removes it. If only the upper/lower case differs, the existing spelling is used.
        - **Save** (Ctrl + S) writes the change, **Discard** reverts it. Browsing to another image or closing the window saves automatically.
        ## Several captures (Ctrl + T)
        - Shows for each tag on how many of the selected images it is present (e.g. `3/10`).
        - Clicking a tag sets it on all; if it is already present everywhere, it is removed from all. **×** removes it from all.
        - A summary shows the planned changes. **Apply** (Ctrl + Enter) writes all files; files that could not be written are listed at the end.

        # 7. Deleting
        - Captures are moved to the **Windows Recycle Bin** and can be restored from there.
        - The confirmation can be switched off with **Don't ask again** or via **Ask before deleting**.
        - Deleted captures disappear immediately from the gallery, the filters and the index.

        # 8. Export and clipboard
        | Function | Result |
        | Export images (PNG) | Images with annotations as PNG into a target folder |
        | Copy .snagx files to | Original files into a target folder |
        | Save image (detail view) | One image as PNG, optionally the original |
        | Copy image | Current image to the clipboard |
        | Copy texts (Ctrl + C) | Texts of the selection to the clipboard |
        Existing files in the target are never overwritten but numbered (`File (2).png`). The last export folder is remembered. Exports into the Snagit folder are blocked.

        # 9. Data safety when writing tags
        - If the file was changed by another program (e.g. Snagit) since it was read, nothing is written.
        - Beforehand a **backup copy** is created in `%LOCALAPPDATA%\SnagitBrowser\Backups\` and verified.
        - Only the tag entry in `metadata.json` is replaced; images and annotations stay identical byte for byte. If the entry is missing, it is inserted.
        - The new version is verified and then replaced **atomically** – after a crash, either the old or the new version is fully intact.
        - Backup copies older than 30 days are deleted at startup.

        # 10. Language, settings and storage locations
        - Choose the **language** (Deutsch/English) at the top right. It takes effect immediately and is saved for the next start. The default is German.
        - Also saved: source folder, last export folder, Ask before deleting, Show tags on the images and the state of the tag bar.
        | File/folder in %LOCALAPPDATA%\SnagitBrowser | Content |
        | settings.json | Settings |
        | index-….db | Search index per source folder |
        | Backups\ | Backup copies before tag changes |

        # 11. Keyboard shortcuts
        ## Gallery
        | Key | Function |
        | F1 | This help |
        | Enter / double-click | Open detail view |
        | Left/Right arrow | Move selection |
        | Ctrl + A | Select all |
        | Ctrl + C | Copy texts |
        | Ctrl + T | Edit tags |
        | Del | Move to Recycle Bin |
        | Ctrl + mouse wheel | Change thumbnail size |
        ## Detail view
        | Key | Function |
        | Arrow keys, Page Up/Down, mouse wheel | Previous/next image |
        | Home / End | First/last image |
        | Ctrl + mouse wheel, Ctrl + Plus/Minus | Zoom |
        | Ctrl + 0, double-click | Fit / 100 % |
        | Ctrl + S | Save tags |
        | Ctrl + E | Open in Snagit |
        | Del | Move to Recycle Bin |
        | Esc | Clear input or close window |
        ## Tags for several captures
        | Key | Function |
        | Enter | Set tag on all |
        | Ctrl + Enter | Apply |
        | Esc | Clear input or cancel |

        > Snagit Browser, version {version}
        """;
}
