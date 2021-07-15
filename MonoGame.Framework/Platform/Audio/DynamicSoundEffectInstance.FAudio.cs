﻿#if FAUDIO
// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MonoGame.OpenAL;

namespace Microsoft.Xna.Framework.Audio
{
    public sealed partial class DynamicSoundEffectInstance : SoundEffectInstance
    {
        internal FAudio.FAudioWaveFormatEx format;

        private List<IntPtr> queuedBuffers;
        private List<uint> queuedSizes;

        private const int MINIMUM_BUFFER_CHECK = 3;

        private void PlatformCreate()
        {
            format = new FAudio.FAudioWaveFormatEx();
            format.wFormatTag = 1;
            format.nChannels = (ushort)_channels;
            format.nSamplesPerSec = (uint)_sampleRate;
            format.wBitsPerSample = 16;
            format.nBlockAlign = (ushort)(2 * format.nChannels);
            format.nAvgBytesPerSec = format.nBlockAlign * format.nSamplesPerSec;
            format.cbSize = 0;

            SoundEffect.Device();

            queuedBuffers = new List<IntPtr>();
            queuedSizes = new List<uint>();

            InitDSPSettings(format.nChannels);
        }

        private int PlatformGetPendingBufferCount()
        {
            return queuedBuffers.Count;
        }

        private void PlatformPlay()
        {
            base.Play();

            /*
            lock (FrameworkDispatcher.Streams)
            {
                FrameworkDispatcher.Streams.Add(this);
            }*/
        }

        private void PlatformPause()
        {
            
        }

        private void PlatformResume()
        {

        }

        private void PlatformStop()
        {

        }

        private void PlatformSubmitBuffer(byte[] buffer, int offset, int count)
        {
            IntPtr next = Marshal.AllocHGlobal(count);
            Marshal.Copy(buffer, offset, next, count);
            lock (queuedBuffers)
            {
                queuedBuffers.Add(next);
                if (State != SoundState.Stopped)
                {
                    FAudio.FAudioBuffer buf = new FAudio.FAudioBuffer();
                    buf.AudioBytes = (uint)count;
                    buf.pAudioData = next;
                    buf.PlayLength = (
                        buf.AudioBytes /
                        (uint)_channels /
                        (uint)(format.wBitsPerSample / 8)
                    );
                    FAudio.FAudioSourceVoice_SubmitSourceBuffer(
                        handle,
                        ref buf,
                        IntPtr.Zero
                    );
                }
                else
                {
                    queuedSizes.Add((uint)count);
                }
            }
        }

        private void PlatformDispose(bool disposing)
        {
            base.Dispose(disposing);
        }

        internal void QueueInitialBuffers()
        {
            FAudio.FAudioBuffer buffer = new FAudio.FAudioBuffer();
            lock (queuedBuffers)
            {
                for (int i = 0; i < queuedBuffers.Count; i += 1)
                {
                    buffer.AudioBytes = queuedSizes[i];
                    buffer.pAudioData = queuedBuffers[i];
                    buffer.PlayLength = (
                        buffer.AudioBytes /
                        (uint)_channels /
                        (uint)(format.wBitsPerSample / 8)
                    );
                    FAudio.FAudioSourceVoice_SubmitSourceBuffer(
                        handle,
                        ref buffer,
                        IntPtr.Zero
                    );
                }
                queuedSizes.Clear();
            }
        }

        internal void ClearBuffers()
        {
            lock (queuedBuffers)
            {
                foreach (IntPtr buf in queuedBuffers)
                {
                    Marshal.FreeHGlobal(buf);
                }
                queuedBuffers.Clear();
                queuedSizes.Clear();
            }
        }

        private void PlatformUpdateQueue()
        {
            if (State != SoundState.Playing)
            {
                // Shh, we don't need you right now...
                return;
            }

            if (handle != IntPtr.Zero)
            {
                FAudio.FAudioVoiceState state;
                FAudio.FAudioSourceVoice_GetState(
                    handle,
                    out state,
                    FAudio.FAUDIO_VOICE_NOSAMPLESPLAYED
                );
                while (PendingBufferCount > state.BuffersQueued)
                {
                    lock (queuedBuffers)
                    {
                        Marshal.FreeHGlobal(queuedBuffers[0]);
                        queuedBuffers.RemoveAt(0);
                    }
                }
            }
        }
    }
}
#endif
