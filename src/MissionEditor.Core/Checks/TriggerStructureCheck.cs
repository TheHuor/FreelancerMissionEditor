using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.Core.Checks;

/// <summary>
/// The shape of [Trigger] sections as content.dll reads them: a nickname is required, values before
/// it are skipped, exactly one Cnd_* is expected (none crashes on activation, only the last of
/// several counts) and misspelt Cnd_/Act_ keys are silently ignored.
/// </summary>
public sealed class TriggerStructureCheck : IMissionCheck
{
    /// <inheritdoc/>
    public string Id => "trigger-structure";

    /// <inheritdoc/>
    public string Title => "Aufbau der Trigger";

    /// <inheritdoc/>
    public IEnumerable<Finding> Run(MissionScript script)
    {
        foreach (IniSection section in script.TriggerSectionsWithoutNickname)
        {
            yield return new Finding(FindingSeverity.Error, Id, section.FirstLineNumber, null,
                "[Trigger] ohne nickname – die Engine überspringt die ganze Sektion.");
        }

        // Shadowed duplicates never reach the engine; the duplicate check reports them.
        foreach (Trigger trigger in script.EffectiveTriggers)
        {
            if (trigger.Conditions.Count == 0)
            {
                yield return new Finding(FindingSeverity.Error, Id, trigger.NicknameLine.Number, trigger.Nickname,
                    $"Trigger '{trigger.Nickname}' hat keine Cnd_*-Bedingung – das Spiel stürzt ab, sobald er aktiviert wird.");
            }
            else if (trigger.Conditions.Count > 1)
            {
                IniLine last = trigger.Conditions[^1];
                yield return new Finding(FindingSeverity.Warning, Id, trigger.Conditions[0].Number, trigger.Nickname,
                    $"Trigger '{trigger.Nickname}' hat {trigger.Conditions.Count} Bedingungen; nur die letzte gilt ({last.Key} = {last.RawValue}, Zeile {last.Number}).");
            }

            foreach (IgnoredEntry ignored in trigger.IgnoredEntries)
            {
                string message = ignored.Reason == IgnoreReason.BeforeNickname
                    ? $"'{ignored.Line.Key}' steht vor nickname – die Engine ignoriert die Zeile."
                    : $"Unbekannter Schlüssel '{ignored.Line.Key}' – die Engine ignoriert die Zeile (Tippfehler?).";
                yield return new Finding(FindingSeverity.Warning, Id, ignored.Line.Number, trigger.Nickname, message);
            }
        }
    }
}
