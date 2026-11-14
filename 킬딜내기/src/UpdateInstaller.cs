using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace PubgScore
{
    internal sealed class UpdateInstallPlan
    {
        public int ParentProcessId;
        public long ParentStartTicks;
        public string Role;
        public string TargetDirectory;
        public string ExecutableName;
        public string PdfName;
        public string ExpectedVersion;
        public string ExecutableSha256;
        public string PdfSha256;
        public string[] OriginalArguments;
        public int WorkerProcessId;
    }

    // Runs from a disposable copy of the current executable. It never elevates,
    // downloads files or accesses Firebase, credentials or other application data.
    internal static class UpdateInstaller
    {
        internal const string ApplyArgument="--apply-update";
        internal const string CleanupArgument="--update-cleanup";
        internal const string PlanFileName="plan.json";
        internal const string WorkerFileName="update-worker.exe";
        static readonly JavaScriptSerializer Serializer=new JavaScriptSerializer {MaxJsonLength=65536};

        internal static string CurrentRole
        {
            get
            {
#if ADMIN
                return "BJ";
#else
                return "Viewer";
#endif
            }
        }
        internal static string UpdatesRoot
        {
            get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PubgScoreFirebase","updates");}
        }
        internal static string ExecutableForRole(string role)
        {
            if(role=="BJ")return "킬딜내기_BJ.exe";
            if(role=="Viewer")return "킬딜내기_시청자.exe";
            throw new InvalidDataException("업데이트 프로그램 종류가 올바르지 않습니다.");
        }
        // GitHub normalizes non-ASCII asset names. A downloaded standalone
        // executable therefore has a fixed ASCII alias, while existing users
        // keep their original Korean executable name. Always replace/restart
        // the same filename; never create a second executable beside it.
        internal static string AssetExecutableForRole(string role)
        {
            if(role=="BJ")return "KillDeal-BJ.exe";
            if(role=="Viewer")return "KillDeal-Viewer.exe";
            throw new InvalidDataException("업데이트 프로그램 종류가 올바르지 않습니다.");
        }
        internal static bool IsExecutableForRole(string role,string name)
        {
            return String.Equals(name,ExecutableForRole(role),StringComparison.Ordinal)||
                String.Equals(name,AssetExecutableForRole(role),StringComparison.Ordinal);
        }
        internal static string PdfForRole(string role,Version version)
        {
            return "킬딜내기-"+(role=="BJ"?"BJ":"시청자")+"-사용방법-V"+version.ToString(3)+".pdf";
        }

        internal static bool TryHandle(string[] args)
        {
            if(args==null||args.Length==0||args[0]!=ApplyArgument)return false;
            string transaction=null;List<InstalledFile> applied=null;
            try
            {
                if(args.Length!=2)throw new InvalidDataException("업데이트 작업 인수가 올바르지 않습니다.");
                string planFile=RequirePlanPath(args[1],UpdatesRoot,CurrentRole);
                transaction=Path.GetDirectoryName(planFile);
                string workerPath=Path.Combine(transaction,WorkerFileName);
                if(!SamePath(typeof(UpdateInstaller).Assembly.Location,workerPath))throw new InvalidDataException("업데이트 작업 파일의 위치가 올바르지 않습니다.");
                var plan=ReadPlan(planFile);
                ValidatePlan(plan,transaction,UpdatesRoot,true);
                if(plan.ParentProcessId==Process.GetCurrentProcess().Id)throw new InvalidDataException("업데이트 대상 프로세스가 올바르지 않습니다.");
                WaitForParent(plan);
                applied=ApplyFilesCore(plan,transaction,UpdatesRoot);
                VerifyInstalledSignature(plan);
                plan.WorkerProcessId=Process.GetCurrentProcess().Id;
                File.WriteAllText(planFile,Serializer.Serialize(plan),new UTF8Encoding(false));
                var arguments=(plan.OriginalArguments??new string[0]).Concat(new[]{CleanupArgument,planFile}).ToArray();
                var start=new ProcessStartInfo(Path.Combine(plan.TargetDirectory,plan.ExecutableName),JoinArguments(arguments))
                {
                    UseShellExecute=false,WorkingDirectory=plan.TargetDirectory,CreateNoWindow=true
                };
                using(var restarted=Process.Start(start))
                    if(restarted==null)throw new IOException("업데이트된 프로그램을 실행할 수 없습니다.");
                Environment.ExitCode=0;
            }
            catch(Exception exception)
            {
                Exception failure=exception;
                if(applied!=null)
                {
                    var rollback=RestoreFiles(applied);
                    if(rollback.Count>0)failure=new AggregateException("프로그램 실행 실패 후 파일 복구 중 오류가 발생했습니다. 백업 파일을 보존했습니다.",new[]{exception}.Concat(rollback));
                }
                LogFailure(transaction,failure,UpdatesRoot);
                Environment.ExitCode=1;
                MessageBox.Show("업데이트를 완료하지 못했습니다. 기존 파일을 복구했거나 백업을 보존했습니다.\r\n"+failure.Message,
                    CurrentRole=="BJ"?"킬딜내기_BJ 업데이트":"킬딜내기_시청자 업데이트",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            }
            return true;
        }

        // Consume control arguments before another update check can copy them
        // into a new plan. Parsing must never remove rollback files itself.
        internal static string[] ConsumeCleanupArguments(string[] args,out string planFile)
        {
            planFile=null;
            if(args==null)return new string[0];
            var remaining=new List<string>();bool found=false,ambiguous=false;
            for(int index=0;index<args.Length;index++)
            {
                if(args[index]==CleanupArgument)
                {
                    if(found)ambiguous=true;
                    found=true;
                    if(index+1<args.Length)planFile=args[++index];
                }
                else remaining.Add(args[index]);
            }
            if(ambiguous)planFile=null;
            return remaining.ToArray();
        }

        // Call only after the new main window has been shown successfully.
        // Construction/startup failures therefore retain rollback files.
        internal static void CompletePrevious(string planFile)
        {
            if(planFile==null)return;
            string transaction=null;
            try
            {
                planFile=RequirePlanPath(planFile,UpdatesRoot,CurrentRole);
                transaction=Path.GetDirectoryName(planFile);
                var plan=ReadPlan(planFile);
                ValidatePlan(plan,transaction,UpdatesRoot,false);
                ValidateCleanupExecutable(plan,typeof(UpdateInstaller).Assembly.Location);
                if(!WorkerFinished(plan,transaction))return;
                CleanupFiles(plan,transaction,UpdatesRoot,typeof(UpdateInstaller).Assembly.Location);
            }
            catch(Exception exception){LogFailure(transaction,exception,UpdatesRoot);}
        }

        internal static UpdateInstallPlan ReadPlan(string planFile)
        {
            var info=new FileInfo(planFile);
            if(!info.Exists||info.Length>65536)throw new InvalidDataException("업데이트 작업 정보 파일을 읽을 수 없습니다.");
            var plan=Serializer.Deserialize<UpdateInstallPlan>(File.ReadAllText(planFile,Encoding.UTF8));
            if(plan==null)throw new InvalidDataException("업데이트 작업 정보가 비어 있습니다.");
            return plan;
        }
        internal static void ValidatePlan(UpdateInstallPlan plan,string directory,string updatesRoot,bool requirePayload)
        {
            if(plan==null||plan.Role!=CurrentRole)throw new InvalidDataException("현재 프로그램과 다른 종류의 업데이트입니다.");
            string transaction=RequireTransactionDirectory(directory,updatesRoot,plan.Role);
            if(plan.ParentProcessId<=0||plan.ParentStartTicks<=0)throw new InvalidDataException("업데이트 대상 프로세스 정보가 올바르지 않습니다.");
            Version version;
            if(!Version.TryParse(plan.ExpectedVersion,out version)||version.Major<0||version.Minor<0||version.Build<0||version.Revision<0||version.ToString(4)!=plan.ExpectedVersion)
                throw new InvalidDataException("업데이트 버전은 네 자리 숫자여야 합니다.");
            // An omitted manual is represented by an empty name and hash. Old
            // plans containing a canonical versioned manual remain supported.
            plan.PdfName=plan.PdfName??String.Empty;
            plan.PdfSha256=plan.PdfSha256??String.Empty;
            bool hasPdf=plan.PdfName.Length>0;
            if(hasPdf!=(plan.PdfSha256.Length>0))throw new InvalidDataException("사용방법 PDF 이름과 검증값은 함께 있어야 합니다.");
            if(!IsExecutableForRole(plan.Role,plan.ExecutableName)||(hasPdf&&plan.PdfName!=PdfForRole(plan.Role,version)))throw new InvalidDataException("업데이트 파일 이름이 올바르지 않습니다.");
            if(!IsHash(plan.ExecutableSha256)||(hasPdf&&!IsHash(plan.PdfSha256)))throw new InvalidDataException("업데이트 검증값이 올바르지 않습니다.");
            if(plan.OriginalArguments!=null&&(plan.OriginalArguments.Length>128||plan.OriginalArguments.Any(value=>value==null||value.Length>8192||value==ApplyArgument||value==CleanupArgument||value.IndexOf('\0')>=0)))
                throw new InvalidDataException("프로그램 실행 인수가 올바르지 않습니다.");
            if(String.IsNullOrWhiteSpace(plan.TargetDirectory)||!Path.IsPathRooted(plan.TargetDirectory))throw new InvalidDataException("업데이트 대상 폴더는 절대 경로여야 합니다.");
            plan.TargetDirectory=FullPath(plan.TargetDirectory);
            if(plan.TargetDirectory.StartsWith(@"\\",StringComparison.Ordinal)||!Directory.Exists(plan.TargetDirectory)||SamePath(plan.TargetDirectory,Path.GetPathRoot(plan.TargetDirectory))||Within(plan.TargetDirectory,updatesRoot))
                throw new InvalidDataException("업데이트 대상 폴더가 올바르지 않습니다.");
            EnsureNoReparseAncestors(plan.TargetDirectory);
            foreach(string name in PayloadNames(plan))
            {
                string target=Path.Combine(plan.TargetDirectory,name);
                if(Directory.Exists(target))throw new IOException("업데이트 대상 파일 위치에 같은 이름의 폴더가 있습니다.");
                if(File.Exists(target))EnsureRegularFile(target);
            }
            if(requirePayload)
            {
                if(version<=typeof(UpdateInstaller).Assembly.GetName().Version)throw new InvalidDataException("현재 버전보다 새로운 업데이트만 설치할 수 있습니다.");
                string payload=Path.Combine(transaction,"payload");
                if(!Directory.Exists(payload))throw new InvalidDataException("업데이트 파일 폴더가 없습니다.");
                EnsureNoReparseAncestors(payload);
                if(Directory.GetDirectories(payload).Length!=0||Directory.GetFiles(payload).Length!=(hasPdf?2:1))throw new InvalidDataException("업데이트 파일은 실행 파일 하나 또는 실행 파일과 사용방법 PDF 두 개여야 합니다.");
                RequireHash(Path.Combine(payload,plan.ExecutableName),plan.ExecutableSha256);
                if(hasPdf)RequireHash(Path.Combine(payload,plan.PdfName),plan.PdfSha256);
                UpdateSignature.Verify(Path.Combine(payload,plan.ExecutableName),plan.Role,version,hasPdf?Path.Combine(payload,plan.PdfName):null);
            }
        }
        static void VerifyInstalledSignature(UpdateInstallPlan plan)
        {
            UpdateSignature.Verify(Path.Combine(plan.TargetDirectory,plan.ExecutableName),plan.Role,Version.Parse(plan.ExpectedVersion),
                String.IsNullOrEmpty(plan.PdfName)?null:Path.Combine(plan.TargetDirectory,plan.PdfName));
        }
        static string[] PayloadNames(UpdateInstallPlan plan)
        {
            return String.IsNullOrEmpty(plan.PdfName)?new[]{plan.ExecutableName}:new[]{plan.ExecutableName,plan.PdfName};
        }
        internal static string RequirePlanPath(string value,string updatesRoot,string role)
        {
            if(String.IsNullOrWhiteSpace(value)||!Path.IsPathRooted(value))throw new InvalidDataException("업데이트 작업 정보 경로가 올바르지 않습니다.");
            string full=FullPath(value);
            if(Path.GetFileName(full)!=PlanFileName)throw new InvalidDataException("업데이트 작업 정보 파일 이름이 올바르지 않습니다.");
            RequireTransactionDirectory(Path.GetDirectoryName(full),updatesRoot,role);
            if(File.Exists(full))EnsureRegularFile(full);
            return full;
        }
        static string RequireTransactionDirectory(string value,string updatesRoot,string role)
        {
            ExecutableForRole(role);
            if(String.IsNullOrWhiteSpace(value)||!Path.IsPathRooted(value))throw new InvalidDataException("업데이트 임시 폴더 경로가 올바르지 않습니다.");
            string full=FullPath(value),expectedParent=Path.Combine(FullPath(updatesRoot),role);
            Guid transaction;
            if(!SamePath(Path.GetDirectoryName(full),expectedParent)||(!Guid.TryParseExact(Path.GetFileName(full),"N",out transaction)&&!Guid.TryParseExact(Path.GetFileName(full),"D",out transaction)))
                throw new InvalidDataException("허용된 업데이트 임시 폴더가 아닙니다.");
            if(!Directory.Exists(full))throw new DirectoryNotFoundException("업데이트 임시 폴더가 없습니다.");
            EnsureNoReparseAncestors(full);
            return full;
        }
        static void WaitForParent(UpdateInstallPlan plan)
        {
            try
            {
                using(var parent=Process.GetProcessById(plan.ParentProcessId))
                {
                    if(parent.StartTime.ToUniversalTime().Ticks!=plan.ParentStartTicks)return;
                    if(!parent.WaitForExit(30000))throw new TimeoutException("프로그램이 아직 실행 중입니다. 종료한 뒤 다시 업데이트하세요.");
                }
            }
            catch(ArgumentException){/* Original process has already exited. */}
        }
        static bool WorkerFinished(UpdateInstallPlan plan,string transaction)
        {
            if(plan.WorkerProcessId<=0)return true;
            try
            {
                using(var worker=Process.GetProcessById(plan.WorkerProcessId))
                {
                    if(!SamePath(worker.MainModule.FileName,Path.Combine(transaction,WorkerFileName)))return true;
                    return worker.WaitForExit(5000);
                }
            }
            catch(ArgumentException){return true;}
            catch(System.ComponentModel.Win32Exception){return false;}
            catch(InvalidOperationException){return true;}
        }

        internal static void ApplyFiles(UpdateInstallPlan plan,string directory)
        {
            ApplyFilesCore(plan,directory,UpdatesRoot);
        }
        // Explicit root is for offline tests; the worker always uses UpdatesRoot.
        internal static void ApplyFiles(UpdateInstallPlan plan,string directory,string updatesRoot)
        {
            ApplyFilesCore(plan,directory,updatesRoot);
        }
        sealed class InstalledFile
        {
            internal string Target,Backup,NewHash,OriginalHash;internal bool HadOriginal;
        }
        static List<InstalledFile> ApplyFilesCore(UpdateInstallPlan plan,string directory,string updatesRoot)
        {
            ValidatePlan(plan,directory,updatesRoot,true);
            string backupDirectory=Path.Combine(directory,"backup");
            if(Directory.Exists(backupDirectory)&&Directory.EnumerateFileSystemEntries(backupDirectory).Any())throw new IOException("기존 업데이트 백업이 있어 덮어쓰지 않았습니다.");
            Directory.CreateDirectory(backupDirectory);EnsureNoReparseAncestors(backupDirectory);
            var applied=new List<InstalledFile>();
            try
            {
                string[] names=PayloadNames(plan);
                string[] hashes=String.IsNullOrEmpty(plan.PdfName)?new[]{plan.ExecutableSha256}:new[]{plan.ExecutableSha256,plan.PdfSha256};
                for(int index=0;index<names.Length;index++)
                {
                    var item=new InstalledFile {Target=Path.Combine(plan.TargetDirectory,names[index]),Backup=Path.Combine(backupDirectory,names[index]),NewHash=hashes[index]};
                    string staged=Path.Combine(directory,"payload",names[index]);
                    RequireHash(staged,item.NewHash);
                    item.HadOriginal=File.Exists(item.Target);
                    // A sandbox may map AppData and the target to different
                    // backing volumes even when both paths begin with C:.
                    // Replace only between files beside the target itself.
                    string prepared=Path.Combine(plan.TargetDirectory,".killdeal-update-"+Guid.NewGuid().ToString("N")+".tmp");
                    try
                    {
                        File.Copy(staged,prepared,false);RequireHash(prepared,item.NewHash);
                        if(index==0)UpdateSignature.Verify(prepared,plan.Role,Version.Parse(plan.ExpectedVersion),
                            String.IsNullOrEmpty(plan.PdfName)?null:Path.Combine(directory,"payload",plan.PdfName));
                        if(item.HadOriginal)
                        {
                            item.OriginalHash=FileHash(item.Target);
                            File.Copy(item.Target,item.Backup,false);RequireHash(item.Backup,item.OriginalHash);
                            File.Replace(prepared,item.Target,null,true);
                        }
                        else File.Move(prepared,item.Target);
                        applied.Add(item);
                    }
                    finally{if(File.Exists(prepared))File.Delete(prepared);}
                    RequireHash(item.Target,item.NewHash);
                }
                VerifyInstalledSignature(plan);
                return applied;
            }
            catch(Exception exception)
            {
                var rollback=RestoreFiles(applied);
                if(rollback.Count>0)throw new AggregateException("업데이트 파일 복구 중 오류가 발생했습니다. 백업을 보존했습니다.",new[]{exception}.Concat(rollback));
                throw;
            }
        }
        static List<Exception> RestoreFiles(List<InstalledFile> applied)
        {
            var failures=new List<Exception>();
            for(int index=applied.Count-1;index>=0;index--)
            {
                var item=applied[index];
                try
                {
                    if(item.HadOriginal)
                    {
                        if(!File.Exists(item.Backup))throw new IOException("기존 파일 백업이 없습니다: "+Path.GetFileName(item.Backup));
                        RequireHash(item.Backup,item.OriginalHash);
                        string prepared=Path.Combine(Path.GetDirectoryName(item.Target),".killdeal-rollback-"+Guid.NewGuid().ToString("N")+".tmp");
                        try
                        {
                            File.Copy(item.Backup,prepared,false);RequireHash(prepared,item.OriginalHash);
                            if(File.Exists(item.Target))File.Replace(prepared,item.Target,null,true);
                            else File.Move(prepared,item.Target);
                        }
                        finally{if(File.Exists(prepared))File.Delete(prepared);}
                    }
                    else if(File.Exists(item.Target))
                    {
                        // Do not delete a file another process changed after installation.
                        RequireHash(item.Target,item.NewHash);File.Delete(item.Target);
                    }
                }
                catch(Exception exception){failures.Add(exception);}
            }
            return failures;
        }
        internal static void ValidateCleanupExecutable(UpdateInstallPlan plan,string executablePath)
        {
            if(!SamePath(executablePath,Path.Combine(plan.TargetDirectory,plan.ExecutableName)))throw new InvalidDataException("업데이트 정리를 요청한 실행 파일의 위치가 올바르지 않습니다.");
            RequireHash(executablePath,plan.ExecutableSha256);
            if(!String.IsNullOrEmpty(plan.PdfName))RequireHash(Path.Combine(plan.TargetDirectory,plan.PdfName),plan.PdfSha256);
            VerifyInstalledSignature(plan);
        }
        internal static void CleanupFiles(UpdateInstallPlan plan,string directory,string updatesRoot,string executablePath)
        {
            ValidatePlan(plan,directory,updatesRoot,false);
            ValidateCleanupExecutable(plan,executablePath);
            // Keep the user's existing manual when no replacement is shipped.
            if(!String.IsNullOrEmpty(plan.PdfName))
            {
                Version current=Version.Parse(plan.ExpectedVersion);
                string role=plan.Role=="BJ"?"BJ":"시청자";
                var ownPdf=new Regex("^킬딜내기-"+role+"-사용방법-V([0-9]+\\.[0-9]+\\.[0-9]+)\\.pdf$",RegexOptions.CultureInvariant);
                foreach(string file in Directory.GetFiles(plan.TargetDirectory,"*.pdf",SearchOption.TopDirectoryOnly))
                {
                    var match=ownPdf.Match(Path.GetFileName(file));Version previous;
                    if(!match.Success||SamePath(file,Path.Combine(plan.TargetDirectory,plan.PdfName))||!Version.TryParse(match.Groups[1].Value,out previous)||previous>=new Version(current.Major,current.Minor,current.Build))continue;
                    EnsureRegularFile(file);File.Delete(file);
                }
            }
            // Check the tree before recursive deletion so links can never expand it.
            EnsureNoReparseTree(directory);
            Directory.Delete(RequireTransactionDirectory(directory,updatesRoot,plan.Role),true);
        }
        static bool IsHash(string value){return value!=null&&Regex.IsMatch(value,"\\A[0-9a-fA-F]{64}\\z",RegexOptions.CultureInvariant);}
        static string FileHash(string file)
        {
            using(var stream=File.OpenRead(file))using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","");
        }
        static void RequireHash(string file,string expected)
        {
            EnsureRegularFile(file);
            if(!String.Equals(FileHash(file),expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("업데이트 파일 검증에 실패했습니다: "+Path.GetFileName(file));
        }
        static void EnsureRegularFile(string file)
        {
            if(!File.Exists(file))throw new FileNotFoundException("업데이트 파일이 없습니다.",file);
            var attributes=File.GetAttributes(file);
            if((attributes&(FileAttributes.Directory|FileAttributes.ReparsePoint))!=0)throw new InvalidDataException("업데이트 파일에 링크를 사용할 수 없습니다.");
        }
        static void EnsureNoReparseAncestors(string directory)
        {
            var current=new DirectoryInfo(FullPath(directory));
            while(current!=null)
            {
                if(current.Exists&&(current.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("업데이트 경로에 링크 폴더를 사용할 수 없습니다.");
                current=current.Parent;
            }
        }
        static void EnsureNoReparseTree(string directory)
        {
            EnsureNoReparseAncestors(directory);
            foreach(string entry in Directory.GetFileSystemEntries(directory))
            {
                var attributes=File.GetAttributes(entry);
                if((attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("업데이트 정리 경로에 링크가 있습니다.");
                if((attributes&FileAttributes.Directory)!=0)EnsureNoReparseTree(entry);
            }
        }
        static string FullPath(string value){return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);}
        static bool SamePath(string left,string right){return String.Equals(FullPath(left),FullPath(right),StringComparison.OrdinalIgnoreCase);}
        static bool Within(string candidate,string parent)
        {
            string child=FullPath(candidate),root=FullPath(parent);
            return String.Equals(child,root,StringComparison.OrdinalIgnoreCase)||child.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        }
        internal static string JoinArguments(IEnumerable<string> values)
        {
            return String.Join(" ",values.Select(QuoteArgument).ToArray());
        }
        internal static string QuoteArgument(string value)
        {
            if(value==null)throw new ArgumentNullException("value");
            var result=new StringBuilder("\"");int slashes=0;
            foreach(char character in value)
            {
                if(character=='\\'){slashes++;continue;}
                if(character=='\"'){result.Append('\\',slashes*2+1);result.Append(character);slashes=0;continue;}
                result.Append('\\',slashes);slashes=0;result.Append(character);
            }
            result.Append('\\',slashes*2);result.Append('\"');return result.ToString();
        }
        static void LogFailure(string transaction,Exception exception,string updatesRoot)
        {
            try
            {
                string roleDirectory=Path.Combine(updatesRoot,CurrentRole);Directory.CreateDirectory(roleDirectory);EnsureNoReparseAncestors(roleDirectory);
                string file=Path.Combine(roleDirectory,"update-error.log");
                if(File.Exists(file)&&new FileInfo(file).Length>1024*1024)File.Delete(file);
                File.AppendAllText(file,DateTime.Now.ToString("s",CultureInfo.InvariantCulture)+" "+exception.GetType().Name+" HResult=0x"+exception.HResult.ToString("X8",CultureInfo.InvariantCulture)+"\r\n"+exception.ToString()+"\r\n",Encoding.UTF8);
            }
            catch(Exception){/* Logging must not hide the original installation failure. */}
        }
    }
}
