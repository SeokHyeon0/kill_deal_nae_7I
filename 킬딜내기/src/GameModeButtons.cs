using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PubgScore
{
    public sealed partial class MainForm
    {
        readonly Dictionary<string,Button> gameModeButtons=new Dictionary<string,Button>(StringComparer.Ordinal);
        TableLayoutPanel gameModePanel;
        int modeNoticeSequence;

        Control BuildGameModeButtons()
        {
            gameModePanel=new TableLayoutPanel {ColumnCount=9,RowCount=1,Width=477,Height=43,Margin=Padding.Empty,Padding=Padding.Empty,BackColor=Color.White};
            gameModePanel.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            string[] modes={"match","kills","killdeal","kill","damage"};
            for(int i=0;i<modes.Length;i++)
            {
                string mode=modes[i];gameModePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,20));
                if(i<modes.Length-1)gameModePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,8));
                var button=new ModeChoiceButton {Text=GameModes.Name(mode),Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=Padding.Empty,FlatStyle=FlatStyle.Flat,BackColor=DialogPalette.Surface,ForeColor=DialogPalette.Text,Font=UiFont(11F,FontStyle.Bold),Cursor=Cursors.Hand,UseVisualStyleBackColor=false,AccessibleName=GameModes.Name(mode)+" 종목 선택",CornerRadius=12};
                button.FlatAppearance.BorderColor=DialogPalette.Border;button.FlatAppearance.BorderSize=1;
                button.Click+=async delegate {await ChooseGameMode(mode);};
                gameModeButtons.Add(mode,button);gameModePanel.Controls.Add(button,i*2,0);
            }
            return gameModePanel;
        }

        void RenderGameModeButtons()
        {
            if(gameModePanel==null)return;
            gameModePanel.Visible=adminMode&&showingSetup&&view.RoomId.Length>0;
            if(layoutMasthead!=null)layoutMasthead();
            bool hasMode=GameModes.IsValid(view.Mode);
            bool enabled=adminMode&&view.IsAdmin&&view.Connected&&view.Authenticated&&CanEdit&&!roomDialogOpen&&pending.Count==0;
            foreach(var entry in gameModeButtons)
            {
                bool selected=entry.Key==view.Mode;var button=entry.Value;
                button.Enabled=enabled;button.BackColor=selected?DialogPalette.AccentSoft:DialogPalette.Surface;button.ForeColor=selected?DialogPalette.Accent:DialogPalette.Text;
                button.FlatAppearance.BorderColor=DialogPalette.Border;
                button.FlatAppearance.BorderSize=1;
                button.AccessibleDescription=hasMode?(selected?"선택된 종목 · 확인 후 기본 차감 점수를 다시 적용할 수 있습니다.":"확인 후 종목을 변경할 수 있습니다. 경기 기록은 유지됩니다."):"팀 입력 후 선택하면 기본 차감 점수가 적용됩니다.";
                string defaultDeduction=entry.Key=="match"?"기본 차감 없음":"기본 차감 "+String.Join(" / ",GameModes.Deductions(entry.Key).Select(value=>value.ToString()).ToArray());
                sheetTips.SetToolTip(button,GameModes.Name(entry.Key)+" · "+defaultDeduction+(hasMode?" · 변경 전 확인":""));
            }
        }

        async Task ChooseGameMode(string mode)
        {
            if(!adminMode||!view.IsAdmin||!view.Connected||!view.Authenticated||!CanEdit||roomDialogOpen||pending.Count>0)return;
            if(!TryCommitSetupEdits()||!grid.EndEdit())return;
            if(pending.Count>0){saveStatus="입력값 저장이 끝난 뒤 종목을 선택하세요";Render();return;}
            if(!GameModes.HasPlayers(state))
            {DesignMessageBox.Show(this,"팀 짜기로 선수 이름을 먼저 입력한 뒤 종목을 선택하세요.","종목 선택",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
            var client=backend as FirebaseGameClient;if(client==null)return;
            var expected=client.View();
            if(GameModes.IsValid(expected.Mode))
            {
                roomDialogOpen=true;Render();DialogResult answer;
                try{answer=DesignMessageBox.Show(this,GameModes.Name(mode)+" 모드로 변경하시겠습니까?\n\n경기 기록은 유지하고 차감 점수만 "+GameModes.Name(mode)+" 기본값으로 적용합니다.","모드 변경",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2);}
                finally{roomDialogOpen=false;Render();}
                if(answer!=DialogResult.Yes||IsDisposed||Disposing)return;
            }
            hostBusy=true;saveStatus="종목과 차감 점수 저장 중…";Render();
            try{await client.SelectMode(mode,expected);RefreshView();ShowModeAppliedNotice(mode);}
            catch(Exception error){saveStatus="종목 저장 실패 · 현재 값을 확인하세요";DesignMessageBox.Show(this,error.Message,"종목 선택 실패",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
            finally{hostBusy=false;RefreshView();Render();}
        }

        async void ShowModeAppliedNotice(string mode)
        {
            string message=GameModes.Name(mode)+" 적용 완료 · 기록 유지 · 기본 차감 점수 적용";
            int sequence=++modeNoticeSequence;saveStatus=message;
            await Task.Delay(3000);
            // A previous notice must not erase a newer save, error, or mode notice.
            if(closing||IsDisposed||Disposing||sequence!=modeNoticeSequence||saveStatus!=message)return;
            saveStatus="";Render();
        }
    }
    internal sealed class ModeChoiceButton : DesignButton
    {
        // Keep the selected face color without changing the neutral border on focus or hover.
        protected override void OnPaint(PaintEventArgs e)
        {
            Color canvas=Parent==null?Color.White:Parent.BackColor;
            using(var fill=new SolidBrush(canvas))e.Graphics.FillRectangle(fill,ClientRectangle);
            if(Width<2||Height<2)return;
            float scale=DeviceDpi/96F;var previous=e.Graphics.Save();
            e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using(var shape=DesignTheme.Round(new RectangleF(.5F,.5F,Width-1,Height-1),CornerRadius*scale))
            {
                using(var fill=new SolidBrush(BackColor))e.Graphics.FillPath(fill,shape);
                using(var border=new Pen(DialogPalette.Border,scale))e.Graphics.DrawPath(border,shape);
            }
            e.Graphics.Restore(previous);
            Color ink=Enabled?ForeColor:DialogPalette.Muted;
            TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,ink,TextFormatFlags.SingleLine|TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding);
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(4,4,Math.Max(0,Width-8),Math.Max(0,Height-8)),DialogPalette.Muted,BackColor);
        }
    }
}
