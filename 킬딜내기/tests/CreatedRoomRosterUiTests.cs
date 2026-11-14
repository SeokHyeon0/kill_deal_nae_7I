// UI-only regression tests. No Firebase login, Begin, token persistence, or network calls.
// Compile with production sources except CredentialVault.cs, plus TestCredentialVault.cs.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PubgScore;

internal static class CreatedRoomRosterUiTests
{
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly string Output = Path.Combine(Path.GetTempPath(), "pubg-created-roster-ui-" + Guid.NewGuid().ToString("N"));
    static int checks, blockedRequests;
    static object Get(object instance, string name) { return instance.GetType().GetField(name, Private).GetValue(instance); }
    static T Get<T>(object instance, string name) { return (T)Get(instance, name); }
    static void Set(object instance, string name, object value) { instance.GetType().GetField(name, Private).SetValue(instance, value); }
    static void Call(object instance, string name, params object[] arguments)
    {
        try { instance.GetType().GetMethod(name, Private).Invoke(instance, arguments); }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
        Console.WriteLine("PASS " + message);
    }
    static void Until(Func<bool> condition, string message)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            Application.DoEvents();
            if (watch.ElapsedMilliseconds > 5000) throw new Exception("Timeout: " + message);
            Thread.Sleep(10);
        }
        Application.DoEvents();
    }
    static void Pump()
    {
        for (int i = 0; i < 5; i++) { Application.DoEvents(); Thread.Sleep(10); }
    }
    sealed class RejectNetwork : IWebRequestCreate
    {
        public WebRequest Create(Uri uri)
        {
            Interlocked.Increment(ref blockedRequests);
            throw new InvalidOperationException("Network is forbidden in CreatedRoomRosterUiTests.");
        }
    }
    sealed class Fixture : IDisposable
    {
        internal readonly string RoomId = new string('a', 32);
        internal readonly FirebaseGameClient Client;
        internal readonly MainForm Form;
        internal Fixture(bool owner)
        {
            Client = new FirebaseGameClient(new FirebaseConfig {
                DatabaseUrl = "http://127.0.0.1:1", AuthEmulatorUrl = "http://127.0.0.1:1",
                ApiKey = "demo-ui-only", ProjectId = "demo-created-roster-ui", EmulatorMode = true,
                TestIdentityDirectory = Path.Combine(Output, "identity")
            });
            // Only assign view state. Do not invoke authentication, Begin, or stream methods.
            foreach (string flag in new[] { "started", "streamReady", "healthy", "roleReady", "lobbyReady" }) Set(Client, flag, true);
            Set(Client, "adminMode", owner); Set(Client, "administrator", owner);
            Set(Client, "generation", 0L); Set(Client, "minimumGeneration", 0L); Set(Client, "session", 0L);
            Set(Client, "roomId", RoomId); Set(Client, "roomName", "UI test room");
            Set(Client, "roomOwner", "fixture-owner"); Set(Client, "roomStatus", "waiting");
            Set(Get(Client, "auth"), "uid", owner ? "fixture-owner" : "fixture-viewer");
            Set(Get(Client, "auth"), "token", "LOCAL-UI-ONLY-NOT-A-TOKEN");
            foreach (string name in new[] { "proof", "roleProof", "lobbyProof" }) Get<Stopwatch>(Client, name).Start();
            Get<Dictionary<string, RoomInfo>>(Client, "rooms")[RoomId] = new RoomInfo { Id = RoomId, Name = "UI test room", OwnerUid = "fixture-owner", Status = "waiting" };
            Form = new MainForm(owner, Client);
            Get<System.Windows.Forms.Timer>(Form, "timer").Stop();
            Form.Show(); Refresh(); Pump();
        }
        internal void Refresh()
        {
            Set(Client, "sequence", Get<long>(Client, "sequence") + 1);
            Call(Form, "RefreshView"); Call(Form, "RefreshRooms");
        }
        public void Dispose()
        {
            // MainForm closing disposes the fixture client; Dispose only aborts existing streams.
            if (!Form.IsDisposed) { Set(Form, "backend", null); Form.Close(); Form.Dispose(); }
            Client.Dispose();
        }
    }
    static void WithRoster(Action open, Action<RosterEntryDialog> inspect, string message)
    {
        bool visited = false; Exception failure = null;
        using (var timer = new System.Windows.Forms.Timer { Interval = 20 })
        {
            timer.Tick += delegate {
                var dialog = Application.OpenForms.OfType<RosterEntryDialog>().FirstOrDefault();
                if (dialog == null) return;
                timer.Stop(); visited = true;
                try { inspect(dialog); }
                catch (Exception error) { failure = error; }
                finally { if (!dialog.IsDisposed) dialog.Close(); }
            };
            timer.Start(); open(); Until(() => visited, message);
        }
        if (failure != null) throw failure;
        Check(visited && !Application.OpenForms.OfType<RosterEntryDialog>().Any(), message);
    }
    static void FillRoster(RosterEntryDialog dialog)
    {
        Get<RosterEditorBox>(dialog, "editor").Text = "one\ntwo\nthree\nfour";
        Get<RadioButton>(dialog, "kills").Checked = true;
    }
    static void PendingAndManualOpening()
    {
        using (var fixture = new Fixture(true))
        {
            var form = fixture.Form; var client = fixture.Client;
            Set(client, "streamReady", false); fixture.Refresh();
            Set(form, "createdRoomRoster", fixture.RoomId);
            Call(form, "OpenCreatedRoomRosterWhenReady", client.View()); Pump();
            Check(!Get<bool>(form, "rosterOpenQueued") && !Application.OpenForms.OfType<RosterEntryDialog>().Any() && Get<string>(form, "createdRoomRoster") == fixture.RoomId,
                "created-room editor waits for initial connected room state");
            Set(client, "streamReady", true);
            WithRoster(() => {
                Call(form, "OpenCreatedRoomRosterWhenReady", client.View());
                Check(Get<bool>(form, "rosterOpenQueued"), "ready owner queues editor on the UI thread");
                Call(form, "OpenCreatedRoomRosterWhenReady", client.View());
            }, dialog => {
                Check(Get<bool>(form, "roomDialogOpen") && Get<string>(form, "createdRoomRoster") == "", "auto-open consumes pending room before entering modal loop");
                Call(form, "RefreshRooms"); Call(form, "OpenCreatedRoomRosterWhenReady", client.View());
                Call(form, "OpenRosterEditor");
                Check(Application.OpenForms.OfType<RosterEntryDialog>().Count() == 1, "modal refresh and repeated open requests do not create a second editor");
            }, "ready created room opens its editor once");
            fixture.Refresh(); Pump();
            Check(client.View().RoomId == fixture.RoomId && !client.View().Running && !Application.OpenForms.OfType<RosterEntryDialog>().Any(),
                "canceling automatic editor keeps the waiting room and does not reopen it");
            var button = Get<Button>(form, "rosterButton");
            Check(button.Visible && button.Enabled, "waiting owner has an enabled manual team button");
            WithRoster(() => button.PerformClick(), dialog => { }, "manual team button opens while waiting");
            Set(client, "running", true); Set(client, "session", 1L); Set(client, "roomStatus", "running"); fixture.Refresh();
            Check(button.Visible && button.Enabled, "running owner retains the manual team button");
            WithRoster(() => button.PerformClick(), dialog => { }, "manual team button opens while running");
            Set(form, "createdRoomRoster", new string('b', 32));
            Call(form, "OpenCreatedRoomRosterWhenReady", client.View());
            Check(Get<string>(form, "createdRoomRoster") == "", "pending creation for a different room is discarded");
        }
        using (var fixture = new Fixture(false))
        {
            Set(fixture.Form, "createdRoomRoster", fixture.RoomId);
            Call(fixture.Form, "OpenCreatedRoomRosterWhenReady", fixture.Client.View());
            Call(fixture.Form, "OpenRosterEditor"); Pump();
            Check(!Get<Button>(fixture.Form, "rosterButton").Visible && !Get<bool>(fixture.Form, "rosterOpenQueued") && !Application.OpenForms.OfType<RosterEntryDialog>().Any(),
                "waiting participant cannot open team editor automatically or manually");
            Check(fixture.Client.View().CanEdit, "participant can edit without a game-start step");
        }
    }
    static void StaleEditorSaves()
    {
        foreach (string changed in new[] { "generation", "session" })
        using (var fixture = new Fixture(true))
        {
            WithRoster(() => Get<Button>(fixture.Form, "rosterButton").PerformClick(), dialog => {
                FillRoster(dialog); Set(fixture.Client, changed, 1L);
                Get<Button>(dialog, "save").PerformClick(); Pump();
                Check(!dialog.IsDisposed && dialog.Visible && Get<Label>(dialog, "message").Text.Contains("방 또는 게임 상태가 변경"),
                    "changing " + changed + " rejects stale editor save and keeps the input open");
                Check(Get<Button>(dialog, "save").Enabled && blockedRequests == 0,
                    "stale " + changed + " is rejected before any network request");
            }, "stale " + changed + " editor can be canceled");
        }
    }
    static void RosterSaveFailure()
    {
        var completion = new TaskCompletionSource<int>(); int attempts = 0;
        using (var dialog = new RosterEntryDialog((names, kills) => { attempts++; return completion.Task; }))
        {
            dialog.Show(); FillRoster(dialog); var save = Get<Button>(dialog, "save");
            save.PerformClick(); save.PerformClick();
            Check(attempts == 1 && !save.Enabled && !Get<RosterEditorBox>(dialog, "editor").Enabled, "pending roster save disables input and prevents double submit");
            dialog.Close(); Pump();
            Check(!dialog.IsDisposed && dialog.Visible, "pending roster save cannot be closed");
            completion.SetException(new IOException("fixture save failure"));
            Until(() => save.Enabled, "roster save failure");
            Check(!dialog.IsDisposed && dialog.DialogResult != DialogResult.OK && Get<Label>(dialog, "message").Text.Contains("fixture save failure") && attempts == 1,
                "roster save failure keeps entered text and dialog open without automatic retry");
            Check(Get<RosterEditorBox>(dialog, "editor").Text.Contains("three"), "roster save failure preserves entered player names");
            dialog.Close();
        }
    }
    static void RoomCreationOutcomes()
    {
        var completion = new TaskCompletionSource<int>(); int attempts = 0;
        using (var dialog = new RoomCreateDialog(name => { attempts++; Check(name == "new room", "room creation trims the submitted name"); return completion.Task; }))
        {
            dialog.Show(); dialog.Controls.OfType<TextBox>().Single().Text = " new room ";
            var create = dialog.Controls.OfType<Button>().Single(x => x.Text == "방 만들기");
            create.PerformClick(); create.PerformClick();
            Check(attempts == 1 && !create.Enabled && dialog.DialogResult != DialogResult.OK && dialog.Visible, "room dialog waits for successful creation and prevents duplicate submits");
            dialog.Close(); Pump(); Check(!dialog.IsDisposed && dialog.Visible, "pending room creation cannot be closed");
            completion.SetResult(0); Until(() => dialog.IsDisposed || !dialog.Visible, "room creation success");
            Check(dialog.DialogResult == DialogResult.OK, "room dialog returns OK only after creation succeeds");
        }
        completion = new TaskCompletionSource<int>(); attempts = 0;
        using (var dialog = new RoomCreateDialog(name => { attempts++; return completion.Task; }))
        {
            dialog.Show(); dialog.Controls.OfType<TextBox>().Single().Text = "failed room";
            var create = dialog.Controls.OfType<Button>().Single(x => x.Text == "방 만들기"); create.PerformClick();
            completion.SetException(new IOException("fixture create failure")); Until(() => create.Enabled, "room failure");
            Check(dialog.Visible && dialog.DialogResult != DialogResult.OK && dialog.Controls.OfType<Label>().Any(x => x.Text.Contains("fixture create failure")) && attempts == 1,
                "failed creation keeps dialog open and never returns OK");
            dialog.Controls.OfType<Button>().Single(x => x.Text == "취소").PerformClick();
            Check(dialog.DialogResult == DialogResult.Cancel, "cancel after creation failure does not report success");
        }
        attempts = 0;
        using (var dialog = new RoomCreateDialog(name => { attempts++; return Task.FromResult(0); }))
        {
            dialog.Show(); dialog.Controls.OfType<Button>().Single(x => x.Text == "취소").PerformClick();
            Check(dialog.DialogResult == DialogResult.Cancel && attempts == 0, "cancel before creation calls no create operation");
        }
    }
    [STAThread]
    static int Main()
    {
        Directory.CreateDirectory(Output); RosterWindowPreferences.PathOverride = Path.Combine(Output, "roster-window.txt");
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            Check(WebRequest.RegisterPrefix("http://", new RejectNetwork()) && WebRequest.RegisterPrefix("https://", new RejectNetwork()), "all HTTP and HTTPS requests are blocked in this test process");
            PendingAndManualOpening(); StaleEditorSaves(); RosterSaveFailure(); RoomCreationOutcomes();
            Check(blockedRequests == 0, "UI regression suite made zero network attempts");
            Check(RosterWindowPreferences.PathOverride.StartsWith(Output, StringComparison.Ordinal), "roster preferences remain confined to the test temp directory");
            Console.WriteLine("PASS " + checks + " CreatedRoomRosterUiTests checks; output=" + Output);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            foreach (Form form in Application.OpenForms.Cast<Form>().ToArray()) form.Dispose();
            RosterWindowPreferences.PathOverride = null;
        }
    }
}
