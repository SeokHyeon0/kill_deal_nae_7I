using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace PubgScore
{
    public sealed partial class MainForm
    {
        readonly Panel pageHost=new Panel();
        readonly Panel setupPage=new Panel();
        Control setupPageHost;
        Control scorePage;
        readonly Button setupTab=new NavigationTabButton();
        readonly Dictionary<string,TextBox> setupInputs=new Dictionary<string,TextBox>();
        readonly HashSet<string> setupDirty=new HashSet<string>();
        readonly ErrorProvider setupErrors=new ErrorProvider();
        bool showingSetup=true;

        void BuildSetupPage()
        {
            setupErrors.ContainerControl=this;setupErrors.BlinkStyle=ErrorBlinkStyle.NeverBlink;
            setupPage.Dock=DockStyle.Fill;setupPage.Margin=Padding.Empty;setupPage.BackColor=PageColor;setupPage.AutoScroll=true;setupPageHost=new DesignScrollHost(setupPage);pageHost.Controls.Add(setupPageHost);
            var content=SheetTable(1,2,PageColor);content.Dock=DockStyle.Top;content.Height=864;content.RowStyles.Clear();content.RowStyles.Add(new RowStyle(SizeType.Absolute,64));content.RowStyles.Add(new RowStyle(SizeType.Absolute,800));setupPage.Controls.Add(content);
            content.Controls.Add(BuildModeSettingsHeading(),0,0);
            var cards=SheetTable(2,5,PageColor);content.Controls.Add(cards,0,1);
            for(int t=0;t<ScoreRules.TeamCount;t++)
            {
                int selected=t;Color tint=Tint(TeamColors[t],5);
                var card=new SurfacePanel {Dock=DockStyle.Fill,Padding=new Padding(12,6,12,6),Margin=new Padding(t%2==0?0:8,8,t%2==0?8:0,0)};cards.Controls.Add(card,t%2,t/2);
                var layout=SheetTable(1,3,Color.White);layout.RowStyles.Clear();layout.RowStyles.Add(new RowStyle(SizeType.Absolute,26));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,16));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));card.Controls.Add(layout);
                var teamHeading=SheetTable(3,1,tint);teamHeading.ColumnStyles.Clear();teamHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,56));teamHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));teamHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,90));layout.Controls.Add(teamHeading,0,0);
                var badge=SheetLabel((t+1)+"팀",tint);badge.Font=UiFont(11F,FontStyle.Bold);badge.Margin=Padding.Empty;teamHeading.Controls.Add(badge,0,0);
                badge.Paint+=delegate(object sender,PaintEventArgs e){using(var brush=new SolidBrush(TeamColors[selected]))e.Graphics.FillRectangle(brush,0,4,3,Math.Max(0,badge.Height-8));};
                var members=SheetLabel("선수 4명",tint);members.TextAlign=ContentAlignment.MiddleLeft;members.Font=UiFont(8F);members.ForeColor=Muted;teamHeading.Controls.Add(members,1,0);
                var open=new Button {Text="기록 보기  ›",Dock=DockStyle.Fill,FlatStyle=FlatStyle.Flat,BackColor=tint,ForeColor=Ink,Margin=Padding.Empty,Font=UiFont(8F),Cursor=Cursors.Hand};open.FlatAppearance.BorderSize=0;open.FlatAppearance.MouseOverBackColor=Tint(TeamColors[t],3);open.Click+=delegate {NavigateToTeam(selected);};teamHeading.Controls.Add(open,2,0);
                var headings=SetupPlayerRow();headings.Controls.Add(SetupColumnCaption("선수"),0,0);headings.Controls.Add(SetupColumnCaption("이름"),1,0);headings.Controls.Add(SetupColumnCaption("차감 점수"),2,0);layout.Controls.Add(headings,0,1);
                var players=SheetTable(1,4,Color.White);layout.Controls.Add(players,0,2);
                for(int p=0;p<4;p++)
                {
                    var row=SetupPlayerRow();row.BackColor=Color.White;players.Controls.Add(row,0,p);
                    var number=SheetLabel((p+1).ToString()+"티어",Color.White);number.ForeColor=Muted;number.Font=UiFont(9F,FontStyle.Bold);number.Margin=new Padding(0,1,8,1);row.Controls.Add(number,0,0);
                    var name=SetupInput("t"+t+".p"+p+".name",60);name.Margin=new Padding(0,0,20,0);name.AccessibleName=(t+1)+"팀 "+(p+1)+"번 이름";row.Controls.Add(SetupField(name,false),1,0);
                    var penalty=SetupInput("t"+t+".p"+p+".penalty",24);penalty.Margin=new Padding(0,0,20,0);penalty.TextAlign=HorizontalAlignment.Right;penalty.AccessibleName=(t+1)+"팀 "+(p+1)+"번 차감 점수";row.Controls.Add(SetupField(penalty,true),2,0);
                    sheetTips.SetToolTip(penalty,"탑텐 1회 차감 · 0~999999 · 빈칸은 차감 없음");
                }
            }
        }
        Control SetupField(TextBox input,bool penalty)
        {
            var field=new SetupFieldPanel {Dock=DockStyle.Fill,Margin=new Padding(0,1,8,1),Padding=new Padding(9,2,22,1),BackColor=penalty?Color.FromArgb(245,242,251):Color.FromArgb(246,248,251)};
            input.Margin=Padding.Empty;input.BackColor=field.BackColor;input.ForeColor=penalty?Accent:Ink;
            field.Controls.Add(input);
            input.Enter+=delegate {field.Active=true;field.Invalidate();};input.Leave+=delegate {field.Active=false;field.Invalidate();};
            field.Click+=delegate {input.Focus();};return field;
        }
        TableLayoutPanel SetupPlayerRow()
        {
            var row=SheetTable(3,1,Color.White);row.ColumnStyles.Clear();row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,56));row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,68));row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,32));return row;
        }
        Label SetupColumnCaption(string text)
        {
            var label=SheetLabel(text,Color.White);label.ForeColor=Muted;label.Font=UiFont(8F);return label;
        }
        TextBox SetupInput(string key,int maxLength)
        {
            var input=new TextBox {Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,Font=UiFont(9F),ForeColor=Ink,BackColor=Color.White,MaxLength=maxLength,Tag=key,AccessibleName=key};setupInputs.Add(key,input);
            input.Enter+=delegate {if(CanEdit)editGenerations[key]=CurrentGeneration;editSessions[key]=CurrentSession;if(!setupDirty.Contains(key)&&!drafts.ContainsKey(key))editVersions[key]=state.Version(key);};
            input.TextChanged+=delegate {if(!refreshing&&CanEdit&&input.Focused){if(!editGenerations.ContainsKey(key)){editGenerations[key]=CurrentGeneration;editSessions[key]=CurrentSession;editVersions[key]=state.Version(key);}setupDirty.Add(key);}};
            input.Validating+=delegate(object sender,CancelEventArgs e) {if(refreshing||!CanEdit||!setupDirty.Contains(key))return;string value;if(!ValidateSetupInput(input,out value))e.Cancel=true;};
            input.Validated+=delegate {if(!refreshing)CommitSetupInput(input);};
            input.KeyDown+=delegate(object sender,KeyEventArgs e) {
                if(e.KeyCode==Keys.Enter){if(TryCommitSetupEdits())SelectNextControl(input,true,true,true,true);e.SuppressKeyPress=true;}
                if(e.KeyCode==Keys.Escape){CancelSetupEdit();e.SuppressKeyPress=true;}
            };
            return input;
        }
        string SetupStoredValue(string key)
        {
            return state.Get(key,key.Length==7&&key.EndsWith(".name",StringComparison.Ordinal)?(Int32.Parse(key.Substring(1,1))+1)+"팀":"");
        }
        bool ValidateSetupInput(TextBox input,out string value)
        {
            string key=(string)input.Tag;value=input.Text.Trim();
            try
            {
                if(key.EndsWith(".penalty",StringComparison.Ordinal)&&value.Length>0)
                {
                    decimal amount;
                    if(!Decimal.TryParse(value,NumberStyles.Number,CultureInfo.CurrentCulture,out amount)||amount<0||amount>999999)
                        throw new ArgumentException("차감 점수는 − 없이 0~999999의 숫자로 입력하세요.");
                    value=amount.ToString(CultureInfo.InvariantCulture);
                }
                if(key.EndsWith(".name",StringComparison.Ordinal)&&key.Length==7&&value.Length==0)throw new ArgumentException("팀 이름을 입력하세요.");
                value=ScoreRules.Normalize(key,value);setupErrors.SetError(input,"");return true;
            }
            catch(ArgumentException ex){setupErrors.SetError(input,ex.Message);sheetTips.SetToolTip(input,ex.Message);return false;}
        }
        bool CommitSetupInput(TextBox input)
        {
            string key=(string)input.Tag;if(!CanEdit)return false;if(!setupDirty.Contains(key)||pending.Contains(key))return true;
            string value;if(!ValidateSetupInput(input,out value))return false;
            setupDirty.Remove(key);
            if(value==SetupStoredValue(key)){drafts.Remove(key);editVersions[key]=state.Version(key);Render();return true;}
            SaveClearedCell(key,value,editVersions.ContainsKey(key)?editVersions[key]:state.Version(key));return true;
        }
        bool TryCommitSetupEdits()
        {
            if(!CanEdit){setupDirty.Clear();return true;}
            // Validate before committing so a bad value cannot be hidden by navigation.
            foreach(string key in setupDirty.ToArray())
            {
                string value;if(!ValidateSetupInput(setupInputs[key],out value)){setupInputs[key].Focus();return false;}
            }
            foreach(string key in setupDirty.ToArray())if(!CommitSetupInput(setupInputs[key]))return false;
            return true;
        }
        bool CancelSetupEdit()
        {
            var input=setupInputs.Values.FirstOrDefault(box=>box.ContainsFocus);if(input==null)return false;
            string key=(string)input.Tag;if(pending.Contains(key))return false;
            setupDirty.Remove(key);drafts.Remove(key);setupErrors.SetError(input,"");editVersions[key]=state.Version(key);
            bool old=refreshing;refreshing=true;try{input.Text=SetupStoredValue(key);}finally{refreshing=old;}Render();return true;
        }
        void RenderSetupInputs()
        {
            bool old=refreshing;refreshing=true;
            try
            {
                foreach(var entry in setupInputs)
                {
                    if((!entry.Value.Focused||!view.CanEdit)&&!setupDirty.Contains(entry.Key))SetText(entry.Value,drafts.ContainsKey(entry.Key)?drafts[entry.Key]:SetupStoredValue(entry.Key));
                    entry.Value.ReadOnly=!view.CanEdit;entry.Value.Enabled=!hostBusy&&!pending.Contains(entry.Key);
                }
                setupTab.Enabled=!hostBusy;setupTab.BackColor=Color.White;setupTab.ForeColor=Ink;setupTab.FlatAppearance.BorderColor=showingSetup?Accent:SheetLine;setupTab.FlatAppearance.BorderSize=showingSetup?2:1;
                if(showingSetup)for(int i=0;i<teamTabs.Length;i++){teamTabs[i].BackColor=Tint(TeamColors[i],8);teamTabs[i].ForeColor=Ink;teamTabs[i].FlatAppearance.BorderColor=Tint(TeamColors[i],3);teamTabs[i].FlatAppearance.BorderSize=1;}

            }
            finally {refreshing=old;}
        }
        void ShowSetupPage()
        {
            if(!TryCommitSetupEdits())return;
            if(playerNames.Any(name=>name!=null&&name.Focused))grid.Focus();if(!grid.EndEdit())return;
            showingSetup=true;setupPageHost.Visible=true;scorePage.Visible=false;setupPageHost.BringToFront();Render();
        }
        void ShowScorePage()
        {
            CollapseScoreSelection();showingSetup=false;scorePage.Visible=true;setupPageHost.Visible=false;scorePage.BringToFront();AlignSheetHeaders();
        }
        void NavigateToTeam(int selected)
        {
            if(!teams.Enabled||!TryCommitSetupEdits())return;
            if(teams.SelectedIndex!=selected){teams.SelectedIndex=selected;return;}
            if(!grid.EndEdit())return;ShowScorePage();Render();
        }
    }
    internal sealed class SetupFieldPanel : Panel
    {
        public bool Active;
        public SetupFieldPanel(){DoubleBuffered=true;ResizeRedraw=true;}
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent==null?Color.White:Parent.BackColor);if(Width<12||Height<12)return;
            using(var shape=new GraphicsPath())
            {
                int d=8;float right=Width-1,bottom=Height-1;
                shape.AddArc(0,0,d,d,180,90);shape.AddArc(right-d,0,d,d,270,90);shape.AddArc(right-d,bottom-d,d,d,0,90);shape.AddArc(0,bottom-d,d,d,90,90);shape.CloseFigure();
                var previous=e.Graphics.SmoothingMode;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                using(var brush=new SolidBrush(BackColor))e.Graphics.FillPath(brush,shape);
                using(var pen=new Pen(Active?Color.FromArgb(110,88,185):Color.FromArgb(227,231,239),Active?2F:1F))e.Graphics.DrawPath(pen,shape);
                e.Graphics.SmoothingMode=previous;
            }
        }
    }

}
