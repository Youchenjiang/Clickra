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
        int generatedCount = ProcessFeatureIcons(featuresDir, outputDir, force)
                           + ProcessPrimaryAssets(primaryDir, outputDir, force);

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

    private static int ProcessFeatureIcons(string featuresDir, string outputDir, bool force)
    {
        if (!Directory.Exists(featuresDir))
        {
            return 0;
        }

        DateTime generatorTime = File.GetLastWriteTimeUtc(typeof(Program).Assembly.Location);
        int count = 0;
        foreach (string svgFile in Directory.GetFiles(featuresDir, "*.svg"))
        {
            string cmdName = Path.GetFileNameWithoutExtension(svgFile);
            string outIco = Path.Combine(outputDir, $"menu-{cmdName}.ico");

            if (!force && File.Exists(outIco) &&
                File.GetLastWriteTimeUtc(outIco) >= File.GetLastWriteTimeUtc(svgFile) &&
                File.GetLastWriteTimeUtc(outIco) >= generatorTime)
            {
                continue;
            }

            GenerateIco(svgFile, outIco, FeatureSizes);
            count++;
        }
        return count;
    }

    /// <summary>
    /// Synchronizes primary application icons from the raster assets.
    /// The pre-rasterized primary assets in assets/primary are the authoritative source of truth
    /// for app branding, ensuring byte-for-byte fidelity with store packaging requirements.
    /// If primary raster assets are absent but the master SVG exists, generates from the SVG fallback.
    /// </summary>
    private static int ProcessPrimaryAssets(string primaryDir, string outputDir, bool force)
    {
        int count = 0;
        string primaryIco = Path.Combine(primaryDir, "clickra-icon-primary.ico");
        string primaryPng = Path.Combine(primaryDir, "clickra-icon-primary-256.png");
        string masterSvg = Path.Combine(primaryDir, "clickra-icon-primary-master.svg");
        string outAppIco = Path.Combine(outputDir, "app.ico");
        string outAppPng = Path.Combine(outputDir, "app.png");

        if (File.Exists(primaryIco))
        {
            if (force || !File.Exists(outAppIco) || File.GetLastWriteTimeUtc(outAppIco) < File.GetLastWriteTimeUtc(primaryIco))
            {
                File.Copy(primaryIco, outAppIco, true);
                count++;
            }
        }
        else if (File.Exists(masterSvg))
        {
            GenerateIco(masterSvg, outAppIco, PrimaryIcoSizes);
            count++;
        }

        if (File.Exists(primaryPng))
        {
            if (force || !File.Exists(outAppPng) || File.GetLastWriteTimeUtc(outAppPng) < File.GetLastWriteTimeUtc(primaryPng))
            {
                File.Copy(primaryPng, outAppPng, true);
                count++;
            }
        }
        else if (File.Exists(masterSvg))
        {
            GeneratePng(masterSvg, outAppPng, 256, 256);
            count++;
        }

        return count;
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
        for (int frameIndex = 0; frameIndex < sizes.Length; frameIndex++)
        {
            int frameSize = sizes[frameIndex];
            using var bmp = svgDoc.Draw(frameSize, frameSize);
            using var msPng = new MemoryStream();
            bmp.Save(msPng, ImageFormat.Png);
            pngBytes[frameIndex] = msPng.ToArray();
        }

        // Directory entries: 16 bytes each
        int offset = 6 + (16 * sizes.Length);
        for (int frameIndex = 0; frameIndex < sizes.Length; frameIndex++)
        {
            int frameSize = sizes[frameIndex];
            int length = pngBytes[frameIndex].Length;

            bw.Write((byte)(frameSize >= 256 ? 0 : frameSize)); // Width
            bw.Write((byte)(frameSize >= 256 ? 0 : frameSize)); // Height
            bw.Write((byte)0); // Color palette
            bw.Write((byte)0); // Reserved
            bw.Write((short)1); // Planes
            bw.Write((short)32); // Bits per pixel
            bw.Write(length); // Bytes in resource
            bw.Write(offset); // Offset
            offset += length;
        }

        // Write raw PNG bytes
        for (int frameIndex = 0; frameIndex < sizes.Length; frameIndex++)
        {
            bw.Write(pngBytes[frameIndex]);
        }

        string tempIco = $"{icoPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(tempIco, msIco.ToArray());
            File.Move(tempIco, icoPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempIco))
            {
                try { File.Delete(tempIco); } catch { /* ignore cleanup error */ }
            }
            throw;
        }
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
