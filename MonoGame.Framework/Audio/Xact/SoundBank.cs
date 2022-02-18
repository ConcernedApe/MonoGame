// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using MonoGame.Framework.Utilities;

namespace Microsoft.Xna.Framework.Audio
{
    public class CueDefinition
    {
        public string name;
        public List<XactSoundBankSound> sounds = new List<XactSoundBankSound>();
        //public List<float> probabilities;
        public int instanceLimit = 255;

        public LimitBehavior limitBehavior = LimitBehavior.FailToPlay;

        public enum LimitBehavior
        {
            FailToPlay = 0,
            ReplaceOldest = 2,
        }

        public CueDefinition()
        {
        }

        public CueDefinition(string cue_name, SoundEffect sound_effect, int category_id, bool loop = false, bool use_reverb = false)
        {
            this.name = cue_name;
            SetSound(sound_effect, category_id, loop, use_reverb);
        }

        public CueDefinition(string cue_name, SoundEffect[] sound_effects, int category_id, bool loop = false, bool use_reverb = false)
        {
            this.name = cue_name;
            SetSound(sound_effects, category_id, loop, use_reverb);
        }

        // Sets this cue to a play the specified sound effect.
        public virtual void SetSound(SoundEffect sound_effect, int category_id, bool loop = false, bool use_reverb = false)
        {
            SetSound(new SoundEffect[] { sound_effect }, category_id, loop, use_reverb);
        }

        // Sets this cue to a play one of the specified sound effects.
        public virtual void SetSound(SoundEffect[] sound_effects, int category_id, bool loop = false, bool use_reverb = false)
        {
            // Remove dependencies on any of the sounds that were previously assigned to this cue.

            foreach (var sound in sounds)
            {
                if (sound.soundClips == null)
                {
                    sound.soundBank.GetSoundEffect(sound.waveBankIndex, sound.trackIndex).RemoveDependency();
                }
                else if (sound.soundClips != null)
                {
                    foreach (var clip in sound.soundClips)
                    {
                        foreach (var clip_event in clip.clipEvents)
                        {
                            if (clip_event is PlayWaveEvent)
                            {
                                var play_event = clip_event as PlayWaveEvent;

                                foreach (var variant in play_event.GetVariants())
                                {
                                    variant.GetSoundEffect().RemoveDependency();
                                }
                            }
                        }
                    }
                }
            }

            sounds.Clear();
            sounds.Add(new XactSoundBankSound(sound_effects, category_id, loop, use_reverb));

            foreach (var sound_effect in sound_effects)
            {
                sound_effect.AddDependency();
            }

            foreach (var sound in sounds)
            {
                if (sound.soundClips != null)
                {
                    foreach (var clip in sound.soundClips)
                    {
                        foreach (var clip_event in clip.clipEvents)
                        {
                            if (clip_event is PlayWaveEvent)
                            {
                                var play_event = clip_event as PlayWaveEvent;

                                // TODO: Set pitch, volume variations.
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>Represents a collection of Cues.</summary>
    public class SoundBank : IDisposable
    {
        readonly AudioEngine _audioengine;
        readonly string[] _waveBankNames;
        readonly WaveBank[] _waveBanks;

        readonly float [] defaultProbability = new float [1] { 1.0f };

        readonly Dictionary<string, CueDefinition> _cues = new Dictionary<string, CueDefinition>();

        /// <summary>
        /// Is true if the SoundBank has any live Cues in use.
        /// </summary>
        public bool IsInUse { get; private set; }

        /// <param name="audioEngine">AudioEngine that will be associated with this sound bank.</param>
        /// <param name="fileName">Path to a .xsb SoundBank file.</param>
        public SoundBank(AudioEngine audioEngine, string fileName)
        {
            if (audioEngine == null)
                throw new ArgumentNullException("audioEngine");
            if (string.IsNullOrEmpty(fileName))
                throw new ArgumentNullException("fileName");

            _audioengine = audioEngine;

            using (var stream = AudioEngine.OpenStream(fileName, true))
            using (var reader = new BinaryReader(stream))
            {
                // Thanks to Liandril for "xactxtract" for some of the offsets.

                uint magic = reader.ReadUInt32();
                if (magic != 0x4B424453) //"SDBK"
                    throw new Exception ("Bad soundbank format");

                reader.ReadUInt16(); // toolVersion

                uint formatVersion = reader.ReadUInt16();
                if (formatVersion != 43)
                    Debug.WriteLine("Warning: SoundBank format {0} not supported.", formatVersion);

                reader.ReadUInt16(); // crc, TODO: Verify crc (FCS16)

                reader.ReadUInt32(); // lastModifiedLow
                reader.ReadUInt32(); // lastModifiedHigh
                reader.ReadByte(); // platform ???

                uint numSimpleCues = reader.ReadUInt16();
                uint numComplexCues = reader.ReadUInt16();
                reader.ReadUInt16(); //unkn
                reader.ReadUInt16(); // numTotalCues
                uint numWaveBanks = reader.ReadByte();
                reader.ReadUInt16(); // numSounds
                uint cueNameTableLen = reader.ReadUInt16();
                reader.ReadUInt16(); //unkn

                uint simpleCuesOffset = reader.ReadUInt32();
                uint complexCuesOffset = reader.ReadUInt32(); //unkn
                uint cueNamesOffset = reader.ReadUInt32();
                reader.ReadUInt32(); //unkn
                reader.ReadUInt32(); // variationTablesOffset
                reader.ReadUInt32(); //unkn
                uint waveBankNameTableOffset = reader.ReadUInt32();
                reader.ReadUInt32(); // cueNameHashTableOffset
                reader.ReadUInt32(); // cueNameHashValsOffset
                reader.ReadUInt32(); // soundsOffset
                    
                //name = System.Text.Encoding.UTF8.GetString(soundbankreader.ReadBytes(64),0,64).Replace("\0","");

                //parse wave bank name table
                stream.Seek(waveBankNameTableOffset, SeekOrigin.Begin);
                _waveBanks = new WaveBank[numWaveBanks];
                _waveBankNames = new string[numWaveBanks];

                for (int i = 0; i < numWaveBanks; i++)
                {
                    _waveBankNames[i] = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(64), 0, 64).Replace("\0", "");
                }
                    
                //parse cue name table
                stream.Seek(cueNamesOffset, SeekOrigin.Begin);
                string[] cue_names = System.Text.Encoding.UTF8.GetString(reader.ReadBytes((int)cueNameTableLen), 0, (int)cueNameTableLen).Split('\0');

                // Simple cues
                if (numSimpleCues > 0)
                {
                    stream.Seek(simpleCuesOffset, SeekOrigin.Begin);
                    for (int i = 0; i < numSimpleCues; i++)
                    {
                        CueDefinition cue = new CueDefinition();
                        reader.ReadByte(); // flags
                        uint soundOffset = reader.ReadUInt32();

                        var oldPosition = stream.Position;
                        stream.Seek(soundOffset, SeekOrigin.Begin);
                        XactSoundBankSound sound = new XactSoundBankSound(audioEngine, this, reader);
                        stream.Seek(oldPosition, SeekOrigin.Begin);

                        cue.sounds.Clear();
                        cue.sounds.Add(sound);
                        //cue.probabilities = defaultProbability;

                        cue.name = cue_names[i];
                        _cues[cue_names[i]] = cue;
                    }
                }
                    
                // Complex cues
                if (numComplexCues > 0)
                {
                    stream.Seek(complexCuesOffset, SeekOrigin.Begin);
                    for (int i = 0; i < numComplexCues; i++)
                    {
                        CueDefinition cue = new CueDefinition();

                        byte flags = reader.ReadByte();
                        if (((flags >> 2) & 1) != 0)
                        {
                            uint soundOffset = reader.ReadUInt32();
                            reader.ReadUInt32(); //unkn

                            var oldPosition = stream.Position;
                            stream.Seek(soundOffset, SeekOrigin.Begin);
                            XactSoundBankSound sound = new XactSoundBankSound(audioEngine, this, reader);
                            stream.Seek(oldPosition, SeekOrigin.Begin);

                            cue.sounds.Clear();
                            cue.sounds.Add(sound);
                            //cue.probabilities = defaultProbability;
                        }
                        else
                        {
                            uint variationTableOffset = reader.ReadUInt32();
                            reader.ReadUInt32(); // transitionTableOffset

                            //parse variation table
                            long savepos = stream.Position;
                            stream.Seek(variationTableOffset, SeekOrigin.Begin);

                            uint numEntries = reader.ReadUInt16();
                            uint variationflags = reader.ReadUInt16();
                            reader.ReadByte();
                            reader.ReadUInt16();
                            reader.ReadByte();

                            List<XactSoundBankSound> cue_sounds = new List<XactSoundBankSound>();
                            float[] probs = new float[numEntries];

                            uint tableType = (variationflags >> 3) & 0x7;
                            for (int j = 0; j < numEntries; j++)
                            {
                                switch (tableType)
                                {
                                    case 0: //Wave
                                        {
                                            int trackIndex = reader.ReadUInt16();
                                            int waveBankIndex = reader.ReadByte();
                                            reader.ReadByte(); // weightMin
                                            reader.ReadByte(); // weightMax

                                            cue_sounds.Add(new XactSoundBankSound(this, waveBankIndex, trackIndex));
                                            break;
                                        }
                                    case 1:
                                        {
                                            uint soundOffset = reader.ReadUInt32();
                                            reader.ReadByte(); // weightMin
                                            reader.ReadByte(); // weightMax

                                            var oldPosition = stream.Position;
                                            stream.Seek(soundOffset, SeekOrigin.Begin);
                                            cue_sounds.Add(new XactSoundBankSound(audioEngine, this, reader));
                                            stream.Seek(oldPosition, SeekOrigin.Begin);
                                            break;
                                        }
                                    case 3:
                                        {
                                            uint soundOffset = reader.ReadUInt32();
                                            reader.ReadSingle(); // weightMin
                                            reader.ReadSingle(); // weightMax
                                            reader.ReadUInt32(); // flags

                                            var oldPosition = stream.Position;
                                            stream.Seek(soundOffset, SeekOrigin.Begin);
                                            cue_sounds.Add(new XactSoundBankSound(audioEngine, this, reader));
                                            stream.Seek(oldPosition, SeekOrigin.Begin);
                                            break;
                                        }
                                    case 4: //CompactWave
                                        {
                                            int trackIndex = reader.ReadUInt16();
                                            int waveBankIndex = reader.ReadByte();
                                            cue_sounds.Add(new XactSoundBankSound(this, waveBankIndex, trackIndex));
                                            break;
                                        }
                                    default:
                                        throw new NotSupportedException();
                                }
                            }

                            stream.Seek(savepos, SeekOrigin.Begin);

                            cue.sounds = cue_sounds;
                            //cue.probabilities = probs;
                        }

                        // Instance limiting
                        cue.instanceLimit = (int)reader.ReadByte(); //instanceLimit
                        reader.ReadUInt16(); //fadeInSec, divide by 1000.0f
                        reader.ReadUInt16(); //fadeOutSec, divide by 1000.0f
                        cue.limitBehavior = (CueDefinition.LimitBehavior)(reader.ReadByte() >> 3); //instanceFlags

                        // Store the cue.
                        cue.name = cue_names[numSimpleCues + i];
                        _cues[cue_names[numSimpleCues + i]] = cue;
                    }
                }
            }
        }

        public SoundEffect GetSoundEffect(int waveBankIndex, int trackIndex)
        {
            var waveBank = _waveBanks[waveBankIndex];

            // If the wave bank has not been resolved then do so now.
            if (waveBank == null)
            {
                var name = _waveBankNames[waveBankIndex];
                if (!_audioengine.Wavebanks.TryGetValue(name, out waveBank))
                    throw new Exception("The wave bank '" + name + "' was not found!");
                _waveBanks[waveBankIndex] = waveBank;
            }

            return waveBank.GetSoundEffect(trackIndex);
        }

        public SoundEffectInstance GetSoundEffectInstance(int waveBankIndex, int trackIndex, out bool streaming)
        {
            var waveBank = _waveBanks[waveBankIndex];

            // If the wave bank has not been resolved then do so now.
            if (waveBank == null)
            {
                var name = _waveBankNames[waveBankIndex];
                if (!_audioengine.Wavebanks.TryGetValue(name, out waveBank))
                    throw new Exception("The wave bank '" + name + "' was not found!");
                _waveBanks[waveBankIndex] = waveBank;                
            }

            return waveBank.GetSoundEffectInstance(trackIndex, out streaming);
        }

        public CueDefinition GetCueDefinition(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException("name");

            CueDefinition cue_definition;

            if (!_cues.TryGetValue(name, out cue_definition))
            {
                throw new ArgumentException();
            }

            return _cues[name];
        }

        /// <summary>
        /// Returns a pooled Cue object.
        /// </summary>
        /// <param name="name">Friendly name of the cue to get.</param>
        /// <returns>a unique Cue object from a pool.</returns>
        /// <remarks>
        /// <para>Cue instances are unique, even when sharing the same name. This allows multiple instances to simultaneously play.</para>
        /// </remarks>
        public Cue GetCue(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException("name");

            CueDefinition cue_definition;

            if (!_cues.TryGetValue(name, out cue_definition))
            {
                throw new ArgumentException();
            }

            IsInUse = true;

            var cue = new Cue(_audioengine, cue_definition);
            cue.Prepare();
            return cue;
        }
        
        /// <summary>
        /// Plays a cue.
        /// </summary>
        /// <param name="name">Name of the cue to play.</param>
        public void PlayCue(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException("name");

            CueDefinition cue_definition;

            if (!_cues.TryGetValue(name, out cue_definition))
            {
                throw new ArgumentException();
            }

            IsInUse = true;
            var cue = new Cue(_audioengine, cue_definition);
            cue.Prepare();
            cue.Play();
        }

        /// <summary>
        /// Plays a cue with static 3D positional information.
        /// </summary>
        /// <remarks>
        /// Commonly used for short lived effects.  To dynamically change the 3D 
        /// positional information on a cue over time use <see cref="GetCue"/> and <see cref="Cue.Apply3D"/>.</remarks>
        /// <param name="name">The name of the cue to play.</param>
        /// <param name="listener">The listener state.</param>
        /// <param name="emitter">The cue emitter state.</param>
        public void PlayCue(string name, AudioListener listener, AudioEmitter emitter)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException("name");

            CueDefinition cue_definition;

            if (!_cues.TryGetValue(name, out cue_definition))
            {
                throw new ArgumentException();
            }

            IsInUse = true;

            var cue = new Cue(_audioengine, cue_definition);
            cue.Prepare();
            cue.Apply3D(listener, emitter);
            cue.Play();
        }

        /// <summary>
        /// This event is triggered when the SoundBank is disposed.
        /// </summary>
        public event EventHandler<EventArgs> Disposing;

        /// <summary>
        /// Is true if the SoundBank has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Disposes the SoundBank.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~SoundBank()
        {
            Dispose(false);
        }

        private void Dispose(bool disposing)
        {
            if (IsDisposed)
                return;

            IsDisposed = true;

            if (disposing)
            {
                IsInUse = false;
                EventHelpers.Raise(this, Disposing, EventArgs.Empty);
            }
        }

        public void AddCue(CueDefinition cue_definition)
        {
            _cues[cue_definition.name] = cue_definition;
        }
    }
}

