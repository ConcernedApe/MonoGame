// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using NVorbis;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace Microsoft.Xna.Framework.Audio
{

#if DESKTOPGL
    public class OggStreamSoundEffect : SoundEffect
    {
        const int BufferSize = 1024 * 16;
        const int BytesPerSample = 2;
        const int BufferSamples = BufferSize / BytesPerSample;

        private string OggFileName;
        private long TotalSamplesPerChannel;
        private int SampleRate;
        private AudioChannels Channels;

        public OggStreamSoundEffect(string oggFileName)
        {
            OggFileName = oggFileName;

            using (VorbisReader reader = new VorbisReader(OggFileName))
            {
                TotalSamplesPerChannel = reader.TotalSamples;
                SampleRate = reader.SampleRate;
                Channels = (reader.Channels == 2)
                    ? AudioChannels.Stereo
                    : AudioChannels.Mono;
            }
        }

        public override SoundEffectInstance GetPooledInstance(bool forXAct)
        {
            DynamicSoundEffectInstance sound = new DynamicSoundEffectInstance(SampleRate, Channels);
            sound._isXAct = forXAct;

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

            Thread thread = new Thread(() =>
            {
                int wrapBufferSize = (int)((TotalSamplesPerChannel * ((int)Channels) * BytesPerSample) % BufferSize);

                int timesPlayed = 0;

                float[] readBuffer = new float[BufferSamples];
                short[] castBuffer = new short[BufferSamples];

                int bindex = 0;
                byte[][] buffers = new byte[][]
                {
                    new byte[BufferSize],
                    new byte[BufferSize],
                    new byte[BufferSize],
                    new byte[BufferSize],
                };
                byte[] wrapBuffer = new byte[wrapBufferSize];

                VorbisReader reader = new VorbisReader(OggFileName);

            RESTART:
                reader.DecodedPosition = 0;

                while (!sound.IsDisposed)
                {
                    while (queue.Count < 3 && reader.DecodedPosition < TotalSamplesPerChannel)
                    {
                        byte[] buffer = wrapBuffer;

                        int read = Math.Min(BufferSamples, (int)((TotalSamplesPerChannel - reader.DecodedPosition) * ((int)Channels)));

                        if (read == BufferSamples)
                        {
                            buffer = buffers[bindex];
                            bindex = (bindex + 1) % 4;
                        }

                        read = reader.ReadSamples(readBuffer, 0, read);
                        OggStream.CastBuffer(readBuffer, castBuffer, read);
                        Buffer.BlockCopy(castBuffer, 0, buffer, 0, read * BytesPerSample);                                                

                        queue.Enqueue(buffer);

                        // If we've run out of file then the sound should 
                        // stop and this task can complete.
                        if (reader.DecodedPosition >= TotalSamplesPerChannel)
                            goto DONE;
                    }

                    // Wait for a signal that we need more buffers.
                    signal.WaitOne(1000);
                }

            DONE:

                if (!sound.IsDisposed && (sound.LoopCount >= 255 || (timesPlayed++) < sound.LoopCount))
                    goto RESTART;

                reader.Dispose();

                stop.Set();
                return;
            });
            thread.Priority = ThreadPriority.Highest;
            thread.Start();

            return sound;
        }
    }
#endif

}
