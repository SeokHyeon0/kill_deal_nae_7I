using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace PubgScore
{
    internal sealed class GitHubReleaseAsset
    {
        internal Version Version;
        internal string Tag,Name,DownloadUrl,Sha256;
        internal long Size;
        internal bool IsZip;
    }

    // GitHub supplies the asset's SHA256 and size, so a normal release does not
    // need a separate XML manifest. No updater or browser assembly is used here.
    internal static class GitHubReleaseUpdate
    {
        internal const string ApiUrl="https://api.github.com/repos/SeokHyeon0/kill_deal_nae_7I/releases/latest";
        internal const int MaxJsonLength=1024*1024;
        internal const int MaxAssetSize=32*1024*1024;
        const string Repository="https://github.com/SeokHyeon0/kill_deal_nae_7I";
        static readonly Regex StableTag=new Regex(@"\Av?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z",RegexOptions.CultureInvariant);
        static readonly Regex Digest=new Regex(@"\Asha256:([0-9a-fA-F]{64})\z",RegexOptions.CultureInvariant);

        internal static string AssetName(bool administrator)
        {return administrator?"KillDeal-BJ.exe":"KillDeal-Viewer.exe";}

        static Dictionary<string,object> ReadRoot(string json)
        {
            if(String.IsNullOrEmpty(json)||json.Length>MaxJsonLength)throw Invalid("GitHub 업데이트 응답 크기가 올바르지 않습니다.");
            try
            {
                var serializer=new JavaScriptSerializer {MaxJsonLength=MaxJsonLength,RecursionLimit=32};
                var root=serializer.DeserializeObject(json) as Dictionary<string,object>;
                if(root==null)throw Invalid("GitHub 업데이트 응답 형식이 올바르지 않습니다.");
                return root;
            }
            catch(InvalidDataException){throw;}
            catch(Exception error)
            {
                if(!(error is ArgumentException)&&!(error is InvalidOperationException))throw;
                throw new InvalidDataException("GitHub 업데이트 JSON을 읽을 수 없습니다.",error);
            }
        }

        static object Get(Dictionary<string,object> item,string key)
        {object value;return item.TryGetValue(key,out value)?value:null;}

        static string Text(Dictionary<string,object> item,string key)
        {return Get(item,key) as string;}

        static Version ReleaseVersion(Dictionary<string,object> root,out string tag)
        {
            object draft=Get(root,"draft"),preview=Get(root,"prerelease");
            if(!(draft is bool)||(bool)draft||!(preview is bool)||(bool)preview)throw Invalid("정식으로 게시된 GitHub Release만 사용할 수 있습니다.");
            tag=Text(root,"tag_name");
            var match=StableTag.Match(tag??"");int major,minor,patch;
            if(!match.Success||!Int32.TryParse(match.Groups[1].Value,NumberStyles.None,CultureInfo.InvariantCulture,out major)||!Int32.TryParse(match.Groups[2].Value,NumberStyles.None,CultureInfo.InvariantCulture,out minor)||!Int32.TryParse(match.Groups[3].Value,NumberStyles.None,CultureInfo.InvariantCulture,out patch))throw Invalid("GitHub Release 태그는 v2.1.2와 같은 정식 버전이어야 합니다.");
            return new Version(major,minor,patch,0);
        }

        internal static Version ReadVersion(string json)
        {string tag;return ReleaseVersion(ReadRoot(json),out tag);}

        static long Integer(Dictionary<string,object> item,string key)
        {
            object value=Get(item,key);
            if(value is int)return (int)value;
            if(value is long)return (long)value;
            throw Invalid("GitHub 업데이트 파일의 숫자 정보가 올바르지 않습니다.");
        }

        static GitHubReleaseAsset ReadAsset(Dictionary<string,object> item,Version version,string tag,string name,bool zip,bool allowLoopback)
        {
            if(Integer(item,"id")<=0||Text(item,"state")!="uploaded")throw Invalid("GitHub 업데이트 파일 업로드가 완료되지 않았습니다.");
            long size=Integer(item,"size");
            if(size<=0||size>MaxAssetSize)throw Invalid("GitHub 업데이트 파일 크기가 올바르지 않습니다.");
            var digest=Digest.Match(Text(item,"digest")??"");
            if(!digest.Success)throw Invalid("GitHub 업데이트 파일의 SHA256 정보가 없습니다.");
            string url=Text(item,"browser_download_url");Uri address;
            if(!Uri.TryCreate(url,UriKind.Absolute,out address)||address.UserInfo.Length!=0||address.Query.Length!=0||address.Fragment.Length!=0)throw Invalid("GitHub 업데이트 다운로드 주소가 올바르지 않습니다.");
            string path="/releases/download/"+tag+"/"+name;
            bool local=allowLoopback&&address.IsLoopback&&address.Scheme==Uri.UriSchemeHttp;
            // Compare the complete original URL to prevent encoded paths,
            // alternate repositories, query redirects and path normalization.
            string expected=local?address.GetLeftPart(UriPartial.Authority)+path:Repository+path;
            if(!String.Equals(url,expected,StringComparison.Ordinal))throw Invalid("등록한 GitHub 저장소의 해당 프로그램 파일만 업데이트할 수 있습니다.");
            return new GitHubReleaseAsset {Version=version,Tag=tag,Name=name,DownloadUrl=url,Sha256=digest.Groups[1].Value.ToLowerInvariant(),Size=size,IsZip=zip};
        }

        internal static GitHubReleaseAsset Parse(string json,bool administrator,bool allowLoopback)
        {
            var root=ReadRoot(json);string tag;Version version=ReleaseVersion(root,out tag);
            var assets=Get(root,"assets") as object[];
            if(assets==null)throw Invalid("GitHub Release에 업데이트 파일 목록이 없습니다.");
            string exe=AssetName(administrator),zip="KillDeal-"+(administrator?"BJ":"Viewer")+"-V"+version.ToString(3)+".zip";
            GitHubReleaseAsset executable=null,archive=null;
            foreach(object raw in assets)
            {
                var item=raw as Dictionary<string,object>;
                if(item==null)throw Invalid("GitHub Release의 파일 목록 형식이 올바르지 않습니다.");
                string name=Text(item,"name");
                if(String.Equals(name,exe,StringComparison.OrdinalIgnoreCase)&&name!=exe||String.Equals(name,zip,StringComparison.OrdinalIgnoreCase)&&name!=zip)throw Invalid("업데이트 파일 이름의 대소문자가 올바르지 않습니다.");
                if(name==exe)
                {
                    if(executable!=null)throw Invalid("같은 이름의 업데이트 실행 파일이 두 개 있습니다.");
                    executable=ReadAsset(item,version,tag,name,false,allowLoopback);
                }
                else if(name==zip)
                {
                    if(archive!=null)throw Invalid("같은 이름의 업데이트 ZIP 파일이 두 개 있습니다.");
                    archive=ReadAsset(item,version,tag,name,true,allowLoopback);
                }
            }
            // A ZIP may include a revised manual; prefer it when both formats
            // are published. The installer separately validates its contents.
            if(archive!=null)return archive;
            if(executable!=null)return executable;
            throw Invalid("GitHub Release에 해당 프로그램의 업데이트 파일이 없습니다.");
        }

        static InvalidDataException Invalid(string message){return new InvalidDataException(message);}
    }
}
