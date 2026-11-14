using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace PubgScore
{
    // Make the bundled font available to both WinForms and GDI+ in this process.
    internal sealed class BundledFontResource : IDisposable
    {
        internal const string SummaryFont="PubgScore.Bundled.NanumSquareR.ttf";
        internal const string ResultFont="PubgScore.Bundled.DoHyeon-Regular.ttf";
        readonly PrivateFontCollection collection=new PrivateFontCollection();
        IntPtr memory,registration;bool disposed;
        internal FontFamily Family {get;private set;}

        [DllImport("gdi32.dll",ExactSpelling=true)]
        static extern IntPtr AddFontMemResourceEx(IntPtr font,uint length,IntPtr reserved,out uint count);
        [DllImport("gdi32.dll",ExactSpelling=true)]
        [return:MarshalAs(UnmanagedType.Bool)]
        static extern bool RemoveFontMemResourceEx(IntPtr handle);

        internal BundledFontResource(string resourceName)
        {
            try
            {
                using(var stream=typeof(BundledFontResource).Assembly.GetManifestResourceStream(resourceName))
                {
                    if(stream==null||stream.Length<=0||stream.Length>2097152)throw new InvalidDataException("The bundled font is missing or invalid.");
                    var bytes=new byte[(int)stream.Length];int offset=0,count;
                    while(offset<bytes.Length&&(count=stream.Read(bytes,offset,bytes.Length-offset))>0)offset+=count;
                    if(offset!=bytes.Length)throw new EndOfStreamException();
                    memory=Marshal.AllocHGlobal(bytes.Length);Marshal.Copy(bytes,0,memory,bytes.Length);
                    collection.AddMemoryFont(memory,bytes.Length);
                    uint registered;registration=AddFontMemResourceEx(memory,(uint)bytes.Length,IntPtr.Zero,out registered);
                    if(registration==IntPtr.Zero||registered==0)throw new InvalidOperationException("Unable to register the private font.");
                    var families=collection.Families;
                    if(families.Length!=1)throw new InvalidDataException("Unexpected bundled font family count.");
                    Family=families[0];
                    if(!Family.IsStyleAvailable(FontStyle.Regular))throw new InvalidDataException("The bundled regular font is unavailable.");
                }
            }
            catch{Dispose();throw;}
        }

        public void Dispose()
        {
            if(disposed)return;disposed=true;
            if(Family!=null){Family.Dispose();Family=null;}
            collection.Dispose();
            if(registration!=IntPtr.Zero){RemoveFontMemResourceEx(registration);registration=IntPtr.Zero;}
            if(memory!=IntPtr.Zero){Marshal.FreeHGlobal(memory);memory=IntPtr.Zero;}
        }
    }
}
