using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MissionEditor.App.Controls;

internal static partial class Program
{
  private static void CheckHexEditor ()
  {
    var editor = new HexEditor ();
    byte[] original = Enumerable.Range (0, 256).Select (value => (byte)value).ToArray ();
    editor.LoadBytes (original);
    var dialog = new Window { Content = editor, Width = 820, Height = 360, ShowInTaskbar = false };
    dialog.Show (); dialog.UpdateLayout ();
    var list = ((DockPanel)editor.Content).Children.OfType<ListBox> ().Single ();
    void Type (string text) => list.RaiseEvent (new TextCompositionEventArgs (InputManager.Current.PrimaryKeyboardDevice,
        new TextComposition (InputManager.Current, list, text)) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });
    editor.SelectByte (65); Type ("42");
    if (editor.Bytes[65] != 0x42 || editor.SelectedOffset != 66) throw new Exception ("Hex typing replaces one byte and advances");
    editor.SelectByte (65, true); Type ("C");
    if (editor.Bytes[65] != (byte)'C') throw new Exception ("Character typing updates the same byte");
    editor.SelectByte (65); SavePreview (dialog, "hex-byte-selection");
    editor.ReplaceByte (original[65]);
    if (!editor.Bytes.SequenceEqual (original)) throw new Exception ("Restoring a byte removes its change");
    editor.IsReadOnly = true; editor.ReplaceByte (0);
    if (!editor.Bytes.SequenceEqual (original)) throw new Exception ("Read-only byte views reject edits");
    dialog.Close ();
    Console.WriteLine ("PASS hex and character typing share a byte selection, preserve originals and support read-only blocks");
  }
}
