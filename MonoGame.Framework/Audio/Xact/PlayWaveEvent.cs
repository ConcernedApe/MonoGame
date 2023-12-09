// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;
using System.Collections.Generic;
using System.IO;

namespace Microsoft.Xna.Framework.Audio
{
    public enum VariationType
    {
        Ordered,
        OrderedFromRandom,
        Random,
        RandomNoImmediateRepeats,
        Shuffle
    };

    public class PlayWaveEvent : ClipEvent
    {
        private SoundBank _soundBank;

        public VariationType variationType;

        private uint _loopCount;

        private bool _newWaveOnLoop;

        protected List<PlayWaveVariant> _variants;
        
        protected int _totalWeights;

        protected float _trackVolume;
        protected float _trackPitch;
        protected float _trackFilterFrequency;
        protected float _trackFilterQFactor;

        protected float _clipReverbMix;

        public readonly Vector4? randomFilterRange;
        public readonly Vector2? randomVolumeRange;
        public readonly Vector2? randomPitchRange;

        public bool Loop
        {
            get
            {
                return _loopCount == 255;
            }
            set
            {
                if (value)
                {
                    _loopCount = 255;
                }
                else
                {
                    _loopCount = 0;
                }
            }
        }

       private bool _streaming;

        public PlayWaveEvent(XactClip clip, List<PlayWaveVariant> variants, float time_stamp = 0, float random_offset = 0, bool loop = false) : base(clip, time_stamp, random_offset)
        {
            _variants = variants;

            if (loop)
            {
                _loopCount = 255;
            }

            _totalWeights = 0;
            for (int i = 0; i < _variants.Count; i++)
            {
                var variant = variants[i];
                _totalWeights += variant.weight;
            }
        }

        public PlayWaveEvent(   XactClip clip, float timeStamp, float randomOffset, SoundBank soundBank,
                                int[] waveBanks, int[] tracks, byte[] weights, int totalWeights,
                                VariationType variation, Vector2? volumeVar, Vector2? pitchVar, Vector4? filterVar,
                                uint loopCount, bool newWaveOnLoop)
            : base(clip, timeStamp, randomOffset)
        {
            _soundBank = soundBank;

            _variants = new List<PlayWaveVariant>();
            _totalWeights = 0;

            for (int i = 0; i < tracks.Length; i++)
            {
                PlayWaveVariant variant = new PlayWaveVariant();
                variant.soundBank = _soundBank;
                variant.waveBank = waveBanks[i];
                variant.track = tracks[i];

                if (weights != null)
                {
                    variant.weight = weights[i];
                }
                else
                {
                    variant.weight = 1;
                }

                _totalWeights += variant.weight;

                _variants.Add(variant);
            }

            randomVolumeRange = volumeVar;
            randomPitchRange = pitchVar;
            randomFilterRange = filterVar;

            _trackVolume = 1.0f;
            _trackPitch = 0;
            _trackFilterFrequency = 0;
            _trackFilterQFactor = 0;

            if (_clip.UseReverb)
            {
                _clipReverbMix = 1.0F;
            }
            else
            {
                _clipReverbMix = 0;
            }

            variationType = variation;
            _loopCount = loopCount;
            _newWaveOnLoop = newWaveOnLoop;
        }

        public override void Fire(Cue cue)
        {
            Play(true, cue);
        }

        private void Play(bool pickNewWav, Cue cue)
        {
            var trackCount = _variants.Count;

            int variant_index = cue.VariantIndex;

            if (variant_index < 0)
            {
                variant_index = 0;
            }

            // Do we need to pick a new wav to play first?
            if (pickNewWav)
            {
                switch (variationType)
                {
                    case VariationType.Ordered:
                        variant_index = (variant_index + 1) % trackCount;
                        break;

                    case VariationType.OrderedFromRandom:
                        variant_index = (variant_index + 1) % trackCount;
                        break;

                    case VariationType.Random:
                        {
                            var sum = XactHelpers.Random.Next(_totalWeights + 1); // 7/30/2021 ARTHUR: This is an integer based Random, so we need to +1 to make it inclusive.
                            for (var i=0; i < trackCount; i++)
                            {
                                sum -= _variants[i].weight;
                                if (sum <= 0)
                                {
                                    variant_index = i;
                                    break;
                                }
                            }
                        }
                        break;

                    case VariationType.RandomNoImmediateRepeats:
                    {
                        var last = variant_index;
                        var sum = XactHelpers.Random.Next(_totalWeights + 1);
                        for (var i=0; i < trackCount; i++)
                        {
                            sum -= _variants[i].weight;
                            if (sum <= 0)
                            {
                                variant_index = i;
                                break;
                            }
                        }

                        if (variant_index == last)
                            variant_index = (variant_index + 1) % trackCount;
                        break;
                    }

                    case VariationType.Shuffle:
                        // TODO: Need some sort of deck implementation.
                        variant_index = XactHelpers.Random.Next() % trackCount;
                        break;
                };
            }

            SoundEffectInstance new_wave = _variants[variant_index].GetSoundEffectInstance();

            if (new_wave == null)
            {
                // We couldn't create a sound effect instance, most likely
                // because we've reached the sound pool limits.
                return;
            }

            _trackVolume = _clip.DefaultVolume;

            // Do all the randoms before we play.
            if (randomVolumeRange.HasValue)
                _trackVolume = randomVolumeRange.Value.X + ((float)XactHelpers.Random.NextDouble() * randomVolumeRange.Value.Y);
            if (randomPitchRange.HasValue)
                _trackPitch = randomPitchRange.Value.X + ((float)XactHelpers.Random.NextDouble() * randomPitchRange.Value.Y);
            if (_clip.FilterEnabled)
            {
                if (randomFilterRange.HasValue)
                {
                    _trackFilterFrequency = randomFilterRange.Value.X + ((float)XactHelpers.Random.NextDouble() * randomFilterRange.Value.Y);
                    _trackFilterQFactor = randomFilterRange.Value.Z + ((float)XactHelpers.Random.NextDouble() * randomFilterRange.Value.W);
                }
                else
                {
                    _trackFilterFrequency = _clip.FilterFrequency;
                    _trackFilterQFactor = _clip.FilterQ;                
                }
            }
 
            new_wave.LoopCount = _loopCount;
            new_wave.Volume = _trackVolume;
            new_wave.Pitch = _trackPitch;

            if (_clip.UseReverb)
            {
                new_wave.PlatformSetReverbMix(_clipReverbMix);
            }

            if (_clip.FilterEnabled)
            {
                new_wave.PlatformSetFilter(_clip.FilterMode, _trackFilterQFactor, _trackFilterFrequency);
            }

            cue.Volume = _trackVolume;
            cue.Pitch = _trackPitch;
            cue.PlaySoundInstance(new_wave, variant_index);
        }

        public List<PlayWaveVariant> GetVariants()
        {
            return _variants;
        }

        public void SetVariants(List<PlayWaveVariant> variants)
        {
            _variants = variants;
            _totalWeights = 0;

            foreach (var variant in _variants)
            {
                _totalWeights += variant.weight;
            }
        }
    }

    public class PlayWaveVariant
    {
        public SoundEffect overrideSoundEffect = null;
        public SoundBank soundBank = null;
        public int waveBank = -1;
        public int track = -1;

        public byte weight = 1;

        public SoundEffect GetSoundEffect()
        {
            if (overrideSoundEffect != null)
            {
                return overrideSoundEffect;
            }

            if (soundBank != null)
            {
                return soundBank.GetSoundEffect(waveBank, track);
            }

            return null;
        }

        public SoundEffectInstance GetSoundEffectInstance()
        {
            if (overrideSoundEffect != null)
            {
                return overrideSoundEffect.GetPooledInstance(true);
            }

            if (soundBank != null)
            {
                bool streaming;
                return soundBank.GetSoundEffectInstance(waveBank, track, out streaming);
            }

            return null;
        }
    }
}

