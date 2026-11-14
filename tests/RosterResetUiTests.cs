// Local UI regression checks. Callback fakes never contact Firebase or reset a real room.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PubgScore;

internal static class RosterResetUiTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    static T Get<T>(object target, string name) { return (T)target.GetType().GetField(name, Private).GetValue(target); }
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++; Console.WriteLine("PASS " + message);
    }
    static void Pump() { Application.DoEvents(); Thread.Sleep(10); Application.DoEvents(); }
    static void Until(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition()) { Pump(); if (clock.ElapsedMilliseconds > 4000) throw new Exception("UI operation did not finish"); }
    }
    static void Fill(RosterEntryDialog dialog)
    {
        Get<RosterEditorBox>(dialog, "editor").Text = "A B\nC D\nE F\nG H";

    }
    static void FreshAndCancel()
    {
        int calls = 0;
        using (var dialog = new RosterEntryDialog(names => { calls++; return Task.FromResult(0); }))
        {
            dialog.Show(); Pump();
            var editor = Get<RosterEditorBox>(dialog, "editor");
            Check(editor.Text == "", "fresh editor contains no previous players");
            Check(!dialog.Controls.OfType<RadioButton>().Any(),
                "fresh editor has no mode selector");
            Check(dialog.AcceptButton == null && editor.Multiline, "Enter is not assigned to the save button");
            editor.Text = "first"; editor.SelectionStart = editor.TextLength;
            // Exercise the actual native RichEdit character handling rather than changing Text to include a newline.
            editor.Focus(); SendMessage(editor.Handle, 0x0100, new IntPtr(13), IntPtr.Zero);
            SendMessage(editor.Handle, 0x0102, new IntPtr(13), IntPtr.Zero);
            SendMessage(editor.Handle, 0x0101, new IntPtr(13), IntPtr.Zero); Pump();
            Check(editor.Text.Replace("\r", "") == "first\n" && calls == 0 && dialog.Visible,
                "Enter inserts a line break and does not apply or reset (text=" + editor.Text.Replace("\r", "<CR>").Replace("\n", "<LF>") + ")");
            Fill(dialog); dialog.Size = new Size(910, 650); editor.ZoomFactor = 1.4F;
            ((Button)dialog.CancelButton).PerformClick(); Pump();
            Check(calls == 0 && dialog.DialogResult != DialogResult.OK, "closing entered roster calls no apply or reset operation");
        }
        using (var dialog = new RosterEntryDialog(names => { calls++; return Task.FromResult(0); }))
        {
            dialog.Show(); Pump();
            Check(Get<RosterEditorBox>(dialog, "editor").Text == "" &&
                !dialog.Controls.OfType<RadioButton>().Any(),
                "reopening starts with blank players and no mode controls");
            Check(Math.Abs(Get<RosterEditorBox>(dialog, "editor").ZoomFactor - 1.4F) < 0.01F,
                "fresh roster retains the user's text zoom");
            Check(dialog.Size == new Size(910, 650), "fresh roster retains the user's window size");
            dialog.Close();
        }
    }
    static void InvalidInputDoesNotApply()
    {
        int calls = 0;
        using (var dialog = new RosterEntryDialog(names => { calls++; return Task.FromResult(0); }))
        {
            dialog.Show(); Pump();
            var save = Get<Button>(dialog, "save");
            Get<RosterEditorBox>(dialog, "editor").Text = "A\nB\nC\nD\nE";
            save.PerformClick(); Pump();
            Check(calls == 0 && dialog.Visible && Get<Label>(dialog, "message").Text.Contains("4줄"),
                "five rows reject save before any reset or apply callback");
    
            Get<RosterEditorBox>(dialog, "editor").Text = "A\nB\nC";
            save.PerformClick(); Pump();
            Check(calls == 0 && dialog.Visible && save.Enabled, "invalid row count never reaches reset or apply");
            Get<RosterEditorBox>(dialog, "editor").Text = "A B\nC\nD E\nF G";
            save.PerformClick(); Pump();
            Check(calls == 0 && dialog.Visible, "different team counts never reach reset or apply");
            dialog.Close();
        }
    }
    static void PendingFailureAndRetry()
    {
        int calls = 0;
        var completion = new TaskCompletionSource<int>();
        using (var dialog = new RosterEntryDialog(names => {
            calls++;
            Check(names.Teams == 2 && names.Names[3, 1] == "H", "callback receives the complete current roster without a mode");
            return completion.Task;
        }))
        {
            dialog.Show(); Pump(); Fill(dialog);
            var save = Get<Button>(dialog, "save"); var editor = Get<RosterEditorBox>(dialog, "editor");
            save.PerformClick(); save.PerformClick(); Pump();
            Check(calls == 1 && !save.Enabled && !editor.Enabled, "double click cannot begin two reset/apply operations");
            Check(!((Button)dialog.CancelButton).Enabled,
                "close remains disabled during reset/apply");
            dialog.Close(); Pump();
            Check(dialog.Visible && !dialog.IsDisposed && calls == 1, "title-bar close is blocked while reset/apply is running");
            completion.SetException(new IOException("초기화 완료 · 새 팀 저장 실패"));
            Until(() => save.Enabled);
            Check(dialog.Visible && dialog.DialogResult != DialogResult.OK && editor.Text == "A B\nC D\nE F\nG H",
                "failed second phase preserves input and does not report success");
            Check(Get<Label>(dialog, "message").Text.Contains("초기화 완료") && Get<Label>(dialog, "message").Text.Contains("자동 재전송하지"),
                "failure explanation preserves the completed reset stage and manual retry instruction");
            Pump(); Check(calls == 1, "failed apply is never automatically resubmitted");
            completion = new TaskCompletionSource<int>(); save.PerformClick(); Pump();
            Check(calls == 2 && !save.Enabled && dialog.DialogResult != DialogResult.OK,
                "explicit save retries once and waits for the callback");
            completion.SetResult(0); Until(() => dialog.IsDisposed || !dialog.Visible);
            Check(dialog.DialogResult == DialogResult.OK, "dialog reports success only after apply callback completes");
        }
    }
    [STAThread]
    static int Main()
    {
        var output = Path.Combine(Path.GetTempPath(), "pubg-roster-reset-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output); RosterWindowPreferences.PathOverride = Path.Combine(output, "roster-window.txt");
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        try {
            FreshAndCancel(); InvalidInputDoesNotApply(); PendingFailureAndRetry();
            Console.WriteLine("PASS " + checks + " RosterResetUiTests checks; no network/backend created"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally {
            foreach (Form form in Application.OpenForms.Cast<Form>().ToArray()) form.Dispose();
            RosterWindowPreferences.PathOverride = null;
        }
    }
}
