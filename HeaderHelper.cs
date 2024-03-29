﻿using BundleHelper;
using System.Text;

namespace Helper
{
    internal class HeaderHelper
    {
        public static void RemoveHeader(string platform)
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
                if (!File.Exists($"files/{platform}/{fileName}")) continue;
                byte[] data = File.ReadAllBytes($"files/{platform}/{fileName}");
                var index = 0;
                while (index < data.Length)
                {
                    if (data.Skip(index).Take(UNITY_HEADER.Length).SequenceEqual(UNITY_HEADER))
                    {
                        break;
                    }
                    index++;
                }
                File.WriteAllBytes($"out/{platform}/{fileName}-mod", data.Skip(index).ToArray());
                File.WriteAllBytes($"out/{platform}/{fileName}-header", data.Take(index).ToArray());
                Bundle bundleData;
                using (var reader = new EndianBinaryReader(File.OpenRead($"out/{platform}/{fileName}-mod")))
                {
                    bundleData = new Bundle(reader);
                }
                List<string> fileList = new();
                foreach (var file in bundleData.FileList)
                {
                    if (file.fileName.EndsWith(".resS"))
                    {
                        using var fs = File.OpenWrite($"out/{platform}/old-{file.fileName}");
                        file.stream.CopyTo(fs);
                    }
                }
            }
        }

        public static void AddHeader(string platform)
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
                if (!File.Exists($"out/{platform}/{fileName}-mod") || !File.Exists($"out/{platform}/{fileName}-header")) continue;
                Bundle bundleData;
                using (var reader = new EndianBinaryReader(File.OpenRead($"out/{platform}/{fileName}-mod")))
                {
                    bundleData = new Bundle(reader);
                }

                List<string> fileList = new();
                foreach (var file in bundleData.FileList)
                {
                    if (File.Exists($"out/{platform}/{file.fileName}"))
                    {
                        Console.WriteLine($"Replacing: {file.fileName}");
                        file.stream = File.OpenRead($"out/{platform}/{file.fileName}");
                        fileList.Add($"out/{platform}/{file.fileName}");
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
                    using (FileStream fileStream = File.Create($"out/{platform}/{fileName}"))
                    {
                        fileStream.Write(File.ReadAllBytes($"out/{platform}/{fileName}-header"));
                        fileStream.Write(memoryStream.ToArray());
                    }
                }
            }
        }
    }
}
