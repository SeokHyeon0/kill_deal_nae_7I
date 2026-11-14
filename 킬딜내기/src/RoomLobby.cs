using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace PubgScore
{
    internal sealed class LobbyStatusChip : Label
    {
        internal LobbyStatusChip()
        {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            AutoSize=false;Size=new Size(84,26);Margin=new Padding(0,0,8,0);TextAlign=ContentAlignment.MiddleCenter;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Color canvas=Parent==null?Color.White:Parent.BackColor;
            using(var brush=new SolidBrush(canvas))e.Graphics.FillRectangle(brush,ClientRectangle);
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var shape=DesignTheme.Round(new RectangleF(0,0,Width,Height),13))
            using(var brush=new SolidBrush(BackColor))e.Graphics.FillPath(brush,shape);
            using(var brush=new SolidBrush(ForeColor))e.Graphics.FillEllipse(brush,10,(Height-4)/2,4,4);
            TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(20,0,Math.Max(0,Width-26),Height),ForeColor,TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.HorizontalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
        }
    }
    internal sealed class RoomCard : Button
    {
        internal RoomInfo Room;
        internal Font DetailFont,LinkFont,StateFont;
        internal bool Emphasized;
        bool hovered;
        static readonly Color CardColor=Color.White;
        static readonly Color BorderColor=Color.FromArgb(223,226,234);
        static readonly Color Purple=Color.FromArgb(109,92,232);
        internal RoomCard(Font font)
        {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            AutoEllipsis=true;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;
            BackColor=CardColor;ForeColor=Color.FromArgb(29,36,51);Font=font;
            TextAlign=ContentAlignment.MiddleLeft;Padding=new Padding(16);
            Height=190;Margin=new Padding(0,0,14,14);Cursor=Cursors.Hand;AccessibleRole=AccessibleRole.PushButton;
        }
        static GraphicsPath Rounded(RectangleF bounds,float radius)
        {
            var path=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(bounds.Width,bounds.Height));
            if(d<=0)return path;
            path.AddArc(bounds.Left,bounds.Top,d,d,180,90);path.AddArc(bounds.Right-d,bounds.Top,d,d,270,90);
            path.AddArc(bounds.Right-d,bounds.Bottom-d,d,d,0,90);path.AddArc(bounds.Left,bounds.Bottom-d,d,d,90,90);path.CloseFigure();return path;
        }
        Color CanvasColor()
        {
            for(Control parent=Parent;parent!=null;parent=parent.Parent)if(parent.BackColor.A==255)return parent.BackColor;
            return Color.FromArgb(243,244,248);
        }
        protected override void OnPaintBackground(PaintEventArgs e){using(var brush=new SolidBrush(CanvasColor()))e.Graphics.FillRectangle(brush,ClientRectangle);}
        protected override void OnPaint(PaintEventArgs e)
        {
            if(Width<40||Height<40)return;
            var graphics=e.Graphics;using(var canvas=new SolidBrush(CanvasColor()))graphics.FillRectangle(canvas,ClientRectangle);graphics.SmoothingMode=SmoothingMode.AntiAlias;
            bool live=Room!=null&&Room.Available&&Room.ActivityActive;
            bool accent=Focused||hovered;
            using(var shape=Rounded(new RectangleF(1,1,Width-3,Height-3),10))
            {
                using(var fill=new SolidBrush(Enabled?(hovered?Color.FromArgb(248,249,252):CardColor):Color.FromArgb(248,249,252)))graphics.FillPath(fill,shape);
                using(var border=new Pen(accent?Purple:live?Color.FromArgb(207,198,251):BorderColor,accent?1.5F:1F))graphics.DrawPath(border,shape);
                if(live)
                {
                    var previous=graphics.Clip;
                    graphics.SetClip(shape);
                    using(var brush=new SolidBrush(Purple))graphics.FillRectangle(brush,0,0,Width,3);
                    graphics.Clip=previous;previous.Dispose();
                }
            }
            int pad=16,inner=Math.Max(1,Width-pad*2);
            var stateFont=StateFont??Font;var detailFont=DetailFont??Font;var linkFont=LinkFont??Font;
            string state=Room==null?"":Room.Available?Room.StatusText:"확인 필요";
            string title=Room==null?Text:Room.Name;
            int requestedPill=Math.Max(76,TextRenderer.MeasureText(state,stateFont).Width+24);
            int inlinePill=Math.Min(requestedPill,Math.Max(76,inner/2));
            int inlineTitle=Math.Max(24,inner-inlinePill-12);
            bool stacked=Width<240||requestedPill>inner/2||TextRenderer.MeasureText(title,Font).Width>inlineTitle;
            int pillWidth=Math.Min(requestedPill,stacked?inner:Math.Max(76,inner/2));
            var pill=stacked?new Rectangle(pad,54,pillWidth,26):new Rectangle(Width-pad-pillWidth,24,pillWidth,28);
            using(var shape=Rounded(pill,14))
            using(var brush=new SolidBrush(live?Color.FromArgb(227,245,236):Room!=null&&!Room.Available?Color.FromArgb(253,241,216):Color.FromArgb(240,242,246)))graphics.FillPath(brush,shape);
            Color stateInk=live?Color.FromArgb(35,136,91):Room!=null&&!Room.Available?Color.FromArgb(154,100,0):Color.FromArgb(98,106,125);
            using(var dot=new SolidBrush(stateInk))graphics.FillEllipse(dot,pill.Left+8,pill.Top+(pill.Height-4)/2,4,4);
            TextRenderer.DrawText(graphics,state,stateFont,new Rectangle(pill.Left+18,pill.Top,pill.Width-23,pill.Height),stateInk,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
            var titleBounds=stacked?new Rectangle(pad,18,inner,28):new Rectangle(pad,23,Math.Max(24,inner-pillWidth-12),32);
            TextRenderer.DrawText(graphics,title,Font,titleBounds,Enabled?ForeColor:Color.FromArgb(109,116,132),TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
            string description=Room!=null&&!Room.Available?Room.StatusText:"팀별 점수와 경기 기록을 한눈에";
            TextRenderer.DrawText(graphics,description,detailFont,new Rectangle(pad,stacked?94:76,inner,stacked?36:48),Color.FromArgb(98,106,125),TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(graphics,"입장하기 ›",linkFont,new Rectangle(pad,Height-51,inner,30),Enabled?Color.FromArgb(90,72,214):Color.FromArgb(109,116,132),TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(graphics,new Rectangle(6,6,Width-12,Height-12),Purple,CardColor);
        }
        protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hovered=true;Invalidate();}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hovered=false;Invalidate();}
        protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
        protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);if(Width<28||Height<28)return;
            using(var shape=Rounded(new RectangleF(0,0,Width,Height),10)){var previous=Region;Region=new Region(shape);if(previous!=null)previous.Dispose();}
        }
    }
    public sealed partial class MainForm
    {
        Panel lobbyPanel;TableLayoutPanel roomShell;FlowLayoutPanel roomCards;Label lobbyMessage,roomCaption;Button createRoomButton,lobbyLogin,lobbyLogout,leaveRoomButton;
        LobbyStatusChip lobbyAuthenticationStatus,lobbyConnectionStatus;
        long renderedLobby=-1;string displayedRoom="";bool roomDialogOpen;
        FirebaseGameClient displayedLobbyClient;
        readonly Dictionary<string,RoomCard> cards=new Dictionary<string,RoomCard>();
        Button LobbyAction(string text,bool primary,string glyph)
        {
            var button=new DesignButton {Text=text,Glyph=glyph,CornerRadius=10,AutoSize=false,Width=104,Height=36,Margin=new Padding(0,0,8,0),Font=UiFont(8F),BackColor=Color.White,ForeColor=Color.FromArgb(29,36,51),FlatStyle=FlatStyle.Flat,UseVisualStyleBackColor=false,Cursor=Cursors.Hand};
            button.FlatAppearance.BorderColor=Color.FromArgb(207,211,222);button.FlatAppearance.BorderSize=1;return button;
        }
        void BuildRoomNavigation()
        {
            var existing=Controls.Cast<Control>().ToArray();roomShell=new TableLayoutPanel {Dock=DockStyle.Fill,RowCount=2,ColumnCount=1,Margin=Padding.Empty,Padding=Padding.Empty};roomShell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));roomShell.RowStyles.Add(new RowStyle(SizeType.Absolute,64));roomShell.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            foreach(var c in existing)roomShell.Controls.Add(c,0,1);Controls.Add(roomShell);
            var bar=new Panel {Dock=DockStyle.Fill,BackColor=Color.White,Margin=Padding.Empty,Padding=new Padding(24,10,24,10)};
            roomCaption=new Label {Dock=DockStyle.Fill,ForeColor=Color.FromArgb(39,48,68),TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true};
            leaveRoomButton=new DesignButton {Text="방 나가기",Dock=DockStyle.Top,Width=100,Height=42,CornerRadius=12,Font=UiFont(9F),FlatStyle=FlatStyle.Flat,BackColor=DialogPalette.Surface,ForeColor=DialogPalette.Text,UseVisualStyleBackColor=false,CausesValidation=false};leaveRoomButton.FlatAppearance.BorderColor=Color.FromArgb(207,211,222);leaveRoomButton.Click+=delegate {LeaveCurrentRoom();};var actions=new Panel {Dock=DockStyle.Right,Width=100,BackColor=bar.BackColor};actions.Controls.Add(leaveRoomButton);bar.Controls.Add(roomCaption);bar.Controls.Add(actions);roomShell.Controls.Add(bar,0,0);
            lobbyPanel=new Panel {Dock=DockStyle.Fill,BackColor=Color.FromArgb(243,244,248),Padding=Padding.Empty};Controls.Add(lobbyPanel);
            var titleArea=new Panel {Dock=DockStyle.Top,Height=94,BackColor=Color.White,Padding=new Padding(18,18,18,14)};
            var heading=new Label {Text="킬내기 방 목록",Font=UiFont(16F,FontStyle.Bold),ForeColor=Color.FromArgb(29,36,51),Dock=DockStyle.Top,Height=33,AutoEllipsis=true,Margin=Padding.Empty};
            var subtitle=new Label {Text="입장할 방을 선택하세요",Font=UiFont(8F),ForeColor=Color.FromArgb(98,106,125),Dock=DockStyle.Top,Height=23,Padding=new Padding(0,3,0,0),AutoEllipsis=true,Margin=Padding.Empty};
            titleArea.Controls.Add(subtitle);titleArea.Controls.Add(heading);
            var body=new Panel {Dock=DockStyle.Fill,BackColor=lobbyPanel.BackColor,Padding=new Padding(18)};
            var toolbar=new FlowLayoutPanel {Dock=DockStyle.Top,Height=56,WrapContents=false,Margin=Padding.Empty,Padding=Padding.Empty,BackColor=lobbyPanel.BackColor};
            createRoomButton=ButtonText("＋ 방 만들기",true);createRoomButton.Visible=false;
            lobbyLogin=LobbyAction("BJ 로그인",false,"");lobbyLogin.Visible=adminMode;lobbyLogin.Click+=delegate {PromptAdminLogin();};toolbar.Controls.Add(lobbyLogin);
            lobbyLogout=LobbyAction("로그아웃",false,"Logout");lobbyLogout.Visible=adminMode;lobbyLogout.Click+=delegate {LogoutAdministrator();};toolbar.Controls.Add(lobbyLogout);
            var retry=LobbyAction("다시 연결",true,"Refresh");retry.Click+=async delegate {if(hostBusy)return;if(backend==null||!CurrentStatus().Authenticated)await InitializeAuthentication();else await backend.ConnectAnonymous();};toolbar.Controls.Add(retry);
            lobbyMessage=new Label {Dock=DockStyle.Top,Height=60,ForeColor=Color.FromArgb(98,106,125),BackColor=Color.White,Font=UiFont(8F),Text="자동 인증 및 방 목록 연결 중…",TextAlign=ContentAlignment.MiddleCenter,Padding=new Padding(14,10,14,10)};
            roomCards=new FlowLayoutPanel {Dock=DockStyle.Fill,AutoScroll=true,WrapContents=true,Margin=Padding.Empty,Padding=Padding.Empty,BackColor=lobbyPanel.BackColor};roomCards.SizeChanged+=delegate {SizeRoomCards();};
            body.Controls.Add(new DesignScrollHost(roomCards){BackColor=lobbyPanel.BackColor});body.Controls.Add(lobbyMessage);body.Controls.Add(toolbar);
            var informationFooter=new Panel {Dock=DockStyle.Bottom,Height=44,Margin=Padding.Empty,Padding=new Padding(18,8,18,8),BackColor=Color.White};
            informationFooter.Paint+=delegate(object sender,PaintEventArgs e){using(var pen=new Pen(Color.FromArgb(223,226,234)))e.Graphics.DrawLine(pen,0,0,informationFooter.Width,0);};
            var footerStatus=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty,Padding=Padding.Empty,BackColor=Color.White};
            lobbyAuthenticationStatus=new LobbyStatusChip {Text="인증 대기",Font=UiFont(8F),BackColor=Color.FromArgb(240,242,246),ForeColor=Color.FromArgb(98,106,125)};
            lobbyConnectionStatus=new LobbyStatusChip {Text="연결 대기",Font=UiFont(8F),BackColor=Color.FromArgb(240,242,246),ForeColor=Color.FromArgb(98,106,125)};
            footerStatus.Controls.Add(lobbyAuthenticationStatus);footerStatus.Controls.Add(lobbyConnectionStatus);informationFooter.Controls.Add(footerStatus);
#if SINGLEFILE || VIEWER_SINGLEFILE
            var information=CreateDeploymentInformationButton(Color.White,Color.FromArgb(98,106,125));
            information.Dock=DockStyle.Right;
            informationFooter.Controls.Add(information);
#endif
            lobbyPanel.Controls.Add(body);lobbyPanel.Controls.Add(informationFooter);lobbyPanel.Controls.Add(titleArea);roomShell.Visible=false;lobbyPanel.BringToFront();
        }
        void UpdateLobbyStatus(GameView current,LobbyView lobby)
        {
            lobbyAuthenticationStatus.Text=current.Authenticated?"인증됨":"인증 필요";
            lobbyAuthenticationStatus.BackColor=current.Authenticated?Color.FromArgb(227,245,236):Color.FromArgb(253,241,216);
            lobbyAuthenticationStatus.ForeColor=current.Authenticated?Color.FromArgb(35,136,91):Color.FromArgb(154,100,0);
            bool connected=current.Authenticated&&lobby.Ready&&lobby.Connected;
            bool error=current.Authenticated&&lobby.Error.Length>0;
            lobbyConnectionStatus.Text=connected?"연결됨":error?"연결 확인":current.Authenticated?"연결 중":"연결 대기";
            lobbyConnectionStatus.BackColor=connected?Color.FromArgb(227,245,236):error?Color.FromArgb(253,241,216):Color.FromArgb(240,242,246);
            lobbyConnectionStatus.ForeColor=connected?Color.FromArgb(35,136,91):error?Color.FromArgb(154,100,0):Color.FromArgb(98,106,125);
            lobbyMessage.BackColor=error?Color.FromArgb(253,241,216):Color.White;
            lobbyMessage.ForeColor=error?Color.FromArgb(154,100,0):Color.FromArgb(98,106,125);
        }
        void SizeRoomCards()
        {
            if(roomCards==null)return;
            int usable=Math.Max(190,roomCards.ClientSize.Width-SystemInformation.VerticalScrollBarWidth);
            int columns=usable>=746?4:usable>=366?2:1,index=0,width=Math.Min(267,Math.Max(176,(usable-(columns-1)*14)/columns));
            roomCards.SuspendLayout();
            try{foreach(var card in roomCards.Controls.OfType<RoomCard>()){card.Width=width;card.Height=190;bool rowEnd=index%columns==columns-1;card.Margin=new Padding(0,0,rowEnd?0:14,14);roomCards.SetFlowBreak(card,rowEnd);index++;}}
            finally{roomCards.ResumeLayout();}
        }
        void RefreshRooms()
        {
            if(lobbyPanel==null)return;var client=backend as FirebaseGameClient;var current=backend==null?new GameView():CurrentStatus();
            bool entered=current.RoomId.Length>0;
            var lobby=client==null?new LobbyView():client.Lobby(!entered);
            if(client!=null&&client.RoomUnavailable&&!roomDialogOpen){LeaveCurrentRoom();DesignMessageBox.Show(this,"게임이 종료되어 방이 삭제되었습니다. 로비로 이동합니다.","방 삭제 안내",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
            roomShell.Visible=entered;lobbyPanel.Visible=!entered;leaveRoomButton.Enabled=!hostBusy&&!roomDialogOpen;
            if(displayedRoom!=current.RoomId){displayedRoom=current.RoomId;if(entered)ShowSetupPage();}
            roomCaption.Text=current.RoomName+"  ·  자유 입장";
            createRoomButton.Enabled=false;lobbyLogin.Visible=adminMode&&!current.Authenticated;lobbyLogin.Enabled=!hostBusy&&!current.Authenticated;lobbyLogout.Visible=adminMode&&current.Authenticated;lobbyLogout.Enabled=!hostBusy&&current.Authenticated;
            if(!ReferenceEquals(displayedLobbyClient,client)){displayedLobbyClient=client;renderedLobby=-1;}
            if(!entered){lobbyMessage.Text=!current.Authenticated?"인증이 필요합니다.":!lobby.Ready?lobby.Error.Length>0?lobby.Error:"방 목록을 불러오는 중…":!lobby.Connected?(lobby.Error.Length>0?lobby.Error:"방 목록 연결 오류 · 재연결 중…"):lobby.Error.Length>0?lobby.Error:lobby.Rooms.Count==0?"등록된 방이 없습니다.":"";lobbyMessage.Visible=lobbyMessage.Text.Length>0;
                UpdateLobbyStatus(current,lobby);
                if(renderedLobby!=lobby.Sequence||cards.Count!=lobby.Rooms.Count){renderedLobby=lobby.Sequence;bool layoutChanged=false;var ids=new HashSet<string>(lobby.Rooms.Select(r=>r.Id));foreach(string id in cards.Keys.Where(id=>!ids.Contains(id)).ToArray()){cards[id].Dispose();cards.Remove(id);layoutChanged=true;}int order=0;foreach(var room in lobby.Rooms){RoomCard card;if(!cards.TryGetValue(room.Id,out card)){card=new RoomCard(UiFont(10.5F,FontStyle.Bold)){DetailFont=UiFont(8F),LinkFont=UiFont(8F,FontStyle.Bold),StateFont=UiFont(8F)};cards[room.Id]=card;roomCards.Controls.Add(card);layoutChanged=true;var selected=card;card.Click+=delegate {OpenRoomDialog(selected.Room);};}card.Room=room;bool first=order==0;if(card.Emphasized!=first){card.Emphasized=first;card.Invalidate();}if(roomCards.Controls.GetChildIndex(card)!=order){roomCards.Controls.SetChildIndex(card,order);layoutChanged=true;}order++;}if(layoutChanged)SizeRoomCards();}
                foreach(var card in cards.Values){SetText(card,card.Room.Name+"\n\n"+card.Room.StatusText);card.Enabled=current.Authenticated&&lobby.Connected&&card.Room.Available&&!roomDialogOpen&&!hostBusy;sheetTips.SetToolTip(card,card.Room.Name+" · "+card.Room.StatusText);}
            }
        }
        void OpenRoomDialog(RoomInfo room)
        {
            var client=backend as FirebaseGameClient;if(client==null||roomDialogOpen||room==null||!room.Available||hostBusy)return;roomDialogOpen=true;
            try{using(var dialog=new RoomJoinDialog(client,room))dialog.ShowDialog(this);}catch(Exception ex){DesignMessageBox.Show(this,ex.Message,"방 입장 확인");}
            finally{roomDialogOpen=false;lastSequence=-1;RefreshView();RefreshRooms();}
        }
        void LeaveCurrentRoom()
        {
            var client=backend as FirebaseGameClient;if(client==null)return;editEpoch++;DiscardLocalEdits();pending.Clear();client.LeaveRoom();lastSequence=-1;RefreshView();
        }
    }
}
