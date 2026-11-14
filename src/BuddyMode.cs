using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace PubgScore
{
    internal sealed class BuddyStanding
    {
        internal int FirstTeam, Rank;
        internal int[] Members;
        internal bool Active;
        internal string Name;
        internal decimal Total, Chicken;
    }

    internal static class BuddyScores
    {
        internal static int Size(ScoreState state){return Math.Max(1,Math.Min(5,state.GetInt("buddySize",1)));}
        internal static BuddyStanding[] Calculate(ScoreState state,Func<int,TeamSummary> summary)
        {
            int size=Size(state);var result=new List<BuddyStanding>();
            for(int first=0;first<ScoreRules.TeamCount;first+=size)
            {
                var row=new BuddyStanding {FirstTeam=first,Members=Enumerable.Range(first,Math.Min(size,ScoreRules.TeamCount-first)).ToArray()};
                foreach(int member in row.Members)
                {
                    bool active=Enumerable.Range(0,ScoreRules.PlayerCount).Any(p=>!String.IsNullOrWhiteSpace(state.Get("t"+member+".p"+p+".name","")));
                    if(active)row.Active=true;if(!active&&size>1)continue;var score=summary(member);row.Total+=score.Total;row.Chicken+=score.ChickenSum;
                }
                if(size==1){string leader=state.Get("t"+first+".p0.name","").Trim();row.Name=leader.Length>0?leader+"팀":"";}
                else row.Name=String.Join(", ",row.Members.Where(i=>Enumerable.Range(0,ScoreRules.PlayerCount).Any(p=>!String.IsNullOrWhiteSpace(state.Get("t"+i+".p"+p+".name","")))).Select(i=>{string name=state.Get("t"+i+".p0.name","").Trim();return name.Length>0?name:(i+1)+"팀 (1티어 미입력)";}));
                result.Add(row);
            }
            foreach(var row in result)row.Rank=row.Active?1+result.Count(other=>other.Active&&other.Total>row.Total):0;
            return result.OrderByDescending(row=>row.Active).ThenByDescending(row=>row.Active?row.Total:0).ThenBy(row=>row.FirstTeam).ToArray();
        }
    }

    public sealed partial class MainForm
    {
        readonly Button buddyButton=new DesignButton();
        readonly Label buddyDescription=new Label();
        ContextMenuStrip buddyMenu;

        Control BuildModeSettingsHeading()
        {
            var heading=SheetTable(2,2,PageColor);heading.ColumnStyles.Clear();
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,330));
            heading.RowStyles.Clear();heading.RowStyles.Add(new RowStyle(SizeType.Absolute,36));heading.RowStyles.Add(new RowStyle(SizeType.Absolute,26));
            var title=SheetLabel("팀 설정",PageColor);title.TextAlign=ContentAlignment.MiddleLeft;title.Font=UiFont(17F,FontStyle.Bold);heading.Controls.Add(title,0,0);
            var settings=SheetTable(2,1,PageColor);settings.ColumnStyles[0].Width=45;settings.ColumnStyles[1].Width=55;heading.Controls.Add(settings,1,0);
            var countHost=new DialogSurface {Dock=DockStyle.Fill,BackColor=DialogPalette.AccentSoft,Margin=new Padding(0,0,8,0),Padding=new Padding(8,0,8,0),CornerRadius=8,DrawBorder=false};
            var count=SheetLabel("10팀 · 40명",DialogPalette.AccentSoft);count.ForeColor=DialogPalette.Accent;count.Font=UiFont(9F,FontStyle.Bold);countHost.Controls.Add(count);settings.Controls.Add(countHost,0,0);
            var buddyHost=new Panel {Dock=DockStyle.Fill,Margin=new Padding(0,3,0,3)};settings.Controls.Add(buddyHost,1,0);
            CopyButtonStyle(buddyButton,"깐부모드",false);buddyButton.AutoSize=false;buddyButton.Dock=DockStyle.Fill;buddyButton.Padding=Padding.Empty;buddyButton.Margin=Padding.Empty;buddyHost.Controls.Add(buddyButton);
            StyleCell(buddyDescription,"깐부 꺼짐",PageColor);buddyHost.Controls.Add(buddyDescription);
            buddyMenu=new ContextMenuStrip {AutoSize=false,Renderer=new LightMenuRenderer(),BackColor=DialogPalette.Surface,ForeColor=DialogPalette.Text,ShowImageMargin=false,ShowCheckMargin=false,Padding=new Padding(6),Font=UiFont(10F)};foreach(int size in new[]{2,3,4,5,1})
            {
                if(size==1)buddyMenu.Items.Add(new ToolStripSeparator {AutoSize=false,Margin=Padding.Empty});
                int selected=size;var item=new ToolStripMenuItem(size==1?"깐부 끄기":size+"팀 깐부");item.Tag=size;
                item.AutoSize=false;item.Size=new Size(208,40);item.Margin=Padding.Empty;item.Padding=new Padding(12,0,28,0);
                item.Click+=async delegate {if(!adminMode||!view.IsAdmin||!CanEdit||pending.Contains("buddySize"))return;editGenerations["buddySize"]=CurrentGeneration;editSessions["buddySize"]=CurrentSession;await Save("buddySize",selected.ToString(CultureInfo.InvariantCulture),state.Version("buddySize"));};buddyMenu.Items.Add(item);
            }
            buddyMenu.SizeChanged+=delegate {RoundBuddyMenu();};SizeBuddyMenu();buddyMenu.Opening+=delegate {SizeBuddyMenu();};
            buddyButton.Click+=delegate {foreach(ToolStripMenuItem item in buddyMenu.Items.OfType<ToolStripMenuItem>())item.Checked=(int)item.Tag==BuddyScores.Size(state);buddyMenu.Show(buddyButton,new Point(0,buddyButton.Height));};
            Disposed+=delegate {buddyMenu.Dispose();};
            var hint=SheetLabel("차감 점수는 − 기호 없이 숫자만 입력 · Enter 또는 다른 칸으로 이동하면 저장",PageColor);hint.ForeColor=Muted;hint.Font=UiFont(9F);hint.TextAlign=ContentAlignment.MiddleLeft;AddCell(heading,hint,0,1,2,1);
            return heading;
        }
        void SizeBuddyMenu()
        {
            if(buddyMenu==null)return;
            float scale=Math.Max(1F,buddyButton.DeviceDpi/96F);
            int padding=(int)Math.Round(6*scale),width=Math.Max(padding*2+1,buddyButton.Width);
            int rowHeight=(int)Math.Round(40*scale),separatorHeight=(int)Math.Round(13*scale),height=padding*2;
            buddyMenu.SuspendLayout();
            try
            {
                // Fix the popup itself: item widths alone still allow preferred-text autosizing.
                buddyMenu.AutoSize=false;buddyMenu.Padding=new Padding(padding);
                foreach(ToolStripItem item in buddyMenu.Items)
                {
                    int itemHeight=item is ToolStripSeparator?separatorHeight:rowHeight;
                    item.AutoSize=false;item.Margin=Padding.Empty;item.Size=new Size(width-padding*2,itemHeight);
                    item.Padding=item is ToolStripSeparator?Padding.Empty:new Padding((int)Math.Round(12*scale),0,(int)Math.Round(28*scale),0);
                    height+=itemHeight;
                }
                buddyMenu.MinimumSize=new Size(width,height);buddyMenu.Size=new Size(width,height);
            }
            finally{buddyMenu.ResumeLayout(true);}
            RoundBuddyMenu();
        }
        void RoundBuddyMenu()
        {
            if(buddyMenu==null||buddyMenu.ClientSize.Width<2||buddyMenu.ClientSize.Height<2)return;
            float scale=Math.Max(1F,buddyButton.DeviceDpi/96F);
            using(var shape=DesignTheme.Round(new RectangleF(0,0,buddyMenu.ClientSize.Width,buddyMenu.ClientSize.Height),12*scale))
            {
                var previous=buddyMenu.Region;buddyMenu.Region=new Region(shape);if(previous!=null)previous.Dispose();
            }
        }
        void RenderModeSettings()
        {
            int size=BuddyScores.Size(state);buddyButton.Text=size==1?"깐부모드":("깐부모드 · "+size+"팀 깐부");buddyDescription.Text=size==1?"깐부 꺼짐":size+"팀 깐부";
            buddyButton.BackColor=size==1?DialogPalette.Surface:DialogPalette.AccentSoft;buddyButton.ForeColor=size==1?DialogPalette.Text:DialogPalette.Accent;buddyButton.FlatAppearance.BorderColor=DialogPalette.Border;
            buddyDescription.BackColor=size==1?DialogPalette.Background:DialogPalette.AccentSoft;buddyDescription.ForeColor=size==1?DialogPalette.Muted:DialogPalette.Accent;
            buddyButton.Visible=adminMode;buddyDescription.Visible=!adminMode;
            buddyButton.Enabled=adminMode&&view.IsAdmin&&CanEdit&&!pending.Contains("buddySize");
        }
        void RenderBuddyStandings()
        {
            int size=BuddyScores.Size(state);var groups=BuddyScores.Calculate(state,Summary);var selected=groups.First(row=>row.Members.Contains(team));
            summaryHeading.Text=size==1?"우리 팀 요약":"깐부 요약 ("+selected.Name+")";
            string modeName=GameModes.Name(view.Mode);
            rankingTitle.Text=modeName.Length==0?"등수":modeName+" 등수";
            if(size>1){summaryTotal.Text=F(selected.Total);summaryChicken.Text=F(selected.Chicken);}
            sheetTips.SetToolTip(summaryHeading,summaryHeading.Text);
            summaryRank.Text=selected.Active?selected.Rank+"등":"";
            for(int r=0;r<ScoreRules.TeamCount;r++)
            {
                bool exists=r<groups.Length;var entry=exists?groups[r]:null;
                for(int c=0;c<3;c++)
                {
                    var cell=rankCells[r,c];cell.Visible=exists;
                    if(!exists){cell.Text="";cell.Tag=null;continue;}
                    cell.Tag=entry.FirstTeam;cell.Text=c==0?(entry.Rank>0?entry.Rank+"등":""):c==1?(entry.Active?entry.Name:""):F(entry.Total);
                    cell.BackColor=c==0?Tint(TeamColors[entry.FirstTeam],4):entry.Members.Contains(team)?SheetPurple:r%2==0?Color.FromArgb(247,249,252):Color.White;
                    sheetTips.SetToolTip(cell,(entry.Active?entry.Name:"선수 미입력")+" · 두 번 클릭하여 "+(entry.FirstTeam+1)+"팀 이동");
                }
            }
        }
    }
}
