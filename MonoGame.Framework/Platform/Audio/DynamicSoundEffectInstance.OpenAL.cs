// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;
using System.Collections.Generic;
using MonoGame.OpenAL;

namespace Microsoft.Xna.Framework.Audio
{
    public partial class DynamicSoundEffectInstance : SoundEffectInstance
    {
        private Queue<OALSoundBuffer> _queuedBuffers;
        private ALFormat _format;
        private bool _finishedQueueing = false;

        public void FinishedQueueing()
        {
            _finishedQueueing = true;
        }

        private void PlatformCreate()
        {
            if (_msadpcm)
            {
                _format = _channels == AudioChannels.Mono ? ALFormat.MonoMSAdpcm : ALFormat.StereoMSAdpcm;
                _sampleAlignment = AudioLoader.SampleAlignment(_format, _sampleAlignment);
            }
            else
            {
                _format = _channels == AudioChannels.Mono ? ALFormat.Mono16 : ALFormat.Stereo16;
                _sampleAlignment = 0;
            }

            InitializeSound();

            SourceId = controller.ReserveSource();
            HasSourceId = true;

            _queuedBuffers = new Queue<OALSoundBuffer>();
        }

        private int PlatformGetPendingBufferCount()
        {
            return _queuedBuffers.Count;
        }

        private void PlatformPlay()
        {
            _finishedQueueing = false;

            AL.GetError();

            // Ensure that the source is not looped (due to source recycling)
            AL.Source(SourceId, ALSourceb.Looping, false);
            ALHelper.CheckError("Failed to set source loop state.");

            AL.SourcePlay(SourceId);
            ALHelper.CheckError("Failed to play the source.");
        }

        private void PlatformPause()
        {
            AL.GetError();
            AL.SourcePause(SourceId);
            ALHelper.CheckError("Failed to pause the source.");
        }

        private void PlatformResume()
        {
            AL.GetError();
            AL.SourcePlay(SourceId);
            ALHelper.CheckError("Failed to play the source.");
        }

        private void PlatformStop()
        {
            _finishedQueueing = true;

            AL.GetError();
            AL.SourceStop(SourceId);
            ALHelper.CheckError("Failed to stop the source.");

            // Remove all queued buffers
            AL.Source(SourceId, ALSourcei.Buffer, 0);
            while (_queuedBuffers.Count > 0)
            {
                var buffer = _queuedBuffers.Dequeue();
                buffer.Dispose();
            }
        }

        private void PlatformSubmitBuffer(byte[] buffer, int offset, int count)
        {
            // Get a buffer
            OALSoundBuffer oalBuffer = new OALSoundBuffer();

            byte[] offsetBuffer = buffer;

            if (offset != 0)
            {
                // BindDataBuffer does not support offset
                offsetBuffer = new byte[count];
                Array.Copy(buffer, offset, offsetBuffer, 0, count);
                
            }

            if (IsFilterEnabled())
            {
                switch (_format)
                {
                    case ALFormat.Mono8:
                    case ALFormat.Stereo8:
                        FilterBuffer8(offsetBuffer, (int)_channels, _sampleRate);
                        break;

                    case ALFormat.Mono16:
                    case ALFormat.Stereo16:
                        FilterBuffer16(offsetBuffer, (int)_channels, _sampleRate);
                        break;
                }
            }

            // Bind the data
            oalBuffer.BindDataBuffer(offsetBuffer, _format, count, _sampleRate, _sampleAlignment);

            // Queue the buffer
            _queuedBuffers.Enqueue(oalBuffer);
            AL.SourceQueueBuffer(SourceId, oalBuffer.OpenALDataBuffer);
            ALHelper.CheckError();

            // If the source has run out of buffers, restart it
            var sourceState = AL.GetSourceState(SourceId);
            if (_state == SoundState.Playing && sourceState == ALSourceState.Stopped)
            {
                AL.SourcePlay(SourceId);
                ALHelper.CheckError("Failed to resume source playback.");
            }
        }

        private void PlatformDispose(bool disposing)
        {
            // SFXI disposal handles buffer detachment and source recycling
            base.Dispose(disposing);

            if (disposing)
            {
                while (_queuedBuffers.Count > 0)
                {
                    var buffer = _queuedBuffers.Dequeue();
                    buffer.Dispose();
                }

                DynamicSoundEffectInstanceManager.RemoveInstance(this);
            }
        }

        private void PlatformUpdateQueue()
        {
            // Get the completed buffers
            AL.GetError();
            int numBuffers;
            AL.GetSource(SourceId, ALGetSourcei.BuffersProcessed, out numBuffers);
            ALHelper.CheckError("Failed to get processed buffer count.");

            // Unqueue them
            if (numBuffers > 0)
            {
                AL.SourceUnqueueBuffers(SourceId, numBuffers);
                ALHelper.CheckError("Failed to unqueue buffers.");
                for (int i = 0; i < numBuffers; i++)
                {
                    var buffer = _queuedBuffers.Dequeue();
                    buffer.Dispose();
                }
            }

            // Raise the event for each removed buffer, if needed
            for (int i = 0; i < numBuffers; i++)
                CheckBufferCount();

            if (_queuedBuffers.Count == 0 && _finishedQueueing)
                Stop(true);
        }
    }
}
