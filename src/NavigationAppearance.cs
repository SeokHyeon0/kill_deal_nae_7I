using System.Drawing;

namespace PubgScore
{
    public sealed partial class MainForm
    {
        static readonly Color[] NavigationTabColors={
            Color.FromArgb(253,226,226),Color.FromArgb(221,238,252),Color.FromArgb(234,251,224),
            Color.FromArgb(255,249,217),Color.FromArgb(253,234,234),Color.FromArgb(220,235,217),
            Color.FromArgb(253,245,236),Color.FromArgb(253,235,208),Color.FromArgb(236,233,251),Color.FromArgb(224,244,241)
        };
        void RenderNavigationAppearance()
        {
            var setup=(NavigationTabButton)setupTab;setup.Selected=showingSetup;
            setup.BackColor=DialogPalette.Background;setup.ForeColor=showingSetup?DialogPalette.Text:DialogPalette.Muted;setup.FlatAppearance.BorderSize=0;
            setup.Font=UiFont(9.5F,showingSetup?FontStyle.Bold:FontStyle.Regular);
            for(int index=0;index<teamTabs.Length;index++)
            {
                var tab=teamTabs[index] as NavigationTabButton;if(tab==null)continue;
                bool selected=!showingSetup&&index==team;tab.Selected=selected;tab.FlatAppearance.BorderSize=0;
                tab.BackColor=NavigationTabColors[index];tab.ForeColor=selected?DialogPalette.Text:DialogPalette.Muted;
                tab.Font=UiFont(9.5F,selected?FontStyle.Bold:FontStyle.Regular);
            }
        }
    }
}
