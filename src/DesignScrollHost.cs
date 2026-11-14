using System;
using System.Drawing;
using System.Windows.Forms;

namespace PubgScore
{
    // The original control still handles wheel, keyboard, focus and edit scrolling.
    // Clip only its native vertical bar and mirror that position with the themed bar.
    internal sealed class DesignScrollHost : Panel
    {
        readonly Control content;
        readonly Panel viewport=new Panel();
        readonly DesignScrollBar bar=new DesignScrollBar();
        readonly DataGridView grid;
        readonly ScrollableControl scrollPanel;
        readonly VScrollBar nativeGridBar;
        bool synchronizing;

        internal DesignScrollHost(Control content)
        {
            if(content==null)throw new ArgumentNullException("content");
            this.content=content;grid=content as DataGridView;scrollPanel=content as ScrollableControl;
            if(grid==null&&scrollPanel==null)throw new ArgumentException("A scrollable panel or grid is required.","content");
            Dock=DockStyle.Fill;Margin=Padding.Empty;Padding=Padding.Empty;
            BackColor=Color.FromArgb(243,245,250);CausesValidation=false;
            viewport.Margin=Padding.Empty;viewport.Padding=Padding.Empty;viewport.CausesValidation=false;
            viewport.BackColor=BackColor;Controls.Add(viewport);Controls.Add(bar);
            content.Dock=DockStyle.None;content.Anchor=AnchorStyles.Top|AnchorStyles.Left;
            viewport.Controls.Add(content);
            bar.ValueChanged+=BarValueChanged;
            content.Layout+=ContentLayout;content.SizeChanged+=ContentChanged;
            content.VisibleChanged+=ContentChanged;content.EnabledChanged+=ContentChanged;
            if(grid!=null)
            {
                foreach(Control child in grid.Controls)if(child is VScrollBar){nativeGridBar=(VScrollBar)child;break;}
                if(nativeGridBar!=null){nativeGridBar.VisibleChanged+=ContentChanged;nativeGridBar.ValueChanged+=ContentChanged;}
                grid.Scroll+=ContentScroll;grid.RowsAdded+=GridRowsAdded;grid.RowsRemoved+=GridRowsRemoved;
            }
            else
            {
                scrollPanel.Scroll+=ContentScroll;
                scrollPanel.ControlAdded+=ScrollContentAdded;scrollPanel.ControlRemoved+=ScrollContentRemoved;
                foreach(Control child in scrollPanel.Controls)ObserveScrollChild(child,true);
            }
        }

        internal DesignScrollBar ScrollBar {get{return bar;}}
        internal Rectangle ViewportBounds {get{return viewport.Bounds;}}
        protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);Synchronize();}
        protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);Synchronize();}
        void ContentChanged(object sender,EventArgs e){Synchronize();}
        void ContentLayout(object sender,LayoutEventArgs e){Synchronize();}
        void ContentScroll(object sender,ScrollEventArgs e){Synchronize();}
        void GridRowsAdded(object sender,DataGridViewRowsAddedEventArgs e){Synchronize();}
        void GridRowsRemoved(object sender,DataGridViewRowsRemovedEventArgs e){Synchronize();}
        void ObserveScrollChild(Control child,bool observe)
        {
            if(observe){child.LocationChanged+=ContentChanged;child.SizeChanged+=ContentChanged;}
            else{child.LocationChanged-=ContentChanged;child.SizeChanged-=ContentChanged;}
        }
        void ScrollContentAdded(object sender,ControlEventArgs e){ObserveScrollChild(e.Control,true);Synchronize();}
        void ScrollContentRemoved(object sender,ControlEventArgs e){ObserveScrollChild(e.Control,false);Synchronize();}

        internal void Synchronize()
        {
            if(synchronizing||content==null||content.IsDisposed||IsDisposed)return;
            synchronizing=true;
            try
            {
                // Keep the source at its original full width: its existing native bar
                // already reserves exactly the space used by the custom bar.
                content.SetBounds(0,0,ClientSize.Width,ClientSize.Height);
                int maximum,viewportSize,value,small,page;
                bool hasNativeBar;
                if(grid!=null)
                {
                    int rows=grid.Rows.GetRowCount(DataGridViewElementStates.Visible);
                    int shown=Math.Max(1,grid.DisplayedRowCount(false));
                    maximum=Math.Max(0,rows-shown);viewportSize=shown;
                    value=Math.Max(0,grid.FirstDisplayedScrollingRowIndex);small=1;page=shown;
                    hasNativeBar=nativeGridBar!=null&&nativeGridBar.Visible;
                }
                else
                {
                    var vertical=scrollPanel.VerticalScroll;
                    maximum=vertical.Visible?Math.Max(0,vertical.Maximum-vertical.LargeChange+1):0;
                    viewportSize=Math.Max(1,scrollPanel.ClientSize.Height);
                    value=Math.Max(0,-scrollPanel.AutoScrollPosition.Y);
                    small=Math.Max(1,vertical.SmallChange);page=Math.Max(1,vertical.LargeChange);
                    hasNativeBar=vertical.Visible;
                }
                bool needed=hasNativeBar&&maximum>0;
                int width=needed?(nativeGridBar==null?SystemInformation.VerticalScrollBarWidth:nativeGridBar.Width):0;
                width=Math.Max(0,Math.Min(ClientSize.Width,width));
                viewport.SetBounds(0,0,Math.Max(0,ClientSize.Width-width),ClientSize.Height);
                bar.SetBounds(Math.Max(0,ClientSize.Width-width),0,width,ClientSize.Height);
                viewport.BackColor=BackColor;bar.BackColor=BackColor;bar.Visible=needed;bar.Enabled=content.Enabled&&needed;
                bar.SmallChange=small;bar.LargeChange=page;bar.SetRange(maximum,viewportSize,value);
            }
            finally{synchronizing=false;}
        }

        void BarValueChanged(object sender,EventArgs e)
        {
            if(synchronizing||content.IsDisposed||IsDisposed)return;
            synchronizing=true;
            try
            {
                if(grid!=null)
                {
                    int row=bar.Value;
                    if(row<grid.RowCount&&grid.FirstDisplayedScrollingRowIndex!=row)
                        grid.FirstDisplayedScrollingRowIndex=row;
                }
                else scrollPanel.AutoScrollPosition=new Point(Math.Max(0,-scrollPanel.AutoScrollPosition.X),bar.Value);
            }
            // A validating editor can reject a programmatic scroll. Restore the
            // indicator to the source position rather than committing the edit.
            catch(InvalidOperationException){}
            finally{synchronizing=false;Synchronize();}
        }

        protected override void Dispose(bool disposing)
        {
            if(disposing)
            {
                bar.ValueChanged-=BarValueChanged;
                content.Layout-=ContentLayout;content.SizeChanged-=ContentChanged;
                content.VisibleChanged-=ContentChanged;content.EnabledChanged-=ContentChanged;
                if(grid!=null)
                {
                    grid.Scroll-=ContentScroll;grid.RowsAdded-=GridRowsAdded;grid.RowsRemoved-=GridRowsRemoved;
                    if(nativeGridBar!=null){nativeGridBar.VisibleChanged-=ContentChanged;nativeGridBar.ValueChanged-=ContentChanged;}
                }
                else
                {
                    scrollPanel.Scroll-=ContentScroll;
                    scrollPanel.ControlAdded-=ScrollContentAdded;scrollPanel.ControlRemoved-=ScrollContentRemoved;
                    foreach(Control child in scrollPanel.Controls)ObserveScrollChild(child,false);
                }
            }
            base.Dispose(disposing);
        }
    }
}

