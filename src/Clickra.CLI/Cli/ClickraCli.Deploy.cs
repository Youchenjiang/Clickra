using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Clickra.Core;
using Clickra.UI;

namespace Clickra
{
    partial class ClickraCli
    {

        static void DeployAssets(string targetDir)
        {
            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

            var assembly = typeof(ClickraCli).Assembly;
            var resources = new Dictionary<string, string>
            {
                { "Clickra.Resources.AppxManifest.xml", "AppxManifest.xml" },
                { "Clickra.Resources.app.png", "app.png" },
                { "Clickra.Resources.Clickra.exe.manifest", "Clickra.exe.manifest" },
                { "Clickra.Resources.ClickraShell.dll.manifest", "ClickraShell.dll.manifest" },
                { "Clickra.Resources.ClickraShell.dll", "ClickraShell.dll" }
            };

            foreach (var res in resources)
            {
                string targetPath = Path.Combine(targetDir, res.Value);
                Console.WriteLine($"Deploying {res.Value}...");

                try
                {
                    WriteResourceToFile(assembly, res.Key, targetPath);
                }
                catch (IOException)
                {
                    // 檔案鎖定處理邏輯：如果被佔用，嘗試改名備份
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string backupPath = targetPath + ".old_" + timestamp;
                    try
                    {
                        Console.WriteLine($"[Warning] File {res.Value} is locked. Renaming to bypass lock...");
                        File.Move(targetPath, backupPath);
                        WriteResourceToFile(assembly, res.Key, targetPath);
                        Console.WriteLine("Successfully deployed via rename-bypass.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Error] Critical failure deploying {res.Value}: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error] Failed to deploy {res.Value}: {ex.Message}");
                }
            }
            Console.WriteLine("Deployment completed.");
        }

        static void WriteResourceToFile(Assembly assembly, string resourceName, string targetPath)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) throw new Exception($"Resource {resourceName} not found.");
            using var fileStream = File.Create(targetPath);
            stream.CopyTo(fileStream);
        }
    }
}
