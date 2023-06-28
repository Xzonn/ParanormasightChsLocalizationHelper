using BundleHelper;
using System.Text;

namespace Helper
{
    internal class HeaderHelper
    {
        public static void RemoveHeader()
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
                Bundle bundleData;
                using (var reader = new EndianBinaryReader(File.OpenRead($"out/{fileName}-mod")))
                {
                    bundleData = new Bundle(reader);
                }
                List<string> fileList = new();
                foreach (var file in bundleData.FileList)
                {
                    if (file.fileName.EndsWith(".resS"))
                    {
                        using var fs = File.OpenWrite($"out/old-{file.fileName}");
                        file.stream.CopyTo(fs);
                    }
                }
            }
        }

        public static void AddHeader()
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
                using (var reader = new EndianBinaryReader(File.OpenRead($"out/{fileName}-mod")))
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
                    using (var writer = new EndianBinaryWriter(memoryStream))
                    {
                        bundleData.DumpRaw(writer);
                    }
                    using (FileStream fileStream = File.Create($"out/{fileName}"))
                    {
                        fileStream.Write(File.ReadAllBytes($"out/{fileName}-header"));
                        fileStream.Write(memoryStream.ToArray());
                    }
                }
            }
        }
    }
}
