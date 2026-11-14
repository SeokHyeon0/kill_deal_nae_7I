using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PubgScore
{
    public sealed partial class MainForm : Form
    {
        IGameClient backend;readonly bool adminMode;GameView view=new GameView();ScoreState state=new ScoreState();
        readonly System.Threading.SemaphoreSlim streamSlot=new System.Threading.SemaphoreSlim(1,1);
        // Controls with identical typography share one native font for this window.
        readonly Dictionary<string,Font> uiFonts=new Dictionary<string,Font>(StringComparer.Ordinal);
        BundledFontResource summaryFont;bool summaryFontUnavailable;
        bool uiResourcesDisposed;
        Font UiFont(float size,FontStyle style=FontStyle.Regular)
        {
            string key=size.ToString("R",CultureInfo.InvariantCulture)+"|"+((int)style).ToString(CultureInfo.InvariantCulture);
            Font value;
            if(!uiFonts.TryGetValue(key,out value)){value=new Font("Malgun Gothic",size,style,GraphicsUnit.Point);uiFonts.Add(key,value);}
            return value;
        }
        Font SummaryFont(float size)
        {
            if(summaryFontUnavailable)return UiFont(size,FontStyle.Regular);
            try
            {
                if(summaryFont==null)summaryFont=new BundledFontResource(BundledFontResource.SummaryFont);
                string key="summary|"+size.ToString("R",CultureInfo.InvariantCulture);Font value;
                if(!uiFonts.TryGetValue(key,out value)){value=new Font(summaryFont.Family,size,FontStyle.Regular,GraphicsUnit.Point);uiFonts.Add(key,value);}
                return value;
            }
            catch{summaryFontUnavailable=true;return UiFont(size,FontStyle.Regular);}
        }
        readonly Color[] colors={Color.FromArgb(255,215,220),Color.FromArgb(255,228,199),Color.FromArgb(252,242,182),Color.FromArgb(209,239,222)};
        readonly ComboBox teams=new ComboBox();readonly Label connection=new ConnectionStatusLabel(),total=new Label(),notice=new StatusNotice();
        readonly Label[] playerTotals=new Label[4],playerDetails=new Label[4];readonly TextBox[] playerNames=new TextBox[4];readonly DataGridView grid=new SheetGrid();
        readonly Button configButton=new DesignButton(),connectButton=new DesignButton(),resetButton=new DesignButton(),endButton=new DesignButton(),rosterButton=new DesignButton(),loginButton=new DesignButton(),logoutButton=new DesignButton();
        readonly Timer timer=new Timer();readonly Dictionary<string,int> editVersions=new Dictionary<string,int>();readonly Dictionary<string,long> editGenerations=new Dictionary<string,long>();readonly Dictionary<string,long> editSessions=new Dictionary<string,long>();readonly HashSet<string> pending=new HashSet<string>();readonly Dictionary<string,string> drafts=new Dictionary<string,string>();
        readonly Dictionary<string,string> scoreClearPreview=new Dictionary<string,string>();
        string saveStatus="";
        bool refreshing,closing,hostBusy,loginPromptOpen,viewportRenderQueued,deletingScoreSelection;int team,editEpoch;long lastSequence=-1;
        internal string ProgramDisplayName {get{return adminMode?"킬딜내기_BJ":"킬딜내기_시청자";}}
        bool CanEdit {get{return !closing&&!hostBusy&&backend!=null&&CurrentStatus().CanEdit;}}
        long CurrentGeneration {get{return backend==null?-1:CurrentStatus().Generation;}}
        long CurrentSession {get{return backend==null?-1:CurrentStatus().Session;}}
        public MainForm(bool administrator):this(administrator,null){}
        internal MainForm(bool administrator,IGameClient client)
        {
            adminMode=administrator;backend=client;Text=ProgramDisplayName;StartPosition=FormStartPosition.CenterScreen;Font=UiFont(9F);BackColor=PageColor;MinimumSize=new Size(960,760);Size=new Size(1240,920);AutoScaleMode=AutoScaleMode.Dpi;
            BuildUI();if(client==null||client is FirebaseGameClient)BuildRoomNavigation();AttachBackgroundFocusHandlers(this);RefreshView();if(client==null)Shown+=async delegate {await InitializeAuthentication();};timer.Interval=150;timer.Tick+=delegate {if(backend is FirebaseGameClient)timer.Stop();RefreshView();RefreshRooms();};BindUpdates();FormClosing+=OnClosing;if(client==null||client is FirebaseGameClient)InitializeWindowPreferences();
        }
        void RefreshView()
        {
            if(closing)return;var next=backend==null?new GameView {Info=adminMode?"BJ 자동 인증 대기":"자동 인증 및 공용 데이터 연결 대기"}:NextView();
            if(lastSequence==next.Sequence&&view.CanEdit==next.CanEdit&&view.Connected==next.Connected&&view.Info==next.Info&&view.Mode==next.Mode)return;
            bool lostAuthentication=view.Authenticated&&!next.Authenticated;
            bool modeChanged=view.Mode!=next.Mode;
            bool invalidate=modeChanged||(view.CanEdit&&!next.CanEdit)||view.RoomId!=next.RoomId||view.Generation!=next.Generation||view.Session!=next.Session||view.Connected&&!next.Connected;
            view=next;lastSequence=next.Sequence;
            if(next.State!=null){if(next.FullState){state=next.State;InvalidateSummaries();}else foreach(var change in next.State.Cells){if(change.Value==null)state.Cells.Remove(change.Key);else state.Cells[change.Key]=change.Value;InvalidateSummary(change.Key);}}
            
            if(modeChanged)InvalidateSummaries();
            if(invalidate){editEpoch++;DiscardLocalEdits();saveStatus="";}Render();
            if(adminMode&&lostAuthentication&&backend!=null&&!hostBusy&&!loginPromptOpen&&IsHandleCreated)BeginInvoke((MethodInvoker)delegate {PromptAdminLogin();});
        }
        void DiscardLocalEdits()
        {
            bool previous=refreshing;refreshing=true;
            try{grid.CancelEdit();grid.EndEdit();CollapseScoreSelection();scoreClearPreview.Clear();drafts.Clear();pending.Clear();setupDirty.Clear();editVersions.Clear();editGenerations.Clear();editSessions.Clear();foreach(var input in setupInputs.Values){setupErrors.SetError(input,"");input.Text=SetupStoredValue((string)input.Tag);}}
            finally{refreshing=previous;}
        }
        string TeamKey { get { return "t" + team.ToString(CultureInfo.InvariantCulture); } }
        Label LabelText(string text, int size, bool bold)
        {
            return new Label { Text=text, AutoSize=true, Font=UiFont(size,bold?FontStyle.Bold:FontStyle.Regular), ForeColor=Color.FromArgb(34,45,63), Margin=new Padding(0,0,0,5) };
        }
        Button ButtonText(string text, bool primary)
        {
            var b = new Button { Text=text, AutoSize=true, AutoSizeMode=AutoSizeMode.GrowAndShrink, Height=35, Padding=new Padding(12,6,12,6), Margin=new Padding(0,0,8,0), FlatStyle=FlatStyle.Flat, BackColor=primary?Color.FromArgb(47,69,106):Color.White, ForeColor=primary?Color.White:Color.FromArgb(47,59,78) };
            b.FlatAppearance.BorderColor=Color.FromArgb(205,212,223);
            return b;
        }
        void CopyButtonStyle(Button b,string text,bool primary)
        {
            b.Text=text;b.AutoSize=true;b.Padding=new Padding(12,6,12,6);b.Margin=new Padding(0,0,8,0);b.FlatStyle=FlatStyle.Flat;b.BackColor=primary?Color.FromArgb(47,69,106):Color.White;b.ForeColor=primary?Color.White:Color.FromArgb(47,59,78);b.FlatAppearance.BorderColor=Color.FromArgb(205,212,223);
        }
        void ConfigureGrid()
        {
            grid.Dock=DockStyle.Fill;grid.BackgroundColor=Color.White;grid.BorderStyle=BorderStyle.FixedSingle;grid.AllowUserToAddRows=false;grid.AllowUserToDeleteRows=false;grid.AllowUserToResizeRows=false;grid.RowHeadersVisible=false;grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;grid.ColumnHeadersHeight=45;grid.RowTemplate.Height=31;grid.SelectionMode=DataGridViewSelectionMode.CellSelect;grid.MultiSelect=true;grid.EnableHeadersVisualStyles=false;grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(232,237,244);grid.ColumnHeadersDefaultCellStyle.ForeColor=Color.FromArgb(48,58,73);grid.ColumnHeadersDefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter;grid.DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter;grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(70,96,141);grid.DefaultCellStyle.SelectionForeColor=Color.White;grid.GridColor=Color.FromArgb(218,224,233);
            grid.Columns.Add(new DataGridViewTextBoxColumn {Name="round",HeaderText="판",ReadOnly=true,FillWeight=38,MinimumWidth=35,SortMode=DataGridViewColumnSortMode.NotSortable});
            var chicken=new DataGridViewComboBoxColumn {Name="chicken",Tag="chicken",HeaderText="치킨 / 순위",FillWeight=86,MinimumWidth=75,FlatStyle=FlatStyle.Flat,DisplayStyle=DataGridViewComboBoxDisplayStyle.DropDownButton,SortMode=DataGridViewColumnSortMode.NotSortable};
            foreach(string v in new[]{"","10","6","5","4","3","2","1","-1","-2","-3","-4","-5","800","500","300","8","0"})chicken.Items.Add(v);
            chicken.DefaultCellStyle.BackColor=Color.FromArgb(239,228,249);chicken.HeaderCell.Style.BackColor=Color.FromArgb(225,210,237);grid.Columns.Add(chicken);
            for(int p=0;p<4;p++)
            {
                grid.Columns.Add(new DataGridViewTextBoxColumn {Name="score"+p,Tag="p"+p+".score",HeaderText="P"+(p+1)+" 점수",FillWeight=81,MinimumWidth=62,SortMode=DataGridViewColumnSortMode.NotSortable});
                var check=new DataGridViewCheckBoxColumn {Name="check"+p,Tag="p"+p+".check",HeaderText="탑텐",FillWeight=48,MinimumWidth=43,SortMode=DataGridViewColumnSortMode.NotSortable};grid.Columns.Add(check);
                grid.Columns[2+p*2].HeaderCell.Style.BackColor=colors[p];grid.Columns[3+p*2].HeaderCell.Style.BackColor=colors[p];
            }
            grid.Columns.Add(new DataGridViewTextBoxColumn {Name="sum",HeaderText="총합",ReadOnly=true,FillWeight=83,MinimumWidth=65,SortMode=DataGridViewColumnSortMode.NotSortable});grid.Columns[10].DefaultCellStyle.BackColor=Color.FromArgb(236,244,215);grid.Columns[10].DefaultCellStyle.Font=UiFont(10F,FontStyle.Bold);
            // Append the new fields so the original column indices remain stable.
            for(int p=0;p<4;p++)foreach(string field in new[]{"kills","damage"})
                grid.Columns.Add(new DataGridViewTextBoxColumn {Name=field+p,Tag="p"+p+"."+field,HeaderText="P"+(p+1)+(field=="kills"?" 킬":" 딜"),Visible=false,FillWeight=50,MinimumWidth=23,SortMode=DataGridViewColumnSortMode.NotSortable});
            int displayIndex=2;
            for(int p=0;p<4;p++)foreach(string field in new[]{"score","kills","damage","check"})grid.Columns[field+p].DisplayIndex=displayIndex++;
            grid.Columns["sum"].DisplayIndex=displayIndex;
            StyleSheetGrid();grid.Scroll+=delegate {QueueViewportRender();};
            grid.SizeChanged+=delegate {QueueViewportRender();};
            grid.CellBeginEdit+=delegate(object s,DataGridViewCellCancelEventArgs e){string key=CellKey(e.RowIndex,e.ColumnIndex);if(key!=null){if(!CanEdit||pending.Contains(key)){e.Cancel=true;return;}editVersions[key]=state.Version(key);editGenerations[key]=CurrentGeneration;editSessions[key]=CurrentSession;grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Tag=key;}};
            grid.CellValidating+=delegate(object s,DataGridViewCellValidatingEventArgs e){if(refreshing||e.RowIndex<0||e.ColumnIndex<0||!(grid.Columns[e.ColumnIndex] is DataGridViewTextBoxColumn)||grid.Columns[e.ColumnIndex].Tag==null)return;decimal number;string text=Convert.ToString(e.FormattedValue,CultureInfo.InvariantCulture);try{if(text.Length>0){if(!decimal.TryParse(text,NumberStyles.Number,CultureInfo.CurrentCulture,out number))throw new ArgumentException("숫자를 입력하세요.");ScoreRules.Normalize(CellKey(e.RowIndex,e.ColumnIndex),number.ToString(CultureInfo.InvariantCulture));}grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText="";}catch(ArgumentException error){grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText=error.Message;e.Cancel=true;}};
            grid.CellEndEdit+=async delegate(object s,DataGridViewCellEventArgs e){if(refreshing||!CanEdit)return;var cell=grid.Rows[e.RowIndex].Cells[e.ColumnIndex];string key=cell.Tag as string??CellKey(e.RowIndex,e.ColumnIndex);if(key==null)return;string text=ValueFromCell(e.RowIndex,e.ColumnIndex);if(text==state.Get(key,""))return;await Save(key,text,editVersions.ContainsKey(key)?editVersions[key]:state.Version(key));};
            grid.CurrentCellDirtyStateChanged+=delegate {if(grid.IsCurrentCellDirty&&grid.CurrentCell is DataGridViewCheckBoxCell)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
            grid.EditingControlShowing+=delegate(object sender,DataGridViewEditingControlShowingEventArgs e){var combo=e.Control as ComboBox;if(combo!=null){ConfigureChickenEditor(combo);combo.SelectionChangeCommitted-=ChickenSelectionCommitted;combo.SelectionChangeCommitted+=ChickenSelectionCommitted;}};
            grid.CellValueChanged+=async delegate(object s,DataGridViewCellEventArgs e){if(refreshing||!CanEdit||e.RowIndex<0||e.ColumnIndex<0)return;if(grid.Columns[e.ColumnIndex].Name!="chicken"&&!(grid.Columns[e.ColumnIndex] is DataGridViewCheckBoxColumn))return;string key=CellKey(e.RowIndex,e.ColumnIndex);if(key==null||pending.Contains(key))return;await Save(key,ValueFromCell(e.RowIndex,e.ColumnIndex),editVersions.ContainsKey(key)?editVersions[key]:state.Version(key));};
            grid.KeyDown+=delegate(object sender,KeyEventArgs e)
            {
                if(e.KeyData==Keys.Delete&&DeleteSelectedCells()){e.SuppressKeyPress=true;e.Handled=true;return;}
                if(e.KeyCode!=Keys.Escape||grid.CurrentCell==null)return;
                string key=CellKey(grid.CurrentCell.RowIndex,grid.CurrentCell.ColumnIndex);
                if(key==null||pending.Contains(key))return;
                refreshing=true;try { grid.CancelEdit();grid.EndEdit();drafts.Remove(key);grid.CurrentCell.ErrorText=""; } finally { refreshing=false; }
                Render();e.SuppressKeyPress=true;e.Handled=true;
            };
            grid.DataError+=delegate(object s,DataGridViewDataErrorEventArgs e){e.ThrowException=false;};
        }
        void ChickenSelectionCommitted(object sender,EventArgs e){if(!refreshing&&CanEdit&&grid.CurrentCell is DataGridViewComboBoxCell)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);}
        bool DeleteSelectedCells()
        {
            if(!grid.ContainsFocus||grid.CurrentCell==null||grid.IsCurrentCellInEditMode)return false;
            if(!CanEdit||deletingScoreSelection)return true;

            bool isRange=grid.SelectedCells.Count>1;
            var changes=new List<ChangeRequest>();
            foreach(var cell in grid.SelectedCells.Cast<DataGridViewCell>().OrderBy(cell=>cell.RowIndex).ThenBy(cell=>cell.OwningColumn.DisplayIndex))
            {
                // A rectangular selection can span chicken and top-ten fields between scores.
                if(!cell.Visible||cell.ReadOnly||(isRange&&!(cell is DataGridViewTextBoxCell)))continue;
                string key=CellKey(cell.RowIndex,cell.ColumnIndex);
                if(key==null||pending.Contains(key))continue;
                string cleared=cell is DataGridViewCheckBoxCell?"0":"";
                cell.ErrorText="";
                bool hasDraft=drafts.ContainsKey(key);
                if(!hasDraft&&state.Get(key,cleared)==cleared)continue;
                int version;
                if(!hasDraft||!editVersions.TryGetValue(key,out version))version=state.Version(key);
                changes.Add(new ChangeRequest {Key=key,Value=cleared,Version=version});
            }
            if(changes.Count>0)SaveClearedCells(changes);
            return true;
        }
        async void SaveClearedCell(string key,string value,int version) {await Save(key,value,version);}
        async void SaveClearedCells(List<ChangeRequest> changes)
        {
            var capturedView=view;
            var capturedClient=backend;
            int epoch=editEpoch;
            deletingScoreSelection=true;
            try
            {
                // Preview the entire range before the first write; scores still use confirmed values.
                foreach(var change in changes)
                {
                    scoreClearPreview.Add(change.Key,change.Value);
                    drafts.Remove(change.Key);
                }
                Render();
                foreach(var change in changes)
                {
                    if(epoch!=editEpoch||!ReferenceEquals(backend,capturedClient))break;
                    if(!await SaveWithContext(change.Key,change.Value,change.Version,capturedView))break;
                    scoreClearPreview.Remove(change.Key);
                }
            }
            finally
            {
                scoreClearPreview.Clear();
                deletingScoreSelection=false;
                if(!closing)Render();
            }
        }
        void CollapseScoreSelection()
        {
            if(grid.SelectedCells.Count<=1)return;
            grid.ClearSelection();
            if(grid.CurrentCell!=null)grid.CurrentCell.Selected=true;
        }
        string CellKey(int row,int col)
        {
            if(row<0||col<0||col>=grid.Columns.Count)return null;
            string suffix=grid.Columns[col].Tag as string;
            return suffix==null?null:TeamKey+".r"+(row+1)+"."+suffix;
        }
        string ValueFromCell(int row,int col)
        {
            object value=grid.Rows[row].Cells[col].Value;
            if(grid.Columns[col] is DataGridViewCheckBoxColumn)return value is bool&&(bool)value?"1":"0";
            string s=Convert.ToString(value,CultureInfo.InvariantCulture);decimal number;
            if(s.Length>0&&decimal.TryParse(s,NumberStyles.Number,CultureInfo.CurrentCulture,out number))return number.ToString(CultureInfo.InvariantCulture);return s;
        }
        static string F(decimal number) {return number.ToString("0.##",CultureInfo.CurrentCulture);}
        void QueueViewportRender()
        {
            if(closing||refreshing||showingSetup||!IsHandleCreated||!grid.Visible||viewportRenderQueued)return;
            viewportRenderQueued=true;
            // Scroll/layout events arrive before DataGridView updates its displayed-row flags.
            try{BeginInvoke((MethodInvoker)delegate {viewportRenderQueued=false;if(!closing&&!showingSetup&&grid.Visible)Render();});}
            catch(InvalidOperationException){viewportRenderQueued=false;}
        }
        void Render()
        {
            UiPasses++;if(closing)return;if(WindowState==FormWindowState.Minimized){visualPending=true;return;}refreshing=true;
            try
            {
                Text=ProgramDisplayName;roomBrand.Text=view.RoomName.TrimEnd().EndsWith("킬내기",StringComparison.Ordinal)?view.RoomName:(view.RoomName.Length>0?view.RoomName+" ":"")+"킬내기";sheetTips.SetToolTip(roomBrand,roomBrand.Text);
                RefreshScoreColumns();
                var score=showingSetup?null:Summary(team);
                for(int i=0;i<ScoreRules.TeamCount;i++)teams.Items[i]=state.Get("t"+i+".name",(i+1)+"팀");
                if(!showingSetup)RenderSheetSummary(score);
                RenderModeSettings();
                RenderGameModeButtons();
                if(showingSetup)RenderSetupInputs();
                else {setupTab.Enabled=!hostBusy;setupTab.BackColor=Color.White;setupTab.ForeColor=Ink;setupTab.FlatAppearance.BorderColor=SheetLine;setupTab.FlatAppearance.BorderSize=1;}
                RenderNavigationAppearance();
                if(!showingSetup){RefreshPlacementOptions();
                while(grid.Rows.Count<state.RoundCount)grid.Rows.Add();
                while(grid.Rows.Count>state.RoundCount)grid.Rows.RemoveAt(grid.Rows.Count-1);
                for(int r=grid.Rows.GetFirstRow(DataGridViewElementStates.Displayed);r>=0;r=grid.Rows.GetNextRow(r,DataGridViewElementStates.Displayed))
                {
                    var row=grid.Rows[r];SetCell(row.Cells[0],(r+1).ToString(CultureInfo.InvariantCulture));
                    foreach(DataGridViewColumn column in grid.Columns)
                    {
                        if(!column.Visible||column.Tag==null)continue;
                        int c=column.Index;string key=CellKey(r,c);var cell=row.Cells[c];
                        if(grid.CurrentCell==cell&&grid.IsCurrentCellInEditMode&&(cell.Tag as string)==key)continue;
                        string v;
                        if(!scoreClearPreview.TryGetValue(key,out v)&&!drafts.TryGetValue(key,out v))v=state.Get(key,"");
                        if(column.Name=="chicken")KeepStoredChickenVisible((DataGridViewComboBoxCell)cell,v);if(column is DataGridViewCheckBoxColumn)SetCell(cell,v=="1");else SetCell(cell,v);
                    }
                    SetCell(row.Cells[10],F(score.RoundTotals[r]));
                }
                }
                grid.ReadOnly=!view.CanEdit||deletingScoreSelection;grid.Enabled=!hostBusy;teams.Enabled=!hostBusy;configButton.Enabled=!hostBusy;
                foreach(var tab in teamTabs)tab.Enabled=teams.Enabled;
                connectButton.Enabled=!hostBusy;
                bool canControl=!hostBusy&&view.Connected&&view.IsAdmin;resetButton.Enabled=canControl;
                endButton.Enabled=canControl&&(view.Running||view.RoomStatus=="waiting");
                // The room owner can reopen team entry whenever the room is connected.
                bool rosterPhase=view.RoomStatus=="waiting"||view.Running;
                rosterButton.Visible=adminMode&&backend is FirebaseGameClient&&view.IsAdmin&&rosterPhase;
                rosterButton.Enabled=CanOpenRoster(view)&&!roomDialogOpen;
                loginButton.Visible=adminMode&&!view.Authenticated;logoutButton.Visible=adminMode&&view.Authenticated;loginButton.Enabled=logoutButton.Enabled=!hostBusy&&pending.Count==0;
                connection.Text=(view.Authenticated?"인증됨":"미인증")+" · "+(view.Connected?"연결됨":"미접속")+" · "+(view.CanEdit?"입력 가능":"조회 전용");
                sheetTips.SetToolTip(connection,connection.Text);
                string noticeInfo=view.Info=="입력 가능"?"":view.Info??"";
                notice.Text=pending.Count>0?"저장 중…":drafts.Count>0?"저장 실패한 칸을 확인해 다시 입력하거나, 해당 칸에서 Esc로 취소하세요.":noticeInfo+(saveStatus.Length>0?(noticeInfo.Length>0?" · ":"")+saveStatus:"");

            }
            finally {refreshing=false;}
        }
        Task<bool> Save(string key,string value,int version) {return SaveWithContext(key,value,version,null);}
        async Task<bool> SaveWithContext(string key,string value,int version,GameView expected)
        {
            if(!CanEdit||pending.Contains(key))return false;
            long session,gameSession;
            if(expected!=null)
            {
                var current=CurrentStatus();
                if(!current.CanEdit||current.Uid!=expected.Uid||current.RoomId!=expected.RoomId||current.Navigation!=expected.Navigation||
                    current.Generation!=expected.Generation||current.Session!=expected.Session||
                    current.Mode!=expected.Mode||current.ModeVersion!=expected.ModeVersion)return false;
                session=expected.Generation;
                gameSession=expected.Session;
            }
            else
            {
                if(!editSessions.TryGetValue(key,out gameSession))gameSession=CurrentSession;
                if(!editGenerations.TryGetValue(key,out session))session=CurrentGeneration;
            }
            value=ScoreRules.Normalize(key,value);if(value==state.Get(key,key.EndsWith(".check")?"0":"")){drafts.Remove(key);return true;}
            if(session!=CurrentGeneration||gameSession!=CurrentSession){DiscardLocalEdits();RefreshView();return false;}
            int epoch=editEpoch;saveStatus="";pending.Add(key);
            if(expected==null)drafts[key]=value;
            Render();
            try
            {
                var result=await backend.Write(key,value,version,session,gameSession);
                if(epoch!=editEpoch||!CanEdit||CurrentGeneration!=session||CurrentSession!=gameSession)return false;
                // SSE is authoritative; a successful save is never replayed automatically.
                if(state.Version(key)<result.Version||(state.Version(key)==result.Version&&state.Get(key,"")!=result.Value)){state.Cells[key]=result;InvalidateSummary(key);}editVersions[key]=result.Version;drafts.Remove(key);saveStatus="저장 완료 · "+DateTime.Now.ToString("HH:mm:ss");return true;
            }
            catch(Exception ex)
            {
                if(expected!=null)scoreClearPreview.Clear();
                if(epoch==editEpoch&&CurrentGeneration==session&&CurrentSession==gameSession&&CanEdit)
                {
                    saveStatus="저장 실패 · 서버 반영을 확인하지 못했습니다";
                    if(ex is ConflictException)editVersions[key]=((ConflictException)ex).LatestVersion;
                    if(expected!=null)Render();
                    DesignMessageBox.Show(this,ex.Message+"\n자동 재전송하지 않습니다. 최신 값을 확인하세요.","저장 실패",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                }
                return false;
            }
            finally{if(epoch==editEpoch)pending.Remove(key);RefreshView();if(!closing)Render();}
        }
        async Task Connect()
        {
            if(hostBusy)return;hostBusy=true;Render();
            try{if(backend==null)backend=new FirebaseGameClient(FirebaseConfig.Load(),streamSlot);BindUpdates();await backend.ConnectAnonymous();ShowSetupPage();}
            catch(Exception ex){DesignMessageBox.Show(this,ex.Message,"접속 확인");}
            finally{hostBusy=false;RefreshView();Render();}
        }
        async Task InitializeAuthentication()
        {
            hostBusy=true;Render();
            try{if(backend==null)backend=new FirebaseGameClient(FirebaseConfig.Load(),streamSlot);BindUpdates();await backend.AutoLogin(adminMode);}
            catch(Exception ex){if(!adminMode)DesignMessageBox.Show(this,ex.Message,"자동 인증 확인");}
            finally{hostBusy=false;RefreshView();Render();}
            if(adminMode&&!view.Authenticated)PromptAdminLogin();
        }
        void PromptAdminLogin()
        {
            if(!adminMode||hostBusy||loginPromptOpen||closing)return;
            loginPromptOpen=true;
            try{if(backend!=null)backend.Dispose();backend=new FirebaseGameClient(FirebaseConfig.Load(),streamSlot);BindUpdates();bool loggedIn;using(var dialog=new AdminLoginDialog(backend))loggedIn=dialog.ShowDialog(this)==DialogResult.OK;
                if(loggedIn){RefreshView();DesignMessageBox.Show(this,"로그인 완료","로그인 완료",MessageBoxButtons.OK,MessageBoxIcon.Information);}}
            catch(Exception ex){DesignMessageBox.Show(this,ex.Message,"로그인 확인");}
            finally{RefreshView();loginPromptOpen=false;}
        }
        void LogoutAdministrator()
        {
            if(hostBusy||pending.Count>0||backend==null)return;
            try{backend.Logout();backend=null;BindUpdates();editEpoch++;DiscardLocalEdits();lastSequence=-1;RefreshView();}
            catch(Exception ex){DesignMessageBox.Show(this,ex.Message,"로그아웃 확인");}
        }
        async Task EndGame()
        {
            if(hostBusy||backend==null)return;if(backend is FirebaseGameClient&&DesignMessageBox.Show(this,"게임을 종료하면 이 방의 모든 기록이 즉시 삭제됩니다. 종료하시겠습니까?","게임 종료 · 방 삭제",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;hostBusy=true;editEpoch++;DiscardLocalEdits();Render();
            try{await backend.SetRunning(false);saveStatus="방 삭제 완료";}
            catch(Exception ex){saveStatus="게임 상태 저장 실패";DesignMessageBox.Show(this,ex.Message,"게임 상태 확인");}
            finally{hostBusy=false;RefreshView();Render();}
        }
        bool CanOpenRoster(GameView current)
        {return adminMode&&!closing&&!hostBusy&&backend is FirebaseGameClient&&current.Connected&&current.IsAdmin&&(current.RoomStatus=="waiting"||current.Running);}
        void OpenRosterEditor()
        {
            var client=backend as FirebaseGameClient;if(client==null||roomDialogOpen)return;
            var opened=client.Status();if(!CanOpenRoster(opened))return;
            roomDialogOpen=true;Render();
            try{var attempt=client.BeginRosterSave();using(var dialog=new RosterEntryDialog(async names=>{
                hostBusy=true;editEpoch++;DiscardLocalEdits();Render();
                try{await client.SaveRoster(names,attempt);}
                finally{hostBusy=false;RefreshView();Render();}
            },Icon))
                if(dialog.ShowDialog(this)==DialogResult.OK)saveStatus="새 팀 적용 완료 · 상단에서 종목을 선택하세요";
            }finally{roomDialogOpen=false;RefreshView();Render();}
        }
        async Task ConfirmReset()
        {
            if(backend==null||!adminMode||!CurrentStatus().IsAdmin||!CurrentStatus().Connected)return;
            long expected=CurrentGeneration;
            using(var confirm=new ResetConfirmationDialog())if(confirm.ShowDialog(this)!=DialogResult.Yes)return;
            await ResetInputs(expected);
        }
        async Task<bool> ResetInputs(long expected)
        {
            if(backend==null||!adminMode||!CurrentStatus().IsAdmin)return false;
            hostBusy=true;editEpoch++;DiscardLocalEdits();saveStatus="전체 초기화 저장 중…";Render();
            try{await backend.ResetAll(expected);RefreshView();saveStatus="전체 초기화 완료 · 모든 입력이 비워졌습니다";return true;}
            catch(Exception ex){bool resetConfirmed=ex is ResetSynchronizationException;saveStatus=resetConfirmed?"전체 초기화 반영 완료 · 화면 동기화 확인 필요":"전체 초기화 실패 · 서버 반영을 확인하지 못했습니다";DesignMessageBox.Show(this,ex.Message,resetConfirmed?"초기화 동기화 확인":"전체 초기화 실패",MessageBoxButtons.OK,MessageBoxIcon.Warning);return false;}
            finally{hostBusy=false;RefreshView();Render();}
        }
        void AttachBackgroundFocusHandlers(Control control)
        {
            if(control is TextBoxBase||control is ComboBox||control is ButtonBase||control is SetupFieldPanel||control is DesignScrollBar)return;
            if(control==grid){control.MouseDown+=delegate(object sender,MouseEventArgs e){if(grid.HitTest(e.X,e.Y).Type==DataGridViewHitTestType.None)ReleaseInputFocus();};return;}
            control.MouseDown+=delegate {ReleaseInputFocus();};
            foreach(Control child in control.Controls)AttachBackgroundFocusHandlers(child);
        }
        void ReleaseInputFocus()
        {
            if(refreshing||closing||hostBusy)return;
            // Commit valid input before leaving the editor; invalid input keeps its error and focus.
            if(!TryCommitSetupEdits()||!grid.EndEdit())return;
            ActiveControl=null;Focus();grid.ClearSelection();
        }
        void ShowSettings(){ShowSetupPage();}
        void ExportBackup()
        {
            using(var save=new SaveFileDialog {Filter="점수판 백업 (*.json)|*.json",FileName="배그점수판_"+DateTime.Now.ToString("yyyyMMdd_HHmm")+".json"})if(save.ShowDialog(this)==DialogResult.OK)File.WriteAllText(save.FileName,FirebaseHttp.Json().Serialize(state),new System.Text.UTF8Encoding(false));
        }
        void ShowHelp()
        {
            DesignMessageBox.Show(this,"실행 시 자동 인증 후 방 목록이 표시됩니다. 방을 선택하면 바로 입장하고 입력할 수 있습니다. BJ는 팀 짜기로 선수 이름을 저장할 수 있습니다. BJ 프로그램 종료·로그아웃은 게임 상태를 바꾸지 않습니다. 게임 종료 버튼은 방과 모든 기록을 즉시 삭제합니다.\n팀 설정에서 이름·차감 점수를 입력하며 빈칸은 계산 시 0입니다.\nDelete는 선택값을 비웁니다.\n방장의 전체 초기화는 현재 방의 모든 팀과 모든 저장된 라운드의 입력을 지웁니다. 라운드 수와 인증은 유지됩니다.\n게임 종료·초기화 또는 연결 끊김 시 미전송 입력을 폐기하며 자동 재전송하지 않습니다.","사용 방법");
        }
        bool CancelActiveEdit()
        {
            if(CancelSetupEdit())return true;
            if(grid.ContainsFocus&&grid.CurrentCell!=null){string key=CellKey(grid.CurrentCell.RowIndex,grid.CurrentCell.ColumnIndex);if(key==null||pending.Contains(key))return false;refreshing=true;try{grid.CancelEdit();grid.EndEdit();drafts.Remove(key);}finally{refreshing=false;}Render();return true;}return false;
        }
        protected override bool ProcessCmdKey(ref Message message,Keys keyData)
        {if(keyData==Keys.Delete&&DeleteSelectedCells())return true;if(keyData==Keys.Escape&&CancelActiveEdit())return true;return base.ProcessCmdKey(ref message,keyData);}
        protected override bool ProcessDialogKey(Keys keyData)
        {if(keyData==Keys.Delete&&DeleteSelectedCells())return true;if(keyData==Keys.Escape&&CancelActiveEdit())return true;return base.ProcessDialogKey(keyData);}
        void OnClosing(object sender,FormClosingEventArgs e)
        {
            if(hostBusy){e.Cancel=true;notice.Text="진행 중인 인증·초기화 처리가 끝난 뒤 다시 닫아 주세요.";return;}
            FinishClose();
        }
        void FinishClose()
        {
            if(closing)return;
            closing=true;
            try
            {
                UnbindUpdates();timer.Stop();timer.Dispose();DiscardLocalEdits();setupErrors.Dispose();
            }
            finally{if(backend!=null)backend.Dispose();}
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing&&!uiResourcesDisposed)FinishClose();
            try{base.Dispose(disposing);}
            finally
            {
                if(disposing&&!uiResourcesDisposed)
                {
                    uiResourcesDisposed=true;sheetTips.Dispose();notice.Dispose();
                    foreach(var font in uiFonts.Values)font.Dispose();
                    uiFonts.Clear();
                    if(summaryFont!=null){summaryFont.Dispose();summaryFont=null;}
                }
            }
        }
    }
}
