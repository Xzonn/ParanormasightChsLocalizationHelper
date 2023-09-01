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

            string[] PLATFORMS = {
                "Windows",
                "Switch"
            };
            foreach (var platform in PLATFORMS)
            {
                Console.WriteLine($"--------------------\nPlatform: {platform}");
                Directory.CreateDirectory($"out/{platform}/");
                HeaderHelper.RemoveHeader(platform);
                PatchHelper.MakePatch(platform);
                HeaderHelper.AddHeader(platform);
                PatchHelper.CreatePatchFolder(platform);
                PatchHelper.CleanDirectory(platform);
            }
        }
    }
}