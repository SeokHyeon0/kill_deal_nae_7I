using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PubgScore
{
    // Visual components only: dialogs keep their original commands and event handlers.
    internal static class DialogPalette
    {
        internal static readonly Color Surface=Color.White;
        internal static readonly Color Background=Color.FromArgb(246,247,251);
        internal static readonly Color Field=Color.FromArgb(247,248,252);
        internal static readonly Color Border=Color.FromArgb(223,226,234);
        internal static readonly Color Text=Color.FromArgb(29,36,51);
        internal static readonly Color Muted=Color.FromArgb(98,106,125);
        internal static readonly Color Primary=Color.FromArgb(45,63,99);
        internal static readonly Color Accent=Color.FromArgb(109,92,232);
        internal static readonly Color AccentSoft=Color.FromArgb(239,236,253);
        internal static readonly Color Danger=Color.FromArgb(201,58,58);
        internal static readonly Color DangerSoft=Color.FromArgb(253,232,232);
        internal static DesignButton Button(string text,bool primary)
        {
            var button=new DesignButton {Text=text,Height=44,Width=104,CornerRadius=12,
                BackColor=primary?Primary:Surface,ForeColor=primary?Color.White:Text,
                Margin=Padding.Empty};
            button.FlatAppearance.BorderSize=1;
            button.FlatAppearance.BorderColor=primary?Primary:Color.FromArgb(207,211,222);
            return button;
        }
    }

    // Confirmation choices show keyboard selection with the rounded face only.
    internal sealed class ConfirmationChoiceButton : DesignButton
    {
        bool selected;
        internal Color SelectionColor=DialogPalette.Primary;
        internal bool Selected {get{return selected;}set{if(selected==value)return;selected=value;Invalidate();}}
        internal ConfirmationChoiceButton(string text)
        {
            Text=text;CornerRadius=12;BackColor=DialogPalette.Surface;ForeColor=DialogPalette.Text;
            Margin=Padding.Empty;FlatAppearance.BorderSize=0;
        }
        internal static void Bind(Form dialog,ConfirmationChoiceButton yes,ConfirmationChoiceButton no)
        {
            yes.Selected=true;no.Selected=false;
            dialog.AcceptButton=yes;dialog.ActiveControl=yes;
        }
        public override void NotifyDefault(bool value)
        {base.NotifyDefault(value);Selected=value;}
        protected override void OnPaint(PaintEventArgs e)
        {
            Color canvas=Parent==null?DialogPalette.Surface:Parent.BackColor;
            using(var fill=new SolidBrush(canvas))e.Graphics.FillRectangle(fill,ClientRectangle);
            if(Width<2||Height<2)return;
            float scale=DeviceDpi/96F;var previous=e.Graphics.Save();
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            if(Selected)using(var shape=DesignTheme.Round(new RectangleF(.5F,.5F,Width-1,Height-1),CornerRadius*scale))
            using(var fill=new SolidBrush(SelectionColor))e.Graphics.FillPath(fill,shape);
            e.Graphics.Restore(previous);
            Color ink=!Enabled?DialogPalette.Muted:Selected?Color.White:ForeColor;
            TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,ink,TextFormatFlags.SingleLine|TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding);
        }
    }

    internal sealed class DialogSurface : Panel
    {
        public int CornerRadius=12;
        public Color BorderColor=DialogPalette.Border;
        public bool DrawBorder=true;
        public DialogSurface()
        {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|
                ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            BackColor=DialogPalette.Surface;
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {using(var canvas=new SolidBrush(Parent==null?DialogPalette.Surface:Parent.BackColor))e.Graphics.FillRectangle(canvas,ClientRectangle);}
        protected override void OnPaint(PaintEventArgs e)
        {
            using(var canvas=new SolidBrush(Parent==null?DialogPalette.Surface:Parent.BackColor))e.Graphics.FillRectangle(canvas,ClientRectangle);
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            if(Width>1&&Height>1)using(var shape=DesignTheme.Round(new RectangleF(.5F,.5F,Width-1,Height-1),CornerRadius*DeviceDpi/96F))
            {
                using(var fill=new SolidBrush(BackColor))e.Graphics.FillPath(fill,shape);
                if(DrawBorder)using(var pen=new Pen(BorderColor))e.Graphics.DrawPath(pen,shape);
            }
            base.OnPaint(e);
        }
    }

    internal sealed class DialogIconBadge : Control
    {
        internal bool ResetIcon;
        internal DialogIconBadge(bool reset)
        {
            ResetIcon=reset;Size=new Size(44,44);TabStop=false;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
            BackColor=reset?DialogPalette.DangerSoft:DialogPalette.AccentSoft;
            ForeColor=reset?DialogPalette.Danger:DialogPalette.Accent;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            using(var canvas=new SolidBrush(Parent==null?DialogPalette.Surface:Parent.BackColor))e.Graphics.FillRectangle(canvas,ClientRectangle);
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var shape=DesignTheme.Round(new RectangleF(0,0,Width,Height),12))
            using(var fill=new SolidBrush(BackColor))e.Graphics.FillPath(fill,shape);
            var state=e.Graphics.Save();e.Graphics.TranslateTransform(Width/2F-11,Height/2F-11);
            using(var pen=new Pen(ForeColor,1.8F){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
            {
                if(ResetIcon)
                {e.Graphics.DrawArc(pen,3,3,16,16,225,315);e.Graphics.DrawLines(pen,new[]{new Point(2,3),new Point(2,8),new Point(7,8)});}
                else
                {e.Graphics.DrawLine(pen,11,2,11,14);e.Graphics.DrawLines(pen,new[]{new Point(6,10),new Point(11,15),new Point(16,10)});e.Graphics.DrawLine(pen,5,20,17,20);}
            }
            e.Graphics.Restore(state);
        }
    }

    internal sealed class DialogProgress : Control
    {
        readonly Timer animation=new Timer();
        int percent,phase;
        ProgressBarStyle style=ProgressBarStyle.Marquee;
        internal event EventHandler DisplayChanged;
        internal int Value {get{return percent;}set{int next=Math.Max(0,Math.Min(100,value));if(percent==next)return;percent=next;Invalidate();Changed();}}
        internal ProgressBarStyle Style {get{return style;}set{style=value;UpdateAnimation();Invalidate();Changed();}}
        internal DialogProgress()
        {
            Height=10;BackColor=Color.FromArgb(231,233,240);ForeColor=DialogPalette.Accent;TabStop=false;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            animation.Interval=80;animation.Tick+=delegate {phase=(phase+6)%140;Invalidate();};
            AccessibleRole=AccessibleRole.ProgressBar;AccessibleName="다운로드 진행률";
        }
        void Changed(){AccessibleDescription=style==ProgressBarStyle.Marquee?"처리 중":percent+"%";if(DisplayChanged!=null)DisplayChanged(this,EventArgs.Empty);}
        void UpdateAnimation(){var window=FindForm();animation.Enabled=IsHandleCreated&&Visible&&(window==null||window.Opacity>0)&&style==ProgressBarStyle.Marquee;}
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);UpdateAnimation();}
        protected override void OnHandleDestroyed(EventArgs e){animation.Stop();base.OnHandleDestroyed(e);}
        protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);UpdateAnimation();}
        protected override void OnPaint(PaintEventArgs e)
        {
            using(var canvas=new SolidBrush(Parent==null?DialogPalette.Surface:Parent.BackColor))e.Graphics.FillRectangle(canvas,ClientRectangle);
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            if(Width<1||Height<1)return;
            using(var track=DesignTheme.Round(new RectangleF(0,0,Width,Height),Height/2F))
            {
                using(var fill=new SolidBrush(BackColor))e.Graphics.FillPath(fill,track);
                var previous=e.Graphics.Save();e.Graphics.SetClip(track,CombineMode.Intersect);
                float width=style==ProgressBarStyle.Marquee?Math.Max(32,Width*.28F):Width*percent/100F;
                float left=style==ProgressBarStyle.Marquee?(phase/100F)*Width-width:0;
                if(width>0)using(var bar=DesignTheme.Round(new RectangleF(left,0,width,Height),Height/2F))
                using(var fill=new SolidBrush(ForeColor))e.Graphics.FillPath(fill,bar);
                e.Graphics.Restore(previous);
            }
        }
        protected override void Dispose(bool disposing){if(disposing)animation.Dispose();base.Dispose(disposing);}
    }
}
