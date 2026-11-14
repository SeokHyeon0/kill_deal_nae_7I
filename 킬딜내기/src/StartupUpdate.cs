using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;
using AutoUpdaterDotNET;

namespace PubgScore
{
    internal static class StartupUpdate
    {
        internal const string Repository="https://github.com/SeokHyeon0/kill_deal_nae_7I";
        internal static string Role(bool administrator){return administrator?"BJ":"Viewer";}
        internal static string Name(bool administrator){return administrator?"킬딜내기_BJ":"킬딜내기_시청자";}
        internal static string PdfName(bool administrator,Version version){return "킬딜내기-"+(administrator?"BJ":"시청자")+"-사용방법-V"+version.ToString(3)+".pdf";}
        internal static string ZipName(bool administrator,Version version){return "KillDeal-"+(administrator?"BJ":"Viewer")+"-V"+version.ToString(3)+".zip";}
        internal static string Feed(bool administrator){return Repository+"/releases/latest/download/"+(administrator?"bj-update.xml":"viewer-update.xml");}
        internal static string CacheRoot(bool administrator){return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PubgScoreFirebase","updates",Role(administrator));}
        internal static void Log(bool administrator,string message)
        {
            try{string root=CacheRoot(administrator);Directory.CreateDirectory(root);string file=Path.Combine(root,"startup-update.log");if(File.Exists(file)&&new FileInfo(file).Length>65536)File.Delete(file);File.AppendAllText(file,DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture)+" "+message+Environment.NewLine,Encoding.UTF8);}catch{}
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool Run(bool administrator,string[] originalArguments)
        {
            try
            {
                using(var form=new UpdateStartupForm(administrator,originalArguments))
                {Application.Run(form);return form.ContinueToApplication;}
            }
            catch(Exception exception){Log(administrator,"CHECK_FAILED "+exception.GetType().Name+": "+exception.Message);return true;}
        }
        internal static UpdateInfoEventArgs Parse(string xml)
        {
            var settings=new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=262144};
            using(var source=new StringReader(xml.TrimStart('\uFEFF')))
            using(var reader=XmlReader.Create(source,settings))return (UpdateInfoEventArgs)new XmlSerializer(typeof(UpdateInfoEventArgs)).Deserialize(reader);
        }
        // The usual startup check must not load AutoUpdater.NET, its serializer
        // assembly or optional UI dependencies for the rest of the score session.
        internal static Version ReadFeedVersion(string xml)
        {
            var settings=new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=262144};
            var document=new XmlDocument {XmlResolver=null};
            using(var source=new StringReader(xml.TrimStart('\uFEFF')))
            using(var reader=XmlReader.Create(source,settings))document.Load(reader);
            var root=document.DocumentElement;
            if(root==null||root.Name!="item"||root.NamespaceURI.Length!=0)throw new InvalidDataException("업데이트 안내 형식이 올바르지 않습니다.");
            var nodes=root.SelectNodes("version");Version version;
            if(nodes.Count!=1||!Version.TryParse(nodes[0].InnerText,out version)||version.Build<0||version.Revision<0)throw new InvalidDataException("업데이트 버전 형식이 올바르지 않습니다.");
            return version;
        }
        internal static Version Validate(UpdateInfoEventArgs info,bool administrator,bool allowLoopback)
        {
            Version version;if(info==null||!Version.TryParse(info.CurrentVersion,out version)||version.Build<0||version.Revision<0)throw new InvalidDataException("업데이트 버전 형식이 올바르지 않습니다.");
            Uri address;if(!Uri.TryCreate(info.DownloadURL,UriKind.Absolute,out address))throw new InvalidDataException("업데이트 다운로드 주소가 없습니다.");
            bool local=allowLoopback&&address.IsLoopback&&address.Scheme==Uri.UriSchemeHttp;
            string prefix=Repository+"/releases/download/";
            if(!local&&(address.Scheme!=Uri.UriSchemeHttps||!address.AbsoluteUri.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("등록한 GitHub 저장소의 파일만 업데이트할 수 있습니다.");
            if(address.UserInfo.Length!=0||address.Fragment.Length!=0||!Uri.UnescapeDataString(address.AbsolutePath).EndsWith("/"+ZipName(administrator,version),StringComparison.Ordinal))throw new InvalidDataException("다른 종류의 프로그램 업데이트 파일입니다.");
            if(info.ExecutablePath!=Name(administrator)+".exe"||!String.IsNullOrEmpty(info.InstallerArgs))throw new InvalidDataException("업데이트 실행 파일 정보가 일치하지 않습니다.");
            if(info.CheckSum==null||!String.Equals(info.CheckSum.HashingAlgorithm,"SHA256",StringComparison.OrdinalIgnoreCase)||!Regex.IsMatch(info.CheckSum.Value??"","\\A[0-9a-fA-F]{64}\\z"))throw new InvalidDataException("업데이트 SHA256 검증 정보가 없습니다.");
            return version;
        }
        internal static string Sha256(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        internal static string Sha256File(string file){using(var stream=File.OpenRead(file))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
        internal static UpdateInstallPlan Prepare(byte[] archive,UpdateInfoEventArgs info,bool administrator,string transaction,string targetDirectory,string[] arguments)
        {
            return PrepareZip(archive,new Version(info.CurrentVersion),info.CheckSum.Value,administrator,transaction,targetDirectory,arguments,Name(administrator)+".exe");
        }
        internal static UpdateInstallPlan PrepareRelease(byte[] download,GitHubReleaseAsset asset,bool administrator,string transaction,string targetDirectory,string[] arguments,string executableName)
        {
            if(asset==null||download==null||download.LongLength!=asset.Size)throw new InvalidDataException("다운로드한 파일 크기가 GitHub 정보와 다릅니다.");
            if(!UpdateInstaller.IsExecutableForRole(Role(administrator),executableName))throw new InvalidDataException("실행 중인 프로그램 파일 이름을 확인하세요.");
            if(!String.Equals(Sha256(download),asset.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("다운로드 파일의 SHA256이 일치하지 않습니다.");
            if(asset.IsZip)return PrepareZip(download,asset.Version,asset.Sha256,administrator,transaction,targetDirectory,arguments,executableName);
            string payload=Path.Combine(transaction,"payload");Directory.CreateDirectory(payload);
            using(var output=new FileStream(Path.Combine(payload,executableName),FileMode.CreateNew,FileAccess.Write,FileShare.None))output.Write(download,0,download.Length);
            return CreatePlan(asset.Version,administrator,transaction,targetDirectory,arguments,executableName,false);
        }
        internal static UpdateInstallPlan PrepareZip(byte[] archive,Version version,string checksum,bool administrator,string transaction,string targetDirectory,string[] arguments,string executableName)
        {
            if(!UpdateInstaller.IsExecutableForRole(Role(administrator),executableName))throw new InvalidDataException("실행 중인 프로그램 파일 이름을 확인하세요.");
            if(!String.Equals(Sha256(archive),checksum,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("다운로드 파일의 SHA256이 일치하지 않습니다.");
            string exe=Name(administrator)+".exe",assetExe=UpdateInstaller.AssetExecutableForRole(Role(administrator)),pdf=PdfName(administrator,version),payload=Path.Combine(transaction,"payload");bool includeManual;
            using(var memory=new MemoryStream(archive,false))using(var zip=new ZipArchive(memory,ZipArchiveMode.Read))
            {
                if(zip.Entries.Count<1||zip.Entries.Count>2)throw new InvalidDataException("업데이트 ZIP에는 실행 파일 하나와 선택적인 사용방법 PDF만 있어야 합니다.");
                var names=new HashSet<string>(StringComparer.Ordinal);long total=0;int executables=0;
                foreach(var entry in zip.Entries)
                {
                    if((entry.FullName!=exe&&entry.FullName!=assetExe&&entry.FullName!=pdf)||!names.Add(entry.FullName))throw new InvalidDataException("업데이트 ZIP의 파일 종류 또는 경로가 올바르지 않습니다.");
                    if(entry.FullName==exe||entry.FullName==assetExe)executables++;
                    if(entry.Length<=0||entry.Length>67108864-total)throw new InvalidDataException("업데이트 파일 크기가 올바르지 않습니다.");total+=entry.Length;
                }
                if(executables!=1)throw new InvalidDataException("업데이트 ZIP에는 해당 실행 파일이 정확히 하나 있어야 합니다.");
                includeManual=names.Contains(pdf);
                Directory.CreateDirectory(payload);
                byte[] buffer=new byte[65536];long extracted=0;
                foreach(var entry in zip.Entries)using(var input=entry.Open())using(var output=new FileStream(Path.Combine(payload,entry.FullName==pdf?pdf:executableName),FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {
                    long copied=0;int count;
                    while((count=input.Read(buffer,0,buffer.Length))>0)
                    {
                        if(count>entry.Length-copied||count>67108864-extracted)throw new InvalidDataException("ZIP의 실제 파일 크기가 안내 정보와 다릅니다.");
                        output.Write(buffer,0,count);copied+=count;extracted+=count;
                    }
                    if(copied!=entry.Length)throw new InvalidDataException("ZIP의 실제 파일 크기가 안내 정보와 다릅니다.");
                }
            }
            File.WriteAllBytes(Path.Combine(transaction,"download.zip"),archive);
            return CreatePlan(version,administrator,transaction,targetDirectory,arguments,executableName,includeManual);
        }
        static UpdateInstallPlan CreatePlan(Version version,bool administrator,string transaction,string targetDirectory,string[] arguments,string executableName,bool includeManual)
        {
            string payload=Path.Combine(transaction,"payload"),pdf=PdfName(administrator,version);
            if(version<=typeof(Program).Assembly.GetName().Version)throw new InvalidDataException("현재 버전보다 새로운 업데이트만 설치할 수 있습니다.");
            UpdateSignature.Verify(Path.Combine(payload,executableName),Role(administrator),version,includeManual?Path.Combine(payload,pdf):null);
            var assembly=AssemblyName.GetAssemblyName(Path.Combine(payload,executableName));
            if(assembly.Name!=Name(administrator)||assembly.Version!=version)throw new InvalidDataException("프로그램 종류 또는 버전이 GitHub 안내 정보와 다릅니다.");
            var process=Process.GetCurrentProcess();
            return new UpdateInstallPlan {ParentProcessId=process.Id,ParentStartTicks=process.StartTime.ToUniversalTime().Ticks,Role=Role(administrator),TargetDirectory=Path.GetFullPath(targetDirectory),ExecutableName=executableName,PdfName=includeManual?pdf:"",ExpectedVersion=version.ToString(),ExecutableSha256=Sha256File(Path.Combine(payload,executableName)),PdfSha256=includeManual?Sha256File(Path.Combine(payload,pdf)):"",OriginalArguments=arguments??new string[0]};
        }
        static string Descriptor(GitHubReleaseAsset asset,bool administrator)
        {
            var buffer=new StringBuilder();
            using(var writer=XmlWriter.Create(buffer,new XmlWriterSettings {OmitXmlDeclaration=true}))
            {
                writer.WriteStartElement("item");writer.WriteElementString("version",asset.Version.ToString(4));writer.WriteElementString("url",asset.DownloadUrl);
                writer.WriteElementString("executable",Name(administrator)+".exe");writer.WriteElementString("mandatory","false");
                writer.WriteStartElement("checksum");writer.WriteAttributeString("algorithm","SHA256");writer.WriteString(asset.Sha256);writer.WriteEndElement();writer.WriteEndElement();
            }
            return buffer.ToString();
        }
        internal static async Task<byte[]> Download(Uri address,int maximum,int timeoutMilliseconds,Action<int> progress)
        {
            return await Task.Run(delegate
            {
                var request=(HttpWebRequest)WebRequest.Create(address);request.UserAgent="KillDeal-Desktop-Updater";request.Timeout=timeoutMilliseconds;request.ReadWriteTimeout=timeoutMilliseconds;request.AllowAutoRedirect=true;request.CachePolicy=new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                if(address.Host.Equals("api.github.com",StringComparison.OrdinalIgnoreCase)){request.Accept="application/vnd.github+json";request.Headers["X-GitHub-Api-Version"]="2022-11-28";}
                using(var timer=new System.Threading.Timer(delegate(object ignored){request.Abort();},null,timeoutMilliseconds,System.Threading.Timeout.Infinite))
                using(var response=(HttpWebResponse)request.GetResponse())
                {
                    if(response.StatusCode!=HttpStatusCode.OK||response.ContentLength>maximum)throw new InvalidDataException("업데이트 다운로드 응답 또는 크기가 올바르지 않습니다.");
                    if(!address.IsLoopback&&response.ResponseUri.Scheme!=Uri.UriSchemeHttps)throw new InvalidDataException("보안 연결에서만 업데이트를 받을 수 있습니다.");
                    using(var input=response.GetResponseStream())using(var output=new MemoryStream())
                    {
                        byte[] buffer=new byte[65536];int count;
                        while((count=input.Read(buffer,0,buffer.Length))>0){if(output.Length+count>maximum)throw new InvalidDataException("업데이트 파일이 허용 크기를 초과했습니다.");output.Write(buffer,0,count);if(progress!=null&&response.ContentLength>0)progress((int)(output.Length*100/response.ContentLength));}
                        return output.ToArray();
                    }
                }
            });
        }
        sealed class NoPersistence:IPersistenceProvider
        {
            public Version GetSkippedVersion(){return null;}public DateTime? GetRemindLater(){return null;}
            public void SetSkippedVersion(Version value){}public void SetRemindLater(DateTime? value){}
        }
        internal sealed class UpdateStartupForm:Form
        {
            readonly bool administrator;readonly string[] arguments;readonly Label status=new Label();readonly DialogProgress progress=new DialogProgress();bool finished,handling;string xml;GitHubReleaseAsset releaseAsset;
            internal bool ContinueToApplication=true;
            internal UpdateStartupForm(bool admin,string[] originalArguments)
            {
                administrator=admin;arguments=originalArguments??new string[0];Text=Name(admin);Font=new Font("Malgun Gothic",9F);StartPosition=FormStartPosition.CenterScreen;ClientSize=new Size(500,182);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ControlBox=false;Opacity=0;ShowInTaskbar=false;BackColor=DialogPalette.Surface;ForeColor=DialogPalette.Text;
                var badge=new DialogIconBadge(false){Location=new Point(26,26)};
                var title=new Label {Text="업데이트 확인",AutoSize=false,Font=new Font("Malgun Gothic",12F,FontStyle.Bold),Location=new Point(84,24),Size=new Size(390,28)};
                status.Text="새 버전을 확인하고 있습니다…";status.ForeColor=DialogPalette.Muted;status.SetBounds(84,56,390,36);progress.SetBounds(26,138,448,10);progress.Style=ProgressBarStyle.Marquee;
                var progressCaption=new Label {Text="처리 중",ForeColor=DialogPalette.Muted,Location=new Point(26,108),Size=new Size(336,22)};
                var percentage=new Label {Text="",ForeColor=DialogPalette.Accent,Font=new Font("Malgun Gothic",9F,FontStyle.Bold),TextAlign=ContentAlignment.MiddleRight,Location=new Point(374,108),Size=new Size(100,22)};
                Action renderProgress=delegate {bool downloading=status.Text=="새 버전을 다운로드하고 있습니다…";progressCaption.Text=progress.Style==ProgressBarStyle.Marquee?"처리 중":downloading?"다운로드 중":"";percentage.Text=progress.Style==ProgressBarStyle.Continuous&&downloading?progress.Value+"%":"";};
                progress.DisplayChanged+=delegate {renderProgress();};status.TextChanged+=delegate {renderProgress();};
                Controls.AddRange(new Control[]{badge,title,status,progressCaption,percentage,progress});try{Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);}catch{}
                Shown+=async delegate {await BeginCheck();};FormClosing+=delegate(object sender,FormClosingEventArgs e){if(!finished&&e.CloseReason!=CloseReason.WindowsShutDown&&e.CloseReason!=CloseReason.TaskManagerClosing)e.Cancel=true;};
            }
            async Task BeginCheck()
            {
                Exception failure=null;
                try
                {
                    ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
                    string feed=GitHubReleaseUpdate.ApiUrl;
#if UPDATE_TESTING
                    for(int i=0;i+1<arguments.Length;i++)if(arguments[i]=="--update-test-feed")feed=arguments[i+1];
#endif
                    var address=new Uri(feed+(feed.IndexOf('?')>=0?"&":"?")+"check="+DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
                    string json=Encoding.UTF8.GetString(await Download(address,1048576,8000,null));
                    if(GitHubReleaseUpdate.ReadVersion(json)<=typeof(Program).Assembly.GetName().Version)
                    {Log(administrator,"CURRENT "+typeof(Program).Assembly.GetName().Version);await Finish("최신 버전입니다. 프로그램을 시작합니다.");return;}
                    bool loopback=false;
#if UPDATE_TESTING
                    loopback=true;
#endif
                    releaseAsset=GitHubReleaseUpdate.Parse(json,administrator,loopback);xml=Descriptor(releaseAsset,administrator);
                    CheckAvailableVersion();
                    if(!handling&&!finished)await Finish("업데이트 확인을 마쳤습니다.");
                }
                catch(Exception exception){failure=exception;}
                if(failure!=null)await Failed(failure);
            }
            [MethodImpl(MethodImplOptions.NoInlining)]
            void CheckAvailableVersion()
            {
                AutoUpdater.AppTitle=Name(administrator);AutoUpdater.InstalledVersion=typeof(Program).Assembly.GetName().Version;AutoUpdater.Mandatory=false;AutoUpdater.ReportErrors=false;AutoUpdater.RunUpdateAsAdmin=false;AutoUpdater.PersistenceProvider=new NoPersistence();AutoUpdater.Synchronous=true;
                AutoUpdater.ParseUpdateInfoEvent+=OnParse;AutoUpdater.CheckForUpdateEvent+=OnCheck;
                try{AutoUpdater.Start((string)null,typeof(Program).Assembly);}finally{AutoUpdater.ParseUpdateInfoEvent-=OnParse;AutoUpdater.CheckForUpdateEvent-=OnCheck;}
            }
            void OnParse(ParseUpdateInfoEventArgs data){data.UpdateInfo=Parse(xml);}
            async void OnCheck(UpdateInfoEventArgs info)
            {
                handling=true;
                Exception failure=null;
                try
                {
                    if(info.Error!=null)throw info.Error;
                    if(!info.IsUpdateAvailable){Log(administrator,"CURRENT "+typeof(Program).Assembly.GetName().Version);await Finish("최신 버전입니다. 프로그램을 시작합니다.");return;}
                    if(releaseAsset==null||info.CurrentVersion!=releaseAsset.Version.ToString(4)||info.DownloadURL!=releaseAsset.DownloadUrl||info.ExecutablePath!=Name(administrator)+".exe"||!String.IsNullOrEmpty(info.InstallerArgs)||info.CheckSum==null||info.CheckSum.HashingAlgorithm!="SHA256"||!String.Equals(info.CheckSum.Value,releaseAsset.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("GitHub 업데이트 정보가 일치하지 않습니다.");
                    Version version=releaseAsset.Version;
                    ShowInTaskbar=true;Opacity=1;Activate();
                    string install=Path.GetDirectoryName(typeof(Program).Assembly.Location);
                    string executableName=Path.GetFileName(typeof(Program).Assembly.Location);
                    string probe=Path.Combine(install,".killdeal-update-"+Guid.NewGuid().ToString("N")+".tmp");
                    using(var stream=new FileStream(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.DeleteOnClose))stream.WriteByte(0);
                    status.Text="새 버전을 다운로드하고 있습니다…";progress.Style=ProgressBarStyle.Continuous;progress.Value=0;
                    byte[] package=await Download(new Uri(releaseAsset.DownloadUrl),33554432,600000,delegate(int percent){try{BeginInvoke((MethodInvoker)delegate {if(!IsDisposed)progress.Value=Math.Max(0,Math.Min(100,percent));});}catch(InvalidOperationException){}});
                    status.Text="다운로드한 파일을 검증하고 있습니다…";progress.Style=ProgressBarStyle.Marquee;
                    string transaction=Path.Combine(CacheRoot(administrator),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(transaction);
                    try
                    {
                        var plan=await Task.Run(()=>PrepareRelease(package,releaseAsset,administrator,transaction,install,arguments,executableName));
                        string file=Path.Combine(transaction,"plan.json"),worker=Path.Combine(transaction,"update-worker.exe");
                        File.WriteAllText(file,new JavaScriptSerializer().Serialize(plan),Encoding.UTF8);File.Copy(typeof(Program).Assembly.Location,worker);
                        status.Text="프로그램을 새 버전으로 다시 시작합니다…";
                        var start=new ProcessStartInfo(worker,"--apply-update "+Quote(file)) {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=transaction};
                        using(var child=Process.Start(start)){if(child==null)throw new IOException("업데이트 교체 프로그램을 시작하지 못했습니다.");}
                        Log(administrator,"STAGED "+version);ContinueToApplication=false;finished=true;Close();
                    }
                    catch{try{Directory.Delete(transaction,true);}catch{}throw;}
                }
                catch(Exception exception){failure=exception;}
                if(failure!=null)await Failed(failure);
            }
            async Task Failed(Exception exception)
            {
                Log(administrator,"UPDATE_SKIPPED "+exception.GetType().Name+": "+exception.Message);
                if(exception is UpdateSignatureException)
                {
                    ShowInTaskbar=true;Opacity=1;
                    DesignMessageBox.Show(this,"업데이트 파일의 제작자 서명을 확인할 수 없어 설치를 중단했습니다.\r\n기존 프로그램은 변경하지 않았습니다.\r\n\r\n"+exception.Message,
                        "업데이트 보안 확인",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                }
                await Finish("업데이트를 확인하지 못해 현재 프로그램으로 시작합니다.");
            }
            async Task Finish(string message)
            {
                if(finished)return;status.Text=message;progress.Style=ProgressBarStyle.Continuous;progress.Value=100;if(Opacity>0)await Task.Delay(800);finished=true;Close();
            }
        }
        internal static string Quote(string value)
        {
            var text=new StringBuilder("\"");int slashes=0;
            foreach(char c in value??""){if(c=='\\'){slashes++;continue;}if(c=='\"'){text.Append('\\',slashes*2+1);text.Append(c);slashes=0;continue;}text.Append('\\',slashes);slashes=0;text.Append(c);}text.Append('\\',slashes*2);text.Append('"');return text.ToString();
        }
    }
}
