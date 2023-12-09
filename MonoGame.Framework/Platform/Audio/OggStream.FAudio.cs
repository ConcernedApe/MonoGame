// This code originated from:
//
//    http://theinstructionlimit.com/ogg-streaming-using-opentk-and-nvorbis
//    https://github.com/renaudbedard/nvorbis/
//
// It was released to the public domain by the author (Renaud Bedard).
// No other license is intended or required.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using NVorbis;

namespace Microsoft.Xna.Framework.Audio
{
    public class OggStream : IDisposable
    {
        readonly string oggFileName;

        internal VorbisReader Reader { get; private set; }
        internal bool Ready { get; private set; }
        internal bool Preparing { get; private set; }

        public Action FinishedAction { get; private set; }

        protected DynamicSoundEffectInstance _instance;

        const int DefaultBufferSize = 44100;
        const int BytesPerSample = 2;

        readonly float[] readSampleBuffer;
        readonly short[] castBuffer;
        readonly byte[] xnaBuffer;

        protected int bufferSize = DefaultBufferSize;

        public OggStream(string filename, Action finishedAction = null, int buffer_size = DefaultBufferSize)
        {
            oggFileName = filename;
            FinishedAction = finishedAction;
            bufferSize = buffer_size;

            readSampleBuffer = new float[bufferSize];
            castBuffer = new short[bufferSize];
            xnaBuffer = new byte[bufferSize * BytesPerSample];
        }

        public void Prepare()
        {
            if (Preparing) return;

            if (!Ready)
            {
                Preparing = true;
                Open(precache: true);
            }
        }

        public void Play()
        {
            if (_instance == null)
            {
                return;
            }

            // If there aren't any pre
            if (_instance.PendingBufferCount == 0)
            {
                SubmitBuffer();
            }
            
            _instance.Play();
        }

        public void Pause()
        {
            _instance.Pause();
        }

        public void Resume()
        {
            _instance.Resume();
        }

        public void Stop()
        {
            SeekToPosition(new TimeSpan(0));
            _instance.Stop();
        }

        public void SeekToPosition(TimeSpan pos)
        {
            Reader.DecodedTime = pos;
        }

        public TimeSpan GetPosition()
        {
            if (Reader == null)
                return TimeSpan.Zero;

            return Reader.DecodedTime;
        }

        public TimeSpan GetLength()
        {
            return Reader.TotalTime;
        }

        float volume;
        public float Volume
        {
            get { return volume; }
            set
            {
                _instance.Volume = value;
            }
        }

        public uint LoopCount {
            get
            {
                return _instance.LoopCount;
            }
            set
            {
                _instance.LoopCount = value;
            }
        }

        public void Dispose()
        {
            if (_instance != null)
			{
                _instance.Dispose();
                _instance = null;
            }
        }

        internal void Open(bool precache = false)
        {
            Reader = new VorbisReader(oggFileName);

            _instance = new DynamicSoundEffectInstance(Reader.SampleRate, (Reader.Channels == 1) ? AudioChannels.Mono : AudioChannels.Stereo);

            _instance.BufferNeeded += (s, e) => { SubmitBuffer(); };

            if (precache)
            {
                SubmitBuffer();
            }

            Ready = true;
            Preparing = false;
        }

        public virtual void SubmitBuffer()
        {
            // Handle stream end.
            if (Reader.DecodedPosition >= Reader.TotalSamples)
            {
                if (LoopCount > 0)
                {
                    Reader.DecodedPosition = 0;
                }
                else
                {
                    if (_instance.PendingBufferCount == 0)
                    {
                        if (FinishedAction != null)
                        {
                            FinishedAction();
                        }
                    }

                    return;
                }
            }

            int read_samples = Reader.ReadSamples(readSampleBuffer, 0, bufferSize);

            /*
            This code populates a sample that is shorter than the buffer with its contents, but isn't used because looping is handled by MediaPlayer.

            if (IsLooped)
            {
                while (read_samples < bufferSize)
                {
                    int samples_to_read = bufferSize - read_samples;

                    Reader.DecodedPosition = 0;
                    read_samples += Reader.ReadSamples(readSampleBuffer, read_samples, samples_to_read);
                }
            }
            */

            CastBuffer(readSampleBuffer, castBuffer, read_samples);
            Buffer.BlockCopy(castBuffer, 0, xnaBuffer, 0, read_samples * BytesPerSample);
            _instance.SubmitBuffer(xnaBuffer, 0, read_samples * BytesPerSample);
        }

        public static void CastBuffer(float[] inBuffer, short[] outBuffer, int length)
        {
            for (int i = 0; i < length; i++)
            {
                var temp = (int)(32767f * inBuffer[i]);
                if (temp > short.MaxValue) temp = short.MaxValue;
                else if (temp < short.MinValue) temp = short.MinValue;
                outBuffer[i] = (short)temp;
            }
        }

        public DynamicSoundEffectInstance GetSoundEffectInstance()
        {
            return _instance;
        }

        internal void Close()
        {
            if (Reader != null)
            {
                Reader.Dispose();
                Reader = null;
            }

            if (_instance != null)
            {
                _instance.Dispose();
                _instance = null;
            }

            Ready = false;
        }
    }
}
