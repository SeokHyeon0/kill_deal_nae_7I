using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using PubgScore;

class GitHubReleaseUpdateTests
{
    static int checks;
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer {MaxJsonLength=2*1024*1024};
    static readonly string Hash=new string('a',64);
    static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;}
    static void Reject(Action work,string label)
    {try{work();}catch(InvalidDataException){checks++;return;}throw new Exception("accepted "+label);}
    static Dictionary<string,object> Asset(bool bj,bool zip,string tag)
    {
        string version=tag.StartsWith("v",StringComparison.Ordinal)?tag.Substring(1):tag;
        string name=zip?"KillDeal-"+(bj?"BJ":"Viewer")+"-V"+version+".zip":GitHubReleaseUpdate.AssetName(bj);
        return new Dictionary<string,object> {{"name",name},{"id",12345},{"size",98765},{"state","uploaded"},{"digest","sha256:"+Hash},{"browser_download_url","https://github.com/SeokHyeon0/kill_deal_nae_7I/releases/download/"+tag+"/"+name}};
    }
    static Dictionary<string,object> Release(params object[] assets)
    {return new Dictionary<string,object> {{"tag_name","v2.1.3"},{"draft",false},{"prerelease",false},{"assets",assets}};}
    static GitHubReleaseAsset Parse(Dictionary<string,object> release,bool bj)
    {return GitHubReleaseUpdate.Parse(Json.Serialize(release),bj,false);}
    static void BadField(string field,object value,string label)
    {var asset=Asset(true,false,"v2.1.3");asset[field]=value;Reject(()=>Parse(Release(asset),true),label);}
    static int Main()
    {
        try
        {
            foreach(bool bj in new[]{true,false})foreach(bool zip in new[]{false,true})
            {
                var asset=Parse(Release(Asset(bj,zip,"v2.1.3")),bj);
                Check(asset.Version==new Version(2,1,3,0)&&asset.Tag=="v2.1.3","role/version");
                Check(asset.Name==(string)Asset(bj,zip,"v2.1.3")["name"]&&asset.IsZip==zip,"role/name/type");
                Check(asset.Size==98765&&asset.Sha256==Hash,"size/digest");
                Check(asset.DownloadUrl==(string)Asset(bj,zip,"v2.1.3")["browser_download_url"],"download URL");
                Check(asset.Version>new Version(2,1,2,0),"higher version");
                Check(asset.Version==GitHubReleaseUpdate.ReadVersion(Json.Serialize(Release(Asset(bj,zip,"v2.1.3")))),"light version path");
            }
            Check(GitHubReleaseUpdate.ApiUrl=="https://api.github.com/repos/SeokHyeon0/kill_deal_nae_7I/releases/latest","fixed repository API");
            Check(GitHubReleaseUpdate.ReadVersion("{\"tag_name\":\"2.1.2\",\"draft\":false,\"prerelease\":false}")==new Version(2,1,2,0),"without v and no assets for current check");
            Check(!(GitHubReleaseUpdate.ReadVersion(Json.Serialize(Release()))>new Version(2,1,3,0)),"equal no update");
            Check(!(GitHubReleaseUpdate.ReadVersion(Json.Serialize(Release()))>new Version(3,0,0,0)),"older no update");
            foreach(string tag in new[]{"v2.1.3.0","v02.1.3","v2.01.3","v2.1.03","v2.1.3-beta","v2.1.3+build","2.1"," V2.1.3","v2.1.3\n","v2147483648.1.1","v-1.2.3","v2.1.3/evil","V2.1.3"})
            {var release=Release();release["tag_name"]=tag;Reject(()=>GitHubReleaseUpdate.ReadVersion(Json.Serialize(release)),"tag "+tag);}
            foreach(string key in new[]{"draft","prerelease"})
            {
                foreach(object bad in new object[]{true,"false",0,null})
                {var release=Release();release[key]=bad;Reject(()=>GitHubReleaseUpdate.ReadVersion(Json.Serialize(release)),key+" type/value");}
                var missing=Release();missing.Remove(key);Reject(()=>GitHubReleaseUpdate.ReadVersion(Json.Serialize(missing)),key+" missing");
            }
            Reject(()=>Parse(Release(),true),"empty assets");
            var missingAssets=Release();missingAssets.Remove("assets");Reject(()=>Parse(missingAssets,true),"no assets");
            var objectAssets=Release();objectAssets["assets"]=new Dictionary<string,object>();Reject(()=>Parse(objectAssets,true),"assets wrong type");
            Reject(()=>Parse(Release(Asset(false,false,"v2.1.3")),true),"only wrong role");
            Reject(()=>Parse(Release(Asset(true,false,"v2.1.2")),true),"direct wrong tag URL");
            Reject(()=>Parse(Release(Asset(true,true,"v2.1.2")),true),"ZIP wrong version name");
            Reject(()=>Parse(Release((object)null),true),"null asset");
            foreach(bool zip in new[]{false,true})
            {
                Reject(()=>Parse(Release(Asset(true,zip,"v2.1.3"),Asset(true,zip,"v2.1.3")),true),"duplicate candidate");
                var lower=Asset(true,zip,"v2.1.3");lower["name"]=((string)lower["name"]).ToLowerInvariant();
                Reject(()=>Parse(Release(lower),true),"ambiguous case");
            }
            foreach(object size in new object[]{0,-1,33554433,"12",12.5,null})BadField("size",size,"invalid size");
            foreach(object id in new object[]{0,-1,"123",1.5,null})BadField("id",id,"invalid id");
            foreach(object state in new object[]{"new","open","Uploaded",true,null})BadField("state",state,"unuploaded state");
            foreach(object digest in new object[]{null,"","sha256:","SHA256:"+Hash,"sha512:"+Hash,"sha256:"+new string('a',63),"sha256:"+new string('z',64),"sha256:"+Hash+"\n",Hash})BadField("digest",digest,"invalid digest");
            var upperHash=Asset(true,false,"v2.1.3");upperHash["digest"]="sha256:"+Hash.ToUpperInvariant();Check(Parse(Release(upperHash),true).Sha256==Hash,"uppercase hash normalized");
            var maxSize=Asset(true,false,"v2.1.3");maxSize["size"]=33554432;Check(Parse(Release(maxSize),true).Size==33554432,"maximum download size");
            var longId=Asset(true,false,"v2.1.3");longId["id"]=9000000000L;Check(Parse(Release(longId),true).Size==98765,"64-bit GitHub asset id");
            foreach(string url in new[]{
                "http://github.com/SeokHyeon0/kill_deal_nae_7I/releases/download/v2.1.3/KillDeal-BJ.exe",
                "https://evil.example/SeokHyeon0/kill_deal_nae_7I/releases/download/v2.1.3/KillDeal-BJ.exe",
                "https://github.com/other/repo/releases/download/v2.1.3/KillDeal-BJ.exe",
                "https://github.com/SeokHyeon0/kill_deal_nae_7I/releases/download/v2.1.2/KillDeal-BJ.exe",
                "https://github.com/SeokHyeon0/kill_deal_nae_7I/releases/download/v2.1.3/KillDeal-Viewer.exe",
                "https://github.com/SeokHyeon0/kill_deal_nae_7I/releases/download/v2.1.3/KillDeal-BJ.exe?download=1",
                "https://github.com/SeokHyeon0/kill_deal_nae_7I/releases/download/v2.1.3/KillDeal-BJ.exe#fragment",
                "https://user@github.com/SeokHyeon0/kill_deal_nae_7I/releases/download/v2.1.3/KillDeal-BJ.exe",
                "https://github.com/SeokHyeon0/kill_deal_nae_7I/releases/download/v2.1.3/%4BillDeal-BJ.exe",
                "https://github.com/SeokHyeon0/kill_deal_nae_7I/releases/download/v2.1.3/a/../KillDeal-BJ.exe",
                "https://github.com/SeokHyeon0/kill_deal_nae_7I/releases/download/v2.1.3/KillDeal-BJ.exe/",
                "https://github.com:443/SeokHyeon0/kill_deal_nae_7I/releases/download/v2.1.3/KillDeal-BJ.exe",
                "file:///C:/KillDeal-BJ.exe","","relative.exe","http://127.0.0.1:12345/releases/download/v2.1.3/KillDeal-BJ.exe"})BadField("browser_download_url",url,"bad URL "+url);
            foreach(bool bj in new[]{true,false})
            {
                var direct=Asset(bj,false,"v2.1.3");var zip=Asset(bj,true,"v2.1.3");
                Check(Parse(Release(direct,zip),bj).IsZip,"ZIP priority direct first");
                Check(Parse(Release(zip,direct),bj).IsZip,"ZIP priority ZIP first");
                var extra=new Dictionary<string,object> {{"name","manual.pdf"}};
                Check(!Parse(Release(Asset(!bj,true,"v2.1.3"),extra,direct),bj).IsZip,"unrelated assets ignored");
                direct["browser_download_url"]="http://127.0.0.1:12345/releases/download/v2.1.3/"+direct["name"];
                Check(!GitHubReleaseUpdate.Parse(Json.Serialize(Release(direct)),bj,true).IsZip,"explicit loopback test allowed");
                direct["browser_download_url"]="http://10.0.0.1:12345/releases/download/v2.1.3/"+direct["name"];
                Reject(()=>GitHubReleaseUpdate.Parse(Json.Serialize(Release(direct)),bj,true),"LAN is not loopback");
                direct["browser_download_url"]="http://127.0.0.1:12345/releases/download/v2.1.3/other.exe";
                Reject(()=>GitHubReleaseUpdate.Parse(Json.Serialize(Release(direct)),bj,true),"loopback wrong asset path");
            }
            foreach(string malformed in new[]{"","null","[]","true","{","{\"tag_name\":","{bad}","{\"draft\":false} trailing"})Reject(()=>GitHubReleaseUpdate.ReadVersion(malformed),"malformed JSON");
            Reject(()=>GitHubReleaseUpdate.ReadVersion(new string(' ',GitHubReleaseUpdate.MaxJsonLength+1)),"oversized JSON");
            string depth=new string('[',40)+"0"+new string(']',40);Reject(()=>GitHubReleaseUpdate.ReadVersion(depth),"JSON depth cap");
            Console.WriteLine("PASS "+checks+" GitHub release parser checks");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
