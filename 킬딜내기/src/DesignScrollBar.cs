using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PubgScore
{
    // This control only publishes a position. The host owns the scrolled content.
    internal class DesignScrollBar : Control
    {
        int maximum,viewport=1,currentValue,smallChange=1,largeChange=10;
        long wheelRemainder;
        bool hovered,dragging;
        int dragOffset;

        internal DesignScrollBar()
        {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            SetStyle(ControlStyles.Selectable,false);
            Width=16;TabStop=false;CausesValidation=false;
            BackColor=Color.FromArgb(243,245,250);ForeColor=Color.FromArgb(176,170,203);
            AccessibleRole=AccessibleRole.ScrollBar;AccessibleName="세로 스크롤";
        }

        public event EventHandler ValueChanged;
        public int Value
        {
            get{return currentValue;}
            set{SetValue(value);}
        }
        public int SmallChange
        {
            get{return smallChange;}
            set{smallChange=Math.Max(1,value);}
        }
        public int LargeChange
        {
            get{return largeChange;}
            set{largeChange=Math.Max(1,value);}
        }

        // maximum is the inclusive final position, rather than content length.
        internal void SetRange(int maximum,int viewport,int value)
        {
            int nextMaximum=Math.Max(0,maximum),nextViewport=Math.Max(1,viewport);
            bool rangeChanged=this.maximum!=nextMaximum||this.viewport!=nextViewport;
            this.maximum=nextMaximum;this.viewport=nextViewport;
            if(this.maximum==0){wheelRemainder=0;EndDrag();}
            if(dragging)dragOffset=Math.Min(dragOffset,Math.Max(0,ThumbRectangle.Height-1));
            int previous=currentValue;SetValue(value);
            if(rangeChanged&&previous==currentValue)Invalidate();
        }

        void SetValue(long value)
        {
            int next=(int)Math.Max(0L,Math.Min((long)maximum,value));
            if(currentValue==next)return;
            currentValue=next;Invalidate();
            var handler=ValueChanged;if(handler!=null)handler(this,EventArgs.Empty);
        }

        int Scale(int logical){return Math.Max(1,(int)Math.Round(logical*DeviceDpi/96F));}
        Rectangle TrackRectangle
        {
            get
            {
                int inset=Math.Min(Scale(2),Math.Max(0,Height/2));
                int width=Math.Min(Width,Scale(8));
                return new Rectangle(Math.Max(0,(Width-width)/2),inset,Math.Max(0,width),Math.Max(0,Height-inset*2));
            }
        }
        internal Rectangle ThumbRectangle
        {
            get
            {
                var track=TrackRectangle;
                if(maximum==0||track.Width<1||track.Height<1)return Rectangle.Empty;
                double fraction=viewport/((double)maximum+viewport);
                int height=Math.Min(track.Height,Math.Max(Scale(24),(int)Math.Round(track.Height*fraction)));
                int travel=track.Height-height;
                int top=track.Top+(int)Math.Round(travel*(currentValue/(double)maximum));
                int width=Math.Min(Width,Scale(hovered||dragging?8:6));
                return new Rectangle(Math.Max(0,(Width-width)/2),top,Math.Max(0,width),height);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using(var brush=new SolidBrush(BackColor))e.Graphics.FillRectangle(brush,ClientRectangle);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            // Also fill here: native repaint may skip the background phase.
            using(var brush=new SolidBrush(BackColor))e.Graphics.FillRectangle(brush,ClientRectangle);
            var thumb=ThumbRectangle;
            if(!thumb.IsEmpty)
            {
                var previous=e.Graphics.SmoothingMode;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                Color color=!Enabled?Color.FromArgb(205,201,222):dragging?Color.FromArgb(128,116,166):hovered?Color.FromArgb(153,145,184):ForeColor;
                using(var shape=DesignTheme.Round(thumb,thumb.Width/2F))
                using(var brush=new SolidBrush(color))e.Graphics.FillPath(brush,shape);
                e.Graphics.SmoothingMode=previous;
            }
            base.OnPaint(e);
        }

        protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hovered=true;Invalidate();}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hovered=false;Invalidate();}
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if(!Enabled||e.Button!=MouseButtons.Left||maximum==0)return;
            var thumb=ThumbRectangle;
            if(thumb.IsEmpty)return;
            if(thumb.Contains(e.Location))
            {
                dragging=true;dragOffset=e.Y-thumb.Top;Capture=true;Invalidate();
            }
            else if(e.Y<thumb.Top)SetValue((long)currentValue-largeChange);
            else if(e.Y>=thumb.Bottom)SetValue((long)currentValue+largeChange);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if(!dragging||!Capture||maximum==0)return;
            var track=TrackRectangle;var thumb=ThumbRectangle;
            int travel=track.Height-thumb.Height;
            if(travel<=0)return;
            long top=(long)e.Y-dragOffset-track.Top;
            top=Math.Max(0L,Math.Min((long)travel,top));
            SetValue((long)Math.Round(maximum*(top/(double)travel)));
        }
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button==MouseButtons.Left)EndDrag();}
        protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture&&dragging){dragging=false;Invalidate();}}
        void EndDrag()
        {
            if(!dragging)return;
            dragging=false;if(Capture)Capture=false;Invalidate();
        }
        protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);if(!Enabled){wheelRemainder=0;EndDrag();}Invalidate();}
        protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);if(!Visible)EndDrag();}

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if(!Enabled||maximum==0)return;
            int lines=SystemInformation.MouseWheelScrollLines;
            if(lines==0){wheelRemainder=0;return;}
            wheelRemainder+=e.Delta;
            long notches=wheelRemainder/120;wheelRemainder%=120;
            long step=lines<0?largeChange:Math.Min((long)Int32.MaxValue,(long)smallChange*lines);
            if(notches!=0)SetValue((long)currentValue-notches*step);
            var handled=e as HandledMouseEventArgs;if(handled!=null)handled.Handled=true;
        }
        protected override bool IsInputKey(Keys keyData)
        {
            switch(keyData&Keys.KeyCode)
            {
                case Keys.Up:case Keys.Down:case Keys.PageUp:case Keys.PageDown:case Keys.Home:case Keys.End:return true;
                default:return base.IsInputKey(keyData);
            }
        }
        // No focus is taken from an editor. A host may route these keys here.
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if(!Enabled){base.OnKeyDown(e);return;}
            switch(e.KeyCode)
            {
                case Keys.Up:SetValue((long)currentValue-smallChange);break;
                case Keys.Down:SetValue((long)currentValue+smallChange);break;
                case Keys.PageUp:SetValue((long)currentValue-largeChange);break;
                case Keys.PageDown:SetValue((long)currentValue+largeChange);break;
                case Keys.Home:SetValue(0);break;
                case Keys.End:SetValue(maximum);break;
                default:base.OnKeyDown(e);return;
            }
            e.Handled=true;e.SuppressKeyPress=true;base.OnKeyDown(e);
        }
    }
}
