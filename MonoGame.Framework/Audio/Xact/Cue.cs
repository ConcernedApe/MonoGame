// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;
using System.Collections.Generic;

namespace Microsoft.Xna.Framework.Audio
{
    /// <summary>Manages the playback of a sound or set of sounds.</summary>
    /// <remarks>
    /// <para>Cues are comprised of one or more sounds.</para>
    /// <para>Cues also define specific properties such as pitch or volume.</para>
    /// <para>Cues are referenced through SoundBank objects.</para>
    /// </remarks>
    public class Cue : IDisposable
    {
        protected AudioEngine _engine;
        protected string _name;

        protected List<XactSoundBankSound> _xactSounds;
//        protected float[] _probabilities;
        protected int _instanceLimit = 255;
        protected int _limitBehavior = 0;

        private RpcVariable[] _variables;

        protected bool _applied3D;
        protected bool _played;

        protected XactSoundBankSound _currentXactSound;
        protected int _variantIndex = -1;

        protected SoundEffectInstance _soundEffect;
        protected AudioCategory _playingCategory;

        // Properties specific to this Cue, set by the Pitch and Volume getters/setters.
        protected float _cueVolume = 1;
        protected float _cuePitch = 0;

        // Set by RPC curves.
        protected float _rpcVolume = 1;
        protected float _rpcPitch = 0;
        protected float _rpcReverbMix = 1.0F;
        protected float? _rpcFilterFrequency;
        protected float? _rpcFilterQFactor;

        // Set to 0 when play begins. Less than 0 means stopped.
        protected float _time = -1;

        protected bool? _pitchControlledByRPC;

        public bool IsPitchBeingControlledByRPC
        {
            get
            {
                if (!_pitchControlledByRPC.HasValue)
                {
                    XactSoundBankSound sound = _currentXactSound;
                    if (sound is null && _xactSounds.Count > 0)
                        sound = _xactSounds[0]; // if there's no sound playing, take the first one on the assumption that either (a) there's only one, or (b) they're all configured similarly
                    if (sound is null)
                        return false;

                    var curves = sound.rpcCurves;
                    if (curves.Length > 0)
                    {
                        for (var i = 0; i < curves.Length; i++)
                        {
                            if (_engine.RpcCurves[curves[i]].Parameter == RpcParameter.Pitch)
                            {
                                _pitchControlledByRPC = true;
                                break;
                            }
                        }
                    }

                    if (!_pitchControlledByRPC.HasValue)
                        _pitchControlledByRPC = false;
                }

                return _pitchControlledByRPC.Value;
            }
        }

        /// <summary>Indicates whether or not the cue is currently paused.</summary>
        /// <remarks>IsPlaying and IsPaused both return true if a cue is paused while playing.</remarks>
        public bool IsPaused
        {
            get 
            {
                if (_soundEffect != null)
                    return _soundEffect.State == SoundState.Paused;

                return false;
            }
        }

        public float Pitch
        {
            get
            {
                return _cuePitch;
            }
            set
            {
                if (_cuePitch != value)
                {
                    _cuePitch = value;
                    _UpdateSoundParameters();
                }
            }
        }

        public float Volume
        {
            get
            {
                return _cueVolume;
            }
            set
            {
                if (_cueVolume != value)
                {
                    _cueVolume = value;
                    _UpdateSoundParameters();
                }
            }
        }

        /// <summary>Indicates whether or not the cue is currently playing.</summary>
        /// <remarks>IsPlaying and IsPaused both return true if a cue is paused while playing.</remarks>
        public bool IsPlaying
        {
            get 
            {
                return _time >= 0;
            }
        }

        /// <summary>Indicates whether or not the cue is currently stopped.</summary>
        public bool IsStopped
        {
            get 
            {
                if (_soundEffect != null)
                    return _soundEffect.State == SoundState.Stopped;

                return !IsDisposed && !IsPrepared;
            }
        }

        public bool IsStopping
        {
            get
            {
                // TODO: Implement me!
                return false;
            }
        }

        public bool IsPreparing 
        {
            get { return false; }
        }

        public bool IsPrepared { get; internal set; }

        public bool IsCreated { get; internal set; }

        /// <summary>Gets the friendly name of the cue.</summary>
        /// <remarks>The friendly name is a value set from the designer.</remarks>
        public string Name
        {
            get { return _name; }
        }

        protected CueDefinition _cueDefinition;

        internal Cue(AudioEngine engine, CueDefinition cue)
        {
            _cueDefinition = cue;
            _engine = engine;
            _name = cue.name;
            _xactSounds = cue.sounds;
            //_probabilities = cue.probabilities;
            _instanceLimit = cue.instanceLimit;
            _limitBehavior = (int)cue.limitBehavior;
            _variables = engine.CreateCueVariables();
        }

        internal Cue(AudioEngine engine, string cuename, XactSoundBankSound sound)
        {
            _engine = engine;
            _name = cuename;
            _currentXactSound = sound;
            _variables = engine.CreateCueVariables();
        }
        
        internal Cue(AudioEngine engine, string cuename, List<XactSoundBankSound> sounds, float[] probs)
        {
            _engine = engine;
            _name = cuename;
            _xactSounds = sounds;
            //_probabilities = probs;
            _variables = engine.CreateCueVariables();
        }

        internal void Prepare()
        {
            IsDisposed = false;
            IsCreated = false;
            IsPrepared = true;
            _currentXactSound = null;
        }

        /// <summary>Pauses playback.</summary>
        public void Pause()
        {
            lock (_engine.UpdateLock)
            {
                if (_soundEffect != null)
                {
                    _soundEffect.Pause();
                }
            }
        }

        /// <summary>Requests playback of a prepared or preparing Cue.</summary>
        /// <remarks>Calling Play when the Cue already is playing can result in an InvalidOperationException.</remarks>
        public void Play()
        {
            lock (_engine.UpdateLock)
            {
                // If this sound has an instance limit, perform the limiting.
                if (_instanceLimit < 255 && _instanceLimit > 0)
                {
                    Cue oldest_cue = null;

                    int current_count = 0;

                    foreach (var cue in _engine.ActiveCues)
                    {
                        if (cue.Name == Name)
                        {
                            if (oldest_cue == null)
                            {
                                oldest_cue = cue;
                            }

                            current_count++;

                            if (current_count >= _instanceLimit)
                            {
                                break;
                            }
                        }
                    }

                    if (current_count >= _instanceLimit)
                    {
                        if (_limitBehavior == 0) // Fail
                        {
                            return;
                        }
                        else if (_limitBehavior == 1) // Queue
                        {
                            // Not implemented -- just kill the oldest instance for now.
                            oldest_cue.Stop(AudioStopOptions.Immediate);
                        }
                        else if (_limitBehavior == 2) // Replace oldest
                        {
                            oldest_cue.Stop(AudioStopOptions.Immediate);
                        }
                        else if (_limitBehavior == 3) // Replace quietest
                        {
                            // Not implemented -- just kill the oldest instance for now.
                            oldest_cue.Stop(AudioStopOptions.Immediate);
                        }
                        else if (_limitBehavior == 4) // Replace lowest priority
                        {
                            // Not implemented -- just kill the oldest instance for now.
                            oldest_cue.Stop(AudioStopOptions.Immediate);
                        }
                    }
                }

                if (!_engine.ActiveCues.Contains(this))
                {
                    _engine.ActiveCues.Add(this);
                }

                if (_xactSounds != null)
                {
                    //TODO: Probabilities
                    var index = XactHelpers.Random.Next(_xactSounds.Count);
                    _currentXactSound = _xactSounds[index];

                    if (_currentXactSound == null)
                    {
                        return;
                    }
                }

                var volume = UpdateRpcCurves();

                var category = _engine.Categories[_currentXactSound.categoryID];

                var instance_count = category.GetPlayingInstanceCount();

                if (instance_count >= category.maxInstances)
                {
                    var previous_cue = category.GetOldestInstance();
                    
                    if (previous_cue != null)
                    {
                        //prevSound.SetFade(0.0f, category.fadeOut);
                        previous_cue.Stop(AudioStopOptions.Immediate);
                        //SetFade(category.fadeIn, 0.0f);
                    }
                }

                var wave = _currentXactSound.GetSimpleSoundInstance();

                _time = 0;

                if (wave != null)
                {
                    // Simple sound
                    PlaySoundInstance(wave);
                }
                else
                {
                    // Complex sound
                    if (_currentXactSound.soundClips != null)
                    {
                        foreach (var clip in _currentXactSound.soundClips)
                        {
                            clip.Update(this, -1, _time);
                        }
                    }
                }
            }

            if (_cueDefinition != null)
            {
                _cueDefinition.OnModified += _OnCueDefinitionModified;
            }

            _played = true;
            IsPrepared = false;
        }

        internal void PlaySoundInstance(SoundEffectInstance sound_instance, int variant_index = -1)
        {
            if (_soundEffect != null)
            {
                _soundEffect.Stop(true);

                // This flag must be unset, or else the sound effect will not be pooled correctly.
                _soundEffect._isXAct = false;
                if (!_soundEffect._isPooled)
                    _soundEffect.Dispose();
                _soundEffect = null;
            }

            _soundEffect = sound_instance;

            if (_soundEffect != null)
            {
                _soundEffect.Play();

                _playingCategory = _engine.Categories[_currentXactSound.categoryID];
                _playingCategory.AddSound(this);

                _UpdateSoundParameters();
            }

            _variantIndex = variant_index;
        }

        public int VariantIndex
        {
            get
            {
                return _variantIndex;
            }
        }

        /// <summary>Resumes playback of a paused Cue.</summary>
        public void Resume()
        {
            lock (_engine.UpdateLock)
            {
                if (_soundEffect != null)
                    _soundEffect.Resume();
            }
        }

        /// <summary>Stops playback of a Cue.</summary>
        /// <param name="options">Specifies if the sound should play any pending release phases or transitions before stopping.</param>
        public void Stop(AudioStopOptions options)
        {
            lock (_engine.UpdateLock)
            {
                _time = -1;

                _engine.ActiveCues.Remove(this);

                if (_playingCategory != null)
                {
                    _playingCategory.RemoveSound(this);
                    _playingCategory = null;
                }

                if (_soundEffect != null)
                {
                    _soundEffect.Stop(options == AudioStopOptions.Immediate);

                    // This flag must be unset, or else the sound effect will not be pooled correctly.
                    _soundEffect._isXAct = false;
                    if (!_soundEffect._isPooled)
                        _soundEffect.Dispose();
                    _soundEffect = null;
                }
            }

            if (_cueDefinition != null)
            {
                _cueDefinition.OnModified -= _OnCueDefinitionModified;
            }

            IsPrepared = false;
        }

        protected void _OnCueDefinitionModified()
        {
            if (IsPaused)
            {
                _soundEffect.Stop();
                // Need to call dispose?

                _soundEffect = null;
                Play();

                if (_soundEffect != null)
                {
                    _soundEffect.Pause();
                }
            }
            else if (IsPlaying)
            {
                _soundEffect.Stop();
                // Need to call dispose?

                _soundEffect = null;
                Play();
            }
            else if (_soundEffect != null)
            {
                _soundEffect.Stop();
                // Need to call dispose?

                _soundEffect = null;
            }
        }

        private int FindVariable(string name)
        {
            // Do a simple linear search... which is fast
            // for as little variables as most cues have.
            for (var i = 0; i < _variables.Length; i++)
            {
                if (_variables[i].Name == name)
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Sets the value of a cue-instance variable based on its friendly name.
        /// </summary>
        /// <param name="name">Friendly name of the variable to set.</param>
        /// <param name="value">Value to assign to the variable.</param>
        /// <remarks>The friendly name is a value set from the designer.</remarks>
        public void SetVariable(string name, float value)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException("name");

            var i = FindVariable(name);
            if (i == -1 || !_variables[i].IsPublic)
                throw new IndexOutOfRangeException("The specified variable index is invalid.");

            _variables[i].SetValue(value);
        }

        /// <summary>Gets a cue-instance variable value based on its friendly name.</summary>
        /// <param name="name">Friendly name of the variable.</param>
        /// <returns>Value of the variable.</returns>
        /// <remarks>
        /// <para>Cue-instance variables are useful when multiple instantiations of a single cue (and its associated sounds) are required (for example, a "car" cue where there may be more than one car at any given time). While a global variable allows multiple audio elements to be controlled in unison, a cue instance variable grants discrete control of each instance of a cue, even for each copy of the same cue.</para>
        /// <para>The friendly name is a value set from the designer.</para>
        /// </remarks>
        public float GetVariable(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException("name");

            var i = FindVariable(name);
            if (i == -1 || !_variables[i].IsPublic)
                throw new IndexOutOfRangeException("The specified variable index is invalid.");

            return _variables[i].Value;
        }

        /// <summary>Updates the simulated 3D Audio settings calculated between an AudioEmitter and AudioListener.</summary>
        /// <param name="listener">The listener to calculate.</param>
        /// <param name="emitter">The emitter to calculate.</param>
        /// <remarks>
        /// <para>This must be called before Play().</para>
        /// <para>Calling this method automatically converts the sound to monoaural and sets the speaker mix for any sound played by this cue to a value calculated with the listener's and emitter's positions. Any stereo information in the sound will be discarded.</para>
        /// </remarks>
        public void Apply3D(AudioListener listener, AudioEmitter emitter) 
        {
            if (listener == null)
                throw new ArgumentNullException("listener");
            if (emitter == null)
                throw new ArgumentNullException("emitter");

            if (_played && !_applied3D)
                throw new InvalidOperationException("You must call Apply3D on a Cue before calling Play to be able to call Apply3D after calling Play.");

            var direction = listener.Position - emitter.Position;

            lock (_engine.UpdateLock)
            {
                // Set the distance for falloff.
                var distance = direction.Length();
                var i = FindVariable("Distance");
                _variables[i].SetValue(distance);

                // Calculate the orientation.
                if (distance > 0.0f)
                    direction /= distance;
                var right = Vector3.Cross(listener.Up, listener.Forward);
                var slope = Vector3.Dot(direction, listener.Forward);
                var angle = MathHelper.ToDegrees((float)Math.Acos(slope));
                var j = FindVariable("OrientationAngle");
                _variables[j].SetValue(angle);
                if (_currentXactSound != null)
                {
                    //_curSound.SetCuePan(Vector3.Dot(direction, right));
                }

                // Calculate doppler effect.
                var relativeVelocity = emitter.Velocity - listener.Velocity;
                relativeVelocity *= emitter.DopplerScale;
            }

            _applied3D = true;
        }

        public List<PlayWaveEvent> GetPlayWaveEvents()
        {
            List<PlayWaveEvent> events = null;

            if (_xactSounds == null)
            {
                if (_currentXactSound.complexSound)
                {
                    foreach (var clip in _currentXactSound.soundClips)
                    {
                        foreach (var clip_event in clip.clipEvents)
                        {
                            if (clip_event is PlayWaveEvent)
                            {
                                if (events == null)
                                {
                                    events = new List<PlayWaveEvent>();
                                }

                                events.Add(clip_event as PlayWaveEvent);
                            }
                        }
                    }
                }
            }
            else
            {
                foreach (var xact_sound in _xactSounds)
                {
                    if (xact_sound.complexSound)
                    {
                        foreach (var clip in xact_sound.soundClips)
                        {
                            foreach (var clip_event in clip.clipEvents)
                            {
                                if (clip_event is PlayWaveEvent)
                                {
                                    if (events == null)
                                    {
                                        events = new List<PlayWaveEvent>();
                                    }

                                    events.Add(clip_event as PlayWaveEvent);
                                }
                            }
                        }
                    }
                }
            }

            return events;
        }

        internal void Update(float dt)
        {
            if (_currentXactSound == null)
            {
                return;
            }

            if (_time < 0)
            {
                return;
            }

            if (_soundEffect == null || _soundEffect.State == SoundState.Playing)
            {
                float old_time = _time;
                _time += dt;

                if (_currentXactSound.soundClips != null)
                {
                    foreach (var clip in _currentXactSound.soundClips)
                    {
                        clip.Update(this, old_time, _time);
                    }
                }
            }

            UpdateRpcCurves();

            if (_soundEffect != null && _soundEffect.State == SoundState.Stopped)
            {
                Stop(AudioStopOptions.Immediate);
            }
        }

        private float UpdateRpcCurves()
        {
            var volume = 1.0f;

            // Evaluate the runtime parameter controls.
            var rpcCurves = _currentXactSound.rpcCurves;
            if (rpcCurves.Length > 0)
            {
                var pitch = 0.0f;
                var reverbMix = 1.0f;
                float? filterFrequency = null;
                float? filterQFactor = null;

                for (var i = 0; i < rpcCurves.Length; i++)
                {
                    var rpcCurve = _engine.RpcCurves[rpcCurves[i]];

                    // Some curves are driven by global variables and others by cue instance variables.
                    float value;
                    if (rpcCurve.IsGlobal)
                        value = rpcCurve.Evaluate(_engine.GetGlobalVariable(rpcCurve.Variable));
                    else
                        value = rpcCurve.Evaluate(_variables[rpcCurve.Variable].Value);

                    // Process the final curve value based on the parameter type it is.
                    switch (rpcCurve.Parameter)
                    {
                        case RpcParameter.Volume:
                            volume *= XactHelpers.ParseVolumeFromDecibels(value / 100.0f);
                            break;

                        case RpcParameter.Pitch:
                            pitch += value / 1200.0f; // 7/26/2021 ARTHUR: Changed this from 1000 to 1200. XAct pitch actually ranges from -1200 to 1200, with 1200 representing a whole octave.
                            break;

                        case RpcParameter.ReverbSend:
                            reverbMix *= XactHelpers.ParseVolumeFromDecibels(value / 100.0f);
                            break;

                        case RpcParameter.FilterFrequency:
                            filterFrequency = value;
                            break;

                        case RpcParameter.FilterQFactor:
                            filterQFactor = value;
                            break;

                        default:
                            throw new ArgumentOutOfRangeException("rpcCurve.Parameter");
                    }
                }

                pitch = MathHelper.Clamp(pitch, -1.0f, 1.0f);
                if (volume < 0.0f)
                    volume = 0.0f;

                _rpcVolume = volume;
                _rpcPitch = pitch;
                _rpcReverbMix = reverbMix;
                _rpcFilterFrequency = filterFrequency;
                _rpcFilterQFactor = filterQFactor;

                _UpdateSoundParameters();
            }

            return volume;
        }

        internal void _UpdateSoundParameters()
        {
            if (_soundEffect == null)
            {
                return;
            }

            _soundEffect.Volume = _cueVolume * _playingCategory._volume * _rpcVolume * _currentXactSound.volume;
            _soundEffect.Pitch = _rpcPitch + _cuePitch + _currentXactSound.pitch;

            if (_currentXactSound.useReverb)
            {
                _soundEffect.PlatformSetReverbMix(_rpcReverbMix);
            }

            // The RPC filter overrides the randomized track filter.
            if (_soundEffect.IsFilterEnabled())
            {
                // 9/7/2021 ARTHUR: Previously, if a filterless sound effect had an RPC curve with Frequency defined,
                // it would forcibly apply a filter which would cause FAudio to stop working due to an out-of-range
                // Q value of 0 being used. Instead, we check if the filter was enabled by the play wave sound event
                // before we apply the filter.

                if (_rpcFilterQFactor.HasValue || _rpcFilterFrequency.HasValue)
                {
                    _soundEffect.PlatformSetFilter(_soundEffect._filterMode,
                        _rpcFilterQFactor.HasValue ? _rpcFilterQFactor.Value : _soundEffect._filterQ,
                        _rpcFilterFrequency.HasValue ? _rpcFilterFrequency.Value : _soundEffect._filterFrequency);
                }
            }
        }
        
        /// <summary>
        /// This event is triggered when the Cue is disposed.
        /// </summary>
        public event EventHandler<EventArgs> Disposing;

        /// <summary>
        /// Is true if the Cue has been disposed.
        /// </summary>
        public bool IsDisposed { get; internal set; }

        /// <summary>
        /// Disposes the Cue
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (IsDisposed)
                return;

            IsDisposed = true;

            if (disposing)
            {
                IsCreated = false;
                IsPrepared = false;
                EventHelpers.Raise(this, Disposing, EventArgs.Empty);

                if (_cueDefinition != null)
                {
                    _cueDefinition.OnModified -= _OnCueDefinitionModified;
                    _cueDefinition = null;
                }
            }
        }
    }
}

