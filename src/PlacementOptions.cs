using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PubgScore
{
    public static class PlacementOptions
    {
        static readonly string[] killValues={"","800","500","300"};
        static readonly string[] killOnlyValues={"","8","5","3"};
        static readonly string[] matchValues={"","10","6","5","4","3","2","1","-1","-2","-3","-4","-5"};
        static readonly string[] emptyValues={""};
        internal static string[] SharedValues(string mode)
        {return mode=="match"?matchValues:mode=="kills"||mode=="damage"?killValues:mode=="kill"||mode=="killdeal"?killOnlyValues:emptyValues;}
        public static string[] Values(string mode)
        {
            return (string[])SharedValues(mode).Clone();
        }
        public static string[] Ranks(string mode)
        {
            return mode=="match"?new[]{"1등","2등","3등","4등","5등","6등","7~8등","9~10등","11등","12등","13등","14~16등"}:GameModes.IsValid(mode)?new[]{"에, 미, 태, 론, 비","사녹","파라모, 카라킨"}:new string[0];
        }
    }
    public sealed partial class MainForm
    {
        readonly TableLayoutPanel placementTable=new TableLayoutPanel {Dock=DockStyle.Top,ColumnCount=2,Margin=Padding.Empty,BackColor=Color.White,CellBorderStyle=TableLayoutPanelCellBorderStyle.Single};
        string placementMode=null;
        void RefreshPlacementOptions()
        {
            if(placementMode==view.Mode)return;
            if(grid.IsCurrentCellInEditMode&&grid.CurrentCell.ColumnIndex==1)grid.CancelEdit();
            placementMode=view.Mode;
            bool showPoints=GameModes.IsValid(view.Mode);
            placementTable.Parent.Visible=showPoints;
            overview.ColumnStyles[3].Width=showPoints?158:0;
            var values=PlacementOptions.SharedValues(view.Mode);
            var column=(DataGridViewComboBoxColumn)grid.Columns[1];column.Items.Clear();column.Items.AddRange(values);
            placementTable.SuspendLayout();
            foreach(Control control in placementTable.Controls.Cast<Control>().ToArray()){placementTable.Controls.Remove(control);control.Dispose();}
            placementTable.RowStyles.Clear();placementTable.ColumnStyles.Clear();bool mapNames=view.Mode!="match";placementTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,mapNames?70:50));placementTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,mapNames?30:50));
            var ranks=PlacementOptions.Ranks(view.Mode);placementTable.RowCount=ranks.Length+1;
            placementTable.Height=28+ranks.Length*16;
            placementTable.RowStyles.Add(new RowStyle(SizeType.Absolute,26));
            var title=SheetLabel(view.Mode=="match"?"경쟁 등수 점수":showPoints?GameModes.Name(view.Mode)+" 치킨 점수":"방식 선택 전",Color.FromArgb(239,226,244));title.Font=UiFont(8F,FontStyle.Bold);placementTable.Controls.Add(title,0,0);placementTable.SetColumnSpan(title,2);
            for(int i=0;i<ranks.Length;i++){
                placementTable.RowStyles.Add(new RowStyle(SizeType.Absolute,15));
                var rank=SheetLabel(ranks[i],Color.White);rank.Font=UiFont(8F);
                var score=SheetLabel(values[i+1],Color.White);score.Font=UiFont(8F,FontStyle.Bold);
                placementTable.Controls.Add(rank,0,i+1);placementTable.Controls.Add(score,1,i+1);
            }
            placementTable.ResumeLayout();
            // CancelEdit can keep the reusable combo editor active. Refresh it
            // here as well so a remote mode/reset never leaves the old list open.
            var editor=grid.EditingControl as ComboBox;
            if(editor!=null&&grid.CurrentCell!=null&&grid.CurrentCell.ColumnIndex==1)ConfigureChickenEditor(editor);
        }
        void ConfigureChickenEditor(ComboBox combo)
        {
            var values=PlacementOptions.SharedValues(view.Mode);string current=Convert.ToString(grid.CurrentCell.Value);
            combo.Items.Clear();combo.Items.AddRange(values);combo.SelectedIndex=Array.IndexOf(values,current);
        }
        void KeepStoredChickenVisible(DataGridViewComboBoxCell cell,string value)
        {
            // Old records stay visible, but the editing dropdown offers only the current mode.
            var values=PlacementOptions.SharedValues(view.Mode);bool legacy=Array.IndexOf(values,value)<0;
            bool matches=cell.Items.Count==values.Length+(legacy?1:0);
            for(int i=0;matches&&i<values.Length;i++)matches=Object.Equals(cell.Items[i],values[i]);
            if(matches&&(!legacy||Object.Equals(cell.Items[values.Length],value)))return;
            cell.Items.Clear();cell.Items.AddRange(values);if(legacy)cell.Items.Add(value);
        }
    }
}
