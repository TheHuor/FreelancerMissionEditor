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
        Assert(vm.WindowTitle.Contains(AboutWindow.DisplayVersion), "main caption contains build version");
        var about = new AboutWindow { Owner = window };
        about.Show(); about.UpdateLayout();
        Assert(about.Modules.Count == 4 && about.Modules.All(module => module.Version == AboutWindow.DisplayVersion), "About lists consistent GUI, Core, CLI and MCP repository versions");
        if (Environment.GetEnvironmentVariable("MISSION_EDITOR_ABOUT_PREVIEW") is { Length: > 0 } previewPath)
        {
            var dpi = VisualTreeHelper.GetDpi(about);
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(about.ActualWidth*dpi.DpiScaleX), (int)(about.ActualHeight*dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(about);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var previewStream = File.Create(previewPath);
            encoder.Save(previewStream);
        }
        about.Close();

        // Agent connection: paths with spaces and backslashes survive all three configuration formats.
        const string ServerPath = @"C:\Program Files\Mission Editor\MissionEditor.Mcp.exe";
        const string MissionsPath = @"D:\freelancer crossfire\DATA\MISSIONS";
        string json = AgentConnection.JsonConfiguration(ServerPath, MissionsPath, allowWrites: true);
        using (var parsed = System.Text.Json.JsonDocument.Parse(json))
        {
            var server = parsed.RootElement.GetProperty("mcpServers").GetProperty(AgentConnection.ServerName);
            Assert(server.GetProperty("command").GetString() == ServerPath, "agent JSON keeps the server path");
            Assert(string.Join("|", server.GetProperty("args").EnumerateArray().Select(argument => argument.GetString())) == $"--workspace|{MissionsPath}|--allow-writes", "agent JSON passes workspace and write flag");
        }
        Assert(AgentConnection.CodexConfiguration(ServerPath, MissionsPath, false).Contains(@"command = ""C:\\Program Files\\Mission Editor\\MissionEditor.Mcp.exe"""), "agent TOML escapes backslashes");
        Assert(!AgentConnection.CodexConfiguration(ServerPath, MissionsPath, false).Contains("--allow-writes"), "agent TOML is read-only by default");
        Assert(AgentConnection.ClaudeCodeCommand(ServerPath, MissionsPath, false).Contains($"-- \"{ServerPath}\" --workspace \"{MissionsPath}\""), "Claude Code command quotes paths");
        using (var claudeJson = System.Text.Json.JsonDocument.Parse(AgentConnection.ClaudeCodeJson(ServerPath, MissionsPath, false)))
        {
            var server = claudeJson.RootElement.GetProperty("mcpServers").GetProperty(AgentConnection.ServerName);
            Assert(server.GetProperty("type").GetString() == "stdio" && server.GetProperty("command").GetString() == ServerPath, "Claude Code .claude.json entry is a stdio server with the path");
        }
        CheckAgentClientConfig(ServerPath, MissionsPath);
        CheckAgentPrompts(window);
        CheckAiAdvice(window);
        var agentWindow = new AgentConnectionWindow(tmp, hasUnsavedChanges: false) { Owner = window };
        agentWindow.Show(); agentWindow.UpdateLayout();
        Assert(agentWindow.ClaudeCodeText.Contains($"--workspace \"{Path.GetDirectoryName(tmp)}\""), "agent dialog suggests the folder of the open mission");
        agentWindow.Close();

        about.Close();
        Console.WriteLine("ALL CHECKS PASSED");
        File.Delete(tmp);
        File.Delete(tmp + ".bak");
        window.Close();
        app.Shutdown();
    }

    // Requests for the agent name the file relative to the workspace, the finding and the right tools.
    private static void CheckAgentPrompts(Window owner)
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS " + message); }
        const string Workspace = @"D:\Games\Freelancer\DATA\MISSIONS";
        Check(AgentPrompts.RelativeToWorkspace(@"D:\Games\Freelancer\DATA\MISSIONS\M13\m13.ini", Workspace) == @"M13\m13.ini", "prompt path is relative to the workspace");
        Check(AgentPrompts.RelativeToWorkspace(@"D:\Other\m13.ini", Workspace) is null, "prompt path outside the workspace is detected");
        Check(AgentPrompts.ChooseWorkspace(@"D:\Games\Freelancer\DATA\MISSIONS\M13\m13.ini", [@"D:\Other", Workspace, Workspace + @"\M13"])
            == (Workspace + @"\M13", true), "the innermost workspace containing the mission is chosen");
        Check(AgentPrompts.ChooseWorkspace(@"D:\Other\x.ini", [Workspace]) == (Workspace, false), "a workspace not containing the mission is reported as such");

        var missing = new MissionEditor.Core.Checks.Finding(MissionEditor.Core.Checks.FindingSeverity.Error, "unknown-trigger", 5582, "check_capcf2",
            "Act_ActTrig auf unbekannten Trigger 'turn_off_music' – die Aktivierung passiert nie.", "Hinweis");
        string missingPrompt = AgentPrompts.ForFinding(@"M13\m13.ini", missing);
        Check(missingPrompt.Contains("suggest_missing_trigger für „turn_off_music“") && missingPrompt.Contains(@"Datei: M13\m13.ini")
            && missingPrompt.Contains(AgentConnection.ServerName), "missing-trigger prompt asks for evidence about the missing trigger");

        var duplicate = new MissionEditor.Core.Checks.Finding(MissionEditor.Core.Checks.FindingSeverity.Warning, "duplicate-nickname", 4843, "follow_player",
            "[ObjList] 'follow_player' ist doppelt (erste Definition Zeile 4384).", "Die Engine verwirft die zweite Liste.", 4384);
        string findingPrompt = AgentPrompts.ForFinding(@"M13\m13.ini", duplicate);
        Check(findingPrompt.Contains("Zeile 4843") && findingPrompt.Contains("Erklärung des Editors: Die Engine verwirft") && findingPrompt.Contains("Bezugsstelle: Zeile 4384")
            && findingPrompt.Contains("prepare_section_edit"), "finding prompt carries line, explanation, related line and the draft rule");

        var dialog = new NewMissionPromptWindow(@"D:\Games\Freelancer\DATA\MISSIONS\M13\m13.ini", Workspace, exportAllowed: false) { Owner = owner };
        dialog.Show(); dialog.UpdateLayout();
        string newMission = dialog.BuildPrompt("Drei Piraten greifen am Wegpunkt an.");
        Check(newMission.Contains("Drei Piraten") && newMission.Contains(@"M13\m13.ini als Vorlage") && newMission.Contains("prepare_new_mission")
            && newMission.Contains("nicht freigeschaltet"), "new-mission prompt has description, template and export state");
        dialog.Close();
    }

    // "Frag KI" without network: request context, answer parsing, fix check, the window and the key storage.
    private static void CheckAiAdvice(Window owner)
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS " + message); }
        string folder = Path.Combine(Path.GetTempPath(), "mission-ai-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            string missionPath = Path.Combine(folder, "ai.ini");
            File.WriteAllText(missionPath, "[Trigger]\nnickname = start\nInitState = ACTIVE\nCnd_True = no_params\nAct_ActTrig = secnod\n\n[Trigger]\nnickname = second\nCnd_Timer = 5\n");
            var viewModel = new MainViewModel();
            viewModel.Load(missionPath);
            var finding = viewModel.Findings.Single(item => item.CheckId == "unknown-trigger").Finding;
            var script = viewModel.Script!;

            var request = MissionEditor.App.Ai.AiAdvice.BuildRequest(script, new MissionEditor.Core.Graph.TriggerGraph(script), finding, "ai.ini");
            Check(request.User.Contains("Act_ActTrig = secnod") && request.User.Contains("Ähnliche vorhandene Trigger: second")
                && request.User.Contains("nickname = second") && request.EditableSections.SetEquals([1]), "AI request carries the section, similar triggers and the editable section");

            const string Answer = """
                Hier ist die Antwort: {"explanation":"Tippfehler im Triggernamen.","hasFix":true,"sectionStartLine":1,
                "replacementText":"[Trigger]\nnickname = start\nInitState = ACTIVE\nCnd_True = no_params\nAct_ActTrig = second",
                "fixSummary":"secnod in second korrigiert.","assumptions":[]}
                """;
            var advice = MissionEditor.App.Ai.AiAdvice.Parse(Answer);
            var fix = MissionEditor.App.Ai.AiAdvice.CheckFix(script, request, finding, advice);
            Check(advice.HasFix && fix.FindingResolved && fix.NewFindings.Count == 0 && fix.FindingsAfter < fix.FindingsBefore, "AI fix is checked: the finding disappears, nothing new appears");

            bool rejected = false;
            try { MissionEditor.App.Ai.AiAdvice.CheckFix(script, request, finding, advice with { SectionStartLine = 7 }); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "AI fix for a section that was not offered is rejected");

            var adviceWindow = new AiAdviceWindow(viewModel, finding, askOnOpen: false) { Owner = owner };
            adviceWindow.ShowAnswerForTest(Answer.Substring(Answer.IndexOf('{')));
            Check(adviceWindow.OfferedFix is { FindingResolved: true }, "AI window offers the checked fix");
            if (Environment.GetEnvironmentVariable("MISSION_EDITOR_AI_PREVIEW") is { Length: > 0 } previewPath)
            {
                adviceWindow.Show();
                adviceWindow.UpdateLayout();
                var dpi = VisualTreeHelper.GetDpi(adviceWindow);
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(adviceWindow.ActualWidth * dpi.DpiScaleX), (int)(adviceWindow.ActualHeight * dpi.DpiScaleY),
                    dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                bitmap.Render(adviceWindow);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var previewStream = File.Create(previewPath);
                encoder.Save(previewStream);
            }
            adviceWindow.Close();

            string settingsPath = Path.Combine(folder, "ai-settings.json");
            new MissionEditor.App.Ai.AiSettings { Provider = MissionEditor.App.Ai.AiProvider.CodexCli, ApiKey = "sk-test-not-a-real-key", Model = "claude-opus-5-5" }.Save(settingsPath);
            var loaded = MissionEditor.App.Ai.AiSettings.Load(settingsPath);
            Check(!File.ReadAllText(settingsPath).Contains("sk-test") && loaded.ApiKey == "sk-test-not-a-real-key" && loaded.Provider == MissionEditor.App.Ai.AiProvider.CodexCli,
                "AI settings keep the API key encrypted and restore it");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    // Adding the server keeps everything else in the client files; the manual instructions yield valid files.
    private static void CheckAgentClientConfig(string serverPath, string missionsPath)
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS " + message); }
        string folder = Path.Combine(Path.GetTempPath(), "mission-agent-config-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            string claudeJson = Path.Combine(folder, ".claude.json");
            File.WriteAllText(claudeJson, "{\n  \"numStartups\": 7,\n  \"mcpServers\": {\n    \"other\": { \"command\": \"x.exe\" }\n  }\n}");
            string? backup = AgentClientConfig.AddServer(AgentClient.ClaudeCode, claudeJson, serverPath, missionsPath, false);
            using (var updated = System.Text.Json.JsonDocument.Parse(File.ReadAllText(claudeJson)))
            {
                var root = updated.RootElement;
                Check(root.GetProperty("numStartups").GetInt32() == 7 && root.GetProperty("mcpServers").TryGetProperty("other", out _),
                    "agent add keeps other settings and servers");
                Check(root.GetProperty("mcpServers").GetProperty(AgentConnection.ServerName).GetProperty("type").GetString() == "stdio", "agent add writes the stdio entry");
            }
            Check(backup is not null && File.ReadAllText(backup).Contains("\"other\""), "agent add keeps a backup of the previous file");
            Check(AgentClientConfig.IsConfigured(AgentClient.ClaudeCode, claudeJson), "agent add is detected as configured");

            string broken = Path.Combine(folder, "broken.json");
            File.WriteAllText(broken, "{ not json");
            bool rejected = false;
            try { AgentClientConfig.AddServer(AgentClient.ClaudeDesktop, broken, serverPath, missionsPath, false); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && File.ReadAllText(broken) == "{ not json", "agent add leaves invalid JSON untouched");

            string desktop = Path.Combine(folder, "Claude", "claude_desktop_config.json");
            AgentClientConfig.AddServer(AgentClient.ClaudeDesktop, desktop, serverPath, missionsPath, true);
            Check(File.ReadAllText(desktop).Contains("--allow-writes") && !File.ReadAllText(desktop).Contains("\"type\""), "agent add creates a missing Claude Desktop file");

            string codex = Path.Combine(folder, "config.toml");
            File.WriteAllText(codex, "model = \"o3\"\n\n[mcp_servers.freelancer-missions]\ncommand = \"old.exe\"\nargs = []\n\n[mcp_servers.other]\ncommand = \"y.exe\"\n");
            AgentClientConfig.AddServer(AgentClient.Codex, codex, serverPath, missionsPath, false);
            string toml = File.ReadAllText(codex);
            Check(!toml.Contains("old.exe") && toml.Contains("[mcp_servers.other]") && toml.Contains("model = \"o3\"")
                && toml.Split("[mcp_servers.freelancer-missions]").Length == 2, "agent add replaces only the Codex table");

            // The registered workspace and export flag are read back from every format.
            Check(AgentClientConfig.ConfiguredServer(AgentClient.ClaudeCode, claudeJson) == (missionsPath, false), "registered workspace is read from .claude.json");
            Check(AgentClientConfig.ConfiguredServer(AgentClient.ClaudeDesktop, desktop) == (missionsPath, true), "registered workspace and export flag are read from Claude Desktop");
            Check(AgentClientConfig.ConfiguredServer(AgentClient.Codex, codex) == (missionsPath, false), "registered workspace is read from the Codex table");
            Check(AgentClientConfig.ConfiguredServer(AgentClient.ClaudeDesktop, broken) is null, "no workspace from an invalid file");

            // Manual way: insert the snippet exactly where the instruction says and parse the result.
            string withoutServers = Path.Combine(folder, "plain.json");
            File.WriteAllText(withoutServers, "{\n  \"theme\": \"dark\"\n}");
            (string instruction, string snippet) = AgentClientConfig.ManualSnippet(AgentClient.ClaudeCode, withoutServers, serverPath, missionsPath, false);
            string pasted = File.ReadAllText(withoutServers).Insert(2, snippet + "\n");
            using (var parsed = System.Text.Json.JsonDocument.Parse(pasted))
                Check(instruction.Contains("ersten Zeile") && parsed.RootElement.GetProperty("mcpServers").TryGetProperty(AgentConnection.ServerName, out _)
                    && parsed.RootElement.GetProperty("theme").GetString() == "dark", "manual snippet fits a file without mcpServers");

            string withServers = Path.Combine(folder, "servers.json");
            File.WriteAllText(withServers, "{\n  \"mcpServers\": {\n    \"other\": { \"command\": \"x.exe\" }\n  }\n}");
            (instruction, snippet) = AgentClientConfig.ManualSnippet(AgentClient.ClaudeCode, withServers, serverPath, missionsPath, false);
            string original = File.ReadAllText(withServers);
            int afterServers = original.IndexOf("\"mcpServers\": {", StringComparison.Ordinal) + "\"mcpServers\": {".Length + 1;
            using (var parsed = System.Text.Json.JsonDocument.Parse(original.Insert(afterServers, snippet + "\n")))
                Check(instruction.Contains("\"mcpServers\": {") && parsed.RootElement.GetProperty("mcpServers").EnumerateObject().Count() == 2,
                    "manual snippet fits an existing mcpServers block");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
