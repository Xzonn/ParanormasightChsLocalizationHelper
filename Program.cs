using AssetStudio;
using BundleHelper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Specialized;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;

namespace Helper
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Logger.Default = new LogHelper();
            Directory.CreateDirectory("out/");

            BundleExtractor.ExtractNewBundle();
            HeaderHelper.RemoveHeader();
            PatchHelper.MakePatch();
            HeaderHelper.AddHeader();
            PatchHelper.CreatePatchFolder();
            PatchHelper.CleanDirectory();
        }
    }
}