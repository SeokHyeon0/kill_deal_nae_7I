using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace PubgScore
{
    internal sealed class MainWindowPlacement
    {
        internal Rectangle Bounds, WorkingArea;
        internal int Dpi;
        internal bool Maximized;
        internal string DeviceName;
    }

    internal static class MainWindowPreferences
    {
        internal static string PathForRole(bool administrator)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PubgScoreFirebase", administrator ? "main-window-bj.txt" : "main-window-viewer.txt");
        }

        internal static bool TryRead(string path, out MainWindowPlacement placement)
        {
            placement = null;
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > 4096) return false;
                string[] parts = File.ReadAllText(path).Split('|');
                int[] values = new int[9];
                if (parts.Length != 12 || parts[0] != "v1" || (parts[10] != "0" && parts[10] != "1")) return false;
                for (int i = 0; i < values.Length; i++)
                    if (!Int32.TryParse(parts[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i])) return false;
                var parsed = new MainWindowPlacement {
                    Bounds = new Rectangle(values[0], values[1], values[2], values[3]),
                    WorkingArea = new Rectangle(values[4], values[5], values[6], values[7]),
                    Dpi = values[8], Maximized = parts[10] == "1",
                    DeviceName = Encoding.UTF8.GetString(Convert.FromBase64String(parts[11]))
                };
                if (!Valid(parsed)) return false;
                placement = parsed;
                return true;
            }
            catch (Exception) { return false; } // Optional UI settings cannot prevent startup.
        }

        internal static bool Write(string path, MainWindowPlacement placement)
        {
            if (!Valid(placement)) return false;
            try
            {
                string parent = Path.GetDirectoryName(Path.GetFullPath(path));
                Directory.CreateDirectory(parent);
                Rectangle bounds = placement.Bounds, area = placement.WorkingArea;
                string content = String.Format(CultureInfo.InvariantCulture, "v1|{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}|{9}|{10}",
                    bounds.X, bounds.Y, bounds.Width, bounds.Height, area.X, area.Y, area.Width, area.Height,
                    placement.Dpi, placement.Maximized ? "1" : "0", Convert.ToBase64String(Encoding.UTF8.GetBytes(placement.DeviceName ?? "")));
                File.WriteAllText(path, content);
                return true;
            }
            catch (Exception) { return false; } // A read-only profile cannot prevent closing.
        }

        static bool Valid(MainWindowPlacement placement)
        {
            if (placement == null) return false;
            return ValidRectangle(placement.Bounds, 200, 160) && ValidRectangle(placement.WorkingArea, 200, 160)
                && placement.Dpi >= 48 && placement.Dpi <= 768;
        }

        static bool ValidRectangle(Rectangle rectangle, int minWidth, int minHeight)
        {
            return rectangle.Width >= minWidth && rectangle.Height >= minHeight
                && rectangle.Width <= 65536 && rectangle.Height <= 65536
                && rectangle.X >= -131072 && rectangle.X <= 131072 && rectangle.Y >= -131072 && rectangle.Y <= 131072;
        }

        internal static Rectangle RestoreBounds(MainWindowPlacement saved, Rectangle workingArea, int currentDpi, Size minimumSize)
        {
            // Coordinates are relative to the old monitor, so unplugged/rearranged monitors remain safe.
            double scale = (double)Math.Max(48, currentDpi) / Math.Max(48, saved.Dpi);
            int width = Math.Min(workingArea.Width, Math.Max(Math.Min(minimumSize.Width, workingArea.Width), (int)Math.Round(saved.Bounds.Width * scale)));
            int height = Math.Min(workingArea.Height, Math.Max(Math.Min(minimumSize.Height, workingArea.Height), (int)Math.Round(saved.Bounds.Height * scale)));
            int x = workingArea.X + (int)Math.Round((saved.Bounds.X - saved.WorkingArea.X) * scale);
            int y = workingArea.Y + (int)Math.Round((saved.Bounds.Y - saved.WorkingArea.Y) * scale);
            x = Math.Max(workingArea.Left, Math.Min(x, workingArea.Right - width));
            y = Math.Max(workingArea.Top, Math.Min(y, workingArea.Bottom - height));
            return new Rectangle(x, y, width, height);
        }

        internal static Screen FindScreen(MainWindowPlacement saved)
        {
            foreach (Screen screen in Screen.AllScreens)
                if (String.Equals(screen.DeviceName, saved.DeviceName, StringComparison.OrdinalIgnoreCase)) return screen;
            return Screen.FromRectangle(saved.Bounds);
        }
    }

    internal sealed class MainWindowPreferenceSession
    {
        readonly Form form;
        readonly string path;
        readonly MainWindowPlacement saved;
        MainWindowPlacement latest;
        bool loaded;

        internal MainWindowPreferenceSession(Form form, string path)
        {
            this.form = form;
            this.path = path;
            MainWindowPreferences.TryRead(path, out saved);
            if (saved != null)
            {
                form.StartPosition = FormStartPosition.Manual;
                // Select the intended monitor before the handle is created. Final sizing follows DPI autoscale in Load.
                form.Location = MainWindowPreferences.RestoreBounds(saved, MainWindowPreferences.FindScreen(saved).WorkingArea,
                    saved.Dpi, form.MinimumSize).Location;
            }
            form.Load += OnLoad;
            form.Resize += OnGeometryChanged;
            form.LocationChanged += OnGeometryChanged;
            form.FormClosed += OnClosed;
        }

        void OnLoad(object sender, EventArgs e)
        {
            if (saved != null)
            {
                Rectangle area = MainWindowPreferences.FindScreen(saved).WorkingArea;
                // Keep the whole window reachable if the saved monitor disappeared or its resolution shrank.
                form.MinimumSize = new Size(Math.Min(form.MinimumSize.Width, area.Width), Math.Min(form.MinimumSize.Height, area.Height));
                form.Bounds = MainWindowPreferences.RestoreBounds(saved, area, form.DeviceDpi, form.MinimumSize);
                if (saved.Maximized) form.WindowState = FormWindowState.Maximized;
            }
            loaded = true;
            Capture();
        }

        void OnGeometryChanged(object sender, EventArgs e) { if (loaded) Capture(); }

        void Capture()
        {
            // Minimizing must preserve the previous normal bounds AND the previous maximized state.
            if (form.WindowState == FormWindowState.Minimized) return;
            Rectangle bounds = form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;
            if (bounds.Width < 200 || bounds.Height < 160) return;
            Screen screen = Screen.FromRectangle(form.Bounds);
            latest = new MainWindowPlacement { Bounds = bounds, WorkingArea = screen.WorkingArea,
                Dpi = form.DeviceDpi, Maximized = form.WindowState == FormWindowState.Maximized, DeviceName = screen.DeviceName };
        }

        void OnClosed(object sender, FormClosedEventArgs e)
        {
            // FormClosed only runs after close succeeds. No disk writes while dragging/resizing or on a canceled close.
            if (loaded && latest != null) MainWindowPreferences.Write(path, latest);
        }
    }

    public sealed partial class MainForm
    {
        void InitializeWindowPreferences()
        {
            if (backend != null && !(backend is FirebaseGameClient)) return;
            new MainWindowPreferenceSession(this, MainWindowPreferences.PathForRole(adminMode));
        }
    }
}
