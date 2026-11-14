using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace PubgScore
{
    // Presentation only: saving, validation and Firebase synchronization stay in MainForm.
    public sealed partial class MainForm
    {
        static readonly Color PageColor=Color.FromArgb(243,245,250);
        static readonly Color Ink=Color.FromArgb(39,48,68);
        static readonly Color Muted=Color.FromArgb(109,120,139);
        static readonly Color Accent=Color.FromArgb(119,93,229);
        static readonly Color SheetPurple=Color.FromArgb(239,235,249);
        static readonly Color SheetLine=Color.FromArgb(229,233,240);
        // Preserve the eight team colors from the original sheet.
        static readonly Color[] TeamColors={
            Color.FromArgb(235,69,69),Color.FromArgb(40,160,255),
            Color.FromArgb(154,255,94),Color.FromArgb(255,238,51),
            Color.FromArgb(254,137,145),Color.FromArgb(38,138,38),
            Color.FromArgb(254,218,184),Color.FromArgb(255,150,2),
            Color.FromArgb(145,119,215),Color.FromArgb(68,190,181)
        };
        static readonly Color[] NameColors={Color.FromArgb(255,237,241),Color.FromArgb(255,241,229),Color.FromArgb(255,249,222),Color.FromArgb(234,246,237)};
        static readonly Color[] NetColors={Color.FromArgb(250,216,226),Color.FromArgb(255,224,199),Color.FromArgb(249,237,173),Color.FromArgb(208,233,215)};
        static readonly Color[] PlayerInk={Color.FromArgb(154,55,85),Color.FromArgb(153,80,29),Color.FromArgb(126,101,19),Color.FromArgb(47,115,69)};
        readonly Button[] teamTabs=new Button[ScoreRules.TeamCount];
        readonly Label[] grossTotals=new Label[4],deductionTotals=new Label[4],deductionNames=new Label[4],deductionValues=new Label[4];
        readonly Label[,] rankCells=new Label[ScoreRules.TeamCount,3];
        readonly Label[] summaryNames=new Label[4];
        Label summaryHeading,rankingTitle;
        readonly Label chickenTotal=new Label(),summaryTotal=new Label(),summaryChicken=new Label(),summaryRank=new Label();
        readonly ToolTip sheetTips=new ToolTip();
        readonly Panel sheetHeader=new Panel();
        readonly Label roundHeader=new Label();
        readonly Panel sheetBand=new Panel();
        readonly Label teamBadge=new Label();
        readonly Label roundCountLabel=new Label();
        Label roomBrand;
        TableLayoutPanel overview;
        Action layoutMasthead;
        readonly TableLayoutPanel[] playerHeaders=new TableLayoutPanel[4];
        readonly Label[] scoreInputLabels=new Label[4],damageInputLabels=new Label[4];
        bool? splitScoreColumns;
        TableLayoutPanel chickenHeader,totalHeader;

        TableLayoutPanel SheetTable(int columns,int rows,Color color)
        {
            var table=new TableLayoutPanel {Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=Padding.Empty,ColumnCount=columns,RowCount=rows,BackColor=color};
            for(int c=0;c<columns;c++)table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100F/columns));
            for(int r=0;r<rows;r++)table.RowStyles.Add(new RowStyle(SizeType.Percent,100F/rows));
            return table;
        }
        Label SheetLabel(string text,Color color)
        {
            var label=new Label();StyleCell(label,text,color);return label;
        }
        void StyleCell(Label label,string text,Color color)
        {
            label.Text=text;label.Dock=DockStyle.Fill;label.Margin=Padding.Empty;label.BackColor=color;
            label.ForeColor=Ink;label.TextAlign=ContentAlignment.MiddleCenter;label.AutoEllipsis=true;
            label.Font=UiFont(9F,FontStyle.Regular);
        }
        Label Caption(string text)
        {
            var label=SheetLabel(text,Color.White);label.TextAlign=ContentAlignment.MiddleLeft;
            label.Font=UiFont(9F,FontStyle.Bold);return label;
        }
        void AddCell(TableLayoutPanel table,Control cell,int col,int row,int colSpan,int rowSpan)
        {
            table.Controls.Add(cell,col,row);if(colSpan>1)table.SetColumnSpan(cell,colSpan);if(rowSpan>1)table.SetRowSpan(cell,rowSpan);
        }
        SurfacePanel Card(TableLayoutPanel parent,int column,Padding margin)
        {
            var card=new SurfacePanel {Dock=DockStyle.Fill,Margin=margin,Padding=new Padding(14,12,14,12)};
            parent.Controls.Add(card,column,0);return card;
        }
        void BuildUI()
        {
            var root=SheetTable(1,3,PageColor);root.RowStyles.Clear();
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,adminMode?139:78));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,54));Controls.Add(root);
            var masthead=SheetTable(2,2,Color.White);masthead.Padding=new Padding(24,0,24,8);masthead.ColumnStyles.Clear();
            masthead.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,280));masthead.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));masthead.RowStyles.Clear();masthead.RowStyles.Add(new RowStyle(SizeType.Percent,100));masthead.RowStyles.Add(new RowStyle(SizeType.Absolute,adminMode?51:0));root.Controls.Add(masthead,0,0);
            var branding=SheetTable(1,2,Color.White);branding.Padding=new Padding(0,0,0,4);branding.RowStyles[0].Height=65;branding.RowStyles[1].Height=35;
            var brand=Caption("킬내기");brand.Font=UiFont(20F,FontStyle.Bold);brand.TextAlign=ContentAlignment.TopLeft;branding.Controls.Add(brand,0,0);roomBrand=brand;sheetTips.SetToolTip(brand,"현재 방 제목");
            var subtitle=Caption("팀별 점수와 경기 기록을 한눈에");subtitle.Font=UiFont(9F);subtitle.ForeColor=Muted;branding.Controls.Add(subtitle,0,1);masthead.Controls.Add(branding,0,0);
            var toolbar=new WrapToolbar {Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=new Padding(0,0,0,8),WrapContents=true,FlowDirection=FlowDirection.RightToLeft,AutoScroll=false,BackColor=Color.White};
            CopyButtonStyle(connectButton,"다시 연결",true);connectButton.Click+=async delegate {await Connect();};
            CopyButtonStyle(resetButton,"전체 초기화",false);resetButton.CausesValidation=false;resetButton.Click+=async delegate {await ConfirmReset();};
            CopyButtonStyle(configButton,"팀 설정",false);configButton.Click+=delegate {ShowSettings();};
            CopyButtonStyle(endButton,"게임 종료 · 방 삭제",false);endButton.CausesValidation=false;endButton.Click+=async delegate {await EndGame();};
            CopyButtonStyle(rosterButton,"팀 짜기",true);rosterButton.CausesValidation=false;rosterButton.Click+=delegate {OpenRosterEditor();};
            CopyButtonStyle(loginButton,"BJ 로그인",false);loginButton.Click+=delegate {PromptAdminLogin();};
            CopyButtonStyle(logoutButton,"로그아웃",false);logoutButton.CausesValidation=false;logoutButton.Click+=delegate {LogoutAdministrator();};
            CopyButtonStyle(resultButton,"결과 복사",false);resultButton.Click+=delegate {ExportResult();};
            ((DesignButton)rosterButton).Glyph="Team";((DesignButton)resetButton).Glyph="Reset";((DesignButton)resultButton).Glyph="Copy";
            foreach(var button in new[]{resultButton,loginButton,resetButton,rosterButton})
            {
                button.AutoSizeMode=AutoSizeMode.GrowAndShrink;
                button.Font=UiFont(10F);
                button.MinimumSize=new Size(104,40);
                button.Padding=new Padding(12,7,12,7);
                button.Margin=new Padding(10,20,0,0);
            }
            if(!adminMode)
            {
                masthead.Controls.Add(toolbar,1,0);
            }
            else
            {
                var titleToolbarHost=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty,BackColor=Color.White};
                toolbar.Controls.Add(loginButton);
                titleToolbarHost.Controls.Add(toolbar);
                masthead.Controls.Add(titleToolbarHost,1,0);

                var modeRow=SheetTable(4,1,Color.White);
                modeRow.Padding=new Padding(0,24,0,4);
                modeRow.ColumnStyles.Clear();
                modeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
                masthead.Controls.Add(modeRow,0,1);
                masthead.SetColumnSpan(modeRow,2);

                var selector=BuildGameModeButtons();
                selector.Dock=DockStyle.Left;
                modeRow.Controls.Add(selector,0,0);

                var actions=new[]{rosterButton,resetButton,resultButton};
                var actionMargin=new Padding(10,0,0,0);
                for(int i=0;i<actions.Length;i++)
                {
                    var button=actions[i];
                    int width=button.GetPreferredSize(Size.Empty).Width;
                    modeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,width+actionMargin.Horizontal));
                    button.AutoSize=false;
                    button.Dock=DockStyle.Fill;
                    button.Margin=actionMargin;
                    modeRow.Controls.Add(button,i+1,0);
                }

                int toolbarHeight=72;
                layoutMasthead=delegate {
                    bool inRoom=view.RoomId.Length>0;
                    bool besideTitle=inRoom&&!showingSetup;
                    float scale=DeviceDpi/96F;
                    int setupSpacing=(int)Math.Round(20*scale);
                    int edge=(int)Math.Round(4*scale);
                    int buttonRowHeight=(int)Math.Round(51*scale)-2*edge;
                    int topPadding=besideTitle?(int)Math.Round(6*scale):setupSpacing+edge;
                    int actionTableHeight=topPadding+buttonRowHeight+edge;
                    int bottomHeight=inRoom&&!besideTitle?actionTableHeight:0;
                    int upperHeight=Math.Max((int)Math.Round(88*scale),toolbarHeight+(int)Math.Round(16*scale));
                    if(inRoom&&!loginButton.Visible)upperHeight-=setupSpacing;
                    int required=upperHeight+bottomHeight;
                    if(besideTitle)
                    {
                        int scoreHeight=actionTableHeight+masthead.Padding.Vertical;
                        if(loginButton.Visible)scoreHeight+=toolbarHeight;
                        required=Math.Max(required,scoreHeight);
                    }

                    modeRow.Visible=inRoom;
                    bool reposition=besideTitle!=(modeRow.Parent==titleToolbarHost);
                    if(!reposition&&masthead.RowStyles[1].Height==bottomHeight&&root.RowStyles[0].Height==required&&modeRow.Padding.Top==topPadding)return;
                    root.SuspendLayout();masthead.SuspendLayout();
                    try
                    {
                        if(reposition)
                        {
                            if(besideTitle)
                            {
                                modeRow.Dock=DockStyle.Top;
                                titleToolbarHost.Controls.Add(modeRow);
                            }
                            else
                            {
                                modeRow.Dock=DockStyle.Fill;
                                masthead.Controls.Add(modeRow,0,1);
                                masthead.SetColumnSpan(modeRow,2);
                            }
                        }
                        modeRow.Padding=new Padding(0,topPadding,0,edge);
                        if(besideTitle)modeRow.Height=actionTableHeight;
                        masthead.RowStyles[1].Height=bottomHeight;
                        root.RowStyles[0].Height=required;
                    }
                    finally{masthead.ResumeLayout(true);root.ResumeLayout(true);}
                };
                toolbar.RequiredHeightChanged=delegate(int height){toolbarHeight=height;layoutMasthead();};
            }

            // All navigation goes through the existing validation/commit path.
            teams.DropDownStyle=ComboBoxStyle.DropDownList;for(int i=0;i<ScoreRules.TeamCount;i++)teams.Items.Add((i+1)+"팀");teams.SelectedIndex=0;
            teams.SelectedIndexChanged+=delegate {if(!refreshing){if(!TryCommitSetupEdits()){refreshing=true;try{teams.SelectedIndex=team;}finally{refreshing=false;}return;}if(playerNames.Any(name=>name!=null&&name.Focused))grid.Focus();if(!grid.EndEdit()){teams.SelectedIndex=team;return;}team=teams.SelectedIndex;ShowScorePage();Render();}};
            pageHost.Dock=DockStyle.Fill;pageHost.Margin=Padding.Empty;pageHost.Padding=new Padding(24,20,24,0);pageHost.BackColor=PageColor;root.Controls.Add(pageHost,0,1);
            var sheet=SheetTable(1,4,PageColor);sheet.Margin=Padding.Empty;scorePage=sheet;sheet.RowStyles.Clear();
            sheet.RowStyles.Add(new RowStyle(SizeType.Absolute,272));sheet.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
            sheet.RowStyles.Add(new RowStyle(SizeType.Absolute,116));sheet.RowStyles.Add(new RowStyle(SizeType.Percent,100));pageHost.Controls.Add(sheet);
            overview=SheetTable(4,1,PageColor);overview.ColumnStyles.Clear();
            overview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,36));overview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,30));overview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,34));overview.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,158));sheet.Controls.Add(overview,0,0);
            BuildSummary(overview);
            var teamCard=Card(overview,2,new Padding(7,0,0,0));var teamInfo=SheetTable(1,4,Color.White);teamCard.Controls.Add(teamInfo);teamInfo.RowStyles.Clear();
            teamInfo.RowStyles.Add(new RowStyle(SizeType.Absolute,20));teamInfo.RowStyles.Add(new RowStyle(SizeType.Absolute,56));teamInfo.RowStyles.Add(new RowStyle(SizeType.Absolute,28));teamInfo.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var selectedCaption=Caption("선택한 팀");selectedCaption.ForeColor=Muted;teamInfo.Controls.Add(selectedCaption,0,0);
            StyleCell(teamBadge,"",Color.White);teamBadge.Margin=new Padding(0,2,0,2);teamBadge.Font=UiFont(26F,FontStyle.Bold);teamBadge.AccessibleName="선택한 팀의 1티어 이름";teamInfo.Controls.Add(teamBadge,0,1);
            var penaltyTitle=Caption("선수별 탑텐 차감");penaltyTitle.Cursor=Cursors.Hand;penaltyTitle.Click+=delegate {ShowSettings();};teamInfo.Controls.Add(penaltyTitle,0,2);
            var deductions=SheetTable(2,4,Color.White);deductions.ColumnStyles[0].Width=65;deductions.ColumnStyles[1].Width=35;teamInfo.Controls.Add(deductions,0,3);
            for(int p=0;p<4;p++)
            {
                deductionNames[p]=SheetLabel("",Color.White);deductionNames[p].TextAlign=ContentAlignment.MiddleLeft;deductionNames[p].ForeColor=Muted;
                deductionValues[p]=SheetLabel("",Color.White);deductionValues[p].TextAlign=ContentAlignment.MiddleRight;deductionValues[p].Font=UiFont(9F,FontStyle.Bold);deductionValues[p].ForeColor=Accent;deductionValues[p].Cursor=Cursors.Hand;
                deductions.Controls.Add(deductionNames[p],0,p);deductions.Controls.Add(deductionValues[p],1,p);deductionValues[p].Click+=delegate {ShowSettings();};sheetTips.SetToolTip(deductionValues[p],"클릭하여 개인별 탑텐 차감 설정");
            }
            var pointsCard=Card(overview,3,new Padding(7,0,0,0));pointsCard.Padding=new Padding(8,10,8,10);pointsCard.Controls.Add(placementTable);
            sheetBand.Dock=DockStyle.Fill;sheetBand.Margin=Padding.Empty;sheetBand.BackColor=PageColor;sheet.Controls.Add(sheetBand,0,1);
            var sectionBar=SheetTable(3,1,PageColor);sectionBar.ColumnStyles.Clear();sectionBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));sectionBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));sectionBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,210));sheetBand.Controls.Add(sectionBar);
            var recordHeading=new Label {Text="경기 기록",Dock=DockStyle.Fill,Margin=Padding.Empty,TextAlign=ContentAlignment.MiddleLeft,Font=UiFont(12F,FontStyle.Bold),ForeColor=Ink};sectionBar.Controls.Add(recordHeading,0,0);
            roundCountLabel.Dock=DockStyle.Fill;roundCountLabel.Margin=Padding.Empty;roundCountLabel.TextAlign=ContentAlignment.MiddleLeft;roundCountLabel.ForeColor=Muted;roundCountLabel.Font=UiFont(8F);sectionBar.Controls.Add(roundCountLabel,1,0);
            var keyboardHint=new Label {Text="점수 칸 드래그 후 Delete로 지우기",Dock=DockStyle.Fill,Margin=Padding.Empty,TextAlign=ContentAlignment.MiddleRight,ForeColor=Muted,Font=UiFont(8F)};sectionBar.Controls.Add(keyboardHint,2,0);
            sheetHeader.Dock=DockStyle.Fill;sheetHeader.Margin=Padding.Empty;sheetHeader.BackColor=PageColor;sheet.Controls.Add(sheetHeader,0,2);
            ConfigureGrid();BuildSheetHeader();sheet.Controls.Add(new DesignScrollHost(grid),0,3);
            grid.SizeChanged+=delegate {AlignSheetHeaders();};grid.ColumnWidthChanged+=delegate {AlignSheetHeaders();};sheetHeader.SizeChanged+=delegate {AlignSheetHeaders();};Shown+=delegate {toolbar.PerformLayout();AlignSheetHeaders();};
            notice.Visible=false;
            var bottom=SheetTable(2,1,Color.White);bottom.Padding=new Padding(24,8,24,8);bottom.ColumnStyles.Clear();bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,740));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            var tabs=new FlowLayoutPanel {Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=Padding.Empty,WrapContents=false};
            setupTab.Text="팀 설정";setupTab.Width=100;setupTab.Height=36;setupTab.Margin=new Padding(0,0,10,0);setupTab.FlatStyle=FlatStyle.Flat;setupTab.FlatAppearance.BorderSize=0;setupTab.Font=UiFont(9.5F,FontStyle.Bold);setupTab.Click+=delegate {ShowSetupPage();};tabs.Controls.Add(setupTab);
            for(int i=0;i<ScoreRules.TeamCount;i++)
            {
                int selected=i;var tab=new NavigationTabButton {Text=(i+1)+"팀",Width=56,Height=36,Margin=new Padding(0,0,6,0),FlatStyle=FlatStyle.Flat,Font=UiFont(9.5F),AutoEllipsis=true,Cursor=Cursors.Hand,UseVisualStyleBackColor=false,CornerRadius=10,BackColor=NavigationTabColors[i],ForeColor=DialogPalette.Muted};tab.FlatAppearance.BorderSize=0;
                tab.Click+=delegate {NavigateToTeam(selected);};teamTabs[i]=tab;tabs.Controls.Add(tab);
            }
            bottom.Controls.Add(tabs,0,0);connection.Dock=DockStyle.Fill;connection.TextAlign=ContentAlignment.MiddleRight;connection.AutoEllipsis=true;connection.ForeColor=Muted;connection.Font=UiFont(8F);connection.Margin=Padding.Empty;bottom.Controls.Add(connection,1,0);root.Controls.Add(bottom,0,2);
#if SINGLEFILE || VIEWER_SINGLEFILE
            bottom.ColumnCount=3;bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,58));
            connection.Margin=new Padding(0,0,8,0);
            var information=CreateDeploymentInformationButton(Color.White,Muted);information.Anchor=AnchorStyles.Right;bottom.Controls.Add(information,2,0);
#endif
            BuildSetupPage();setupPageHost.Visible=true;scorePage.Visible=false;setupPageHost.BringToFront();
        }
        void BuildSummary(TableLayoutPanel top)
        {
            var summaryCard=Card(top,0,new Padding(0,0,7,0));var summary=SheetTable(1,4,Color.White);summaryCard.Controls.Add(summary);summary.RowStyles.Clear();
            summary.RowStyles.Add(new RowStyle(SizeType.Absolute,24));summary.RowStyles.Add(new RowStyle(SizeType.Absolute,60));summary.RowStyles.Add(new RowStyle(SizeType.Absolute,20));summary.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            summary.Controls.Add(summaryHeading=Caption("우리 팀 요약"),0,0);StyleSummaryText(summaryHeading,10F);
            var metrics=SheetTable(3,2,Color.White);metrics.ColumnStyles.Clear();metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,45));metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,27.5F));metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,27.5F));metrics.RowStyles[0].Height=32;metrics.RowStyles[1].Height=68;
            string[] metricNames={"총 점수","현재 순위","치킨 합계"};Label[] metricValues={summaryTotal,summaryRank,summaryChicken};
            for(int i=0;i<3;i++){var caption=SheetLabel(metricNames[i],Color.White);StyleSummaryText(caption,9F);metrics.Controls.Add(caption,i,0);StyleCell(metricValues[i],"0",Color.White);StyleSummaryText(metricValues[i],i==0?27F:20F);metrics.Controls.Add(metricValues[i],i,1);}summary.Controls.Add(metrics,0,1);
            var detailHead=SheetTable(2,1,Color.White);var playerCaption=Caption("선택 팀 선수");StyleSummaryText(playerCaption,9F);detailHead.Controls.Add(playerCaption,0,0);
            var scoreCaption=SheetLabel("합계 (점수 - 차감)",Color.White);scoreCaption.TextAlign=ContentAlignment.MiddleRight;StyleSummaryText(scoreCaption,9F);detailHead.Controls.Add(scoreCaption,1,0);summary.Controls.Add(detailHead,0,2);
            var players=SheetTable(2,4,Color.White);players.ColumnStyles[0].Width=43;players.ColumnStyles[1].Width=57;summary.Controls.Add(players,0,3);
            for(int p=0;p<4;p++){summaryNames[p]=SheetLabel("",NameColors[p]);summaryNames[p].TextAlign=ContentAlignment.MiddleLeft;summaryNames[p].Padding=new Padding(8,0,0,0);StyleSummaryText(summaryNames[p],10F);summaryNames[p].Margin=new Padding(0,1,0,1);players.Controls.Add(summaryNames[p],0,p);playerDetails[p]=SheetLabel("0 (0-0)",NameColors[p]);playerDetails[p].TextAlign=ContentAlignment.MiddleRight;playerDetails[p].Padding=new Padding(0,0,8,0);StyleSummaryText(playerDetails[p],10F);playerDetails[p].Margin=new Padding(0,1,0,1);players.Controls.Add(playerDetails[p],1,p);}
            var rankCard=Card(top,1,new Padding(7,0,7,0));var standings=SheetTable(3,ScoreRules.TeamCount+2,Color.White);standings.ColumnStyles.Clear();standings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,50));standings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));standings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,72));rankCard.Controls.Add(standings);standings.RowStyles.Clear();standings.RowStyles.Add(new RowStyle(SizeType.Absolute,26));standings.RowStyles.Add(new RowStyle(SizeType.Absolute,22));for(int r=0;r<ScoreRules.TeamCount;r++)standings.RowStyles.Add(new RowStyle(SizeType.Absolute,20));
            rankingTitle=Caption("등수");rankingTitle.Font=UiFont(11F,FontStyle.Bold);sheetTips.SetToolTip(rankingTitle,"팀을 두 번 클릭하면 해당 팀 기록으로 이동합니다.");AddCell(standings,rankingTitle,0,0,3,1);
            string[] headings={"순위","팀 이름","점수"};for(int c=0;c<3;c++){var heading=SheetLabel(headings[c],Color.White);heading.ForeColor=Muted;heading.Font=UiFont(9F,FontStyle.Bold);if(c==1){heading.TextAlign=ContentAlignment.MiddleLeft;heading.Padding=new Padding(8,0,0,0);}standings.Controls.Add(heading,c,1);}
            for(int r=0;r<ScoreRules.TeamCount;r++)for(int c=0;c<3;c++)
            {
                var label=SheetLabel("",r%2==0?Color.FromArgb(247,249,252):Color.White);label.Font=UiFont(9F,FontStyle.Bold);if(c==1){label.TextAlign=ContentAlignment.MiddleLeft;label.Padding=new Padding(8,0,0,0);}if(c==2){label.TextAlign=ContentAlignment.MiddleRight;label.Padding=new Padding(0,0,8,0);}label.Margin=new Padding(0,1,0,1);rankCells[r,c]=label;standings.Controls.Add(label,c,r+2);label.Cursor=Cursors.Hand;
                label.DoubleClick+=delegate(object sender,EventArgs e){var target=(Label)sender;if(target.Tag is int&&teams.Enabled)NavigateToTeam((int)target.Tag);};
            }
        }
        void StyleSummaryText(Label label,float size)
        {
            label.ForeColor=Color.Black;label.Font=SummaryFont(size);
        }
        void BuildSheetHeader()
        {
            roundHeader.Text="판";roundHeader.BackColor=Color.FromArgb(247,249,252);roundHeader.ForeColor=Muted;roundHeader.TextAlign=ContentAlignment.MiddleCenter;roundHeader.Font=UiFont(9F);sheetHeader.Controls.Add(roundHeader);
            chickenHeader=SheetTable(1,3,SheetPurple);chickenHeader.Dock=DockStyle.None;chickenHeader.Controls.Add(SheetLabel("치킨",SheetPurple),0,0);var chickenCaption=SheetLabel("합계",SheetPurple);chickenCaption.ForeColor=Muted;chickenCaption.Font=UiFont(8F);chickenHeader.Controls.Add(chickenCaption,0,1);StyleCell(chickenTotal,"0",SheetPurple);chickenTotal.Font=UiFont(16F,FontStyle.Bold);chickenTotal.ForeColor=Accent;chickenHeader.Controls.Add(chickenTotal,0,2);sheetHeader.Controls.Add(chickenHeader);
            for(int p=0;p<4;p++)
            {
                int player=p;var header=SheetTable(3,4,NameColors[p]);header.Dock=DockStyle.None;playerHeaders[p]=header;header.RowStyles.Clear();header.RowStyles.Add(new RowStyle(SizeType.Absolute,36));header.RowStyles.Add(new RowStyle(SizeType.Absolute,32));header.RowStyles.Add(new RowStyle(SizeType.Absolute,24));header.RowStyles.Add(new RowStyle(SizeType.Percent,100));
                var nameArea=SheetTable(2,1,NameColors[p]);nameArea.Margin=new Padding(5,6,5,4);nameArea.ColumnStyles.Clear();nameArea.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,27));nameArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
                var playerNumber=SheetLabel((p+1).ToString(),NameColors[p]);playerNumber.Font=UiFont(9F,FontStyle.Bold);playerNumber.ForeColor=PlayerInk[p];nameArea.Controls.Add(playerNumber,0,0);
                var name=new TextBox {Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,ReadOnly=true,TabStop=false,TextAlign=HorizontalAlignment.Center,BackColor=NameColors[p],ForeColor=Ink,Font=UiFont(9F,FontStyle.Bold),MaxLength=60,Margin=Padding.Empty,Tag=p,AccessibleName="플레이어 "+(p+1)+" 이름"};
                playerNames[p]=name;nameArea.Controls.Add(name,1,0);AddCell(header,nameArea,0,0,3,1);sheetTips.SetToolTip(name,"이름은 맨 앞 팀 설정에서 수정합니다.");
                playerTotals[p]=SheetLabel("0",NetColors[p]);playerTotals[p].Font=UiFont(15F,FontStyle.Bold);playerTotals[p].ForeColor=PlayerInk[p];AddCell(header,playerTotals[p],0,1,3,1);sheetTips.SetToolTip(playerTotals[p],"선수 합계 = 점수 - 탑텐 차감");
                var scoreHead=scoreInputLabels[p]=SheetLabel("점수",NameColors[p]);var damageHead=damageInputLabels[p]=SheetLabel("딜",NameColors[p]);var checkHead=SheetLabel("탑텐",NameColors[p]);scoreHead.ForeColor=damageHead.ForeColor=checkHead.ForeColor=PlayerInk[p];header.Controls.Add(scoreHead,0,2);header.Controls.Add(damageHead,1,2);header.Controls.Add(checkHead,2,2);
                grossTotals[p]=SheetLabel("0",NameColors[p]);deductionTotals[p]=SheetLabel("0",NameColors[p]);grossTotals[p].Font=deductionTotals[p].Font=UiFont(9F,FontStyle.Bold);AddCell(header,grossTotals[p],0,3,2,1);header.Controls.Add(deductionTotals[p],2,3);
                header.Paint+=delegate(object sender,PaintEventArgs e){using(var pen=new Pen(Color.White,2))e.Graphics.DrawLine(pen,header.Width-1,0,header.Width-1,header.Height);};sheetHeader.Controls.Add(header);
            }
            totalHeader=SheetTable(1,3,SheetPurple);totalHeader.Dock=DockStyle.None;totalHeader.Controls.Add(SheetLabel("총합",SheetPurple),0,0);var totalCaption=SheetLabel("전체 경기",SheetPurple);totalCaption.ForeColor=Muted;totalCaption.Font=UiFont(8F);totalHeader.Controls.Add(totalCaption,0,1);StyleCell(total,"0",SheetPurple);total.Font=UiFont(16F,FontStyle.Bold);total.ForeColor=Accent;totalHeader.Controls.Add(total,0,2);sheetHeader.Controls.Add(totalHeader);
        }
        void AlignSheetHeaders()
        {
            if(grid.Columns.Count<11||chickenHeader==null)return;
            // Widths remain valid during initial and hidden/resized grid layout.
            int x=grid.Columns[0].Width;roundHeader.SetBounds(0,0,x,sheetHeader.Height);
            int width=grid.Columns[1].Width;chickenHeader.SetBounds(x,0,width,sheetHeader.Height);x+=width;
            for(int p=0;p<4;p++)
            {
                bool split=grid.Columns["kills"+p].Visible;
                int a=grid.Columns[(split?"kills":"score")+p].Width,d=split?grid.Columns["damage"+p].Width:0,b=grid.Columns["check"+p].Width;
                playerHeaders[p].SetBounds(x,0,a+d+b,sheetHeader.Height);
                playerHeaders[p].ColumnStyles[0].SizeType=SizeType.Absolute;playerHeaders[p].ColumnStyles[0].Width=a;
                playerHeaders[p].ColumnStyles[1].SizeType=SizeType.Absolute;playerHeaders[p].ColumnStyles[1].Width=d;
                playerHeaders[p].ColumnStyles[2].SizeType=SizeType.Percent;playerHeaders[p].ColumnStyles[2].Width=100;x+=a+d+b;
            }
            totalHeader.SetBounds(x,0,grid.Columns[10].Width,sheetHeader.Height);
        }
        void RefreshScoreColumns()
        {
            bool split=view.Mode=="killdeal";
            if(splitScoreColumns==split)return;
            if(grid.CurrentCell!=null)
            {
                string column=grid.CurrentCell.OwningColumn.Name;
                if(split&&column.StartsWith("score",StringComparison.Ordinal)||!split&&(column.StartsWith("kills",StringComparison.Ordinal)||column.StartsWith("damage",StringComparison.Ordinal)))
                {grid.CancelEdit();grid.CurrentCell=null;}
            }
            grid.SuspendLayout();
            try
            {
                for(int p=0;p<4;p++)
                {
                    grid.Columns["score"+p].Visible=!split;grid.Columns["kills"+p].Visible=split;grid.Columns["damage"+p].Visible=split;
                    if(scoreInputLabels[p]!=null){scoreInputLabels[p].Text=split?"킬":"점수";damageInputLabels[p].Visible=split;sheetTips.SetToolTip(grossTotals[p],split?"매 경기 킬 + 딜 100당 1점의 합계 · 경기 간 딜 이월 없음":"선수 점수 합계");}
                }
                splitScoreColumns=split;
            }
            finally{grid.ResumeLayout(true);}
            AlignSheetHeaders();
        }
        void StyleSheetGrid()
        {
            grid.BorderStyle=BorderStyle.None;grid.Margin=Padding.Empty;grid.ColumnHeadersVisible=false;grid.RowTemplate.Height=28;grid.DefaultCellStyle.Font=UiFont(10F);grid.DefaultCellStyle.ForeColor=Ink;
            grid.GridColor=SheetLine;grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(233,226,250);grid.DefaultCellStyle.SelectionForeColor=Ink;grid.CellBorderStyle=DataGridViewCellBorderStyle.Single;grid.Columns[0].Visible=true;grid.AllowUserToResizeColumns=false;
            grid.Columns[0].AutoSizeMode=DataGridViewAutoSizeColumnMode.None;grid.Columns[0].Width=44;grid.Columns[0].DefaultCellStyle.ForeColor=Muted;grid.Columns[0].DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter;grid.Columns[0].DefaultCellStyle.Padding=Padding.Empty;
            for(int c=1;c<=10;c++){grid.Columns[c].FillWeight=100;grid.Columns[c].MinimumWidth=46;}
            for(int p=0;p<4;p++)foreach(string field in new[]{"kills","damage"}){grid.Columns[field+p].FillWeight=50;grid.Columns[field+p].MinimumWidth=23;}
            grid.Columns[10].DefaultCellStyle.Font=UiFont(10F,FontStyle.Bold);grid.Columns[10].DefaultCellStyle.ForeColor=Accent;
            grid.CellFormatting+=delegate(object sender,DataGridViewCellFormattingEventArgs e){if(e.RowIndex>=0)e.CellStyle.BackColor=e.ColumnIndex==10?SheetPurple:e.ColumnIndex==0?Color.FromArgb(247,249,252):e.RowIndex%2==0?Color.White:Color.FromArgb(250,251,253);};
            grid.CellPainting+=delegate(object sender,DataGridViewCellPaintingEventArgs e)
            {
                if(e.RowIndex<0||e.ColumnIndex<0)return;
                bool selected=(e.State&DataGridViewElementStates.Selected)!=0;
                if(grid.Columns[e.ColumnIndex] is DataGridViewCheckBoxColumn)
                {
                    e.PaintBackground(e.ClipBounds,true);e.Paint(e.ClipBounds,DataGridViewPaintParts.Border|DataGridViewPaintParts.ErrorIcon);
                    int size=Math.Max(12,Math.Min(16,e.CellBounds.Height-10));var box=new Rectangle(e.CellBounds.X+(e.CellBounds.Width-size)/2,e.CellBounds.Y+(e.CellBounds.Height-size)/2,size,size);bool check=e.Value is bool&&(bool)e.Value;
                    using(var brush=new SolidBrush(check?Accent:Color.White))e.Graphics.FillRectangle(brush,box);
                    using(var pen=new Pen(check?Accent:Color.FromArgb(178,185,200),1.4F))e.Graphics.DrawRectangle(pen,box);
                    if(check)using(var pen=new Pen(Color.White,2))e.Graphics.DrawLines(pen,new[]{new Point(box.Left+3,box.Top+size/2),new Point(box.Left+size/2-1,box.Bottom-4),new Point(box.Right-3,box.Top+4)});
                    e.Handled=true;
                }
                if(selected)
                {
                    if(!e.Handled){e.Paint(e.ClipBounds,DataGridViewPaintParts.All);e.Handled=true;}
                    using(var pen=new Pen(Accent,2))e.Graphics.DrawRectangle(pen,e.CellBounds.X+1,e.CellBounds.Y+1,e.CellBounds.Width-3,e.CellBounds.Height-3);
                }
            };
        }
        static Color Tint(Color color,int whiteParts)
        {
            int divisor=whiteParts+1;return Color.FromArgb((color.R+255*whiteParts)/divisor,(color.G+255*whiteParts)/divisor,(color.B+255*whiteParts)/divisor);
        }
        void RenderSheetSummary(TeamSummary score)
        {
            Color teamColor=TeamColors[team];string leader=state.Get(TeamKey+".p0.name","");teamBadge.Text=String.IsNullOrWhiteSpace(leader)?"":leader;teamBadge.BackColor=Tint(teamColor,3);teamBadge.ForeColor=Ink;sheetTips.SetToolTip(teamBadge,teamBadge.Text);
            summaryTotal.ForeColor=Color.Black;roundCountLabel.Text="총 "+state.RoundCount+"판 · "+(CanEdit?"Enter / 칸 이동 시 저장":"조회 전용")+"";
            for(int p=0;p<4;p++)
            {
                string key=TeamKey+".p"+p+".name";playerNames[p].Text=drafts.ContainsKey(key)?drafts[key]:state.Get(key,"");
                playerTotals[p].Text=F(score.PlayerTotals[p]);grossTotals[p].Text=F(score.Raw[p]);deductionTotals[p].Text=F(-score.Deductions[p]);playerDetails[p].Text=F(score.PlayerTotals[p])+" ("+F(score.Raw[p])+"-"+F(score.Deductions[p])+")";
                string name=state.Get(key,"").Trim();string display=(p+1)+"티어"+(name.Length>0?"   "+name:"");summaryNames[p].Text=deductionNames[p].Text=display;deductionValues[p].Text=F(-state.GetDecimal(TeamKey+".p"+p+".penalty",0));
                sheetTips.SetToolTip(summaryNames[p],display);sheetTips.SetToolTip(deductionNames[p],display);
            }
            total.Text=summaryTotal.Text=F(score.Total);chickenTotal.Text=summaryChicken.Text=F(score.ChickenSum);
            RenderBuddyStandings();
            for(int i=0;i<ScoreRules.TeamCount;i++)
            {
                Color color=TeamColors[i];var tab=teamTabs[i];tab.Text=state.Get("t"+i+".name",(i+1)+"팀");tab.BackColor=Tint(color,8);tab.ForeColor=Ink;tab.FlatAppearance.BorderColor=!showingSetup&&i==team?Ink:Tint(color,3);tab.FlatAppearance.BorderSize=!showingSetup&&i==team?2:1;sheetTips.SetToolTip(tab,(i+1)+"팀 · "+tab.Text);
            }
        }
    }
    internal sealed class WrapToolbar : FlowLayoutPanel
    {
        public Action<int> RequiredHeightChanged;
        bool heightQueued;
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            // Resize the ancestor after its current layout pass has finished.
            if(!IsHandleCreated||heightQueued||RequiredHeightChanged==null)return;
            heightQueued=true;
            BeginInvoke((MethodInvoker)delegate {
                heightQueued=false;if(IsDisposed)return;
                int bottom=Controls.Cast<Control>().Where(c=>c.Visible).Select(c=>c.Bottom).DefaultIfEmpty(54).Max();
                RequiredHeightChanged(Math.Max(72,bottom+12));
            });
        }
    }
    internal sealed class SurfacePanel : Panel
    {
        public SurfacePanel(){DoubleBuffered=true;BackColor=Color.White;ResizeRedraw=true;}
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent==null?Color.White:Parent.BackColor);
            if(Width<20||Height<20)return;
            using(var shape=new GraphicsPath())
            {
                int d=18;shape.AddArc(0,0,d,d,180,90);shape.AddArc(Width-d-1,0,d,d,270,90);shape.AddArc(Width-d-1,Height-d-1,d,d,0,90);shape.AddArc(0,Height-d-1,d,d,90,90);shape.CloseFigure();
                var smoothing=e.Graphics.SmoothingMode;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                using(var brush=new SolidBrush(BackColor))e.Graphics.FillPath(brush,shape);using(var pen=new Pen(Color.FromArgb(224,229,238)))e.Graphics.DrawPath(pen,shape);e.Graphics.SmoothingMode=smoothing;
            }
        }
    }
    internal sealed class SheetGrid : DataGridView
    {
        public SheetGrid(){DoubleBuffered=true;}
    }
}
