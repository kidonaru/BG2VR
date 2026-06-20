using System;
using System.Runtime.InteropServices;
using System.Threading;
using static BG2VR.SpatialVoice.SteamAudio.PhononNative;

namespace BG2VR.SpatialVoice.SteamAudio
{
    /// <summary>
    /// Steam Audio binaural effect の managed ラッパ。init/dispose は main、<see cref="Process"/> は audio スレッド。
    /// init: context → hrtf(DEFAULT) → effect。dispose: 逆順 effect → hrtf → context（partial init も確実 release）。
    /// audio スレッドでは allocation / native 生成 / 例外を出さない（バッファ事前確保・struct は stack）。
    /// in-place 不可のため in(mono) / out(stereo) は別バッファ。全バッファは pin 済み（GC 移動なし）。
    /// </summary>
    internal sealed class BinauralRenderer
    {
        private IntPtr m_context;
        private IntPtr m_hrtf;
        private IntPtr m_effect;
        private volatile bool m_ready; // audio スレッドの実行可否ゲート（dispose で先に false）
        private int m_active;          // Process 実行中カウンタ（Interlocked。dispose との use-after-free barrier）
        private volatile bool m_frameMismatch; // DSP ブロック長が init 時 frameSize と不一致（main が一度だけログ）

        private int m_frameSize;

        /// <summary>init 時に確定した frameSize（dspBufferSize）。</summary>
        public int FrameSize => m_frameSize;
        /// <summary>実 DSP ブロック長が init 時 frameSize と食い違い HRTF を全バイパス中か（main からの診断用）。</summary>
        public bool FrameMismatch => m_frameMismatch;

        // pin 済みバッファ（mono 入力 / stereo 出力）と native へ渡す data ポインタ配列
        private float[] m_inMono, m_outL, m_outR;
        private GCHandle m_hIn, m_hOutL, m_hOutR;
        private IntPtr m_inData;  // float*[1]
        private IntPtr m_outData; // float*[2]

        public bool Ready => m_ready;

        /// <summary>main スレッドで init。失敗時は確実に release して false（呼び出し側はベストエフォート 2D）。</summary>
        public bool Init(int sampleRate, int frameSize)
        {
            if (m_ready) return true;
            m_frameSize = frameSize;
            if (!EnsureLoaded()) return false;
            try
            {
                var ctx = new IPLContextSettings
                {
                    version = STEAMAUDIO_VERSION,
                    logCallback = IntPtr.Zero,
                    allocateCallback = IntPtr.Zero,
                    freeCallback = IntPtr.Zero,
                    simdLevel = IPLSIMDLevel.AVX2, // CPU 能力で自動降格。AVX512 の throttle を避けつつ十分。
                    flags = 0,
                };
                if (iplContextCreate(ref ctx, out m_context) != IPLerror.Success || m_context == IntPtr.Zero)
                {
                    Plugin.Log.LogWarning("[SpatialVoice] iplContextCreate 失敗（本体 2D）。");
                    Dispose();
                    return false;
                }

                var audio = new IPLAudioSettings { samplingRate = sampleRate, frameSize = frameSize };
                var hrtfSettings = new IPLHRTFSettings
                {
                    type = IPLHRTFType.Default, // 内蔵 HRTF（SOFA 不要）
                    sofaFileName = IntPtr.Zero,
                    sofaData = IntPtr.Zero,
                    sofaDataSize = 0,
                    volume = 1f,
                    normType = IPLHRTFNormType.None,
                };
                if (iplHRTFCreate(m_context, ref audio, ref hrtfSettings, out m_hrtf) != IPLerror.Success || m_hrtf == IntPtr.Zero)
                {
                    Plugin.Log.LogWarning("[SpatialVoice] iplHRTFCreate 失敗（本体 2D）。");
                    Dispose();
                    return false;
                }

                var eff = new IPLBinauralEffectSettings { hrtf = m_hrtf };
                if (iplBinauralEffectCreate(m_context, ref audio, ref eff, out m_effect) != IPLerror.Success || m_effect == IntPtr.Zero)
                {
                    Plugin.Log.LogWarning("[SpatialVoice] iplBinauralEffectCreate 失敗（本体 2D）。");
                    Dispose();
                    return false;
                }

                AllocBuffers(frameSize);
                m_ready = true;
                Plugin.Log.LogInfo($"[SpatialVoice] Steam Audio init 完了（{sampleRate}Hz / frame {frameSize}）。");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SpatialVoice] Steam Audio init 例外: {ex.Message}（本体 2D）。");
                Dispose();
                return false;
            }
        }

        /// <summary>
        /// audio スレッドで interleaved stereo の <paramref name="data"/> を HRTF で上書きする。
        /// 入力は mono 化（L 成分）して gain を掛け、出力 stereo を書き戻す。
        /// ブロック長が init 時 frameSize と不一致 / 出力が stereo 未満ならバイパス（data 不変）。
        /// </summary>
        public void Process(float[] data, int channels, IPLVector3 dir, float spatialBlend, float gain)
        {
            // dispose との barrier: 先にカウントしてから状態を読む。dispose は m_ready=false 後 m_active==0 を待つ。
            Interlocked.Increment(ref m_active);
            try
            {
                if (!m_ready) return; // 増分後の二重チェック（締め出された後の使用を防ぐ）
                int n = channels > 0 ? data.Length / channels : 0;
                if (n != m_frameSize || channels < 2)
                {
                    if (n != m_frameSize) m_frameMismatch = true; // ブロック長不一致を main へ通知（無言 2D の検出用）
                    return; // 想定外ブロック → 素通し
                }

                for (int i = 0; i < n; i++)
                    m_inMono[i] = data[i * channels] * gain; // L 成分を mono 入力に（mono clip は L=R）

                var p = new IPLBinauralEffectParams
                {
                    direction = dir,
                    interpolation = IPLHRTFInterpolation.Bilinear, // 頭回転での量子化カクつき回避
                    spatialBlend = spatialBlend,
                    hrtf = m_hrtf,
                    peakDelays = IntPtr.Zero,
                };
                var inBuf = new IPLAudioBuffer { numChannels = 1, numSamples = n, data = m_inData };
                var outBuf = new IPLAudioBuffer { numChannels = 2, numSamples = n, data = m_outData };
                iplBinauralEffectApply(m_effect, ref p, ref inBuf, ref outBuf);

                for (int i = 0; i < n; i++)
                {
                    data[i * channels] = m_outL[i];
                    data[i * channels + 1] = m_outR[i];
                    // channels > 2 の余剰チャンネルは触らない（speakerMode=Stereo 前提）
                }
            }
            finally
            {
                Interlocked.Decrement(ref m_active);
            }
        }

        /// <summary>
        /// main スレッドで dispose。m_ready=false で新規 Process を締め出し、実行中の Process が抜ける（m_active==0）まで
        /// 待ってから native を逆順 release + バッファ free（use-after-free / native ヒープ破壊を防ぐ）。冪等。
        /// 待機は audio ブロック 1 個分（≈21ms @1024/48k）で抜ける。万一抜けなくても上限で諦めて free（app 終了をハングさせない）。
        /// </summary>
        public void Dispose()
        {
            m_ready = false; // 新規 Process を work パスから締め出す（volatile）
            for (int spins = 0; Volatile.Read(ref m_active) != 0 && spins < 200; spins++)
                Thread.Sleep(1); // 実行中の Process が抜けるまで待つ（teardown のみ・main スレッド）
            if (m_effect != IntPtr.Zero) iplBinauralEffectRelease(ref m_effect);
            if (m_hrtf != IntPtr.Zero) iplHRTFRelease(ref m_hrtf);
            if (m_context != IntPtr.Zero) iplContextRelease(ref m_context);
            m_effect = m_hrtf = m_context = IntPtr.Zero;
            FreeBuffers();
        }

        private void AllocBuffers(int n)
        {
            m_inMono = new float[n];
            m_outL = new float[n];
            m_outR = new float[n];
            m_hIn = GCHandle.Alloc(m_inMono, GCHandleType.Pinned);
            m_hOutL = GCHandle.Alloc(m_outL, GCHandleType.Pinned);
            m_hOutR = GCHandle.Alloc(m_outR, GCHandleType.Pinned);
            m_inData = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(m_inData, m_hIn.AddrOfPinnedObject());
            m_outData = Marshal.AllocHGlobal(IntPtr.Size * 2);
            Marshal.WriteIntPtr(m_outData, 0, m_hOutL.AddrOfPinnedObject());
            Marshal.WriteIntPtr(m_outData, IntPtr.Size, m_hOutR.AddrOfPinnedObject());
        }

        private void FreeBuffers()
        {
            if (m_inData != IntPtr.Zero) { Marshal.FreeHGlobal(m_inData); m_inData = IntPtr.Zero; }
            if (m_outData != IntPtr.Zero) { Marshal.FreeHGlobal(m_outData); m_outData = IntPtr.Zero; }
            if (m_hIn.IsAllocated) m_hIn.Free();
            if (m_hOutL.IsAllocated) m_hOutL.Free();
            if (m_hOutR.IsAllocated) m_hOutR.Free();
            m_inMono = m_outL = m_outR = null;
        }
    }
}
