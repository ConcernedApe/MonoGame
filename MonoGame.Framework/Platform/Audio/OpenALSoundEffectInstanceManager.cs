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
        internal static bool paused = false;

        internal static readonly object pauseMutex = new object();

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

        private readonly List<WeakReference> _threadLocalInstances = new List<WeakReference>();

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
        }

        public void Update()
        {
            while (running)
            {
                Thread.Sleep(30);
                if (!running)
                    break;

                lock (pauseMutex)
                {
                    if (!paused)
                    {
                        lock (SoundEffectInstancePool._locker)
                        {
                            _threadLocalInstances.Clear();
                            foreach (SoundEffectInstance instance in SoundEffectInstancePool._playingInstances)
                                _threadLocalInstances.Add(new WeakReference(instance));
                        }

                        SoundEffectInstance inst = null;                  
                        for (var x = 0; x < _threadLocalInstances.Count; ++x)
                        {
                            inst = _threadLocalInstances[x]?.Target as SoundEffectInstance;
                            if (inst.IsDisposed || inst.State != SoundState.Playing || (inst._effect == null && !inst._isDynamic))
                            {
                                if (inst._isXAct)
                                    continue;

                                lock (SoundEffectInstancePool._locker)
                                {
                                    if (!inst.IsDisposed)
                                    {
                                        inst.Stop(true);

                                        // dynamic sound effects already call SoundEffectInstancePool.Add(...)
                                        if (inst._isDynamic)
                                            continue;
                                    }

                                    SoundEffectInstancePool.Add(inst);
                                }
                                continue;
                            }
                            inst.UpdateQueue();
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            running = false;
        }
    }
}
