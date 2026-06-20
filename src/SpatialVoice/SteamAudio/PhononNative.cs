using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace BG2VR.SpatialVoice.SteamAudio
{
    /// <summary>
    /// Steam Audio（phonon.dll・Apache-2.0）C API の P/Invoke 宣言と struct。
    /// 構造体レイアウト・シグネチャは同梱 phonon.h（4.8.1）を権威とする。
    /// BepInEx plugin dir は DLL 検索パス外のため、起動時に <see cref="EnsureLoaded"/> で
    /// plugin dll 自ディレクトリ基準の絶対パスを LoadLibrary してから DllImport("phonon") を解決する
    /// （Windows は base 名一致で既ロードモジュールに解決する）。fork OpenXR.cs の流儀を踏襲。
    /// </summary>
    internal static class PhononNative
    {
        /// <summary>同梱 phonon.dll のバージョン（4.8.1 = (4&lt;&lt;16)|(8&lt;&lt;8)|1）。
        /// IPLContextSettings.version に必須。欠落/不一致は iplContextCreate 即失敗。</summary>
        public const uint STEAMAUDIO_VERSION = (4u << 16) | (8u << 8) | 1u; // = 264193 (0x040801)

        private const string Lib = "phonon";

        // ── enums（phonon.h と一致。int backing = C enum 既定） ──
        internal enum IPLerror { Success = 0, Failure = 1, OutOfMemory = 2, Initialization = 3 }
        internal enum IPLHRTFType { Default = 0, Sofa = 1 }
        internal enum IPLHRTFNormType { None = 0, Rms = 1 }
        internal enum IPLHRTFInterpolation { Nearest = 0, Bilinear = 1 }
        internal enum IPLAudioEffectState { TailRemaining = 0, TailComplete = 1 }
        internal enum IPLSIMDLevel { SSE2 = 0, SSE4 = 1, AVX = 2, AVX2 = 3, AVX512 = 4 }

        // ── structs（[StructLayout(Sequential)] + 既定 Pack = x64 natural alignment = C ABI 一致） ──
        [StructLayout(LayoutKind.Sequential)]
        internal struct IPLVector3
        {
            public float x, y, z;
            public IPLVector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct IPLContextSettings
        {
            public uint version;
            public IntPtr logCallback;      // 既定 NULL
            public IntPtr allocateCallback; // 既定 NULL
            public IntPtr freeCallback;     // 既定 NULL
            public IPLSIMDLevel simdLevel;  // SIMD 上限（CPU 能力で自動降格）
            public int flags;               // IPLContextFlags（0 = 検証なし）
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct IPLAudioSettings
        {
            public int samplingRate;
            public int frameSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct IPLHRTFSettings
        {
            public IPLHRTFType type;
            public IntPtr sofaFileName;  // SOFA 用（DEFAULT では NULL）
            public IntPtr sofaData;
            public int sofaDataSize;
            public float volume;
            public IPLHRTFNormType normType;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct IPLBinauralEffectSettings
        {
            public IntPtr hrtf;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct IPLBinauralEffectParams
        {
            public IPLVector3 direction;            // リスナー → 音源への単位ベクトル
            public IPLHRTFInterpolation interpolation;
            public float spatialBlend;              // 0 = 非空間化 / 1 = 完全空間化
            public IntPtr hrtf;                     // apply 毎に設定（保持ハンドル）
            public IntPtr peakDelays;               // NULL 可
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct IPLAudioBuffer
        {
            public int numChannels;
            public int numSamples;
            public IntPtr data; // float** = チャンネル base ポインタの配列
        }

        // ── native loading ──
        private static class Kernel32
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern IntPtr LoadLibrary(string lpFileName);
        }

        private static IntPtr s_handle = IntPtr.Zero;
        private static bool s_triedLoad;

        /// <summary>phonon.dll がロード済みか。</summary>
        public static bool Loaded => s_handle != IntPtr.Zero;

        /// <summary>
        /// plugin dll と同じディレクトリの phonon.dll を絶対パス LoadLibrary する（冪等・失敗は再試行しない）。
        /// 失敗時は false（呼び出し側はベストエフォートで本体 2D にフォールバック）。
        /// </summary>
        public static bool EnsureLoaded()
        {
            if (s_handle != IntPtr.Zero) return true;
            if (s_triedLoad) return false;
            s_triedLoad = true;
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(dir))
                {
                    Plugin.Log.LogWarning("[SpatialVoice] plugin dll のディレクトリを取得できません（phonon.dll ロード不可）。");
                    return false;
                }
                string path = Path.Combine(dir, Lib + ".dll");
                if (!File.Exists(path))
                {
                    Plugin.Log.LogWarning($"[SpatialVoice] phonon.dll が見つかりません: {path}（空間化ボイス無効・本体 2D）。");
                    return false;
                }
                s_handle = Kernel32.LoadLibrary(path);
                if (s_handle == IntPtr.Zero)
                {
                    Plugin.Log.LogWarning($"[SpatialVoice] phonon.dll の LoadLibrary に失敗（err={Marshal.GetLastWin32Error()}）。本体 2D。");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SpatialVoice] phonon.dll ロード中に例外: {ex.Message}（本体 2D）。");
                return false;
            }
        }

        // ── API（必要分のみ。詳細は phonon.h） ──
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IPLerror iplContextCreate(ref IPLContextSettings settings, out IntPtr context);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void iplContextRelease(ref IntPtr context);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IPLerror iplHRTFCreate(IntPtr context, ref IPLAudioSettings audioSettings,
            ref IPLHRTFSettings hrtfSettings, out IntPtr hrtf);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void iplHRTFRelease(ref IntPtr hrtf);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IPLerror iplBinauralEffectCreate(IntPtr context, ref IPLAudioSettings audioSettings,
            ref IPLBinauralEffectSettings effectSettings, out IntPtr effect);

        // in-place 不可。in は 1 or 2ch、out は 2ch（phonon.h）。
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IPLAudioEffectState iplBinauralEffectApply(IntPtr effect,
            ref IPLBinauralEffectParams parameters, ref IPLAudioBuffer inBuf, ref IPLAudioBuffer outBuf);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void iplBinauralEffectRelease(ref IntPtr effect);
    }
}
