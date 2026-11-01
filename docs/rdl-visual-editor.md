# Visueller RDL-Editor

Für RDL gibt es einen gemeinsamen Reiter **Editor**. **Visuell** und **XML**
wechseln im selben Bereich zwischen Schreibfläche und Quelltext. Ungespeicherte
Änderungen bleiben beim Wechsel erhalten. Die zusätzliche Klartextvorschau und
der separate RDL/XML-Reiter entfallen für RDL; HEX bleibt separat.

Der visuelle Modus bietet eine einzige zusammenhängende Schreibfläche mit einer
Symbolleiste (B/I/U, Farbpalette, Rückgängig/Wiederholen). Er unterstützt Fett,
Kursiv, Unterstreichen, die originale Freelancer-Palette und eigene RGB-Farben.
RDL-Inhalt darf dabei in RT_HTML oder in einer Stringtabelle liegen: der Speichertyp
bleibt erhalten. XML, Klartextvorschau und HEX werden aus dem visuellen Entwurf aktualisiert.

TEXT-Grenzen bleiben unsichtbar: mehrere formatierte TEXT-Knoten stehen innerhalb
desselben Absatzes. Enter erzeugt einen echten PARA-Absatz; das Löschen einer
Absatzgrenze entfernt den PARA-Knoten. Automatischer Zeilenumbruch erzeugt kein PARA.

Schriftfamilie, Größe, STYLE-Vorlagen, JUST-Ausrichtung
und vom Spielkontext geerbte Standardwerte werden nicht als pixelgenaue Spielansicht
nachgebaut. Ihre bestehenden Anweisungen bleiben im Original-RDL erhalten. Komplexe
TEXT-Inhalte (z.B. CDATA oder untergeordnete Elemente) werden weiterhin im XML-Reiter bearbeitet.

## Grundlage aus fl_decomp

Lokal geprüft in D:/Dev/fl_decomp/src:

- Common/common_rdlio.cpp, ParseRdlxTag: TRA erkennt data, mask, def, font,
  bold, italic, underline, color. Bits 0/1/2 sind Fett/Kursiv/Unterstrichen,
  Bits 3–7 enthalten den Fontindex, Bits 8–31 eine COLORREF-Farbe.
- Dieselbe Datei: benannte Farben gray, blue, green, aqua, red, fuchsia,
  yellow, white; numerische Farben sind 1-basiert. Bei #RRGGBB vertauscht
  der Retail-Parser die Nibbles pro Kanal. Neue Farben werden deshalb als
  gepackte TRA-data/mask-Werte geschrieben, mit exakt der gewählten RGB-Farbe.
- Common/common_rdlnodes.cpp, TRANode::Execute und AttribStackNode::Execute;
  Common/common_textrender.cpp, get_default_attributes/push_default_attributes/
  pop_default_attributes: PUSH sichert aktuelle Attribute als Defaults. POP
  entfernt diesen Default-Eintrag, stellt aktuelle Attribute aber nicht wieder her.

## Erhaltung der Quelle

Ungeänderte Abschnitte einschließlich Entities und Attribute bleiben unverändert.
Verborgene Quellzuordnungen an Inline-Spannen erhalten die ursprünglichen TEXT-
Kontexte auch beim Formatieren und Bearbeiten über frühere TEXT-Grenzen hinweg.
Originale PARA-Knoten werden aus den tatsächlich sichtbaren Editor-Absätzen
rekonstruiert; sonstige Layout-/Schriftanweisungen bleiben erhalten. Formatierte Teilstücke
werden mit PUSH und einem auf die geänderten Bits beschränkten TRA umgeben.
Vor POP stellt ein TRA mit gleichem mask und def diese Bits gegen den gesicherten
Kontext wieder her. Dadurch ändern sich weder Schriftindex noch folgende Texte.
Zeilenumbrüche aus der visuellen Eingabe werden als PARA gespeichert.

Keine Bedienung des visuellen Editors schreibt direkt eine DLL. Die bestehenden
Entwurfs-, Validierungs-, Sicherungs- und Konfliktprüfungen bleiben aktiv.

## Quelltext-Inspektor

Der gemeinsame Inspektor im Hauptfenster und im abgedockten Quelltextfenster zeigt
RDL als schreibgeschützte formatierte Leseansicht. Ansicht/XML wechselt zur exakten
Quelle ohne Bearbeitung. Auch RDL in Stringressourcen wird erkannt; einfache
Texte und Fehlerzustände bleiben lesbar. Änderungen erfolgen über Ressource bearbeiten.
