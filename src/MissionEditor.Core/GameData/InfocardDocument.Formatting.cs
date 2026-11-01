using System.Globalization;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace MissionEditor.Core.Model;

/// <summary>Formatting overrides for a text run; null preserves the original game context.</summary>
public sealed record RdlTextRun(string Text, bool? Bold = null, bool? Italic = null, bool? Underline = null, uint? Rgb = null);

/// <summary>Known bits of the FL packed text attributes; unknown bits depend on the game context.</summary>
public readonly record struct RdlTextStyle(uint Data, uint KnownMask);

public sealed partial class InfocardDocument
{
  /// <summary>Read TRA attributes using FL mask/default and PUSH/POP semantics. Game STYLE definitions remain unknown.</summary>
  public IReadOnlyList<RdlTextStyle> TextStyles()
  {
    var result = new List<RdlTextStyle>();
    var stack = new Stack<RdlTextStyle>();
    var current = new RdlTextStyle();
    foreach (var element in XDocument.Parse(_raw).Descendants())
    {
      switch (element.Name.LocalName.ToUpperInvariant())
      {
        case "PUSH": stack.Push(current); break;
        case "POP": if (stack.Count > 0) stack.Pop(); break; // POP does not restore current attributes in FL.
        case "STYLE": current = new(); break;
        case "TEXT": result.Add(current); break;
        case "TRA":
          uint mask = 0, defaults = 0, data = 0;
          foreach (var attribute in element.Attributes())
          {
            string value = attribute.Value;
            string key = attribute.Name.LocalName;
            if (key == "mask") mask = Number(value);
            else if (key == "def") defaults = Number(value);
            else if (key == "data") data = Number(value);
            else
            {
              uint bit = key switch { "bold" => 1u, "italic" => 2u, "underline" => 4u, "font" => 0xF8u, "color" => 0xFFFFFF00u, _ => 0u };
              if (bit == 0) continue;
              mask |= bit;
              if (value == "default") { defaults |= bit; continue; }
              uint own = key == "font" ? unchecked((Number(value) - 1) << 3) & 0xF8u
                  : key == "color" ? ColorValue(value) << 8
                  : value == "true" ? bit : 0;
              data = (data & ~bit) | own;
            }
          }
          var inherited = stack.TryPeek(out var top) ? top : new RdlTextStyle();
          current = new((current.Data & ~mask) | (((data & ~defaults) | (inherited.Data & defaults)) & mask),
              (current.KnownMask & ~mask) | ((~defaults | inherited.KnownMask) & mask));
          break;
      }
    }
    return result;
  }

  /// <summary>Change text/formatting locally without rewriting original TRA, STYLE, font or paragraph instructions.</summary>
  public string WithFormattedSegments(IReadOnlyList<IReadOnlyList<RdlTextRun>> segments)
  {
    if (!CanEditSegments || segments.Count != _segments.Length) throw new InvalidOperationException("Diese Struktur muss im RDL/XML-Reiter bearbeitet werden.");
    var result = new StringBuilder(_raw);
    for (int index = _segments.Length - 1; index >= 0; index--)
    {
      if (segments[index].All(run => run.Bold is null && run.Italic is null && run.Underline is null && run.Rgb is null)
          && string.Concat(segments[index].Select(run => run.Text)) == TextSegments[index]) continue;
      var match = _segments[index];
      string opening = match.Value[..(match.Value.IndexOf('>') + 1)];
      string closing = match.Value[match.Value.LastIndexOf("</", StringComparison.Ordinal)..];
      var replacement = new StringBuilder();
      foreach (var run in segments[index])
      {
        if (run.Text.Contains('\0')) throw new InvalidDataException("Text enthält Nullzeichen.");
        uint mask = 0, data = 0;
        void Flag(bool? value, uint bit) { if (value is { } set) { mask |= bit; if (set) data |= bit; } }
        Flag(run.Bold, 1); Flag(run.Italic, 2); Flag(run.Underline, 4);
        if (run.Rgb is { } rgb)
        {
          mask |= 0xFFFFFF00;
          data |= ((rgb >> 16) & 255) << 8 | ((rgb >> 8) & 255) << 16 | (rgb & 255) << 24;
        }
        if (mask != 0) replacement.Append($"<PUSH/><TRA data=\"0x{data:X8}\" mask=\"0x{mask:X8}\" def=\"0\"/>");
        string[] lines = run.Text.Replace("\r\n", "\n").Split('\n');
        for (int line = 0; line < lines.Length; line++)
        {
          if (line > 0) replacement.Append("<PARA/>");
          replacement.Append(opening).Append(SecurityElement.Escape(lines[line])).Append(closing);
        }
        // Restore against our pushed context BEFORE POP: FL POP only removes the default stack entry.
        if (mask != 0) replacement.Append($"<TRA mask=\"0x{mask:X8}\" def=\"0x{mask:X8}\"/><POP/>");
      }
      if (segments[index].Count == 0) replacement.Append(opening).Append(closing);
      result.Remove(match.Index, match.Length).Insert(match.Index, replacement);
    }
    string edited = result.ToString(); Validate(edited); return edited;
  }

  /// <summary>Map a continuous editor's paragraph breaks back to RDL, retaining all non-paragraph source controls.</summary>
  public string WithContinuousSegments(IReadOnlyList<IReadOnlyList<RdlTextRun>> segments)
  {
    if (!CanEditSegments) throw new InvalidOperationException("Diese Struktur muss im RDL/XML-Reiter bearbeitet werden.");
    // Original PARA nodes are replaced by the paragraph boundaries present in the editor.
    // Work on the original source before serialization so newly emitted PARA nodes are retained.
    string withoutParagraphs = System.Text.RegularExpressions.Regex.Replace(_raw,
        @"<PARA\b[^>]*(?:/>|>\s*</PARA\s*>)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    return Parse(withoutParagraphs).WithFormattedSegments(segments);
  }

  private static uint Number(string value) => value.StartsWith("0x", StringComparison.Ordinal)
      ? uint.Parse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
      : unchecked((uint)long.Parse(value, CultureInfo.InvariantCulture));

  private static uint ColorValue(string value)
  {
    // common_rdlio.cpp: COLORREF, 1-based numeric colors, and retail's swapped hex nibbles.
    if (value.StartsWith('#') && value.Length == 7)
    {
      uint rgb = uint.Parse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
      uint Swap(uint v) => ((v & 15) << 4) | ((v >> 4) & 15);
      return Swap(rgb >> 16) | Swap(rgb >> 8) << 8 | Swap(rgb) << 16;
    }
    return value switch
    {
      "gray" => 0x808080, "blue" => 0xE04848, "green" => 0x1DBF3B, "aqua" => 0xE0C387,
      "red" => 0x1D1DBF, "fuchsia" => 0xC20088, "yellow" => 0x52EAF5, "white" => 0xFFFFFF,
      _ => unchecked(Number(value) - 1)
    };
  }
}
