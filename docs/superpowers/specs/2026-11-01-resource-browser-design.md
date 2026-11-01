# Ressourcenbrowser – Gestaltung und Funktionsumfang

Stand: 01.11.2026. Entwurf zur Abstimmung, noch keine Implementierung.

## Ziel und vereinbarte Richtung

Ressourcentexte neben einer Mission lesen, verifizieren, bearbeiten und anhand ihrer Verwendung einordnen, ohne horizontal durch die Quelltextvorschau zu scrollen. Ein einziges nicht modales, vergrößerbares Fenster pro Editor dient als Textdetail, Editor und Ressourcenbrowser. Der Benutzer hat dieser Richtung sowie dem Speichern in der ursprünglichen Ressource oder als Kopie zugestimmt; die folgenden Einzelheiten sind Gestaltungsvorschläge.

## Betrachtete Ansätze

- Nur Tooltip/aufklappbarer Text: kleiner Eingriff, löst aber längere Lesevorgänge, Kopieren und dateiübergreifendes Nachschlagen schlecht.
- Festes Detailpanel im Hauptfenster: unmittelbarer Kontext, nimmt dem bereits geteilten Missionseditor weitere Fläche.
- Eigenes wiederverwendetes Ressourcenfenster: neben dem Editor oder auf einem zweiten Bildschirm nutzbar, mit eigener Suche und Übersicht. Gewählte Richtung aus dem Gespräch.

## Fensteraufbau

### Abgestimmte Fensteraufteilung

- Hauptfenster: rechte Spalte vergrößerbar, mit kompakter Quelltextvorschau und kurzen Ressourcenhinweisen. Keine langen Ressourcentexte in dieser Spalte. Unten bleiben die Prüfergebnisse.
- Ein separates, nicht modales Quelltextfenster zeigt den ausgewählten Abschnitt groß an. Numerische IDS sind sowohl hier als auch in der Hauptfenstervorschau einzeln anklickbare Links.
- Ein separates, nicht modales Ressourcenfenster bietet den nachfolgend beschriebenen großen Browser einschließlich Bearbeitung. Quelltext und Ressourcentext sind gleichzeitig sichtbar; keine gemeinsamen Reiter anstelle der getrennten Fenster.
- Pro Editor werden Quelltextfenster und Ressourcenfenster jeweils wiederverwendet. Ressourcenlinks aktivieren das vorhandene Fenster und wählen nach Behandlung offener Änderungen die angeforderte ID. Position und Größe beider Fenster werden gespeichert.
- Aufgelöste IDS: cyanfarbene Links. Nicht aufgelöste IDS: rote, unterstrichene Links mit verständlichem Fehlertext im Tooltip. Beide Zustände bleiben anklickbar. Ursache und Auflösungsstatus sind im Ressourcenfenster als Text sichtbar.

Vorschlag: ungefähr 1120 × 760 Geräte-unabhängige Pixel beim ersten Öffnen, minimale Größe 840 × 560. Native Skalierung, Tastaturbedienung, dunkle vorhandene Farbpalette. Position, Größe und Teilungsverhältnisse wiederherstellen, Bildschirmgrenzen berücksichtigen.

1. Kopf: „Ressourcentexte“, aktive Spielinstallation und Missionsdatei; lange Pfade per Tooltip vollständig lesbar.
2. Werkzeugzeile: Bereich „Aktuelle Mission“ / „Gesamte Installation“, Suche nach ID oder Text, Kategorienfilter und DLL-Filter. Anzahl gefilterter und verfügbarer eindeutiger IDs.
3. Linke Liste, etwa 35 % der Breite: numerische IDS, kurze Textvorschau, Verwendungsarten und Ressourcen-DLL. Sortierung standardmäßig numerisch nach IDS, alternativ DLL oder Verwendung. Virtuelle Liste; feste Position der ID, Textvorschau gekürzt. Keine Gruppierung, die IDs mehrfach erscheinen lässt.
4. Rechte Lesefläche, etwa 65 %: ausgewählte IDS, Ressourcentyp und DLL; großer Textbereich mit Zeilenumbruch und erhaltenen Absätzen. Text markieren und kopieren. Lange Texte scrollen nur vertikal. Schaltflächen „ID kopieren“ und „Text kopieren“.
5. „Wird verwendet von“ unterhalb des Textes: getrennt verstellbare Höhe, einklappbar; Mission, Trigger/Abschnitt, Zeile, Verwendung und relevante INI-Zeile. Doppelklick oder „Zur Fundstelle“ aktiviert den Editor und zeigt die genaue Zeile.

### „Wird verwendet von“

- Überschrift mit Anzahl der Fundstellen und explizitem Bereich „Aktuelle Mission“, beispielsweise „Wird verwendet von · 3 Fundstellen“.
- Ein Eintrag pro tatsächlicher Verwendung: Missionsdatei, Trigger-/Abschnittsname, Zeile und Rolle wie Dialogtitel, Dialogtext, Missionsziel oder Name einer Basis. Mehrere Rollen in derselben Quellzeile bleiben unterscheidbar.
- Direkte IDS-Verwendungen und nachvollziehbar aufgelöste Objektverweise unterscheiden. Beispiel: eine Basisreferenz kann über die Basendefinition auf deren Namens-ID führen; den Bezugspfad anzeigen und keine direkte IDS-Verwendung in der Mission vortäuschen.
- Anklickbare Einträge öffnen beziehungsweise aktivieren das Quelltextfenster am passenden Abschnitt und markieren die konkrete Zeile; die Auswahl im Haupteditor wird passend synchronisiert.
- Grundlage ist die aktuelle Mission einschließlich ungespeicherter Missionsänderungen. Nach Bearbeitung, Undo oder Neuladen Fundstellen aktualisieren.
- Keine Treffer bedeutet „Keine Verwendung in aktueller Mission gefunden“, nicht „nirgendwo verwendet“. Andere Missionen werden ohne expliziten missionsübergreifenden Index nicht mitgezählt.
- „Weitere Missionsdateien durchsuchen“ bleibt ein späterer Ausbau; ein solcher Suchlauf muss seinen gewählten Ordner und Umfang anzeigen.

Der Text ist der visuelle Schwerpunkt. Metadaten bleiben klein und zurückhaltend. Cyan kennzeichnet Auswahl und Navigation; Rot/Gelb bleiben Fehlersituationen vorbehalten. Kategorien sind beschriftet und funktionieren ohne Farberkennung.

## Einstieg aus dem Editor

- In der Quelltextvorschau ersetzt eine kompakte Vorschau mit anklickbarer IDS den ungekürzten angehängten Text. Vorschlag: „IDS 123456 · Text öffnen“, ergänzt um einen kurzen Textanfang innerhalb des verfügbaren Platzes.
- Die INI-Zeile selbst bleibt unverändert und separat horizontal scrollbar. Das Ressourcenangebot liegt in einer eigenen, an die sichtbare Panelbreite gebundenen Zeile; es darf nicht mit der langen Codezeile aus dem sichtbaren Bereich wandern.
- Eine Quellzeile kann mehrere anklickbare IDs enthalten, beispielsweise Titel und Text eines Pop-up-Dialogs. Jede ID wird mit ihrer Rolle bezeichnet; kein zusammengeklebter Sammeltext.
- Fehlende Texte behalten ihren Einstieg: „IDS 123456 · nicht gefunden“. Auch fehlerhafte Werte müssen sichtbar erklärbar sein.
- Ein zusätzlicher Toolbar-Eintrag „Ressourcentexte…“ öffnet die Übersicht ohne ausgewählte Quellzeile.
- Ein erneuter Klick aktiviert das bestehende Fenster und wählt die betreffende ID. Bereits geöffnete Fenster werden nicht vervielfacht.
- Navigation von der Quellzeile darf nicht an alten Filtern scheitern: auf Missionsbereich wechseln, verdeckende Filter zurücksetzen und die angeforderte ID sichtbar auswählen.
- Auswahl im Editor wechselt die Ressourcenauswahl nicht automatisch; bewusstes Nachschlagen bleibt stabil.

### Aufgelöste Spielobjekte im Quelltext

- Zusätzlich zu numerischen IDS auch erkannte Basen-, System- und Schiffstyp-Nicknames auflösen. Das bestehende GameData-Modell hält bisher nur Nickname-Mengen; dafür sind strukturierte Datensätze mit INI-Herkunft und `ids_name`/`ids_info` erforderlich.
- Unter der unveränderten Codezeile eine eigene Hinweiszeile: Art, aufgelöster Anzeigename und kurzer Herkunftshinweis. Beispiel: „Basis · Planet Manhattan“ für `Li01_01_Base`. Konkreter Name hängt von den geladenen Spielressourcen ab.
- Der sichtbare Anzeigename führt zur zugehörigen Namens-ID im Ressourcenbrowser; eine vorhandene Infocard erhält einen eigenen Einstieg. Die Herkunfts-INI bleibt separat erreichbar und wird nicht mit der Ressourcen-DLL verwechselt.
- Bei Missionsschiffen zunächst die Objektdefinition und deren `individual_name`, anschließend gegebenenfalls NPCShipArch/Loadout/Schiffstyp verfolgen. Objektname und Typname klar unterscheiden; nicht jeden beliebigen Nickname als globale Schiffstyp-ID behandeln.
- Auflösung nur an bekannten Schlüssel-/Parameterpositionen. Keine Vermutungen aufgrund ähnlich aussehender Namen; fehlende Zuordnungen klar anzeigen.
- Numerische IDs derselben Zeile als einzelne, rollenbeschriftete Vorschauen darunter anzeigen, bei langen Texten mit Ellipse. Wahlweise lokale Klartextansicht aufklappen; „Im Ressourcenbrowser öffnen“ führt zu Bearbeitung, Quelle und sämtlichen Missionsfundstellen.
- Hinweise bleiben unabhängig von der horizontalen Scrollposition der Codezeile sichtbar. Weder ergänzte Namen noch Texte werden Bestandteil des kopierten Missionsquelltexts.

## Bereiche und Kategorien

### Aktuelle Mission (Standard)

Alle erkannten IDS-Verwendungen des aktuellen Dokumentstands, einschließlich ungespeicherter Änderungen. Eine ID erscheint einmal, kann aber mehrere Verwendungsarten und Fundstellen haben. Auch nicht auflösbare IDs werden erfasst. Suchbegriff und Filter wirken gemeinsam.

Fachliche Filter werden aus der INI-Verwendung abgeleitet:

- Missionsziel: `[NNObjective] type = ids / rep_inst`, `Act_NNIds`.
- Missionstitel: `Act_SetTitle`.
- Missionsangebot: `Act_SetOffer`.
- Dialog: `Act_PopUpDialog`, mit getrennten Rollen für Überschrift und Inhalt.
- Name: `individual_name`, `ids_name`, Namensparameter von `Act_EtherComm`.
- Infocard: `ids_info`.
- Weitere Texte: `string_id` und künftig weitere explizit unterstützte Verwendungen.

Diese Kategorien beschreiben Verwendung, keine Eigenschaften der DLL. Mehrfachzuordnung ist erlaubt. Keine beliebigen Zahlen als IDS interpretieren; die bestehende Schlüssel-/Parametererkennung wird strukturiert wiederverwendet.

### Gesamte Installation

Alle tatsächlich vorhandenen String- und Infocardressourcen der in `freelancer.ini` registrierten DLLs, einschließlich `resources.dll`. DLL-Filter und Ressourcentyp „String / Infocard“ sind zuverlässig. Fachliche Verwendungen beziehen sich weiterhin ausdrücklich nur auf die aktuelle Mission; unreferenzierte Einträge heißen „Keine Verwendung in aktueller Mission“, nicht „unbenutzt“.

Beim ersten Aufruf im Hintergrund indexieren, Fortschritt und Abbrechen anbieten. Das Fenster bleibt bedienbar. Keine Brute-force-Abfragen des gesamten möglichen IDS-Zahlenraums. Ressourcen über Windows-Ressourcentabellen enumerieren; Stringblock- und lokale IDs korrekt in globale IDS zurückrechnen. Kein automatischer Scan weiterer Missionsdateien.

## Detailinformationen

- Globale IDS dezimal, lokale ID und DLL-Slot als aufklappbare technische Details.
- Ressourcen-DLL mit vollständigem Pfad bei Bedarf; maßgeblich ist die aktive Installation.
- Tatsächlicher Ressourcentyp, verfügbare Sprachvarianten bei der Enumeration; Standardauflösung zunächst konsistent mit dem bestehenden Leser.
- Volltext mit Absätzen und Unicode; keine Ersatzzeichen für Absätze in der Lesefläche.
- Infocards zunächst als lesbarer Klartext. Hinweis „Klartext aus Infocard“, keine Behauptung einer originalgetreuen Darstellung im Spiel.
- Reiter „Bearbeiten“ und „Vorschau“; für Infocards zusätzlich „RDL/XML“. Die konkrete Bearbeitungsweise und der Formatierungserhalt sind unten festgelegt. Kein Ausführen von HTML oder Skripten.
- Fundstellen zeigen auch bei derselben ID unterschiedliche Rollen und Zeilen.
- Bearbeitung vorhandener Texte einschließlich Speichern in ihre DLL oder in eine vollständige DLL-Kopie gehört zum Zielumfang.

## Textbearbeitung und Speichern

### Bedienung und Entwürfe

- Aktion „Bearbeiten“ öffnet den ausgewählten Text direkt in der großen Lesefläche als Editor. Aktionen: „Änderungen übernehmen“, „Verwerfen“, „Speichern“ und „Speichern unter…“.
- „Änderungen übernehmen“ übernimmt den Text in den internen Änderungsentwurf, schreibt aber noch keine Datei. Der Status benennt das ausdrücklich: „Entwurf – nicht gespeichert“.
- Mehrere IDs derselben DLL können als Entwürfe gesammelt werden. Geänderte Listeneinträge erhalten eine Markierung; am Dateinamen steht die Anzahl ausstehender Änderungen.
- Identität eines Entwurfs: Installations-/Dateipfad, Ressourcentyp, lokale Ressourcen-ID und tatsächliche Sprach-ID; globale IDS allein reicht dafür nicht aus.
- Vor einem ID-Wechsel mit ungespeicherten Änderungen erscheint „Speichern / Verwerfen / Abbrechen“. Gleiches gilt beim Schließen, Missions-/Installationswechsel oder Neuladen mit offenen Entwürfen. Diese ausdrückliche Benutzerentscheidung ersetzt das bisher vorgeschlagene stille Behalten beim ID-Wechsel.
- „Speichern“ führt die gewünschte Navigation erst nach erfolgreichem Schreiben fort. Bei Fehler oder abgebrochenem Speicherdialog bleibt die bisherige ID mitsamt Änderungen ausgewählt. „Verwerfen“ entfernt die betroffenen Änderungen und führt den Wechsel aus; „Abbrechen“ behält Auswahl und Inhalt bei. Andere übernommene Entwürfe nur im ausdrücklich benannten Umfang mitverwerfen.
- Ein Filterwechsel darf keine Bearbeitung verlieren. Würde er die Auswahl ersetzen oder entfernen, gilt dieselbe Rückfrage; andernfalls kann die geänderte ID als aktuelle Detailauswahl sichtbar bleiben.
- Text-Rückgängig/Wiederholen im Editor und „Änderungen an dieser ID verwerfen“; Missions-Undo und Ressourcenentwürfe bleiben getrennte Bereiche.
- Ein geänderter Text kann an mehreren Stellen und auch außerhalb der aktuellen Mission verwendet werden. Die Detailansicht zeigt „Ändert alle Verwendungen dieser Ressource“; die sichtbare Fundstellenzahl bezieht sich ausdrücklich nur auf die aktuelle Mission.
- Vorschau des Entwurfs im Browser; die Quelltextanzeige verwendet erst nach erfolgreichem Speichern die neu geladenen Ressourcentexte, damit sie weiterhin den tatsächlichen Dateistand wiedergibt.

### Strings und Infocards

- Einfache Strings: mehrzeiliger Unicode-Texteditor mit Absätzen; Leerzeichen und Sonderzeichen werden nicht unbemerkt bereinigt. Nullzeichen und nicht darstellbare Ressourcengrößen vor dem Schreiben validieren.
- Infocards: Klartext dient der Lesevorschau. Die bisherige tagsentfernte Anzeige darf nicht als Schreibquelle verwendet werden, weil sie Formatierung und Struktur verliert.
- Standardbearbeitung für Infocards: vorhandene TEXT-Inhalte innerhalb der RDL-Struktur editieren, übrige Elemente und Attribute bewahren. Bei mehreren Textsegmenten ihre Reihenfolge und Absatzgrenzen sichtbar machen.
- „RDL/XML“ bietet fortgeschrittene Bearbeitung des vollständigen Originalinhalts. Wohlgeformtheit und unterstützte RDL-Struktur vor dem Übernehmen prüfen; keine externen XML-Entitäten oder Netzwerkzugriffe.
- Wenn eine Infocard-Struktur nicht verlustfrei im vereinfachten Editor bearbeitet werden kann, bleibt dieser Modus gesperrt; RDL/XML kann weiterhin bearbeitet und validiert werden. Keine stille Umwandlung in unformatierten Text.
- Ressourcentyp und ausgewählte Sprache bleiben erhalten. Der Leser muss die tatsächlich geladene Sprache ausweisen; es darf nicht unter einer geratenen Standard-LANGID gespeichert werden. Andere Sprachvarianten bleiben unverändert.

### Speicheraktionen

- „Speichern“ beziehungsweise Strg+S im Browser schreibt alle übernommenen Entwürfe der ausgewählten DLL. Das Ziel und die Anzahl Änderungen sind vorab im Fenster sichtbar. Der aktuelle Editorinhalt wird vor dem Schreiben validiert und übernommen.
- „Speichern unter…“ erzeugt eine vollständige Kopie dieser DLL mit den übernommenen Änderungen. Es ist weder ein reiner Textexport noch automatisch eine neue Ressourcenregistrierung. Ein vorhandenes Ziel wird über den üblichen Überschreibdialog behandelt.
- Die ursprüngliche Installation bleibt nach „Speichern unter…“ die aktive Quelle. Offene Entwürfe gelten als exportiert, aber nicht ins Original gespeichert; „Änderungen ins Original speichern“ und „Entwürfe verwerfen“ bleiben verfügbar. Diesen Unterschied in Status und Schließdialog benennen.
- Eine anders benannte oder anders registrierte DLL wird nicht automatisch vom Spiel genutzt. Für gleichbleibende globale IDS muss die Kopie anstelle der ursprünglichen DLL im selben Ressourcen-Slot verwendet werden. Keine automatische Änderung an `freelancer.ini` und kein stilles Umschreiben von Missions-IDs.
- Änderungen an mehreren DLLs werden pro Datei gespeichert; keine vorgetäuschte Gesamttransaktion. Erfolgreiche und fehlgeschlagene Dateien getrennt anzeigen, nicht gespeicherte Entwürfe behalten.
- Neue IDs anlegen, Ressourcentypen löschen und DLL-Slots umordnen gehören nicht zu dieser Erweiterung; die Speicherfunktion ändert bestehende Einträge.

### Schreiben und Prüfen

1. Originalzustand der DLL beim Anlegen des ersten Entwurfs erfassen; vor dem Speichern auf externe Änderungen prüfen. Bei Konflikt nicht überschreiben, sondern Entwürfe erhalten und Neuladen/Vergleichen oder Speichern als Kopie anbieten.
2. Eine Arbeitskopie der vollständigen DLL erstellen. Vorhandene Ressourcen beim Öffnen zur Aktualisierung erhalten; ausschließlich ausgewählte Typ-/ID-/Sprach-Einträge ändern.
3. Stringressourcen werden blockweise gespeichert: alle benachbarten Strings desselben Blocks erhalten, auch bei mehreren Änderungen im selben Block. Infocards in ihrer ursprünglichen Kodierung und Struktur speichern.
4. Windows-Ressourcenupdates auf der Arbeitskopie ausführen. Bei Fehlern abbrechen, ursprüngliche DLL unangetastet lassen.
5. Arbeitskopie neu öffnen und die geschriebenen Einträge sowie unveränderte benachbarte Ressourcen prüfen. Erst anschließend Ziel ersetzen. Bei direktem Speichern eine eindeutige Sicherung des unmittelbar vorherigen Dateistands erstellen und deren Pfad anzeigen.
6. Eigene DLL-Handles und laufende Lese-/Indexoperationen für den Austausch koordiniert freigeben. Bei fremder Dateisperre oder fehlenden Schreibrechten eine verständliche Meldung und „Speichern unter…“ anbieten; keine automatische Rechteerhöhung.
7. Nach erfolgreichem Austausch Handles/Cache/Installationsindex invalidieren und die gespeicherten Texte erneut laden. Editorhinweise, Browserliste und Detailansicht aktualisieren. Entwürfe erst nach verifiziertem Erfolg als gespeichert markieren.

Die Wiederherstellung aus einer Sicherung bleibt möglich. Ein Fehler beim Neuladen wird von einem fehlgeschlagenen Schreibvorgang unterschieden; gespeicherte Änderungen nicht doppelt anwenden.

## Zustände und Aktualisierung

- Keine Spielinstallation: vorhandene Missionsreferenzen mit Status „Spielressourcen nicht geladen“, Aktion „Spielordner wählen…“.
- ID fehlt trotz lesbarer DLL: „Kein Ressourceneintrag gefunden“.
- DLL fehlt oder ist nicht lesbar: eigene Ursachenanzeige statt gleichlautendem „Text fehlt“; andere DLLs bleiben benutzbar.
- Keine Auswahl: „ID auswählen, um Text und Fundstellen zu lesen“.
- Keine Suchtreffer: Filter und Suchbegriff sichtbar lassen, „Filter zurücksetzen“ anbieten.
- Beim Editieren, Undo oder Neuladen: Referenzen und Fundstellen erneuern, Auswahl nach globaler ID erhalten. Wenn sie entfällt, sichtbar darauf hinweisen statt alten Text still weiter als aktuelle Missionsreferenz auszugeben.
- Bei Missionswechsel: auf neue Mission wechseln; derselbe Dialog bleibt geöffnet. Quellen und alte Fundstellen konsequent ersetzen.
- Bei Wechsel des Spielordners: Auflösung, Cache und Installationsindex wechseln; gleiche IDs dürfen keine Texte der vorherigen Installation übernehmen.
- Laufende Suche/Indexierung abbrechen oder Ergebnisse verwerfen, wenn ihre Installation beziehungsweise ihr Dokumentstand nicht mehr aktuell ist.
- DLL-Änderungen: zunächst explizite Aktion „Ressourcen neu laden“; dabei Index und Textcache invalidieren. Dateiwächter für sämtliche DLLs sind nicht erforderlich.

## Technische Einordnung

Bestehende Grundlagen:

- `GameData.DescribeIds` erkennt relevante Parameter, reduziert die Ergebnisse derzeit aber auf einen zusammengesetzten Text.
- `GameText.Get` löst eine konkrete IDS über DLL-Slot und lokale ID auf; DLL-Pfade und Cache sind intern.
- `SectionLineItem.Note` hält nur die fertige Anzeige, keine einzeln navigierbaren Referenzen.

Vorgeschlagene Einheiten:

1. Strukturierte `IdsReference`: globale IDS, Rolle/Kategorie, Abschnitt, Triggername soweit vorhanden, Quellzeile und Parameterposition. Mehrere Referenzen pro Zeile sind möglich.
2. Strukturierte Ressourcenauflösung: Text, DLL-Pfad/Slot, lokale ID, Ressourcentyp und Auflösungsstatus; Rohdaten bei Infocards getrennt halten. Bestehende Textabfrage kann als Kompatibilitätswrapper bleiben.
3. Missionsindex: Referenzen nach globaler IDS zusammenführen, Fundstellen separat halten. Snapshot des aktuellen In-Memory-Dokuments.
4. Installationskatalog: tatsächliche Ressourcen enumerieren, Ergebnisse je Installation cachen, Suche abbrechbar im Hintergrund. Lebensdauer der geladenen DLL-Handles und Synchronisierung explizit regeln.
5. `ResourceBrowserViewModel` und `ResourceBrowserWindow`: Filter, Auswahl, Befehle, Status, Einzelfenster-Verwaltung. Navigation zum Editor über vorhandene Abschnitts-/Zeilennavigation ergänzen.
6. Quelltextanzeige: strukturierte ID-Einstiege statt nur `NoteText`; Ressourcenansicht muss unabhängig von der horizontalen Codeposition erreichbar bleiben.
7. Ressourcenentwürfe und Writer: Änderungen je DLL, Typ, lokaler ID und Sprache sammeln; Originalzustand vergleichen; Stringblöcke/RDL verlustfrei schreiben; Arbeitskopie validieren, sichern und ersetzen. Eigene Handle-Lebensdauer und Reload koordiniert regeln.

Auch die bestehende Begrenzung der Stringabfrage auf 4096 Zeichen prüfen: der Browser darf längere Ressourcentexte nicht still abschneiden. Absätze des Klartextes bewahren; ursprüngliche Rohdaten nicht mit der bereinigten Anzeige überschreiben.

## Umsetzung in überprüfbaren Etappen

1. Referenzmodell und zuverlässige Auflösung: mehrere Parameter, fehlende Werte, Herkunft und lange Texte testen; bestehende Anzeigen kompatibel halten.
2. Missionsbrowser mit Lesefläche, Suche, Filtern, Kopieren und Fundstellennavigation; Einstieg aus Quelltext und Toolbar. Dies liefert die unmittelbare Verbesserung für das Lesen.
3. Installationsweiter Katalog mit DLL- und Typfiltern, Hintergrundindexierung, Abbruch und Neuladen; optional Rohdatenansicht für Infocards.
4. Bearbeitung einfacher Strings und Infocards, Änderungsentwürfe, Vorschau, Undo und explizite Speicherziele; danach Writer mit Arbeitskopie, Sicherung, Konflikterkennung und Neuladen.
5. Visuelle und funktionale Abnahme: DPI-Skalierung, minimale Fensterbreite, lange Texte/IDs/DLL-Namen, Tastatur, Auswahlfarben und Binding-Protokollierung. Schreibtests ausschließlich mit temporären DLL-Kopien.

Der Zielumfang enthält beide Bereiche. Die Etappen sind eine Umsetzungsreihenfolge, keine Streichung der installationsweiten Suche.

## Abnahmekriterien

- Vollständige Texte lassen sich ohne horizontales Scrollen im Lesebereich prüfen und markieren.
- Ein Klick auf jede erkannte ID öffnet exakt diese ID; mehrere IDs pro Zeile bleiben unterscheidbar.
- Jede Anzeige benennt ihre Installation und DLL; fehlende Einträge sind von Ladeproblemen unterscheidbar.
- Kategorien werden aus tatsächlichen Verwendungen abgeleitet, Ressourcen-Dateien aus der DLL-Zuordnung.
- Fundstellennavigation funktioniert auch bei mehrfacher Verwendung, nach Bearbeitung und nach Undo.
- Missionswechsel und Installationswechsel zeigen keine alten Fundstellen oder gecachten Fremdtexte.
- Suche und Indexierung blockieren die Oberfläche nicht; Abbrechen und erneutes Laden funktionieren.
- Bei langen Texten ist kein stilles Abschneiden vorhanden, Absätze und Sonderzeichen bleiben lesbar.
- Keine neu eingeführten Binding-Fehler; Bedienung ohne Maus und mit 125/150/200 % Skalierung prüfen.
- Gespeicherte Strings und Infocards lassen sich vollständig wieder einlesen; Nachbarstrings, andere Ressourcentypen und Sprachvarianten bleiben erhalten.
- Infocard-Bearbeitung bewahrt Formatierung und Struktur; fehlerhaftes RDL kann nicht übernommen werden.
- „Speichern unter…“ ändert weder Original-DLL noch Ressourcenregistrierung; der Status unterscheidet exportierte von ins Original gespeicherten Entwürfen.
- Externe Änderungen, Dateisperren, unzureichende Rechte und abgebrochene Schreibvorgänge verlieren keine Entwürfe und beschädigen das Original nicht.
- Nach direktem Speichern zeigen Browser und Editor die neu aus der Datei geladenen Texte; Sicherungspfad ist verfügbar.

## Bewusst spätere Erweiterungen

Neue IDs anlegen, ganze Missionsverzeichnisse auf Verwendungen durchsuchen, Exporttabellen, Sprachvergleich und originalgetreue RDL-Darstellung im Stil des Spiels. Bearbeitung und Speichern bestehender DLL-Texte gehören ausdrücklich zum aktuellen Zielumfang.

## Technische Quellen für den Writer

- Microsoft: [BeginUpdateResourceW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-beginupdateresourcew).
- Microsoft: [UpdateResourceW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-updateresourcew).
- Microsoft: [EndUpdateResourceW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-endupdateresourcew).
