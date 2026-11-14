using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Reflection;using System.Windows.Forms;using PubgScore;
internal static class InteractionTests {
 static int count;static T Field<T>(object o,string n){return (T)o.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);}
 static object Call(object o,string n,params object[] args){return o.GetType().GetMethod(n,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,args);}
 static void Check(bool value,string message){if(!value)throw new Exception(message);count++;Console.WriteLine("PASS "+message);}
 static IEnumerable<Control> All(Control c){foreach(Control child in c.Controls){yield return child;foreach(var nested in All(child))yield return nested;}}
 static void BackgroundClick(Control c){typeof(Control).GetMethod("OnMouseDown",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(c,new object[]{new MouseEventArgs(MouseButtons.Left,1,2,2,0)});Application.DoEvents();}
 [STAThread]static int Main(){
 Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
 var fake=new TestGameClient(Path.Combine(Path.GetTempPath(),"pubg-interaction-"+Guid.NewGuid().ToString("N")+".json")){Administrator=true};
 using(var form=new MainForm(false,fake))using(var admin=new MainForm(true,fake)){
 try{
 form.Show();Application.DoEvents();
 Check(All(form).OfType<WrapToolbar>().Single().Controls.Count==0,"participant has no top buttons");
 Check(Field<Button>(form,"setupTab").Visible&&Field<Button[]>(form,"teamTabs").All(b=>b.Visible),"bottom setup and eight team tabs preserved");
 var inputs=Field<Dictionary<string,TextBox>>(form,"setupInputs");var name=inputs["t0.p0.name"];name.Focus();name.Text="Focus saved";int before=fake.Writes;
 BackgroundClick(Field<Label>(form,"notice"));
 Check(!name.Focused,"background click releases text input focus");
 Check(fake.View().State.Get("t0.p0.name")=="Focus saved"&&fake.Writes==before+1,"background click commits exactly once");
 var penalty=inputs["t0.p0.penalty"];penalty.Focus();penalty.Text="-2";BackgroundClick(Field<Label>(form,"notice"));
 Check(penalty.Focused&&fake.Writes==before+1,"invalid number remains focused and is not saved");
 penalty.Text="2";BackgroundClick(Field<Label>(form,"notice"));Check(!penalty.Focused&&fake.View().State.Get("t0.p0.penalty")=="2","corrected deduction saves and blurs");
 Field<Button[]>(form,"teamTabs")[0].PerformClick();Application.DoEvents();var grid=Field<DataGridView>(form,"grid");grid.CurrentCell=grid.Rows[0].Cells[2];grid.Focus();grid.BeginEdit(true);((TextBox)grid.EditingControl).Text="7";
 BackgroundClick(Field<Label>(form,"notice"));Check(!grid.ContainsFocus&&!grid.IsCurrentCellInEditMode,"background click releases grid editor focus");
 Check(fake.View().State.Get("t0.r1.p0.score")=="7","grid background blur preserves score commit");
 admin.Show();Application.DoEvents();Check(All(admin).OfType<WrapToolbar>().Single().Controls.OfType<Button>().Any(b=>b.Text=="게임 종료 · 방 삭제"),"administrator toolbar retained");
 using(var dialog=new AdminLoginDialog(fake)){
 dialog.Shown+=delegate {var boxes=dialog.Controls[0].Controls.OfType<TextBox>().ToArray();boxes[0].Focus();BackgroundClick(dialog.Controls[0].Controls.OfType<Label>().First());Check(!boxes[0].Focused,"login background click releases email focus");boxes[0].Text="test@example.test";boxes[1].Text="test-only";dialog.Controls[0].Controls.OfType<Button>().Single().PerformClick();};
 var result=dialog.ShowDialog(admin);Check(result==DialogResult.OK&&!dialog.Visible,"successful login closes modal before caller completion message");
 }
 Console.WriteLine("PASS: "+count+" interaction checks.");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{admin.Close();form.Close();}
 }}
}
