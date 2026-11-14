using System;
using System.IO;
using System.Reflection;

namespace PubgScore
{
    internal static class EmbeddedUpdater
    {
        static bool registered;
        internal static void Register()
        {
            if(registered)return;registered=true;
            AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs args)
            {
                var requested=new AssemblyName(args.Name);string resource=null;
                if(requested.Name=="AutoUpdater.NET")resource="PubgScore.Updater.AutoUpdater.NET.dll";
                else if(requested.Name=="AutoUpdater.NET.resources"&&requested.CultureName=="ko")resource="PubgScore.Updater.ko.AutoUpdater.NET.resources.dll";
                if(resource==null)return null;
                foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())if(assembly.FullName==args.Name)return assembly;
                using(var stream=typeof(EmbeddedUpdater).Assembly.GetManifestResourceStream(resource))
                {
                    if(stream==null)return null;
                    using(var buffer=new MemoryStream()){stream.CopyTo(buffer);return Assembly.Load(buffer.ToArray());}
                }
            };
        }
    }
}
