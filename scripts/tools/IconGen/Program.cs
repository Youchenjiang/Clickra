using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Svg;

namespace Clickra.IconGen;

public static class Program
{
    private static readonly int[] FeatureSizes = [16, 20, 24, 32, 48, 64];
    private static readonly int[] PrimaryIcoSizes = [16, 24, 32, 48, 64, 128, 256];

    public static int Main(string[] args)
    {
        bool force = args.Length > 0 && args[0].Equals("--force", StringComparison.OrdinalIgnoreCase);
        string repoRoot = FindRepoRoot();
        if (string.IsNullOrEmpty(repoRoot))
        {
            Console.Error.WriteLine("Error: Unable to locate Clickra repository root.");
            return 1;
        }

        string assetsDir = Path.Combine(repoRoot, "assets");
        string featuresDir = Path.Combine(assetsDir, "features");
        string primaryDir = Path.Combine(assetsDir, "primary");
        string outputDir = Path.Combine(repoRoot, "obj", "icons");

        Directory.CreateDirectory(outputDir);

        var swTotal = Stopwatch.StartNew();
        int generatedCount = 0;

        // 1. Process 20 feature command icons
        if (Directory.Exists(featuresDir))
        {
            foreach (string svgFile in Directory.GetFiles(featuresDir, "*.svg"))
            {
                string cmdName = Path.GetFileNameWithoutExtension(svgFile);
                string outIco = Path.Combine(outputDir, $"menu-{cmdName}.ico");

                if (!force && File.Exists(outIco) && File.GetLastWriteTimeUtc(outIco) >= File.GetLastWriteTimeUtc(svgFile))
                {
                    continue;
                }

                GenerateIco(svgFile, outIco, FeatureSizes);
                generatedCount++;
            }
        }

        // 2. Process primary app icon and PNG
        string primaryIco = Path.Combine(primaryDir, "clickra-icon-primary.ico");
        string primaryPng = Path.Combine(primaryDir, "clickra-icon-primary-256.png");
        string outAppIco = Path.Combine(outputDir, "app.ico");
        string outAppPng = Path.Combine(outputDir, "app.png");

        if (File.Exists(primaryIco))
        {
            if (force || !File.Exists(outAppIco) || File.GetLastWriteTimeUtc(outAppIco) < File.GetLastWriteTimeUtc(primaryIco))
            {
                File.Copy(primaryIco, outAppIco, true);
                generatedCount++;
            }
        }

        if (File.Exists(primaryPng))
        {
            if (force || !File.Exists(outAppPng) || File.GetLastWriteTimeUtc(outAppPng) < File.GetLastWriteTimeUtc(primaryPng))
            {
                File.Copy(primaryPng, outAppPng, true);
                generatedCount++;
            }
        }

        swTotal.Stop();
        if (generatedCount > 0)
        {
            Console.WriteLine($"[IconGen] Generated {generatedCount} icon assets in {swTotal.ElapsedMilliseconds} ms -> {outputDir}");
        }
        else
        {
            Console.WriteLine($"[IconGen] All icon assets are up to date ({swTotal.ElapsedMilliseconds} ms).");
        }

        return 0;
    }

    private static void GenerateIco(string svgPath, string icoPath, int[] sizes)
    {
        var svgDoc = SvgDocument.Open(svgPath);
        using var msIco = new MemoryStream();
        using var bw = new BinaryWriter(msIco);

        // Header: Reserved (0), Type (1 = ICO), ImageCount
        bw.Write((short)0);
        bw.Write((short)1);
        bw.Write((short)sizes.Length);

        var pngBytes = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
        {
            int s = sizes[i];
            using var bmp = svgDoc.Draw(s, s);
            using var msPng = new MemoryStream();
            bmp.Save(msPng, ImageFormat.Png);
            pngBytes[i] = msPng.ToArray();
        }

        // Directory entries: 16 bytes each
        int offset = 6 + (16 * sizes.Length);
        for (int i = 0; i < sizes.Length; i++)
        {
            int s = sizes[i];
            int length = pngBytes[i].Length;

            bw.Write((byte)(s >= 256 ? 0 : s)); // Width
            bw.Write((byte)(s >= 256 ? 0 : s)); // Height
            bw.Write((byte)0); // Color palette
            bw.Write((byte)0); // Reserved
            bw.Write((short)1); // Planes
            bw.Write((short)32); // Bits per pixel
            bw.Write(length); // Bytes in resource
            bw.Write(offset); // Offset
            offset += length;
        }

        // Write raw PNG bytes
        for (int i = 0; i < sizes.Length; i++)
        {
            bw.Write(pngBytes[i]);
        }

        File.WriteAllBytes(icoPath, msIco.ToArray());
    }

    private static void GeneratePng(string svgPath, string pngPath, int width, int height)
    {
        var svgDoc = SvgDocument.Open(svgPath);
        using var bmp = svgDoc.Draw(width, height);
        bmp.Save(pngPath, ImageFormat.Png);
    }

    private static string FindRepoRoot()
    {
        string? current = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, "assets")) &&
                Directory.Exists(Path.Combine(current, "src")))
            {
                return current;
            }
            current = Directory.GetParent(current)?.FullName;
        }
        return string.Empty;
    }
}
