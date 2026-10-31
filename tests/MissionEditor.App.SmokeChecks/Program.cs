using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MissionEditor.App;
using MissionEditor.App.ViewModels;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "MissionEditor.slnx"))) repository = repository.Parent;
        var fixture = Path.Combine(repository?.FullName ?? throw new Exception("Repository root not found"), "tests", "Fixtures", "Missions", "m13sp.ini");
        var app = new App();
        app.InitializeComponent();
        var window = new MainWindow { WindowState = WindowState.Normal };
        var vm = (MainViewModel)window.DataContext;
        void Assert(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS " + message); }
        vm.Load(fixture);
        window.Show();
        Assert(vm.Script!.Triggers.Count == 1711, "large fixture loaded (plus placeholder nodes for missing references)");
        foreach (var size in new[] {new Size(1280,800), new Size(1920,1080)})
        {
            window.Width = size.Width; window.Height = size.Height;
            window.Measure(size); window.Arrange(new Rect(size)); window.UpdateLayout();
            var graph = (ScrollViewer)window.FindName("GraphScroller");
            var preview = (ListBox)window.FindName("SectionPreview");
            Assert(graph.ActualWidth >= 350 && preview.ActualWidth >= 300, "graph/source available at " + size);
        }
        var drawer=(Expander)window.FindName("FindingsExpander");
        var row=(RowDefinition)window.FindName("FindingsRow");
        var tabs=(TabControl)window.FindName("DetailTabs");
        var original=row.ActualHeight;
        drawer.IsExpanded=false; window.UpdateLayout();
        Assert(row.Height.Value == 40, "drawer collapses to header");
        drawer.IsExpanded=true; window.UpdateLayout();
        Assert(Math.Abs(row.Height.Value-original)<1, "drawer restores session height");
        row.Height=new GridLength(500); window.UpdateLayout();
        drawer.IsExpanded=false;
        window.Height=700; window.UpdateLayout();
        drawer.IsExpanded=true; window.UpdateLayout();
        var workspace=(Grid)drawer.Parent;
        Assert(row.Height.Value <= workspace.ActualHeight-workspace.RowDefinitions[0].MinHeight-6,
            "drawer restoration respects remaining workspace height after resize");
        window.Height=1080; window.UpdateLayout();
        tabs.SelectedIndex=1;
        drawer.IsExpanded=false;
        var finding=vm.Findings.First(f=>f.Finding.RelatedLineNumber is not null);
        typeof(MainWindow).GetMethod("ShowInFindingsGrid",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,[finding]);
        window.UpdateLayout();
        Assert(drawer.IsExpanded && tabs.SelectedIndex==0, "finding navigation opens drawer and findings tab");
        Assert(vm.SelectedSectionLines.Count(l=>l.HasActions)>=2, "duplicate preview keeps both editable definitions");
        var tmp=Path.Combine(Path.GetTempPath(),"mission-layout-check-"+Guid.NewGuid()+".ini");
        File.Copy(fixture,tmp);
        vm.Load(tmp);
        var section=vm.SelectedSectionLines.First(l=>l.HasActions).Section!;
        var text=MissionEditor.Core.Ini.IniDocument.GetEditableText(section);
        vm.ReplaceSection(section,text+"\r\n; layout verification\r\n");
        Assert(vm.IsModified,"section edit marks document modified");
        vm.UndoCommand.Execute(null);
        Assert(!vm.IsModified,"undo restores original document");
        section=vm.SelectedSectionLines.First(l=>l.HasActions).Section!;
        vm.ReplaceSection(section,MissionEditor.Core.Ini.IniDocument.GetEditableText(section)+"\r\n; layout verification\r\n");
        vm.Save();
        Assert(File.Exists(tmp+".bak") && File.ReadAllText(tmp).Contains("layout verification"),"save and backup work on temporary copy");
        Console.WriteLine("Temporary validation file: "+tmp);
        string binaryFixture=Path.Combine(repository!.FullName,"tests","Fixtures","Missions","bini-sample.ini");
        string binaryCopy=Path.Combine(Path.GetTempPath(),"mission-bini-check-"+Guid.NewGuid()+".ini");
        string binarySaveAs=binaryCopy+".saved.ini";
        File.Copy(binaryFixture,binaryCopy);
        try
        {
            vm.Load(binaryCopy);
            Assert(vm.Script!.Document.IsBini && vm.Script.Triggers.Count==2,"BINI mission loads into editor graph");
            var binarySection=vm.Script.Document.Sections[0];
            vm.ReplaceSection(binarySection,IniDocumentText(binarySection).Replace("Cnd_Timer = 5","Cnd_Timer = 7"));
            vm.Save();
            Assert(MissionEditor.Core.Ini.IniDocument.Load(binaryCopy).IsBini,"editor Save retains BINI");
            Assert(File.ReadAllBytes(binaryFixture).SequenceEqual(File.ReadAllBytes(binaryCopy+".bak")),"BINI backup preserves original bytes");
            vm.SaveAs(binarySaveAs);
            Assert(MissionEditor.Core.Ini.IniDocument.Load(binarySaveAs).IsBini,"editor Save As retains BINI");
            vm.UndoCommand.Execute(null);
            Assert(vm.Script.Document.IsBini && vm.IsModified,"BINI undo retains format and marks saved edit undone");
            vm.Load(fixture);
        }
        finally
        {
            File.Delete(binaryCopy); File.Delete(binaryCopy+".bak"); File.Delete(binarySaveAs);
        }
        static string IniDocumentText(MissionEditor.Core.Ini.IniSection section) => MissionEditor.Core.Ini.IniDocument.GetEditableText(section);
        foreach(var fg in new[]{"TextBrush","MutedTextBrush","AccentBrush","ErrorBrush","WarningBrush"})
        {
            double L(Color c) {double Ch(byte b) {var v=b/255.0;return v<=0.04045?v/12.92:Math.Pow((v+0.055)/1.055,2.4);}return .2126*Ch(c.R)+.7152*Ch(c.G)+.0722*Ch(c.B);}
            foreach(var bg in new[]{"WindowBrush","SurfaceBrush","InputBrush","SelectionBrush"})
            {
                var a=L(((SolidColorBrush)app.FindResource(fg)).Color);var b=L(((SolidColorBrush)app.FindResource(bg)).Color);var contrast=(Math.Max(a,b)+.05)/(Math.Min(a,b)+.05);
                Assert(contrast>=4.5,$"contrast {fg}/{bg}: {contrast:F2}");
            }
        }
        Console.WriteLine("ALL CHECKS PASSED");
        File.Delete(tmp);
        File.Delete(tmp + ".bak");
        window.Close();
        app.Shutdown();
    }
}
