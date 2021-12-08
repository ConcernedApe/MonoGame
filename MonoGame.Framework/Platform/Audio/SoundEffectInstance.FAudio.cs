#if FAUDIO
using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;

namespace Microsoft.Xna.Framework.Audio
{
	public partial class SoundEffectInstance : IDisposable
	{
		private bool hasStarted;
		private bool is3D;
		private bool usingReverb;
        
		private FAudio.F3DAUDIO_DSP_SETTINGS dspSettings;

		internal IntPtr handle;

        private float _reverbMix = 0.0F;

        internal bool _filterDirty = false;
        internal bool _filterEnabled = false;
        internal FilterMode _filterMode;
        internal float _filterQ;
        internal float _filterFrequency;

        private bool _isLooped;

        internal SoundState _state = SoundState.Stopped;

		internal void PlatformInitialize(byte[] buffer, int sampleRate, int channels)
		{
			SoundEffect.Device();
		}

		private void PlatformApply3D(AudioListener listener, AudioEmitter emitter)
		{
		}

		private void PlatformPlay()
		{
			FAudioContext dev = SoundEffect.Device();

			/* Create handle */
			if (_isDynamic)
			{
				FAudio.FAudio_CreateSourceVoice(
					dev.Handle,
					out handle,
					ref (this as DynamicSoundEffectInstance).format,
					FAudio.FAUDIO_VOICE_USEFILTER,
					FAudio.FAUDIO_DEFAULT_FREQ_RATIO,
					IntPtr.Zero,
					IntPtr.Zero,
					IntPtr.Zero
				);
			}
			else
			{
				FAudio.FAudio_CreateSourceVoice(
					dev.Handle,
					out handle,
					_effect.formatPtr,
					FAudio.FAUDIO_VOICE_USEFILTER,
					FAudio.FAUDIO_DEFAULT_FREQ_RATIO,
					IntPtr.Zero,
					IntPtr.Zero,
					IntPtr.Zero
				);
			}
			if (handle == IntPtr.Zero)
			{
				return; /* What */
			}

            /* Apply current properties */
            PlatformSetVolume(this.Volume);
			UpdatePitch();

			if (is3D || Pan != 0.0f)
			{
				FAudio.FAudioVoice_SetOutputMatrix(
					handle,
					SoundEffect.Device().MasterVoice,
					dspSettings.SrcChannelCount,
					dspSettings.DstChannelCount,
					dspSettings.pMatrixCoefficients,
					0
				);
			}

			/* For static effects, submit the buffer now */
			if (_isDynamic)
			{
				(this as DynamicSoundEffectInstance).QueueInitialBuffers();
			}
			else
			{
                if (IsLooped)
                {
                    _effect.handle.LoopCount = 255;
                    _effect.handle.LoopBegin = _effect.loopStart;
                    _effect.handle.LoopLength = _effect.loopLength;
                }
                else
                {
                    _effect.handle.LoopCount = 0;
                    _effect.handle.LoopBegin = 0;
                    _effect.handle.LoopLength = 0;
                }

                FAudio.FAudioSourceVoice_SubmitSourceBuffer(
                    handle,
                    ref _effect.handle,
                    IntPtr.Zero
                );
			}

            if (!_isDynamic)
			{
				InitDSPSettings(_effect.channels);
			}

			ApplyReverb();
			ApplyFilter();

			/* Play, finally. */
			FAudio.FAudioSourceVoice_Start(handle, 0, 0);

			_state = SoundState.Playing;
			hasStarted = true;
		}

		private void PlatformPause()
		{
			FAudio.FAudioSourceVoice_Stop(handle, 0, 0);
			_state = SoundState.Paused;
		}

		private void PlatformResume()
		{
			FAudio.FAudioSourceVoice_Start(handle, 0, 0);
			_state = SoundState.Playing;
		}

		private void PlatformStop(bool immediate)
		{
			if (handle == IntPtr.Zero)
			{
				return;
			}

			if (immediate)
			{
				FAudio.FAudioSourceVoice_Stop(handle, 0, 0);
				FAudio.FAudioSourceVoice_FlushSourceBuffers(handle);
				FAudio.FAudioVoice_DestroyVoice(handle);
				handle = IntPtr.Zero;
				usingReverb = false;
				_state = SoundState.Stopped;
                hasStarted = false;
                _filterDirty = false;
                _filterEnabled = false;

				if (_isDynamic)
				{
					/*
					lock (FrameworkDispatcher.Streams)
					{
						FrameworkDispatcher.Streams.Remove(
							this as DynamicSoundEffectInstance
						);
					}
					*/
					(this as DynamicSoundEffectInstance).ClearBuffers();
				}
			}
			else
			{
				if (_isDynamic)
				{
					throw new InvalidOperationException();
				}
				FAudio.FAudioSourceVoice_ExitLoop(handle, 0);
			}
		}

		private void PlatformSetIsLooped(bool value)
		{
            _isLooped = value;
        }

		private bool PlatformGetIsLooped()
		{
			return _isLooped;
		}

		private void PlatformSetPan(float value)
		{
            // According to XNA documentation:
            // "Panning, ranging from -1.0f (full left) to 1.0f (full right). 0.0f is centered."
            _pan = MathHelper.Clamp(value, -1.0f, 1.0f);

            if (handle == IntPtr.Zero)
            {
                return;
            }

            SetPanMatrixCoefficients();
        }

		private void PlatformSetPitch(float value)
		{
            if (handle == IntPtr.Zero)
            {
                return;
            }

            UpdatePitch();
        }

		private SoundState PlatformGetState()
		{
			if (!_isDynamic &&
				handle != IntPtr.Zero &&
				_state == SoundState.Playing)
			{
				FAudio.FAudioVoiceState state;

				FAudio.FAudioSourceVoice_GetState(
					handle,
					out state,
					FAudio.FAUDIO_VOICE_NOSAMPLESPLAYED
				);

				if (state.BuffersQueued == 0)
				{
					Stop(true);
				}
			}

			return _state;
		}

		private void PlatformSetVolume(float value)
		{


            if (handle == IntPtr.Zero)
            {
                return;
            }

            FAudio.FAudioVoice_SetVolume(handle, value, 0);
        }

		internal void PlatformSetReverbMix(float mix)
		{
            _reverbMix = mix;

            if (handle == IntPtr.Zero)
            {
                return;
            }

            ApplyReverb();
        }

		internal void PlatformSetFilter(FilterMode mode, float filterQ, float frequency)
		{
            _filterEnabled = true;
            _filterDirty = true;
			_filterMode = mode;
			_filterQ = filterQ;
			_filterFrequency = frequency;

			if (State == SoundState.Playing)
			{
				ApplyFilter();
			}
		}

        internal bool IsFilterEnabled()
        {
            return _filterEnabled;
        }

		internal void PlatformClearFilter()
		{
            _filterDirty = false;
            _filterEnabled = false;
        }

		private void PlatformDispose(bool disposing)
		{
			Marshal.FreeHGlobal(dspSettings.pMatrixCoefficients);
		}

		internal void InitDSPSettings(uint srcChannels)
		{
			dspSettings = new FAudio.F3DAUDIO_DSP_SETTINGS();
			dspSettings.DopplerFactor = 1.0f;
			dspSettings.SrcChannelCount = srcChannels;
			dspSettings.DstChannelCount = SoundEffect.Device().DeviceDetails.OutputFormat.Format.nChannels;

			int memsize = (
				4 *
				(int)dspSettings.SrcChannelCount *
				(int)dspSettings.DstChannelCount
			);
			dspSettings.pMatrixCoefficients = Marshal.AllocHGlobal(memsize);
			unsafe
			{
				byte* memPtr = (byte*)dspSettings.pMatrixCoefficients;
				for (int i = 0; i < memsize; i += 1)
				{
					memPtr[i] = 0;
				}
			}
			SetPanMatrixCoefficients();
		}

        private void UpdatePitch()
        {
            float doppler;
            float dopplerScale = SoundEffect.Device().DopplerScale;
            if (!is3D || dopplerScale == 0.0f)
            {
                doppler = 1.0f;
            }
            else
            {
                doppler = dspSettings.DopplerFactor * dopplerScale;
            }

            FAudio.FAudioSourceVoice_SetFrequencyRatio(
                handle,
                (float)Math.Pow(2.0, _pitch) * doppler,
                0
            );
        }

        void ApplyReverb()
		{
            _ApplyReverb(_reverbMix);
		}

		void ApplyFilter()
		{
			if (!_filterDirty)
			{
				return;
			}

			// From FACT_internal.c FACT_INTERNAL_CalculateFilterFrequency
			float cutoff = (float)(2 * Math.Sin(Math.PI * Math.Min(_filterFrequency / _effect.sampleRate, 0.5F)));
			cutoff = Math.Min(cutoff, 1.0F);

			float one_over_q = 1.0F / _filterQ;

			if (_filterMode == FilterMode.BandPass)
			{
				_ApplyBandPassFilter(cutoff, one_over_q);
			}
			else if (_filterMode == FilterMode.LowPass)
			{
				ApplyLowPassFilter(cutoff, one_over_q);
			}
			else if (_filterMode == FilterMode.HighPass)
			{
				_ApplyHighPassFilter(cutoff, one_over_q);
			}

			_filterDirty = false;
		}

		internal unsafe void _ApplyReverb(float reverb_gain)
		{
			if (handle == IntPtr.Zero)
			{
				return;
			}

            if (!usingReverb)
            {
                SoundEffect.Device().AttachReverb(handle);
                usingReverb = true;
            }

            // Re-using this float array...
            float* outputMatrix = (float*)dspSettings.pMatrixCoefficients;
            outputMatrix[0] = reverb_gain;
            if (dspSettings.SrcChannelCount == 2)
            {
                outputMatrix[1] = reverb_gain;
            }
            FAudio.FAudioVoice_SetOutputMatrix(
                handle,
                SoundEffect.Device().ReverbVoice,
                dspSettings.SrcChannelCount,
                1,
                dspSettings.pMatrixCoefficients,
                0
            );
		}

		internal void ApplyLowPassFilter(float cutoff, float one_over_q)
		{
			if (handle == IntPtr.Zero)
			{
				return;
			}

			FAudio.FAudioFilterParameters p = new FAudio.FAudioFilterParameters();
			p.Type = FAudio.FAudioFilterType.FAudioLowPassFilter;
			p.Frequency = cutoff;
			p.OneOverQ = one_over_q;
			FAudio.FAudioVoice_SetFilterParameters(
				handle,
				ref p,
				0
			);
		}

		internal void _ApplyHighPassFilter(float cutoff, float one_over_q)
		{
			if (handle == IntPtr.Zero)
			{
				return;
			}

			FAudio.FAudioFilterParameters p = new FAudio.FAudioFilterParameters();
			p.Type = FAudio.FAudioFilterType.FAudioHighPassFilter;
			p.Frequency = cutoff;
			p.OneOverQ = one_over_q;
			FAudio.FAudioVoice_SetFilterParameters(
				handle,
				ref p,
				0
			);
		}

		internal void _ApplyBandPassFilter(float center, float one_over_q)
		{
			if (handle == IntPtr.Zero)
			{
				return;
			}

			FAudio.FAudioFilterParameters p = new FAudio.FAudioFilterParameters();
			p.Type = FAudio.FAudioFilterType.FAudioBandPassFilter;
			p.Frequency = center;
			p.OneOverQ = one_over_q;
			FAudio.FAudioVoice_SetFilterParameters(
				handle,
				ref p,
				0
			);
		}

		private unsafe void SetPanMatrixCoefficients()
		{
			/* Two major things to notice:
			 * 1. The spec assumes any speaker count >= 2 has Front Left/Right.
			 * 2. Stereo panning is WAY more complicated than you think.
			 *    The main thing is that hard panning does NOT eliminate an
			 *    entire channel; the two channels are blended on each side.
			 * Aside from that, XNA is pretty naive about the output matrix.
			 * -flibit
			 */

			float* outputMatrix = (float*)dspSettings.pMatrixCoefficients;
			if (dspSettings.SrcChannelCount == 1)
			{
				if (dspSettings.DstChannelCount == 1)
				{
					outputMatrix[0] = 1.0f;
				}
				else
				{
					outputMatrix[0] = (_pan > 0.0f) ? (1.0f - _pan) : 1.0f;
					outputMatrix[1] = (_pan < 0.0f) ? (1.0f + _pan) : 1.0f;
				}
			}
			else
			{
				if (dspSettings.DstChannelCount == 1)
				{
					outputMatrix[0] = 1.0f;
					outputMatrix[1] = 1.0f;
				}
				else
				{
					if (_pan <= 0.0f)
					{
						// Left speaker blends left/right channels
						outputMatrix[0] = 0.5f * _pan + 1.0f;
						outputMatrix[1] = 0.5f * -_pan;
						// Right speaker gets less of the right channel
						outputMatrix[2] = 0.0f;
						outputMatrix[3] = _pan + 1.0f;
					}
					else
					{
						// Left speaker gets less of the left channel
						outputMatrix[0] = -_pan + 1.0f;
						outputMatrix[1] = 0.0f;
						// Right speaker blends right/left channels
						outputMatrix[2] = 0.5f * _pan;
						outputMatrix[3] = 0.5f * -_pan + 1.0f;
					}
				}
			}
		}
	}
}
#endif
