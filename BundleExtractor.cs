using AssetStudio;
using BundleHelper;
using Newtonsoft.Json;
using System.Collections.Specialized;

namespace Helper
{
    internal class BundleExtractor
    {
#if DEBUG
        private static readonly Formatting JsonFormatting = Formatting.Indented;
#else
        private static readonly Formatting JsonFormatting = Formatting.None;
#endif

        static public void ExtractNewBundle()
        {
            if (!File.Exists($"files/paranormasight.bundle")) return;

            AssetsManager manager = new();
            List<string> fileNames = new();
            using (var reader = new BundleHelper.EndianBinaryReader(File.OpenRead($"files/paranormasight.bundle")))
            {
                Bundle bundleData = new(reader);
                foreach (var file in bundleData.FileList)
                {
                    fileNames.Add($"out/{file.fileName}");
                    using var writer = File.OpenWrite($"out/{file.fileName}");
                    file.stream.CopyTo(writer);
                }
            }
            manager.LoadFiles(fileNames.ToArray());
            foreach (var assetsFile in manager.assetsFileList)
            {
                foreach (var @object in assetsFile.Objects)
                {
                    if ((@object is MonoBehaviour m_MonoBehaviour)
                        && m_MonoBehaviour.m_Script.TryGet(out var m_Script)
                        && m_Script.m_Name == "TMP_FontAsset")
                    {
                        DumpMonoBehaviour(m_MonoBehaviour);
                    }
                    else if (@object is Texture2D m_Texture2D)
                    {
                        DumpTexture2D(m_Texture2D);
                    }
                }
            }
            manager.Clear();
            fileNames.ForEach((x) => File.Delete(x));
        }

        static void DumpMonoBehaviour(MonoBehaviour m_MonoBehaviour)
        {
            var m_Type = m_MonoBehaviour.serializedType?.m_Type;
            if (m_Type == null)
            {
                using var fs = File.OpenRead("files/TypeTree/TMP_FontAsset.bin");
                m_Type = TypeTreeHelper.LoadTypeTree(new BinaryReader(fs));
            }
            var type = m_MonoBehaviour.ToType(m_Type);
            string json = JsonConvert.SerializeObject(type, JsonFormatting);
            File.WriteAllText($"files/TMP_FontAsset/{m_MonoBehaviour.m_Name}.json", json);

            Console.WriteLine($"Dumping: (MonoBehaviour) {m_MonoBehaviour.assetsFile.fileName}/{m_MonoBehaviour.m_Name}");
        }

        static void DumpTexture2D(Texture2D m_Texture2D)
        {
            var m_Type = m_Texture2D.serializedType?.m_Type;
            if (m_Type == null)
            {
                using var fs = File.OpenRead("files/TypeTree/Texture2D.bin");
                m_Type = TypeTreeHelper.LoadTypeTree(new BinaryReader(fs));
            }
            var type = m_Texture2D.ToType(m_Type);
            string json = JsonConvert.SerializeObject(type, JsonFormatting);
            File.WriteAllText($"files/Texture2D/{m_Texture2D.m_Name}.json", json);

            byte[] rawData = (byte[])type["image data"]!;
            if (rawData.Length == 0)
            {
                var m_StreamData = (OrderedDictionary)type["m_StreamData"]!;
                string path = Path.GetFileName((string)m_StreamData["path"]!);
                ulong offset = (ulong)m_StreamData["offset"]!;
                uint size = (uint)m_StreamData["size"]!;
                using var reader = File.OpenRead($"out/{path}");
                rawData = new byte[size];
                reader.Seek((int)offset, SeekOrigin.Begin);
                reader.Read(rawData, 0, (int)size);
            }
            File.WriteAllBytes($"files/Texture2D/{m_Texture2D.m_Name}.bin", rawData);

            Console.WriteLine($"Dumping: (Texture2D) {m_Texture2D.assetsFile.fileName}/{m_Texture2D.m_Name}");
        }
    }
}
