# Anzeigewege von Missionstexten (fl_decomp)

Untersucht am 2026-11-01 in D:/Dev/fl_decomp. Dies ist eine belegte Teilzuordnung,
keine vollständige Analyse aller globalen Verwendungen einer IDS. Klassifiziert
wird die konkrete Verwendung, nicht der Inhalt oder der Speichertyp einer Ressource.

| Verwendung | Belegter Anzeigeweg | Bewertung |
| --- | --- | --- |
| Act_PopUpDialog, Parameter 0 (Titel) | MissionAct.cpp: ActPopUpDialog::Execute -> NnMissionMessagePopUp -> NnMissionMessage::Create: FmtStrSprintf, CharUpperW, Title->SetProperty(0x1d) -> NnTextControl::SetProperty -> FontStringSetText | Klartext, kein RDL-Parser im Titelweg |
| Act_PopUpDialog, Parameter 1 (Text) | Gleicher Dialog: FmtStrSprintf -> AppendTextMaybeXmlRdl -> XMLReader, danach RichTextWin | RDL-fähig; Klartext-Fallback |
| Act_SetOffer (Anzeige als Missionsnachricht) | ActSetOffer::Execute -> DisplayMissionMessage Typ 8 -> Nachrichten-Body via AppendTextMaybeXmlRdl | Diese Anzeige ist RDL-fähig; andere Verwendungen nicht damit automatisch geprüft |
| Act_NNIds (Nachrichten-Body) | ActNnIds::Execute -> DisplayMissionMessage mit skriptseitigem MessageType | Kontext-/typabhängig; Body des untersuchten Nachrichtenfensters RDL-fähig, andere Typen weiter prüfen |
| NNObjective im HUD-Zielbanner | FNNObjectiveIds/RepInst::BuildDescription -> FmtStr; MissionObjectiveWindow formatiert den aktuellen Text, wandelt ihn in Großschrift, entfernt LF und setzt NnTextControl-Property 0x1d | Dieser Endpunkt ist Klartext |
| Ziel-/Logeinträge in NeuroNetStoryStar | Dortiger Log-/Missionsmodus formatiert Einträge und übergibt sie an AppendTextMaybeXmlRdl, anschließend Target->AppendContent | Dieser Endpunkt ist RDL-fähig; dieselbe Zielbeschreibung kann auch das Klartextbanner erreichen |
| Act_SetTitle | ActSetTitle::Execute setzt mission.Field_0x50 und sendet nach RenumberAllObjectives weiter | Endpunkte noch nicht vollständig zugeordnet; nicht als RDL-sicher klassifizieren |
| individual_name, ids_name, Sprechername, string_id | Mehrere mögliche Verbraucher bzw. Substitution in FmtStr | Ohne konkrete Anzeigewegprüfung offen; keine pauschale Freigabe |
| ids_info | Üblicher IDS-RDL-Lader versucht RT_HTML, dann Stringtabelle und XMLReader mit Klartext-Fallback | RDL-fähiger Endpunkt nachgewiesen, aber Aufrufstelle/zusätzliche Verwendungen getrennt prüfen |

## Quellstellen

- src/Content/MissionAct.cpp: ActSetOffer::Execute, ActSetTitle::Execute,
  ActNnIds::Execute, ActPopUpDialog::Execute.
- src/Content/MissionObject.cpp: FNNObjectiveIds::BuildDescription,
  FNNObjectiveRepInst::BuildDescription.
- src/freelancer/Client/nn_mission_message.cpp: Popup-Titel um Zeile 240,
  Nachrichten-Body um Zeile 350.
- src/freelancer/Client/nn_text_control.cpp: SetProperty case 0x1d um Zeile 904.
- src/freelancer/Client/nn_story_star.cpp: MissionObjectiveWindow um Zeile 600,
  Bannertext um Zeile 764, Log-/Missions-RDL um Zeile 1170–1245.
- src/freelancer/Client/ids_rdl.cpp: AppendTextMaybeXmlRdl,
  LoadIdsIntoRdl (RT_HTML zuerst, Stringtabelle danach, XML mit Klartext-Fallback).
- src/freelancer/Client/misc_util.cpp: FmtStrSprintf lädt zunächst eine
  Vorlage über GetIdsStringWWithRawFallback und ersetzt Parameter. Diese
  Vorlagenauflösung ist selbst noch keine RDL-Darstellung.

## Konsequenz für den Editor

Eine spätere Kennzeichnung sollte pro Fundstelle Klartext, RDL-fähig, gemischt
oder ungeprüft angeben. Für eine ID werden diese Informationen aus ihren bekannten
Fundstellen zusammengefasst. Sobald ein Klartext-Endpunkt existiert, ist eine
pauschale RDL-Umwandlung dieser ID nicht sicher. Unbekannte Verwendungen bleiben
sichtbar; das Fehlen einer Klartext-Fundstelle ist kein Beweis vollständiger Sicherheit.
FmtStr-/mad-lib-Platzhalter und weitere Verwendungen derselben ID müssen erhalten bleiben.

Die formatierte Editorvorschau sagt lediglich, dass der Inhalt lesbares RDL ist.
Sie bestätigt keine Kompatibilität aller Spielanzeigen. Die konservativ belegten Regeln sind jetzt im Editor und als gemeinsame Missionsprüfung implementiert.


## Implementierter Stand

MissionTextUsage klassifiziert Popup-Titel als Klartext, Popup-Text und Act_SetOffer
als RDL-fähig und NNObjective-Texte als gemischt. Act_SetTitle, Act_NNIds,
Objekt-/Sprechernamen und indirekte Info-Verwendungen bleiben vorerst ungeprüft.
Eine RDL-fähige Nutzung erlaubt auch einfachen Text: dafür entsteht keine Warnung.

ResourceRenderingCheck (Check-ID resource-rendering) meldet Warning, wenn RDL/XML
an einer bekannten Klartext- oder gemischten Fundstelle verwendet wird. Er ist in
MissionChecker integriert und damit GUI, CLI und MCP gemeinsam verfügbar.
Fehlende/unlesbare Ressourcen und ungeprüfte Anzeigewege werden nicht als Konflikt
behauptet. Vorhandene Sprachvarianten werden separat geprüft, mit dem string-first
Ladeweg von FmtStr; überschattete Infocard-Alternativen erzeugen keine falsche Warnung.

Der Ressourcen-Editor zeigt Zusammenfassung, Anzeigeart je Fundstelle und Live-
Warnungen für den aktuellen Entwurf. Der Quelltext-Inspektor zeigt die konkrete
Anzeigeart und eine Warnung beim betroffenen Parameter. Nach einem verifizierten
DLL-Speichern aktualisiert RefreshResourceHints auch die Missionsprüfung.
Die Prüfung repariert oder konvertiert Texte nicht automatisch.
