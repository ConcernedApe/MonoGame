// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;
using System.Diagnostics;
using System.IO;
#if !FAUDIO
using MonoGame.OpenAL;
using RTAudioProcessing;
#endif
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Microsoft.Xna.Framework.Audio
{
    internal static partial class AudioLoader
    {
        internal static unsafe byte[] ConvertMsAdpcmToPcm(byte[] bufferArr, int offset, int count, int channelCount, int blockAlignment)
        {
            Stopwatch watch;

            watch = Stopwatch.StartNew();
            byte[] s2 = ConvertMsAdpcmToPcmOriginal(bufferArr, offset, count, channelCount, blockAlignment);
            watch.Stop();
            long t2 = watch.ElapsedMilliseconds;

#if !FAUDIO
            byte[] s1;

            RtapFormat rtapFormat = (channelCount == 2)
                ? RtapFormat.StereoMSAdpcm
                : RtapFormat.MonoMSAdpcm;

            using(RtapPond pond = new RtapPond(bufferArr, rtapFormat, 0, blockAlignment))
            using(RtapRiver river = new RtapRiver())
            {
                river.SetPond(pond);
                s1 = new byte[pond.Length];

                fixed (byte* b2 = s2)
                fixed (byte* samples = s1)
                {
                    watch = Stopwatch.StartNew();
                    if (true)
                    {
                        for (int i = 0; i < s1.Length; ++i)
                        {
                            river.ReadInto((IntPtr)(samples + i), i, 1);
                            if (samples[i] != b2[i])
                                throw new InvalidOperationException("s1 != s2");
                        }
                    }
                    else
                    {
                        river.ReadInto((IntPtr)samples, 0, s1.Length);
                    }
                    watch.Stop();
                }
            }

            long t1 = watch.ElapsedMilliseconds;

            Console.WriteLine($"{t1} {t2}");
#endif

            return s2;
        }

        static int[] ADAPTATION_TABLE = new int[]
        {
            230, 230, 230, 230, 307, 409, 512, 614,
            768, 614, 512, 409, 307, 230, 230, 230
        };

        static int[] ADAPTATION_COEFF1 = new int[]
        {
            256, 512, 0, 192, 240, 460, 392
        };

        static int[] ADAPTATION_COEFF2 = new int[]
        {
            0, -256, 0, 64, 0, -208, -232
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct MsAdpcmState
        {
            public int coeff1;
            public int coeff2;
            public int delta;
            public int sample1;
            public int sample2;
        }

        private static int AdpcmMsExpandNibbleOrig(ref MsAdpcmState channel, int nibble)
        {
            int nibbleSign = nibble - (((nibble & 0x08) != 0) ? 0x10 : 0);
            int predictor = ((channel.sample1 * channel.coeff1) + (channel.sample2 * channel.coeff2)) / 256 + (nibbleSign * channel.delta);

            if (predictor < -32768)
                predictor = -32768;
            else if (predictor > 32767)
                predictor = 32767;

            channel.sample2 = channel.sample1;
            channel.sample1 = predictor;

            channel.delta = (ADAPTATION_TABLE[nibble] * channel.delta) / 256;
            if (channel.delta < 16)
                channel.delta = 16;

            return predictor;
        }

        // Convert buffer containing MS-ADPCM wav data to a 16-bit signed PCM buffer
        private static byte[] ConvertMsAdpcmToPcmOriginal(byte[] buffer, int offset, int count, int channels, int blockAlignment)
        {
            MsAdpcmState channel0 = new MsAdpcmState();
            MsAdpcmState channel1 = new MsAdpcmState();
            int blockPredictor;

            int sampleCountFullBlock = ((blockAlignment / channels) - 7) * 2 + 2;
            int sampleCountLastBlock = 0;
            if ((count % blockAlignment) > 0)
                sampleCountLastBlock = (((count % blockAlignment) / channels) - 7) * 2 + 2;
            int sampleCount = ((count / blockAlignment) * sampleCountFullBlock) + sampleCountLastBlock;
            var samples = new byte[sampleCount * sizeof(short) * channels];
            int sampleOffset = 0;

            bool stereo = channels == 2;

            while (count > 0)
            {
                int blockSize = blockAlignment;
                if (count < blockSize)
                    blockSize = count;
                count -= blockAlignment;

                int totalSamples = ((blockSize / channels) - 7) * 2 + 2;
                if (totalSamples < 2)
                    break;

                int offsetStart = offset;
                blockPredictor = buffer[offset];
                ++offset;
                if (blockPredictor > 6)
                    blockPredictor = 6;
                channel0.coeff1 = ADAPTATION_COEFF1[blockPredictor];
                channel0.coeff2 = ADAPTATION_COEFF2[blockPredictor];
                if (stereo)
                {
                    blockPredictor = buffer[offset];
                    ++offset;
                    if (blockPredictor > 6)
                        blockPredictor = 6;
                    channel1.coeff1 = ADAPTATION_COEFF1[blockPredictor];
                    channel1.coeff2 = ADAPTATION_COEFF2[blockPredictor];
                }

                channel0.delta = buffer[offset];
                channel0.delta |= buffer[offset + 1] << 8;
                if ((channel0.delta & 0x8000) != 0)
                    channel0.delta -= 0x10000;
                offset += 2;
                if (stereo)
                {
                    channel1.delta = buffer[offset];
                    channel1.delta |= buffer[offset + 1] << 8;
                    if ((channel1.delta & 0x8000) != 0)
                        channel1.delta -= 0x10000;
                    offset += 2;
                }

                channel0.sample1 = buffer[offset];
                channel0.sample1 |= buffer[offset + 1] << 8;
                if ((channel0.sample1 & 0x8000) != 0)
                    channel0.sample1 -= 0x10000;
                offset += 2;
                if (stereo)
                {
                    channel1.sample1 = buffer[offset];
                    channel1.sample1 |= buffer[offset + 1] << 8;
                    if ((channel1.sample1 & 0x8000) != 0)
                        channel1.sample1 -= 0x10000;
                    offset += 2;
                }

                channel0.sample2 = buffer[offset];
                channel0.sample2 |= buffer[offset + 1] << 8;
                if ((channel0.sample2 & 0x8000) != 0)
                    channel0.sample2 -= 0x10000;
                offset += 2;
                if (stereo)
                {
                    channel1.sample2 = buffer[offset];
                    channel1.sample2 |= buffer[offset + 1] << 8;
                    if ((channel1.sample2 & 0x8000) != 0)
                        channel1.sample2 -= 0x10000;
                    offset += 2;
                }

                if (stereo)
                {
                    samples[sampleOffset] = (byte)channel0.sample2;
                    samples[sampleOffset + 1] = (byte)(channel0.sample2 >> 8);
                    samples[sampleOffset + 2] = (byte)channel1.sample2;
                    samples[sampleOffset + 3] = (byte)(channel1.sample2 >> 8);
                    samples[sampleOffset + 4] = (byte)channel0.sample1;
                    samples[sampleOffset + 5] = (byte)(channel0.sample1 >> 8);
                    samples[sampleOffset + 6] = (byte)channel1.sample1;
                    samples[sampleOffset + 7] = (byte)(channel1.sample1 >> 8);
                    sampleOffset += 8;
                }
                else
                {
                    samples[sampleOffset] = (byte)channel0.sample2;
                    samples[sampleOffset + 1] = (byte)(channel0.sample2 >> 8);
                    samples[sampleOffset + 2] = (byte)channel0.sample1;
                    samples[sampleOffset + 3] = (byte)(channel0.sample1 >> 8);
                    sampleOffset += 4;
                }

                blockSize -= (offset - offsetStart);
                if (stereo)
                {
                    for (int i = 0; i < blockSize; ++i)
                    {
                        int nibbles = buffer[offset];

                        int sample = AdpcmMsExpandNibbleOrig(ref channel0, nibbles >> 4);
                        samples[sampleOffset] = (byte)sample;
                        samples[sampleOffset + 1] = (byte)(sample >> 8);

                        sample = AdpcmMsExpandNibbleOrig(ref channel1, nibbles & 0x0f);
                        samples[sampleOffset + 2] = (byte)sample;
                        samples[sampleOffset + 3] = (byte)(sample >> 8);

                        sampleOffset += 4;
                        ++offset;
                    }
                }
                else
                {
                    for (int i = 0; i < blockSize; ++i)
                    {
                        int nibbles = buffer[offset];

                        int sample = AdpcmMsExpandNibbleOrig(ref channel0, nibbles >> 4);
                        samples[sampleOffset] = (byte)sample;
                        samples[sampleOffset + 1] = (byte)(sample >> 8);

                        sample = AdpcmMsExpandNibbleOrig(ref channel0, nibbles & 0x0f);
                        samples[sampleOffset + 2] = (byte)sample;
                        samples[sampleOffset + 3] = (byte)(sample >> 8);

                        sampleOffset += 4;
                        ++offset;
                    }
                }
            }

            return samples;
        }


    }
}
