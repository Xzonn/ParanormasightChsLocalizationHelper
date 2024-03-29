﻿using AssetStudio;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Helper
{
    internal class Texture2DHelper
    {
        public string path = "";
        public ulong offset = 0;
        public uint size = 0;
        public byte[] data = Array.Empty<byte>();
        public OrderedDictionary? type;
        public long m_PathID = 0;
    }

    internal class PatchHelper
    {
        static Dictionary<string, string> LoadTranslation()
        {
            var UNDER_LINE_PATTERN = new Regex(@"<u>(.+?)</u>");

            Dictionary<string, string> data = new();
            foreach (string fileName in Directory.GetFiles("texts/zh_Hans/"))
            {
                string[] lines = File.ReadAllLines(fileName);
                foreach (string line in lines)
                {
                    string[] parts = line.Split(",");
                    if (parts.Length >= 2)
                    {
                        var text_id = parts[0];
                        var text_content = string.Join(",", parts.Skip(1));
                        text_content = UNDER_LINE_PATTERN.Replace(text_content, x => string.Join("", x.Groups[1].Value.Select(y => $"[f\ue001]{y}")));
                        data.Add(text_id, text_content);
                    }
                }
            }
            return data;
        }

        public static void MakePatch(string platform)
        {
            var translation = LoadTranslation();
            var TEXT_FILE_BLACKLIST = new List<string>()
            {
                "command_test",
                "command_test2",
            };

            string[] FILE_NAMES = {
                $"files/{platform}/resources.assets",
                $"files/{platform}/sharedassets0.assets",
                $"out/{platform}/a021-mod",
                $"out/{platform}/a024-mod",
                $"out/{platform}/a035-mod",
                $"out/{platform}/a036-mod",
                $"out/{platform}/a038-mod",
            };
            AssetsManager manager = new();

            manager.LoadFiles(FILE_NAMES.Where(x => File.Exists(x)).ToArray());

            foreach (var assetsFile in manager.assetsFileList)
            {
                var replaceResS = assetsFile.fileName.StartsWith("CAB-") && (assetsFile.Objects.Find((x) => x is Texture2D) != null);
                var texture2DList = new List<Texture2DHelper>();
                var fileName = assetsFile.fileName;
                var replaceStreams = new Dictionary<long, Stream> { };
                foreach (var @object in assetsFile.Objects)
                {
                    if (@object is Texture2D m_Texture2D)
                    {
                        ReplaceTexture2D(platform, m_Texture2D, replaceResS, ref replaceStreams, ref texture2DList);
                    }
                    else if (@object is TextAsset m_TextAsset && !TEXT_FILE_BLACKLIST.Contains(m_TextAsset.m_Name))
                    {
                        ReplaceText(m_TextAsset, ref replaceStreams, translation);
                    }
                    else if ((@object is MonoBehaviour m_MonoBehaviour)
                        && m_MonoBehaviour.m_Script.TryGet(out var m_Script)
                        && m_Script.m_Name == "TMP_FontAsset"
                        && File.Exists($"files/TMP_FontAsset/{m_MonoBehaviour.m_Name}.json"))
                    {
                        ReplaceTMPFont(m_MonoBehaviour, ref replaceStreams);
                    }
                    else if ((@object is Font m_Font))
                    {
                        ReplaceTrueTypeFont(m_Font, ref replaceStreams);
                    }
                }
                if (replaceResS) { SaveTexture2D(platform, assetsFile.fileName, ref replaceStreams, ref texture2DList); }
                if (replaceStreams.Count == 0)
                {
                    continue;
                }

                Console.WriteLine($"Saving: {fileName}");
                assetsFile.SaveAs($"out/{platform}/{fileName}", replaceStreams);
                replaceStreams.Values.ToList().ForEach(x => x.Close());
            }

            manager.Clear();
        }

        static void ReplaceText(TextAsset m_TextAsset, ref Dictionary<long, Stream> replaceStreams, Dictionary<string, string> translation)
        {
            var TEXT_LINE_PATTERN = new Regex(@"(?<=[,\(])(text=""?|name=""?|WindowMessage:).+?((?:""?,(?:.+,)?|\|)txtid=([0-9a-zA-Z_]+))(?=[,\)])");

            if (m_TextAsset.m_Name == "UniTask" && m_TextAsset.assetsFile.m_TargetPlatform != BuildTarget.Switch) { return; }

            MemoryStream memoryStream = new();
            BinaryWriter bw = new(memoryStream);
            var m_Type = m_TextAsset.serializedType?.m_Type;
            if (m_Type == null)
            {
                using var fs = File.OpenRead("files/TypeTree/Text.bin");
                m_Type = TypeTreeHelper.LoadTypeTree(new BinaryReader(fs));
            }
            var type = m_TextAsset.ToType(m_Type);

            string filePath = $"texts/zh_Hans/{m_TextAsset.m_Name.Replace("_JP", "")}.txt";
            if (File.Exists(filePath))
            {
                var text = File.ReadAllText(filePath);
                type["m_Script"] = text;
            }
            else
            {
                var text = (string)type["m_Script"]!;
                var crlf = text.Contains("\r\n");
                if (!crlf)
                {
                    Console.WriteLine($"Converting LF to CRLF: (TextAsset) {m_TextAsset.assetsFile.fileName}/{m_TextAsset.m_Name}");
                    text = text.Replace("\n", "\r\n");
                }
                // text = Regex.Replace(text, @"^(11\d\.cam\.fo\(\))(?=[\r\n])", "100.dt.jpif(label=exit,cond=%ACCOUNTNAME==\"TENOKE\")\r\n$1", RegexOptions.Multiline);
                foreach (var file_from in Directory.GetFiles("files/script_replace/", $"{m_TextAsset.m_Name}_*.from"))
                {
                    var file_to = Path.ChangeExtension(file_from, ".to");
                    if (!File.Exists(file_to)) { continue; }
                    var string_from = File.ReadAllText(file_from);
                    if (!string_from.Contains("\r\n")) { string_from = string_from.Replace("\n", "\r\n"); }
                    var string_to = File.ReadAllText(file_to);
                    if (!string_to.Contains("\r\n")) { string_to = string_to.Replace("\n", "\r\n"); }
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
                if (!crlf)
                {
                    text = text.Replace("\r\n", "\n");
                }
                if ((string)type["m_Script"]! == text)
                {
                    return;
                }
                type["m_Script"] = text;
            }
            var version = Environment.GetEnvironmentVariable("XZ_PATCH_VERSION");
            if (string.IsNullOrEmpty(version))
            {
                version = "dev_unk";
            }
            else if (version.Length > 7)
            {
                version = version[..7];
            }
            type["m_Script"] = ((string)type["m_Script"]!).Replace("{{ version }}", version);
            TypeTreeHelper.WriteType(type, m_Type, bw);
            replaceStreams[m_TextAsset.m_PathID] = memoryStream;

            Console.WriteLine($"Replacing: (TextAsset) {m_TextAsset.assetsFile.fileName}/{m_TextAsset.m_Name}");
        }

        static void ReplaceTMPFont(MonoBehaviour m_MonoBehaviour, ref Dictionary<long, Stream> replaceStreams)
        {
            var m_Type = m_MonoBehaviour.serializedType?.m_Type;
            if (m_Type == null)
            {
                using var fs = File.OpenRead("files/TypeTree/TMP_FontAsset.bin");
                m_Type = TypeTreeHelper.LoadTypeTree(new BinaryReader(fs));
            }
            var type = m_MonoBehaviour.ToType(m_Type);
            string json = File.ReadAllText($"files/TMP_FontAsset/{m_MonoBehaviour.m_Name}.json");
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

        static void ReplaceTexture2D(string platform, Texture2D m_Texture2D, bool replaceResS, ref Dictionary<long, Stream> replaceStreams, ref List<Texture2DHelper> texture2DList)
        {
            var m_Type = m_Texture2D.serializedType?.m_Type;
            if (m_Type == null)
            {
                using var fs = File.OpenRead("files/TypeTree/Texture2D.bin");
                m_Type = TypeTreeHelper.LoadTypeTree(new BinaryReader(fs));
            }
            var type = m_Texture2D.ToType(m_Type);
            byte[] oldData = (byte[])type["image data"]!;

            var textureFileName = $"{m_Texture2D.m_Name}.bin";
            if (m_Texture2D.assetsFile.fileName == "CAB-b1cf5d792b9389884d6c327bca126287" && m_Texture2D.m_Name.Contains("logo"))
            {
                textureFileName = $"{m_Texture2D.m_Name}_a021.bin";
            }
            if (File.Exists($"files/Texture2D/{textureFileName}"))
            {
                if (m_Texture2D.assetsFile.m_TargetPlatform == BuildTarget.Switch)
                {
                    type["m_IsPreProcessed"] = false;
                    ((List<object>)type["m_PlatformBlob"]!).Clear();
                    if (m_Texture2D.m_TextureFormat == TextureFormat.ASTC_RGB_8x8)
                    {
                        type["m_TextureFormat"] = (int)TextureFormat.DXT5Crunched;
                    }
                }
                byte[] rawData = File.ReadAllBytes($"files/Texture2D/{textureFileName}");
                type["m_CompleteImageSize"] = (uint)rawData.Length;

                if (replaceResS && oldData.Length == 0)
                {
                    texture2DList.Add(new Texture2DHelper()
                    {
                        size = (uint)rawData.Length,
                        data = rawData,
                        type = type,
                        m_PathID = m_Texture2D.m_PathID,
                    });
                }
                else
                {
                    type["image data"] = rawData;
                    if (oldData.Length == 0)
                    {
                        var m_StreamData = (OrderedDictionary)type["m_StreamData"]!;
                        m_StreamData["path"] = "";
                        m_StreamData["offset"] = (ulong)0;
                        m_StreamData["size"] = (uint)0;
                        type["m_StreamData"] = m_StreamData;
                    }
                    MemoryStream memoryStream = new();
                    BinaryWriter bw = new(memoryStream);
                    TypeTreeHelper.WriteType(type, m_Type, bw);
                    replaceStreams[m_Texture2D.m_PathID] = memoryStream;

                    Console.WriteLine($"Replacing: (Texture2D) {m_Texture2D.assetsFile.fileName}/{type["m_Name"]}");
                }
            }
            else if (replaceResS && oldData.Length == 0)
            {
                var m_StreamData = (OrderedDictionary)type["m_StreamData"]!;
                string path = Path.GetFileName((string)m_StreamData["path"]!);
                ulong offset = (ulong)m_StreamData["offset"]!;
                uint size = (uint)m_StreamData["size"]!;
                texture2DList.Add(new Texture2DHelper()
                {
                    path = $"out/{platform}/old-{path}",
                    offset = offset,
                    size = size,
                    type = type,
                    m_PathID = m_Texture2D.m_PathID,
                });
            }
        }

        static void SaveTexture2D(string platform, string fileName, ref Dictionary<long, Stream> replaceStreams, ref List<Texture2DHelper> texture2DList)
        {
            var writer = File.Create($"out/{platform}/{fileName}.resS");
            TypeTree m_Type;

            using (var fs = File.OpenRead("files/TypeTree/Texture2D.bin"))
            {
                m_Type = TypeTreeHelper.LoadTypeTree(new BinaryReader(fs));
            }

            foreach (var item in texture2DList)
            {
                var type = item.type!;
                var m_StreamData = (OrderedDictionary)type["m_StreamData"]!;
                m_StreamData["path"] = $"archive:/{fileName}/{fileName}.resS";
                m_StreamData["offset"] = (ulong)writer.Position;
                m_StreamData["size"] = item.size;
                type["m_StreamData"] = m_StreamData;
                if (item.size == 0) { }
                else if (item.path != "")
                {
                    Debug.Assert(item.path == $"out/{platform}/old-{fileName}.resS");
                    using var reader = File.OpenRead(item.path);
                    reader.Seek((long)item.offset, SeekOrigin.Begin);
                    byte[] buffer = new byte[item.size];
                    reader.Read(buffer, 0, buffer.Length);
                    writer.Write(buffer, 0, buffer.Length);
                }
                else
                {
                    writer.Write(item.data, 0, (int)item.size);
                }
                writer.Seek((0x10 - writer.Position % 0x10) % 0x10, SeekOrigin.Current);
                MemoryStream memoryStream = new();
                BinaryWriter bw = new(memoryStream);
                TypeTreeHelper.WriteType(type, m_Type, bw);
                replaceStreams[item.m_PathID] = memoryStream;

                Console.WriteLine($"Replacing: (Texture2D) {fileName}/{type["m_Name"]}");
            }
        }

        static void ReplaceTrueTypeFont(Font m_Font, ref Dictionary<long, Stream> replaceStreams)
        {
            TypeTree m_Type;
            using (var fs = File.OpenRead("files/TypeTree/Font.bin"))
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

        public static void CreatePatchFolder(string platform)
        {
            string root = platform switch
            {
                "Windows" => "out/patch/Windows/PARANORMASIGHT_Data",
                "Switch" => "out/patch/Switch/01006e5019c5a000/romfs/Data",
                _ => "",
            };
            
            Directory.CreateDirectory($"{root}/StreamingAssets/");
            Copy($"out/{platform}/resources.assets", $"{root}/resources.assets");
            Copy($"out/{platform}/sharedassets0.assets", $"{root}/sharedassets0.assets");
            Copy($"out/{platform}/a021", $"{root}/StreamingAssets/a021");
            Copy($"out/{platform}/a024", $"{root}/StreamingAssets/a024");
            Copy($"out/{platform}/a035", $"{root}/StreamingAssets/a035");
            Copy($"out/{platform}/a036", $"{root}/StreamingAssets/a036");
            Copy($"out/{platform}/a038", $"{root}/StreamingAssets/a038");
        }

        private static void Copy(string source, string destination)
        {
            if (!File.Exists(source)) return;
            File.Copy(source, destination, true);
        }

        public static void CleanDirectory(string platform)
        {
#if !DEBUG
            Directory.GetFiles($"out/{platform}/", "old-CAB-*").ToList().ForEach((x) => File.Delete(x));
            Directory.GetFiles($"out/{platform}/", "CAB-*").ToList().ForEach((x) => File.Delete(x));
            Directory.GetFiles($"out/{platform}/", "*-mod").ToList().ForEach((x) => File.Delete(x));
            Directory.GetFiles($"out/{platform}/", "*-header").ToList().ForEach((x) => File.Delete(x));
#endif
        }
    }
}