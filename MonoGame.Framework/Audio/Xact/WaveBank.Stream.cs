// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using RTAudioProcessing;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Xna.Framework.Audio
{
    partial class WaveBank
    {
#if IOS || DESKTOPGL
        private SoundEffectInstance PlatformCreateStream(StreamInfo info)
        {
            MiniFormatTag codec;
            int channels, rate, alignment;
            DecodeFormat(info.Format, out codec, out channels, out rate, out alignment);

            var length = info.FileLength;
            var buffer = new byte[length];

            using (var stream = AudioEngine.OpenStream(_waveBankFileName))
            {
                var start = _playRegionOffset + info.FileOffset;
                stream.Seek(start, SeekOrigin.Begin);
                stream.Read(buffer, 0, length);
            }

            // Our alternate OAL implementation in Stardew will decode the ADPCM audio
            // as it is played removing the decode overhead here.
            var sound = new SoundEffect(codec, buffer, channels, rate, alignment, info.LoopStart, info.LoopLength);

            var inst = sound.CreateInstance();
            inst._isXAct = true;
            return inst;
        }
#elif ANDROID
        private SoundEffectInstance PlatformCreateStream(StreamInfo info)
        {
            MiniFormatTag codec;
            int channels, rate, alignment, bits;
            DecodeFormat(info.Format, out codec, out channels, out rate, out alignment, out bits);

            int bufferSize = 1024 * 16;
            int pcmAlignment = alignment;
            bool msadpcm = (codec == MiniFormatTag.Adpcm);
            bool stereo = (channels == 2);
            if (msadpcm)
            {
                alignment = (alignment + 22) * channels;
                pcmAlignment = (channels + 1) * 2;
            }
            int wrapBufferSize = info.FileLength % bufferSize;

            var sound = new DynamicSoundEffectInstance(false, pcmAlignment, rate, channels == 2 ? AudioChannels.Stereo : AudioChannels.Mono);
            sound._isXAct = true;

            var queue = new ConcurrentQueue<byte[]>();
            var signal = new AutoResetEvent(false);
            var stop = new AutoResetEvent(false);

            sound.BufferNeeded += (o, e) =>
            {
                byte[] buff = null;

                try
                {
                    // We need to retry here until we submit a 
                    // buffer or the stream is finished.
                    while (true)
                    {
                        // Submit all the buffers we got to keep the sound fed.         
                        int submitted = 0;
                        while (queue.Count > 0)
                        {
                            if (queue.TryDequeue(out buff))
                            {
                                sound.SubmitBuffer(buff);
                                submitted++;
                            }
                        }

                        // Tell the task to go read more buffers while
                        // the buffers we just submitted are played.
                        signal.Set();

                        // If we submitted buffers then we're done.
                        if (submitted > 0)
                            return;

                        // If there were no buffers then look and see if we've 
                        // reached the end of the stream and should stop.
                        if (stop.WaitOne(0))
                        {
                            sound.Stop();
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                }
            };

            var thread = new Thread(() =>
            {
                int timesPlayed = 0;
                int start = _playRegionOffset + info.FileOffset;

                int bindex = 0;
                byte[][] buffers = new byte[][]
                {
                    new byte[bufferSize],
                    new byte[bufferSize],
                    new byte[bufferSize],
                    new byte[bufferSize],
                };
                byte[] wrapBuffer = new byte[wrapBufferSize];

                int length = info.FileLength;
                byte[] springBuffer = new byte[length];

                using(Stream stream = TitleContainer.OpenStream(_waveBankFileName))
                {
                    if (stream.CanSeek)
                        stream.Seek(start, SeekOrigin.Begin);
                    else
                    {
                        // Android doesn't support seekable streams
                        // so we need to read to seek.
                        byte[] temp = new byte[1024 * 32];
                        int curr = 0;
                        while (curr != start)
                        {
                            int read = Math.Min(temp.Length, start - curr);
                            curr += stream.Read(temp, 0, read);
                        }
                    }
                    stream.Read(springBuffer, 0, length);
                }

                RtapFormat rtapFormat = RtapFormat.Mono8;
                if (msadpcm)
                {
                    rtapFormat = stereo
                        ? RtapFormat.StereoMSAdpcm
                        : RtapFormat.MonoMSAdpcm;
                }
                else
                {
                    if (bits == 16)
                    {
                        rtapFormat = stereo
                            ? RtapFormat.Stereo16
                            : RtapFormat.Mono16;
                    }
                    else if (stereo)
                    {
                        rtapFormat = RtapFormat.Stereo8;
                    }
                }

                RtapSpring spring = new RtapSpring(springBuffer, rtapFormat, rate, alignment);
                RtapRiver river = new RtapRiver();
                river.SetSpring(spring);

                springBuffer = null;

RESTART:
                int bufferHead = 0;

                while (!sound.IsDisposed)
                {
                    while (queue.Count < 3 && bufferHead < length)
                    {
                        byte[] buffer = wrapBuffer;

                        int read = Math.Min(bufferSize, length - bufferHead);

                        if (read == bufferSize)
                        {
                            buffer = buffers[bindex];
                            bindex = (bindex + 1) % 4;
                        }

                        unsafe
                        {
                            fixed (byte* bufferPtr = buffer)
                            {
                                river.ReadInto((IntPtr)bufferPtr, bufferHead, read);
                            }
                        }

                        bufferHead += read;
                        queue.Enqueue(buffer);

                        // If we've run out of file then the sound should 
                        // stop and this task can complete.
                        if (bufferHead >= length)
                            goto DONE;
                    }

                    // Wait for a signal that we need more buffers.
                    signal.WaitOne(100);
                }

DONE:
                if (!sound.IsDisposed && (sound.LoopCount >= 255 || (timesPlayed++) < sound.LoopCount))
                    goto RESTART;

                river.Dispose();
                spring.Dispose();

                stop.Set();
                return;
            });
            thread.Priority = ThreadPriority.Highest;
            thread.Start();

            return sound;
        }
#else
        private SoundEffectInstance PlatformCreateStream(StreamInfo info)
        {
            MiniFormatTag codec;
            int channels, rate, alignment;
            DecodeFormat(info.Format, out codec, out channels, out rate, out alignment);

            int bufferSize;
            var msadpcm = codec == MiniFormatTag.Adpcm;
            if (msadpcm)
            {
                alignment = (alignment + 22) * channels;
                int samplesPerBlock = ((alignment * 2) / channels) - 12;
                bufferSize = (rate / samplesPerBlock) * alignment;
            }
            else
            {
                // This is 1 second of audio per buffer.
                bufferSize = rate * alignment;
            }
            int wrapBufferSize = info.FileLength % bufferSize;

            var sound = new DynamicSoundEffectInstance(msadpcm, alignment, rate, channels == 2 ? AudioChannels.Stereo : AudioChannels.Mono);
            sound._isXAct = true;

            var queue = new ConcurrentQueue<byte[]>();
            var signal = new AutoResetEvent(false);
            var stop = new AutoResetEvent(false);

            sound.BufferNeeded += (o, e) =>
            {
                byte[] buff = null;

                try
                {
                    // We need to retry here until we submit a 
                    // buffer or the stream is finished.
                    while (true)
                    {
                        // Submit all the buffers we got to keep the sound fed.         
                        int submitted = 0;
                        while (queue.Count > 0)
                        {
                            if (queue.TryDequeue(out buff))
                            {
                                sound.SubmitBuffer(buff);
                                submitted++;
                            }
                        }

                        // Tell the task to go read more buffers while
                        // the buffers we just submitted are played.
                        signal.Set();

                        // If we submitted buffers then we're done.
                        if (submitted > 0)
                            return;

                        // If there were no buffers then look and see if we've 
                        // reached the end of the stream and should stop.
                        if (stop.WaitOne(0))
                        {
                            sound.Stop();
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                }
            };

            var thread = new Thread(() =>
            {
                int timesPlayed = 0;
                var start = _playRegionOffset + info.FileOffset;

                var bindex = 0;
                var buffers = new byte[][]
                {
                    new byte[bufferSize],
                    new byte[bufferSize],
                    new byte[bufferSize],
                    new byte[bufferSize],
                };
                var wrapBuffer = new byte[wrapBufferSize];

            RESTART:

                var stream = TitleContainer.OpenStream(_waveBankFileName);
                var length = info.FileLength;

                if (stream.CanSeek)
                    stream.Seek(start, SeekOrigin.Begin);
                else
                {
                    // Android doesn't support seekable streams
                    // so we need to read to seek.
                    var temp = new byte[1024 * 32];
                    var curr = 0;
                    while (curr != start)
                    {
                        var read = Math.Min(temp.Length, start - curr);
                        curr += stream.Read(temp, 0, read);
                    }
                }

                while (!sound.IsDisposed)
                {
                    while (queue.Count < 3 && length > 0)
                    {
                        var buffer = wrapBuffer;

                        var read = Math.Min(bufferSize, length);

                        if (read == bufferSize)
                        {
                            buffer = buffers[bindex];
                            bindex = (bindex + 1) % 4;
                        }

                        read = stream.Read(buffer, 0, read);
                        length -= read;
                        queue.Enqueue(buffer);

                        // If we've run out of file then the sound should 
                        // stop and this task can complete.
                        if (length <= 0)
                            goto DONE;
                    }

                    // Wait for a signal that we need more buffers.
                    signal.WaitOne(1000);
                }

            DONE:

                stream.Close();

                if (!sound.IsDisposed && (sound.LoopCount >= 255 || (timesPlayed++) < sound.LoopCount))
                    goto RESTART;

                stop.Set();
                return;
            });
            thread.Priority = ThreadPriority.Highest;
            thread.Start();

            return sound;
        }
#endif
    }
}

