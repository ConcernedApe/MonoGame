// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Microsoft.Xna.Framework.Audio
{
    public partial class DynamicSoundEffectInstance : SoundEffectInstance
    {
        internal FAudio.FAudioWaveFormatEx format;

        private List<IntPtr> queuedBuffers;
        private List<uint> queuedSizes;
        private bool _finishedQueueing;

        public void FinishedQueueing()
        {
            _finishedQueueing = true;
        }

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
            _finishedQueueing = false;
            base.Play();
        }

        private void PlatformPause()
        {
            base.Pause();
        }

        private void PlatformResume()
        {
            base.Resume();
        }
        
        private void PlatformStop()
        {
            _finishedQueueing = true;
            base.Stop();
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
            ClearBuffers();
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


                // Raise the event for each removed buffer, if needed
                CheckBufferCount();

                if (state.BuffersQueued == 0 && _finishedQueueing)
                {
                    Stop(true);
                }
            }
        }
    }
}
