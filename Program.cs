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

            RemoveHeader();
            MakePatch();
            AddHeader();
            CreatePatchFolder();
        }

        static Dictionary<string, string> LoadTranslation()
        {
            Dictionary<string, string> data = new();
            foreach (string fileName in Directory.GetFiles("texts/zh_Hans/"))
            {
                string[] lines = File.ReadAllLines(fileName);
                foreach (string line in lines)
                {
                    string[] parts = line.Split(",");
                    if (parts.Length == 2)
                    {
                        data.Add(parts[0], parts[1]);
                    }
                }
            }
            return data;
        }

        static void RemoveHeader()
        {
            string[] FILE_NAMES = {
                "a021",
                "a024",
                "a035",
                "a036",
                "a038",
            };
            byte[] UNITY_HEADER = Encoding.ASCII.GetBytes("UnityFS");

            foreach (var fileName in FILE_NAMES)
            {
                if (!File.Exists($"files/{fileName}")) continue;
                byte[] data = File.ReadAllBytes($"files/{fileName}");
                var index = 0;
                while (index < data.Length)
                {
                    if (data.Skip(index).Take(UNITY_HEADER.Length).SequenceEqual(UNITY_HEADER))
                    {
                        break;
                    }
                    index++;
                }
                File.WriteAllBytes($"out/{fileName}-mod", data.Skip(index).ToArray());
                File.WriteAllBytes($"out/{fileName}-header", data.Take(index).ToArray());
            }
        }

        static void AddHeader()
        {
            string[] FILE_NAMES = {
                "a021",
                "a024",
                "a035",
                "a036",
                "a038",
            };
            foreach (var fileName in FILE_NAMES)
            {
                if (!File.Exists($"out/{fileName}-mod") || !File.Exists($"out/{fileName}-header")) continue;
                Bundle bundleData;
                using (var reader = new BundleHelper.EndianBinaryReader(File.OpenRead($"out/{fileName}-mod")))
                {
                    bundleData = new Bundle(reader);
                }

                List<string> fileList = new();
                foreach (var file in bundleData.FileList)
                {
                    if (File.Exists($"out/{file.fileName}"))
                    {
                        Console.WriteLine($"Replacing: {file.fileName}");
                        file.stream = File.OpenRead($"out/{file.fileName}");
                        fileList.Add($"out/{file.fileName}");
                    }
                }
                if (fileList.Count == 0)
                {
                    continue;
                }

                Console.WriteLine($"Writing: {fileName}");
                using (MemoryStream memoryStream = new())
                {
                    using (var writer = new BundleHelper.EndianBinaryWriter(memoryStream))
                    {
                        bundleData.DumpRaw(writer);
                    }
                    using (FileStream fileStream = File.Create($"out/{fileName}"))
                    {
                        fileStream.Write(File.ReadAllBytes($"out/{fileName}-header"));
                        fileStream.Write(memoryStream.ToArray());
                    }
                }
                fileList.ForEach(file => File.Delete(file));
                File.Delete($"out/{fileName}-mod");
                File.Delete($"out/{fileName}-header");
            }
        }

        static void MakePatch()
        {
            var translation = LoadTranslation();

            string[] FILE_NAMES = {
                "files/resources.assets",
                "files/sharedassets0.assets",
                "out/a021-mod",
                "out/a024-mod",
                "out/a035-mod",
                "out/a036-mod",
                "out/a038-mod",
            };
            AssetsManager manager = new();

            manager.LoadFiles(FILE_NAMES.Where(x => File.Exists(x)).ToArray());

            foreach (var assetsFile in manager.assetsFileList)
            {
                var fileName = assetsFile.fileName;
                var replaceStreams = new Dictionary<long, Stream> { };
                foreach (var @object in assetsFile.Objects)
                {
                    if (@object is TextAsset m_TextAsset)
                    {
                        ReplaceText(m_TextAsset, ref replaceStreams, translation);
                    }
                    else if ((@object is MonoBehaviour m_MonoBehaviour)
                        && m_MonoBehaviour.m_Script.TryGet(out var m_Script)
                        && m_Script.m_Name == "TMP_FontAsset"
                        && File.Exists($"files/{m_MonoBehaviour.m_Name}.json"))
                    {
                        ReplaceTMPFont(m_MonoBehaviour, ref replaceStreams);
                    }
                    else if ((@object is Texture2D m_Texture2D)
                        && File.Exists($"files/{m_Texture2D.m_Name}.png"))
                    {
                        ReplaceTMPAtlas(m_Texture2D, ref replaceStreams);
                    }
                    else if ((@object is AssetStudio.Font m_Font))
                    {
                        ReplaceTrueTypeFont(m_Font, ref replaceStreams);
                    }
                }
                if (replaceStreams.Count == 0)
                {
                    continue;
                }

                Console.WriteLine($"Saving: {fileName}");
                assetsFile.SaveAs($"out/{fileName}", replaceStreams);
                replaceStreams.Values.ToList().ForEach(x => x.Close());
            }

            manager.Clear();
        }

        static void ReplaceText(TextAsset m_TextAsset, ref Dictionary<long, Stream> replaceStreams, Dictionary<string, string> translation)
        {
            var TEXT_LINE_PATTERN = new Regex(@"(?<=[,\(])(text=""?|name=""?|WindowMessage:).+?((?:""?,(?:.+,)?|\|)txtid=([0-9a-zA-Z_]+))(?=[,\)])");

            MemoryStream memoryStream = new();
            BinaryWriter bw = new(memoryStream);
            var type = m_TextAsset.ToType();
            if (type == null) return;
            string filePath = $"texts/zh_Hans/{m_TextAsset.m_Name.Replace("_JP", "")}.txt";
            if (File.Exists(filePath))
            {
                var text = File.ReadAllText(filePath);
                var version = Environment.GetEnvironmentVariable("XZ_PATCH_VERSION");
                if (string.IsNullOrEmpty(version))
                {
                    version = "dev_unk";
                }
                else if (version.Length > 7)
                {
                    version = version[..7];
                }
                text = text.Replace("{{ version }}", version);
                type["m_Script"] = text;
            }
            else
            {
                var text = (string)type["m_Script"]!;
                text = Regex.Replace(text, @"^(11\d\.cam\.fo\(\))(?=[\r\n])", "100.dt.jpif(label=exit,cond=%ACCOUNTNAME==\"TENOKE\")\r\n$1", RegexOptions.Multiline);
                foreach (var file_from in Directory.GetFiles("files/script_replace/", $"{m_TextAsset.m_Name}_*.from"))
                {
                    var file_to = Path.ChangeExtension(file_from, ".to");
                    if (!File.Exists(file_to)) { continue; }
                    var string_from = File.ReadAllText(file_from).Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
                    var string_to = File.ReadAllText(file_to).Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
                    text = text.Replace(string_from, string_to);
                }
                text = TEXT_LINE_PATTERN.Replace(text, x =>
                {
                    if (!translation.TryGetValue(x.Groups[3].Value, out var result))
                    {
                        return x.Groups[0].Value;
                    }
                    if (x.Groups[1].Value.Contains('"'))
                    {
                        result = Uri.EscapeDataString(result);
                    }
                    return $"{x.Groups[1].Value}{result}{x.Groups[2].Value}";
                });
                if ((string)type["m_Script"]! == text)
                {
                    return;
                }
                type["m_Script"] = text;
            }
            var m_Type = m_TextAsset.serializedType?.m_Type;
            TypeTreeHelper.WriteType(type, m_Type, bw);
            replaceStreams[m_TextAsset.m_PathID] = memoryStream;

            Console.WriteLine($"Replacing: (TextAsset) {m_TextAsset.assetsFile.fileName}/{m_TextAsset.m_Name}");
        }

        static void ReplaceTMPFont(MonoBehaviour m_MonoBehaviour, ref Dictionary<long, Stream> replaceStreams)
        {
            var m_Type = m_MonoBehaviour.serializedType?.m_Type;
            if (m_Type == null)
            {
                using var fs = File.OpenRead("files/TMP_FontAsset.bin");
                m_Type = TypeTreeHelper.LoadTypeTree(new BinaryReader(fs));
            }
            var type = m_MonoBehaviour.ToType(m_Type);
            string json = File.ReadAllText($"files/{m_MonoBehaviour.m_Name}.json");
            var jObject = JsonConvert.DeserializeObject<JObject>(json);
            var newType = JsonHelper.ReadType(m_Type, jObject);
            foreach (var _ in new[] { "m_FamilyName", "m_StyleName", "m_PointSize", "m_LineHeight" })
            {
                ((OrderedDictionary)newType["m_FaceInfo"]!)[_] = ((OrderedDictionary)type["m_FaceInfo"]!)[_];
            }
            foreach (var _ in new[] { "m_FaceInfo", "m_GlyphTable", "m_CharacterTable", "m_UsedGlyphRects", "m_FreeGlyphRects" })
            {
                type[_] = newType[_];
            }

            MemoryStream memoryStream = new();
            BinaryWriter bw = new(memoryStream);
            TypeTreeHelper.WriteType(type, m_Type, bw);
            replaceStreams[m_MonoBehaviour.m_PathID] = memoryStream;

            Console.WriteLine($"Replacing: (MonoBehaviour) {m_MonoBehaviour.assetsFile.fileName}/{m_MonoBehaviour.m_Name}");
        }

        static void ReplaceTMPAtlas(Texture2D m_Texture2D, ref Dictionary<long, Stream> replaceStreams)
        {
            var m_Type = m_Texture2D.serializedType?.m_Type;
            if (m_Type == null)
            {
                using var fs = File.OpenRead("files/Texture2D.bin");
                m_Type = TypeTreeHelper.LoadTypeTree(new BinaryReader(fs));
            }
            var type = m_Texture2D.ToType(m_Type);
            if (m_Texture2D.m_Name.Contains("TELOP") && m_Texture2D.assetsFile.m_TargetPlatform == BuildTarget.Switch)
            {
                type["m_IsPreProcessed"] = false;
                ((List<object>)type["m_PlatformBlob"]!).Clear();
            }

            int width = (int)type["m_Width"]!;
            int height = (int)type["m_Height"]!;
            byte[] rawData = (byte[])type["image data"]!;
            if (rawData.Length == 0)
            {
                var m_StreamData = (OrderedDictionary)type["m_StreamData"]!;
                m_StreamData["path"] = "";
                m_StreamData["offset"] = (ulong)0;
                m_StreamData["size"] = (uint)0;
                rawData = new byte[width * height];
            }

            Bitmap bitmap = new($"files/{m_Texture2D.m_Name}.png");
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    rawData[x + y * width] = bitmap.GetPixel(x, height - y - 1).A;
                }
            }

            type["image data"] = rawData;

            MemoryStream memoryStream = new();
            BinaryWriter bw = new(memoryStream);
            TypeTreeHelper.WriteType(type, m_Type, bw);
            replaceStreams[m_Texture2D.m_PathID] = memoryStream;

            Console.WriteLine($"Replacing: (Texture2D) {m_Texture2D.assetsFile.fileName}/{m_Texture2D.m_Name}");
        }

        static void ReplaceTrueTypeFont(AssetStudio.Font m_Font, ref Dictionary<long, Stream> replaceStreams)
        {
            TypeTree m_Type;
            using (var fs = File.OpenRead("files/Font.bin"))
            {
                m_Type = TypeTreeHelper.LoadTypeTree(new BinaryReader(fs));
            }
            var type = m_Font.ToType(m_Type);
            // File.WriteAllText($"out/{m_Font.m_Name}.json", JsonConvert.SerializeObject(type, Formatting.Indented));
            // type["m_FontData"] = File.ReadAllBytes($"files/{m_Font.m_Name}.ttf").Select(x => (object)x).ToList();
            ((List<object>)type["m_FontData"]!).Clear();

            MemoryStream memoryStream = new();
            BinaryWriter bw = new(memoryStream);
            TypeTreeHelper.WriteType(type, m_Type, bw);
            replaceStreams[m_Font.m_PathID] = memoryStream;

            Console.WriteLine($"Replacing: (Font) {m_Font.assetsFile.fileName}/{m_Font.m_Name}");
        }

        static void CreatePatchFolder()
        {
            Directory.CreateDirectory("out/patch/PARANORMASIGHT_Data/StreamingAssets/");
            Copy("out/resources.assets", "out/patch/PARANORMASIGHT_Data/resources.assets");
            Copy("out/sharedassets0.assets", "out/patch/PARANORMASIGHT_Data/sharedassets0.assets");
            Copy("out/a021", "out/patch/PARANORMASIGHT_Data/StreamingAssets/a021");
            Copy("out/a024", "out/patch/PARANORMASIGHT_Data/StreamingAssets/a024");
            Copy("out/a035", "out/patch/PARANORMASIGHT_Data/StreamingAssets/a035");
            Copy("out/a036", "out/patch/PARANORMASIGHT_Data/StreamingAssets/a036");
            Copy("out/a038", "out/patch/PARANORMASIGHT_Data/StreamingAssets/a038");
        }

        static private void Copy(string source, string destination)
        {
            if (!File.Exists(source)) return;
            File.Copy(source, destination, true);
        }
    }
}