// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Microsoft.Xna.Framework.Audio
{
    internal class OpenALSoundEffectInstanceManager : IDisposable
    {
        private static readonly object singletonMutex = new object();

        private static OpenALSoundEffectInstanceManager instance;

        internal static OpenALSoundEffectInstanceManager Instance
        {
            get
            {
                lock (singletonMutex)
                {
                    if (instance == null)
                        throw new InvalidOperationException("No instance running");
                    return instance;
                }
            }

            private set
            {
                lock (singletonMutex)
                    instance = value;
            }
        }

        private readonly Thread underlyingThread;

        private volatile bool running;

        private readonly List<WeakReference> _playingInstances;

        public OpenALSoundEffectInstanceManager()
        {
            lock (singletonMutex)
            {
                if (!(instance == null))
                    throw new InvalidOperationException("Already running");

                running = true;

                instance = this;
                underlyingThread = new Thread(Update)
                {
                    Priority = ThreadPriority.Lowest,
                    IsBackground = true
                };
                underlyingThread.Start();
            }
            _playingInstances = new List<WeakReference>();
        }

        public void Update()
        {
            while (running)
            {
                Thread.Sleep(30);
                if (!running)
                    break;
                lock (SoundEffectInstancePool._locker)
                {
                    SoundEffectInstance inst = null;                  
                    for (var x = 0; x < SoundEffectInstancePool._playingInstances.Count; ++x)
                    {
                        inst = SoundEffectInstancePool._playingInstances[x];
                        if (inst.IsDisposed || inst.State != SoundState.Playing || inst._effect == null || inst._isDynamic)
                            continue;
                        inst.UpdateQueue();
                    }
                }
            }
        }

        public void AddInstance(SoundEffectInstance instance)
        {
            var weakRef = new WeakReference(instance);
            _playingInstances.Add(weakRef);
        }

        public void RemoveInstance(SoundEffectInstance instance)
        {
            for (int i = _playingInstances.Count - 1; i >= 0; i--)
            {
                if (_playingInstances[i].Target == instance)
                {
                    _playingInstances.RemoveAt(i);
                    return;
                }
            }
        }

        public void UpdatePlayingInstances()
        {
            for (int i = _playingInstances.Count - 1; i >= 0; i--)
            {
                var target = _playingInstances[i].Target as SoundEffectInstance;
                if (target != null)
                {
                    if (!target.IsDisposed)
                        target.UpdateQueue();
                }
                else
                {
                    // The instance has been disposed.
                    _playingInstances.RemoveAt(i);
                }
            }
        }

        public void Dispose()
        {
            running = false;
        }
    }
}
