// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;
using System.Runtime.InteropServices;
using MonoGame.OpenAL;
using RTAudioProcessing;

namespace Microsoft.Xna.Framework.Audio
{
    public partial class SoundEffectInstance : IDisposable
    {
        const int DefaultBufferCount = 3;
        const int DefaultBufferSize = 1024 * 32;

		internal SoundState SoundState = SoundState.Stopped;
        private uint _loopCount;
		private float _alVolume = 1f;

		internal int SourceId;
        private float reverb = 0f;
        bool applyFilter = false;
        EfxFilterType filterType;
        float filterQ;
        float frequency;
        int pauseCount;

        private bool _filterEnabled = false;

        private int[] BufferIds;
        private int[] BuffersAvailable;
        private bool HasBufferIds = false;
        private byte[] BufferData;
        private int BufferHead = 0;
        private bool BufferFinished = false;
        private int TimesPlayed = 0;

        private RtapRiver River = new RtapRiver();

        private readonly float[] FilterState = new float[2 * 2];

        internal FilterMode _filterMode
        {
            get
            {
                switch (filterType)
                {
                    case EfxFilterType.Highpass:
                        return FilterMode.HighPass;
                    case EfxFilterType.Bandpass:
                        return FilterMode.BandPass;
                    default:
                        return FilterMode.LowPass;
                }
            }
        }

        internal float _filterQ
        {
            get
            {
                return filterQ;
            }
        }

        internal float _filterFrequency
        {
            get
            {
                return frequency;
            }
        }

        internal readonly object sourceMutex = new object();
        
        internal OpenALSoundController controller;
        
        internal bool HasSourceId = false;

#region Initialization

        /// <summary>
        /// Creates a standalone SoundEffectInstance from given wavedata.
        /// </summary>
        internal void PlatformInitialize(byte[] buffer, int sampleRate, int channels)
        {
            InitializeSound();
        }

        /// <summary>
        /// Gets the OpenAL sound controller, constructs the sound buffer, and sets up the event delegates for
        /// the reserved and recycled events.
        /// </summary>
        internal void InitializeSound()
        {
            controller = OpenALSoundController.Instance;
        }

#endregion // Initialization

        /// <summary>
        /// Converts the XNA [-1, 1] pitch range to OpenAL pitch (0, INF) or Android SoundPool playback rate [0.5, 2].
        /// <param name="xnaPitch">The pitch of the sound in the Microsoft XNA range.</param>
        /// </summary>
        private static float XnaPitchToAlPitch(float xnaPitch)
        {
            return (float)Math.Pow(2, xnaPitch);
        }

        private void PlatformApply3D(AudioListener listener, AudioEmitter emitter)
        {
            if (!HasSourceId)
                return;
            // get AL's listener position
            float x, y, z;
            AL.GetListener(ALListener3f.Position, out x, out y, out z);
            ALHelper.CheckError("Failed to get source position.");

            // get the emitter offset from origin
            Vector3 posOffset = emitter.Position - listener.Position;
            // set up orientation matrix
            Matrix orientation = Matrix.CreateWorld(Vector3.Zero, listener.Forward, listener.Up);
            // set up our final position and velocity according to orientation of listener
            Vector3 finalPos = new Vector3(x + posOffset.X, y + posOffset.Y, z + posOffset.Z);
            finalPos = Vector3.Transform(finalPos, orientation);
            Vector3 finalVel = emitter.Velocity;
            finalVel = Vector3.Transform(finalVel, orientation);

            // set the position based on relative positon
            AL.Source(SourceId, ALSource3f.Position, finalPos.X, finalPos.Y, finalPos.Z);
            ALHelper.CheckError("Failed to set source position.");
            AL.Source(SourceId, ALSource3f.Velocity, finalVel.X, finalVel.Y, finalVel.Z);
            ALHelper.CheckError("Failed to set source velocity.");

            AL.Source(SourceId, ALSourcef.ReferenceDistance, SoundEffect.DistanceScale);
            ALHelper.CheckError("Failed to set source distance scale.");
            AL.DopplerFactor(SoundEffect.DopplerScale);
            ALHelper.CheckError("Failed to set Doppler scale.");
        }

        private void PlatformPause()
        {
            if (!HasSourceId || SoundState != SoundState.Playing)
                return;

            if (pauseCount == 0)
            {
                AL.SourcePause(SourceId);
                ALHelper.CheckError("Failed to pause source.");
            }
            ++pauseCount;
            SoundState = SoundState.Paused;
        }

        private void PlatformPlay()
        {
            if (!HasBufferIds)
            {
                BufferIds = AL.GenBuffers(DefaultBufferCount);
                ALHelper.CheckError("Failed to create SoundEffectInstance buffers.");

                HasBufferIds = true;
                BufferData = new byte[DefaultBufferSize];
            }

            for (int i = 0; i < 4; ++i)
                FilterState[i] = 0.0f;

            BuffersAvailable = new int[DefaultBufferCount];
            for (int i = 0; i < DefaultBufferCount; ++i)
                BuffersAvailable[i] = BufferIds[i];

            BufferHead = 0;
            BufferFinished = false;
            TimesPlayed = 0;

            SourceId = 0;
            HasSourceId = false;
            SourceId = controller.ReserveSource();
            HasSourceId = true;

            // Send the position, gain, looping, pitch, and distance model to the OpenAL driver.
            if (!HasSourceId)
				return;

            AL.Source(SourceId, ALSourcei.SourceRelative, 1);
            ALHelper.CheckError("Failed set source relative.");
            // Distance Model
			AL.DistanceModel (ALDistanceModel.InverseDistanceClamped);
            ALHelper.CheckError("Failed set source distance.");
			// Pan
			AL.Source (SourceId, ALSource3f.Position, _pan, 0f, 0f);
            ALHelper.CheckError("Failed to set source pan.");
            // Velocity
			AL.Source (SourceId, ALSource3f.Velocity, 0f, 0f, 0f);
            ALHelper.CheckError("Failed to set source pan.");
			// Volume
            AL.Source(SourceId, ALSourcef.Gain, _alVolume);
            ALHelper.CheckError("Failed to set source volume.");
            // Looping
            AL.Source(SourceId, ALSourcei.Buffer, 0);

            River.SetSpring(_effect.Spring);
            QueueBuffers();

			// Pitch
			AL.Source (SourceId, ALSourcef.Pitch, XnaPitchToAlPitch(_pitch));
            ALHelper.CheckError("Failed to set source pitch.");

            ApplyReverb ();
            ApplyFilter ();

            AL.SourcePlay(SourceId);
            ALHelper.CheckError("Failed to play source.");

            SoundState = SoundState.Playing;
        }

        private void PlatformResume()
        {
            if (!HasSourceId)
            {
                Play();
                return;
            }

            if (SoundState == SoundState.Paused)
            {
                --pauseCount;
                if (pauseCount == 0)
                {
                    AL.SourcePlay(SourceId);
                    ALHelper.CheckError("Failed to play source.");
                }
            }
            SoundState = SoundState.Playing;
        }

        private void PlatformStop(bool immediate)
        {
            FreeSource();
            SoundState = SoundState.Stopped;
            _filterEnabled = false;
            BufferFinished = true;
        }

        private void FreeSource()
        {
            if (!HasSourceId)
                return;

            lock (sourceMutex)
            {
                if (HasSourceId && AL.IsSource(SourceId))
                {
                    // ARTHUR 6/24/2021: It seems that sound sources aren't properly resetting their values, causing sounds to be distorted (pitched incorrectly) when recycled.
                    AL.Source(SourceId, ALSourceb.Looping, false);
                    AL.Source(SourceId, ALSource3f.Position, 0.0F, 0.0f, 0.1f);
                    AL.Source(SourceId, ALSourcef.Pitch, 1.0F);
                    AL.Source(SourceId, ALSourcef.Gain, 1.0F);

                    AL.SourceStop(SourceId);
                    ALHelper.CheckError("Failed to stop source.");

                    // Reset the SendFilter to 0 if we are NOT using reverb since
                    // sources are recycled
                    if (OpenALSoundController.Efx.IsInitialized) // ARTHUR 6/11/2021: Switched over from OpenALSoundController.SupportsEfx for consistency with reverb binding (and because SupportEfx returns false for some reason).
                    {
                        OpenALSoundController.Efx.BindSourceToAuxiliarySlot(SourceId, 0, 0, 0);
                        ALHelper.CheckError("Failed to unset reverb.");
                        AL.Source(SourceId, ALSourcei.EfxDirectFilter, 0);
                        ALHelper.CheckError("Failed to unset filter.");
                    }

                    controller.FreeSource(this);
                }
            }
        }

        private void PlatformSetIsLooped(bool value)
        {
            PlatformSetLoopCount(value ? 255u : 0u);
        }

        private bool PlatformGetIsLooped()
        {
            return LoopCount != 0u;
        }

        private void PlatformSetLoopCount(uint value)
        {
            _loopCount = value;
        }

        private uint PlatformGetLoopCount()
        {
            return _loopCount;
        }

        private void PlatformSetPan(float value)
        {
            if (HasSourceId)
            {
                AL.Source(SourceId, ALSource3f.Position, value, 0.0f, 0.1f);
                ALHelper.CheckError("Failed to set source pan.");
            }
        }

        private void PlatformSetPitch(float value)
        {
            if (HasSourceId)
            {
                AL.Source(SourceId, ALSourcef.Pitch, XnaPitchToAlPitch(value));
                ALHelper.CheckError("Failed to set source pitch.");
            }
        }

        private SoundState PlatformGetState()
        {
            if (!HasSourceId)
                return SoundState.Stopped;
            
            var alState = AL.GetSourceState(SourceId);
            ALHelper.CheckError("Failed to get source state.");

            switch (alState)
            {
                case ALSourceState.Initial:
                    SoundState = SoundState.Stopped;
                    break;

                case ALSourceState.Stopped:
                    if (SoundState != SoundState.Playing || BufferFinished)
                        SoundState = SoundState.Stopped;
                    break;

                case ALSourceState.Paused:
                    SoundState = SoundState.Paused;
                    break;

                case ALSourceState.Playing:
                    SoundState = SoundState.Playing;
                    break;
            }

            return SoundState;
        }

        private void PlatformSetVolume(float value)
        {
            _alVolume = value;

            if (HasSourceId)
            {
                AL.Source(SourceId, ALSourcef.Gain, _alVolume);
                ALHelper.CheckError("Failed to set source volume.");
            }
        }

        internal void PlatformSetReverbMix(float mix)
        {
            if (!OpenALSoundController.Efx.IsInitialized)
                return;
            reverb = mix;
            if (State == SoundState.Playing)
            {
                ApplyReverb();
                reverb = 0f;
            }
        }

        void ApplyReverb()
        {
            if (reverb > 0f && SoundEffect.ReverbSlot != 0)
            {
                OpenALSoundController.Efx.BindSourceToAuxiliarySlot(SourceId, (int)SoundEffect.ReverbSlot, 0, 0);
                ALHelper.CheckError("Failed to set reverb.");
            }
        }

        void ApplyFilter()
        {
            return;

            if (applyFilter && controller.Filter > 0)
            {
                var freq = frequency / 20000f;
                var lf = 1.0f - freq;
                var efx = OpenALSoundController.Efx;
                efx.Filter(controller.Filter, EfxFilteri.FilterType, (int)filterType);
                ALHelper.CheckError("Failed to set filter.");
                switch (filterType)
                {
                case EfxFilterType.Lowpass:
                    efx.Filter(controller.Filter, EfxFilterf.LowpassGainHF, freq);
                    ALHelper.CheckError("Failed to set LowpassGainHF.");
                    break;
                case EfxFilterType.Highpass:
                    efx.Filter(controller.Filter, EfxFilterf.HighpassGainLF, freq);
                    ALHelper.CheckError("Failed to set HighpassGainLF.");
                    break;
                case EfxFilterType.Bandpass:
                    efx.Filter(controller.Filter, EfxFilterf.BandpassGainHF, freq);
                    ALHelper.CheckError("Failed to set BandpassGainHF.");
                    efx.Filter(controller.Filter, EfxFilterf.BandpassGainLF, lf);
                    ALHelper.CheckError("Failed to set BandpassGainLF.");
                    break;
                }
                AL.Source(SourceId, ALSourcei.EfxDirectFilter, controller.Filter);
                ALHelper.CheckError("Failed to set DirectFilter.");
            }
        }

        internal void PlatformSetFilter(FilterMode mode, float filterQ, float frequency)
        {
            _filterEnabled = true;
            applyFilter = true;
            switch (mode)
            {
            case FilterMode.BandPass:
                filterType = EfxFilterType.Bandpass;
                break;
                case FilterMode.LowPass:
                filterType = EfxFilterType.Lowpass;
                break;
                case FilterMode.HighPass:
                filterType = EfxFilterType.Highpass;
                break;
            }
            this.filterQ = filterQ;
            this.frequency = frequency;
            if (State == SoundState.Playing)
            {
                ApplyFilter();
                applyFilter = false;
            }
        }

        internal bool IsFilterEnabled()
        {
            return _filterEnabled;
        }

        internal void PlatformClearFilter()
        {
            for (int i = 0; i < 4; ++i)
                FilterState[i] = 0.0f;
            applyFilter = false;
            _filterEnabled = false;
        }

        private void PlatformDispose(bool disposing)
        {
            FreeSource();
            if (HasBufferIds)
            {
                AL.DeleteBuffers(BufferIds);
                BufferIds = null;
                HasBufferIds = false;
            }
            River?.Dispose();
        }

        private unsafe bool QueueBuffer(int bufferId)
        {
            int springLength = _effect.Spring.Length;

            if (BufferHead > springLength)
                BufferHead = 0;

            int copySize = springLength - BufferHead;
            if (copySize > DefaultBufferSize)
                copySize = DefaultBufferSize;

            fixed (byte* bufferPtr = BufferData)
            {
                River.ReadInto((IntPtr)bufferPtr, BufferHead, copySize);

                int copyHead = copySize;
                BufferHead += copySize;
                if (BufferHead >= springLength)
                {
                    BufferHead = 0;
                    TimesPlayed++;
                }
                
                int unfilled = DefaultBufferSize - copySize;
                if (LoopCount >= 255 || TimesPlayed <= LoopCount)
                {
                    while (unfilled > 0)
                    {
                        copySize = springLength - BufferHead;
                        if (copySize > unfilled)
                            copySize = unfilled;

                        River.ReadInto((IntPtr)(bufferPtr + copyHead), BufferHead, copySize);

                        BufferHead += copySize;
                        copyHead += copySize;
                        unfilled -= copySize;

                        if (BufferHead >= springLength)
                        {
                            BufferHead = 0;
                            TimesPlayed++;
                        }

                        if (!(LoopCount >= 255 || TimesPlayed <= LoopCount))
                        {
                            BufferFinished = true;
                            break;
                        }
                    }
                }
                else
                {
                    BufferFinished = true;
                }

                // To-Myuu: Implement faster memory zero-ing
                for (int i = 0; i < unfilled; ++i)
                    BufferData[copyHead + i] = (byte)0;
            }

            if (_filterEnabled)
                FilterBuffer();

            fixed (byte* bufferPtr = BufferData)
            {
                AL.alBufferData((uint)bufferId, (int)_effect.SpringFormat, (IntPtr)bufferPtr, DefaultBufferSize, _effect.Spring.SampleRate);
            }

            AL.SourceQueueBuffers(SourceId, 1, new int[1] { bufferId });

            return true;
        }

        public unsafe void FilterBuffer()
        {
            if (!_filterEnabled)
                return;

            int channels = 1;
            int bytesPerSample = 1;
            switch (_effect.SpringFormat)
            {
                case ALFormat.Mono16:
                    bytesPerSample = 2;
                    break;

                case ALFormat.Stereo8:
                    channels = 2;
                    break;

                case ALFormat.Stereo16:
                    channels = 2;
                    bytesPerSample = 2;
                    break;
            }

            if (bytesPerSample == 1)
                FilterBuffer8(channels);
            else
                FilterBuffer16(channels);
        }

        const int FHIGH = (int)FilterMode.HighPass;
        const int FBAND = (int)FilterMode.BandPass;
        const int FLOW = (int)FilterMode.LowPass;

        public unsafe void FilterBuffer8(int channels)
        {
            // Adapted from https://vincentchoqueuse.github.io/personal_website/tutorials/digital_state_filter.html
            float filterFrequency = Math.Min((float)(2.0f * Math.Sin(Math.PI * Math.Min(frequency / ((float)_effect.Spring.SampleRate), 0.5f))), 1.0f);
            float oneOverQ = (float)(1.0f / filterQ);

            float* f = stackalloc float[3];

            int stereo = (channels != 0) ? 1 : 0;

            int clipLength = DefaultBufferSize >> stereo;

            int filterMode = (int)_filterMode;

            fixed (byte* bufferPtr = BufferData)
            {
                for (int s = 0; s < clipLength; ++s)
                {
                    int baseIdx = s << stereo;
                    for (int c = 0; c <= stereo; ++c)
                    {
                        int sc = baseIdx + c;
                        int fc = c << 1;
                        int intSample = bufferPtr[sc];
                        if ((intSample & (1 << 7)) != 0)
                            intSample = -128 + (intSample & 0x7f);
                        float sample = intSample / 128.0f;
                        f[FHIGH] = sample - FilterState[fc + 1] - (oneOverQ * FilterState[fc + 0]);
                        f[FBAND] = (filterFrequency * f[FHIGH]) + FilterState[fc + 0];
                        f[FLOW] = (filterFrequency * f[FBAND]) + FilterState[fc + 1];

                        int final = (int)(f[filterMode] * 127.0f + 0.5f);
                        if (final < -128)
                            final = -128;
                        else if (final > 127)
                            final = 127;

                        bufferPtr[sc] = (byte)final;

                        FilterState[fc + 0] = f[FBAND];
                        FilterState[fc + 1] = f[FLOW];
                    }
                }
            }
        }

        public unsafe void FilterBuffer16(int channels)
        {
            // Adapted from https://vincentchoqueuse.github.io/personal_website/tutorials/digital_state_filter.html
            float filterFrequency = Math.Min((float)(2.0f * Math.Sin(Math.PI * Math.Min(frequency / ((float)_effect.Spring.SampleRate), 0.5f))), 1.0f);
            float oneOverQ = (float)(1.0f / filterQ);

            float* f = stackalloc float[3];

            int stereo = (channels != 0) ? 1 : 0;

            int clipLength = DefaultBufferSize >> (stereo + 1);

            int filterMode = (int)_filterMode;

            fixed (byte* bufferPtr = BufferData)
            {
                for (int s = 0; s < clipLength; ++s)
                {
                    int baseIdx = s << (stereo + 1);
                    for (int c = 0; c <= stereo; ++c)
                    {
                        int sc = baseIdx + (c << 1);
                        int fc = c << 1;
                        int intSample = (((int)bufferPtr[sc + 1]) << 8) | ((int)bufferPtr[sc]);
                        if ((intSample & (1 << 15)) != 0)
                            intSample = -32768 + (intSample & 0x7FFF);
                        float sample = ((float)intSample) / 32768.0f;
                        f[FHIGH] = sample - FilterState[fc + 1] - (oneOverQ * FilterState[fc + 0]);
                        f[FBAND] = (filterFrequency * f[FHIGH]) + FilterState[fc + 0];
                        f[FLOW] = (filterFrequency * f[FBAND]) + FilterState[fc + 1];

                        int final = (int)(f[filterMode] * 32767.0f + 0.5f);
                        if (final < -32768)
                            final = -32768;
                        else if (final > 32767)
                            final = 32767;

                        bufferPtr[sc + 0] = (byte)(final & 0xff);
                        bufferPtr[sc + 1] = (byte)(final >> 8 );

                        FilterState[fc + 0] = f[FBAND];
                        FilterState[fc + 1] = f[FLOW];
                    }
                }
            }
        }

        private void QueueBuffers()
        {
            if (!HasSourceId || !HasBufferIds || BufferFinished || BuffersAvailable == null)
                return;

            int availableCount = BuffersAvailable.Length;
            for (int i = 0; i < availableCount; ++i)
            {
                QueueBuffer(BuffersAvailable[i]);
                if (BufferFinished)
                    break;
            }

            BuffersAvailable = null;
        }

        private void PlatformUpdateQueue()
        {
            if (!HasSourceId || !HasBufferIds)
                return;

            int processed;
            AL.GetSource(SourceId, ALGetSourcei.BuffersProcessed, out processed);
            ALHelper.CheckError("Failed to fetch processed buffers.");

            if (processed != 0)
            {
                BuffersAvailable = AL.SourceUnqueueBuffers(SourceId, processed);
                ALHelper.CheckError("Failed to unqueue buffers.");
            }

            QueueBuffers();

            var alState = AL.GetSourceState(SourceId);
            ALHelper.CheckError("Failed to get source state.");
            if (alState == ALSourceState.Stopped)
            {
                AL.SourcePlay(SourceId);
                ALHelper.CheckError("Failed to play.");
            }
        }
    }
}
