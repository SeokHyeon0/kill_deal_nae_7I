using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace PubgScore
{
    internal sealed class RoomCreateDialog : Form
    {
        internal RoomCreateDialog(Func<string,Task> create)
        {
            Text="방 만들기";StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ClientSize=new Size(460,235);Font=new Font("Malgun Gothic",10);BackColor=Color.FromArgb(28,31,38);ForeColor=Color.White;
            Controls.Add(new Label {Text="방 이름 (1~60자)",Bounds=new Rectangle(24,25,410,26)});
            var name=new TextBox {Bounds=new Rectangle(24,60,410,28),MaxLength=60};Controls.Add(name);
            var message=new Label {Text="방을 만들면 바로 입장하고 점수를 입력할 수 있습니다.",Bounds=new Rectangle(24,100,410,60)};Controls.Add(message);
            var submit=new Button {Text="방 만들기",Bounds=new Rectangle(232,177,96,34),BackColor=Color.FromArgb(92,80,167),FlatStyle=FlatStyle.Flat};Controls.Add(submit);
            var cancel=new Button {Text="취소",Bounds=new Rectangle(338,177,96,34),DialogResult=DialogResult.Cancel,FlatStyle=FlatStyle.Flat};Controls.Add(cancel);AcceptButton=submit;CancelButton=cancel;bool busy=false;
            submit.Click+=async delegate {if(busy)return;if(String.IsNullOrWhiteSpace(name.Text)){message.Text="방 이름을 입력하세요.";return;}busy=true;submit.Enabled=cancel.Enabled=name.Enabled=false;message.Text="방 생성 중…";try{await create(name.Text.Trim());busy=false;DialogResult=DialogResult.OK;Close();}catch(Exception ex){message.Text=ex.Message;}finally{busy=false;if(!IsDisposed)submit.Enabled=cancel.Enabled=name.Enabled=true;}};
            FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy)e.Cancel=true;};
        }
    }
    internal sealed class RoomJoinDialog : Form
    {
        readonly CancellationTokenSource cancel=new CancellationTokenSource();
        internal RoomJoinDialog(FirebaseGameClient client,RoomInfo room)
        {
            Text="방 입장";StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ClientSize=new Size(490,230);Font=new Font("Malgun Gothic",10);BackColor=DialogPalette.Surface;ForeColor=DialogPalette.Text;
            // HTML #entry: a compact connection badge and the existing retry/cancel actions.
            var badge=new RoomConnectionBadge {Location=new Point(24,26)};Controls.Add(badge);
            var title=new Label {Text=room.Name,Bounds=new Rectangle(84,25,382,30),AutoEllipsis=true,Font=new Font(Font.FontFamily,13F,FontStyle.Bold)};Controls.Add(title);
            var message=new Label {Text="방에 연결하고 있습니다…",Bounds=new Rectangle(84,64,382,80),ForeColor=DialogPalette.Muted};Controls.Add(message);
            var retry=DialogPalette.Button("다시 시도",true);retry.SetBounds(250,162,104,44);retry.Visible=false;Controls.Add(retry);
            var close=DialogPalette.Button("취소",false);close.SetBounds(362,162,104,44);close.DialogResult=DialogResult.Cancel;Controls.Add(close);CancelButton=close;
            retry.VisibleChanged+=delegate {badge.Warning=retry.Visible;title.Text=retry.Visible?"확인 필요":room.Name;};
            bool busy=false;
            Func<Task> join=async delegate {
                if(busy||cancel.IsCancellationRequested)return;busy=true;retry.Visible=false;message.Text="방에 연결하고 있습니다…";
                try{await client.JoinRoom(room.Id,"",cancel.Token);if(!IsDisposed&&!cancel.IsCancellationRequested){DialogResult=DialogResult.OK;Close();}}
                catch(OperationCanceledException){}catch(Exception ex){if(!IsDisposed){message.Text=ex.Message;retry.Visible=true;}}
                finally{busy=false;}
            };
            Shown+=async delegate {await join();};retry.Click+=async delegate {await join();};FormClosing+=delegate {cancel.Cancel();};
        }
    }

    internal sealed class RoomConnectionBadge : Control
    {
        bool warning;
        internal bool Warning {get{return warning;}set{if(warning==value)return;warning=value;AccessibleDescription=value?"연결 확인 필요":"방에 연결 중";Invalidate();}}
        internal RoomConnectionBadge()
        {
            Size=new Size(44,44);TabStop=false;AccessibleRole=AccessibleRole.Graphic;AccessibleName="방 연결 상태";AccessibleDescription="방에 연결 중";
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            using(var canvas=new SolidBrush(Parent==null?DialogPalette.Surface:Parent.BackColor))e.Graphics.FillRectangle(canvas,ClientRectangle);
            e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Color tint=warning?Color.FromArgb(253,241,216):DialogPalette.AccentSoft;
            Color ink=warning?Color.FromArgb(154,100,0):DialogPalette.Accent;
            using(var shape=DesignTheme.Round(new RectangleF(0,0,Width,Height),12))
            using(var fill=new SolidBrush(tint))e.Graphics.FillPath(fill,shape);
            var state=e.Graphics.Save();e.Graphics.TranslateTransform(Width/2F-12,Height/2F-12);
            using(var pen=new Pen(ink,2F){StartCap=System.Drawing.Drawing2D.LineCap.Round,EndCap=System.Drawing.Drawing2D.LineCap.Round,LineJoin=System.Drawing.Drawing2D.LineJoin.Round})
            {
                if(warning)
                {
                    e.Graphics.DrawPolygon(pen,new[]{new PointF(12,3),new PointF(22,21),new PointF(2,21)});
                    e.Graphics.DrawLine(pen,12,9,12,14);e.Graphics.DrawLine(pen,12,18,12,18.2F);
                }
                else
                {
                    using(var track=new Pen(Color.FromArgb(213,206,249),2.5F))e.Graphics.DrawEllipse(track,3,3,18,18);
                    e.Graphics.DrawArc(pen,3,3,18,18,230,135);
                }
            }
            e.Graphics.Restore(state);base.OnPaint(e);
        }
    }
}
