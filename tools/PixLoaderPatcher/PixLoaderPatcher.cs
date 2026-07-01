using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using Mono.Cecil;

/// <summary>
/// BepInEx Preloader Patcher — D3D12 初期化前に WinPixGpuCapturer.dll をロードし、
/// PIX の GPU Capture attach を可能にする。dev 専用・リリース非同梱。
/// BepInEx/patchers/ に配置する。
/// </summary>
public static class PixLoaderPatcher
{
    [DllImport("kernel32", SetLastError = true)]
    static extern IntPtr LoadLibraryW([MarshalAs(UnmanagedType.LPWStr)] string lpFileName);

    static readonly ManualLogSource Log = Logger.CreateLogSource("PixLoader");

    public static IEnumerable<string> TargetDLLs { get; } = Array.Empty<string>();

    public static void Patch(AssemblyDefinition assembly) { }

    public static void Initialize()
    {
        var pixDir = FindPixInstallation();
        if (pixDir == null)
        {
            Log.LogInfo("PIX 未インストール — スキップ");
            return;
        }

        var dllPath = Path.Combine(pixDir, "WinPixGpuCapturer.dll");
        if (!File.Exists(dllPath))
        {
            Log.LogWarning($"WinPixGpuCapturer.dll が見つからない: {dllPath}");
            return;
        }

        var handle = LoadLibraryW(dllPath);
        if (handle == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            Log.LogError($"LoadLibrary 失敗 (Win32 error {err}): {dllPath}");
            return;
        }

        Log.LogInfo($"WinPixGpuCapturer.dll ロード完了: {dllPath}");
    }

    static string FindPixInstallation()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pixRoot = Path.Combine(programFiles, "Microsoft PIX");
        if (!Directory.Exists(pixRoot)) return null;

        var dirs = Directory.GetDirectories(pixRoot);
        if (dirs.Length == 0) return null;

        // 最新バージョンを選択
        Array.Sort(dirs);
        return dirs[dirs.Length - 1];
    }
}
