using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PubgScore
{
    internal static class DesignTheme
    {
        internal static GraphicsPath Round(RectangleF bounds,float radius)
        {
            var path=new GraphicsPath();
            float diameter=Math.Max(0,Math.Min(radius*2,Math.Min(bounds.Width,bounds.Height)));
            if(diameter<1){path.AddRectangle(bounds);return path;}
            path.AddArc(bounds.Left,bounds.Top,diameter,diameter,180,90);
            path.AddArc(bounds.Right-diameter,bounds.Top,diameter,diameter,270,90);
            path.AddArc(bounds.Right-diameter,bounds.Bottom-diameter,diameter,diameter,0,90);
            path.AddArc(bounds.Left,bounds.Bottom-diameter,diameter,diameter,90,90);
            path.CloseFigure();return path;
        }
        internal static Color Mix(Color color,Color other,float amount)
        {return Color.FromArgb((int)(color.R+(other.R-color.R)*amount),(int)(color.G+(other.G-color.G)*amount),(int)(color.B+(other.B-color.B)*amount));}
    }

    // One native button, with the existing keyboard and Click behavior intact.
    internal class DesignButton : Button
    {
        public int CornerRadius=10;
        public string Glyph="";
        bool hovered,pressed;
        public DesignButton()
        {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            SetStyle(ControlStyles.Opaque,false);
            FlatStyle=FlatStyle.Flat;UseVisualStyleBackColor=false;Cursor=Cursors.Hand;
        }
        public override Size GetPreferredSize(Size proposedSize)
        {
            var size=base.GetPreferredSize(proposedSize);
            if(!String.IsNullOrEmpty(Glyph))size.Width+=(int)Math.Ceiling(24F*DeviceDpi/96F);
            return size;
        }
        protected override void OnMouseEnter(EventArgs e){hovered=true;base.OnMouseEnter(e);Invalidate();}
        protected override void OnMouseLeave(EventArgs e){hovered=false;pressed=false;base.OnMouseLeave(e);Invalidate();}
        protected override void OnMouseDown(MouseEventArgs e){pressed=e.Button==MouseButtons.Left;base.OnMouseDown(e);Invalidate();}
        protected override void OnMouseUp(MouseEventArgs e){pressed=false;base.OnMouseUp(e);Invalidate();}
        protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Invalidate();}
        protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
        protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
        Color CanvasColor()
        {
            for(Control surface=Parent;surface!=null;surface=surface.Parent)
                if(surface.BackColor.A==255)return surface.BackColor;
            return SystemColors.Control;
        }
        protected override void OnPaintBackground(PaintEventArgs e){using(var brush=new SolidBrush(CanvasColor()))e.Graphics.FillRectangle(brush,ClientRectangle);}
        protected override void OnPaint(PaintEventArgs e)
        {
            // ButtonBase can skip the background phase during native repaint.
            // Paint the whole rectangle before the rounded face in either path.
            using(var brush=new SolidBrush(CanvasColor()))e.Graphics.FillRectangle(brush,ClientRectangle);
            if(Width<2||Height<2)return;
            float scale=DeviceDpi/96F;
            Color fill=BackColor;
            if(Enabled&&hovered)fill=DesignTheme.Mix(fill,fill.GetBrightness()<.5F?Color.White:Color.FromArgb(80,88,104),pressed?.08F:.035F);
            if(!Enabled&&fill.GetBrightness()<.5F)fill=DesignTheme.Mix(fill,Parent==null?Color.White:Parent.BackColor,.12F);
            var old=e.Graphics.SmoothingMode;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var shape=DesignTheme.Round(new RectangleF(.5F,.5F,Width-1,Height-1),CornerRadius*scale))
            {
                using(var brush=new SolidBrush(fill))e.Graphics.FillPath(brush,shape);
                if(FlatAppearance.BorderSize>0)using(var pen=new Pen(Focused?Color.FromArgb(119,93,229):FlatAppearance.BorderColor,Math.Max(1F,FlatAppearance.BorderSize*scale)))e.Graphics.DrawPath(pen,shape);
            }
            e.Graphics.SmoothingMode=old;
            Color textColor=Enabled?ForeColor:fill.GetBrightness()<.5F?Color.FromArgb(179,185,203):Color.FromArgb(127,137,153);
            int glyph=String.IsNullOrEmpty(Glyph)?0:(int)Math.Round(18*scale),gap=glyph==0?0:(int)Math.Round(8*scale);
            if(glyph==0)
                TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,textColor,TextFormatFlags.SingleLine|TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding);
            else
            {
                int available=Math.Max(0,Width-Padding.Horizontal-(int)(12*scale));
                int textWidth=Math.Min(Math.Max(0,available-glyph-gap),TextRenderer.MeasureText(Text,Font,new Size(Int32.MaxValue,Height),TextFormatFlags.SingleLine|TextFormatFlags.NoPadding).Width);
                int left=Math.Max((int)(6*scale),(Width-textWidth-glyph-gap)/2);
                DrawGlyph(e.Graphics,new Rectangle(left,(Height-glyph)/2,glyph,glyph),Glyph,textColor);
                TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(left+glyph+gap,0,Math.Max(0,Width-left-glyph-gap-(int)(6*scale)),Height),textColor,TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding);
            }
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(4,4,Math.Max(0,Width-8),Math.Max(0,Height-8)),textColor,fill);
        }
        static void DrawGlyph(Graphics graphics,Rectangle bounds,string glyph,Color color)
        {
            var state=graphics.Save();graphics.TranslateTransform(bounds.X,bounds.Y);graphics.ScaleTransform(bounds.Width/20F,bounds.Height/20F);graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var pen=new Pen(color,1.6F){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
            {
                if(glyph=="Team")
                {
                    graphics.DrawEllipse(pen,6,1,6,6);graphics.DrawArc(pen,2,9,14,14,180,180);
                    graphics.DrawArc(pen,12,2,5,5,270,180);graphics.DrawArc(pen,12,10,7,11,270,90);
                }
                else if(glyph=="Copy")
                {graphics.DrawRectangle(pen,6,6,11,12);graphics.DrawLines(pen,new[]{new Point(3,14),new Point(3,3),new Point(13,3)});}
                else if(glyph=="Logout")
                {graphics.DrawLines(pen,new[]{new Point(8,2),new Point(3,2),new Point(3,18),new Point(8,18)});graphics.DrawLine(pen,8,10,18,10);graphics.DrawLines(pen,new[]{new Point(14,6),new Point(18,10),new Point(14,14)});}
                else
                {graphics.DrawArc(pen,3,3,14,14,35,285);graphics.DrawLines(pen,new[]{new Point(3,2),new Point(3,7),new Point(8,7)});}
            }
            graphics.Restore(state);
        }
    }
}
