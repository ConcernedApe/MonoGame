// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;
using System.IO;
#if !FAUDIO
using MonoGame.OpenAL;
#endif
using System.Runtime.InteropServices;

namespace Microsoft.Xna.Framework.Audio
{
    internal static partial class AudioLoader
    {
        static int[] adaptationTable = new int[]
        {
            230, 230, 230, 230, 307, 409, 512, 614,
            768, 614, 512, 409, 307, 230, 230, 230
        };

        static int[] adaptationCoeff1 = new int[]
        {
            256, 512, 0, 192, 240, 460, 392
        };

        static int[] adaptationCoeff2 = new int[]
        {
            0, -256, 0, 64, 0, -208, -232
        };

        struct MsAdpcmState
        {
            public int delta;
            public int sample1;
            public int sample2;
            public int coeff1;
            public int coeff2;
        }

        static int AdpcmMsExpandNibble(ref MsAdpcmState channel, int nibble)
        {
            int nibbleSign = nibble - (((nibble & 0x08) != 0) ? 0x10 : 0);
            int predictor = ((channel.sample1 * channel.coeff1) + (channel.sample2 * channel.coeff2)) / 256 + (nibbleSign * channel.delta);

            if (predictor < -32768)
                predictor = -32768;
            else if (predictor > 32767)
                predictor = 32767;

            channel.sample2 = channel.sample1;
            channel.sample1 = predictor;

            channel.delta = (adaptationTable[nibble] * channel.delta) / 256;
            if (channel.delta < 16)
                channel.delta = 16;

            return predictor;
        }

        internal static byte[] ConvertMsAdpcmToPcm(byte[] buffer, int offset, int count, int channels, int blockAlignment)
        {
            return (channels == 2)
                ? ConvertMsAdpcmToPcmStereo(buffer, offset, count, blockAlignment)
                : ConvertMsAdpcmToPcmMono(buffer, offset, count, blockAlignment);
        }

        // Convert buffer containing MS-ADPCM wav data to a 16-bit signed PCM buffer
        internal static byte[] ConvertMsAdpcmToPcmMono(byte[] buffer, int offset, int count, int blockAlignment)
        {
            MsAdpcmState channel0 = new MsAdpcmState();
            int blockPredictor;

            byte[] samples;
            {
                int sampleCountFullBlock = (blockAlignment - 7) * 2 + 2;
                int sampleCountLastBlock = 0;
                if ((count % blockAlignment) > 0)
                    sampleCountLastBlock = ((count % blockAlignment) - 7) * 2 + 2;
                int sampleCount = ((count / blockAlignment) * sampleCountFullBlock) + sampleCountLastBlock;
                samples = new byte[sampleCount * sizeof(short)];
            }
            int sampleOffset = 0;

            while (count > 0)
            {
                int blockSize = blockAlignment;
                if (count < blockSize)
                    blockSize = count;
                count -= blockAlignment;

                {
                    int totalSamples = (blockSize - 7) * 2 + 2;
                    if (totalSamples < 2)
                        break;
                }

                int offsetStart = offset;
                blockPredictor = buffer[offset];
                ++offset;
                if (blockPredictor > 6)
                    blockPredictor = 6;
                channel0.coeff1 = adaptationCoeff1[blockPredictor];
                channel0.coeff2 = adaptationCoeff2[blockPredictor];

                channel0.delta = buffer[offset];
                channel0.delta |= buffer[offset + 1] << 8;
                if ((channel0.delta & 0x8000) != 0)
                    channel0.delta -= 0x10000;
                offset += 2;

                channel0.sample1 = buffer[offset];
                channel0.sample1 |= buffer[offset + 1] << 8;
                if ((channel0.sample1 & 0x8000) != 0)
                    channel0.sample1 -= 0x10000;
                offset += 2;

                channel0.sample2 = buffer[offset];
                channel0.sample2 |= buffer[offset + 1] << 8;
                if ((channel0.sample2 & 0x8000) != 0)
                    channel0.sample2 -= 0x10000;
                offset += 2;

                samples[sampleOffset] = (byte)channel0.sample2;
                samples[sampleOffset + 1] = (byte)(channel0.sample2 >> 8);
                samples[sampleOffset + 2] = (byte)channel0.sample1;
                samples[sampleOffset + 3] = (byte)(channel0.sample1 >> 8);
                sampleOffset += 4;

                blockSize -= (offset - offsetStart);
                for (int i = 0; i < blockSize; ++i)
                {
                    int nibbles = buffer[offset];

                    int sample = AdpcmMsExpandNibble(ref channel0, nibbles >> 4);
                    samples[sampleOffset] = (byte)sample;
                    samples[sampleOffset + 1] = (byte)(sample >> 8);

                    sample = AdpcmMsExpandNibble(ref channel0, nibbles & 0x0f);
                    samples[sampleOffset + 2] = (byte)sample;
                    samples[sampleOffset + 3] = (byte)(sample >> 8);

                    sampleOffset += 4;
                    ++offset;
                }
            }

            return samples;
        }

        // Convert buffer containing MS-ADPCM wav data to a 16-bit signed PCM buffer
        internal static byte[] ConvertMsAdpcmToPcmStereo(byte[] buffer, int offset, int count, int blockAlignment)
        {
            MsAdpcmState channel0 = new MsAdpcmState();
            MsAdpcmState channel1 = new MsAdpcmState();
            int blockPredictor;

            byte[] samples;
            {
                int sampleCountFullBlock = (blockAlignment - 7) * 2 + 2;
                int sampleCountLastBlock = 0;
                if ((count % blockAlignment) > 0)
                    sampleCountLastBlock = ((count % blockAlignment) - 7) * 2 + 2;
                int sampleCount = ((count / blockAlignment) * sampleCountFullBlock) + sampleCountLastBlock;
                samples = new byte[sampleCount * sizeof(short)];
            }
            int sampleOffset = 0;

            while (count > 0)
            {
                int blockSize = blockAlignment;
                if (count < blockSize)
                    blockSize = count;
                count -= blockAlignment;

                {
                    int totalSamples = ((blockSize >> 1) - 7) * 2 + 2;
                    if (totalSamples < 2)
                        break;
                }

                int offsetStart = offset;
                blockPredictor = buffer[offset];
                ++offset;
                if (blockPredictor > 6)
                    blockPredictor = 6;
                channel0.coeff1 = adaptationCoeff1[blockPredictor];
                channel0.coeff2 = adaptationCoeff2[blockPredictor];

                blockPredictor = buffer[offset];
                ++offset;
                if (blockPredictor > 6)
                    blockPredictor = 6;
                channel1.coeff1 = adaptationCoeff1[blockPredictor];
                channel1.coeff2 = adaptationCoeff2[blockPredictor];

                channel0.delta = buffer[offset];
                channel0.delta |= buffer[offset + 1] << 8;
                if ((channel0.delta & 0x8000) != 0)
                    channel0.delta -= 0x10000;
                offset += 2;

                channel1.delta = buffer[offset];
                channel1.delta |= buffer[offset + 1] << 8;
                if ((channel1.delta & 0x8000) != 0)
                    channel1.delta -= 0x10000;
                offset += 2;

                channel0.sample1 = buffer[offset];
                channel0.sample1 |= buffer[offset + 1] << 8;
                if ((channel0.sample1 & 0x8000) != 0)
                    channel0.sample1 -= 0x10000;
                offset += 2;

                channel1.sample1 = buffer[offset];
                channel1.sample1 |= buffer[offset + 1] << 8;
                if ((channel1.sample1 & 0x8000) != 0)
                    channel1.sample1 -= 0x10000;
                offset += 2;

                channel0.sample2 = buffer[offset];
                channel0.sample2 |= buffer[offset + 1] << 8;
                if ((channel0.sample2 & 0x8000) != 0)
                    channel0.sample2 -= 0x10000;
                offset += 2;

                channel1.sample2 = buffer[offset];
                channel1.sample2 |= buffer[offset + 1] << 8;
                if ((channel1.sample2 & 0x8000) != 0)
                    channel1.sample2 -= 0x10000;
                offset += 2;

                samples[sampleOffset] = (byte)channel0.sample2;
                samples[sampleOffset + 1] = (byte)(channel0.sample2 >> 8);
                samples[sampleOffset + 2] = (byte)channel1.sample2;
                samples[sampleOffset + 3] = (byte)(channel1.sample2 >> 8);
                samples[sampleOffset + 4] = (byte)channel0.sample1;
                samples[sampleOffset + 5] = (byte)(channel0.sample1 >> 8);
                samples[sampleOffset + 6] = (byte)channel1.sample1;
                samples[sampleOffset + 7] = (byte)(channel1.sample1 >> 8);
                sampleOffset += 8;

                blockSize -= (offset - offsetStart);
                for (int i = 0; i < blockSize; ++i)
                {
                    int nibbles = buffer[offset];

                    int sample = AdpcmMsExpandNibble(ref channel0, nibbles >> 4);
                    samples[sampleOffset] = (byte)sample;
                    samples[sampleOffset + 1] = (byte)(sample >> 8);

                    sample = AdpcmMsExpandNibble(ref channel0, nibbles & 0x0f);
                    samples[sampleOffset + 2] = (byte)sample;
                    samples[sampleOffset + 3] = (byte)(sample >> 8);

                    sampleOffset += 4;
                    ++offset;
                }

                blockSize -= (offset - offsetStart);
                for (int i = 0; i < blockSize; ++i)
                {
                    int nibbles = buffer[offset];

                    int sample = AdpcmMsExpandNibble(ref channel0, nibbles >> 4);
                    samples[sampleOffset] = (byte)sample;
                    samples[sampleOffset + 1] = (byte)(sample >> 8);

                    sample = AdpcmMsExpandNibble(ref channel1, nibbles & 0x0f);
                    samples[sampleOffset + 2] = (byte)sample;
                    samples[sampleOffset + 3] = (byte)(sample >> 8);

                    sampleOffset += 4;
                    ++offset;
                }
            }

            return samples;
        }
    }
}