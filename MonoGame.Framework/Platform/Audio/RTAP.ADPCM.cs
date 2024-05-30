using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace RTAudioProcessing
{
    internal static partial class RTAP
    {
        private static int[] ADAPTATION_TABLE = new int[]
        {
            230, 230, 230, 230, 307, 409, 512, 614,
            768, 614, 512, 409, 307, 230, 230, 230
        };

        private static int[] ADAPTATION_COEFF1 = new int[]
        {
            256, 512, 0, 192, 240, 460, 392
        };

        private static int[] ADAPTATION_COEFF2 = new int[]
        {
            0, -256, 0, 64, 0, -208, -232
        };

        private const int COEFF1 = 0;
        private const int COEFF2 = 1;
        private const int DELTA = 2;
        private const int SAMPLE1 = 3;
        private const int SAMPLE2 = 4;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void adpcm_write_header_samples(byte* dest, int* channels, int STEREO)
        {
            if (((IntPtr)dest) == IntPtr.Zero)
                return;

            for (int i = 0; i < 2; ++i)
            {
                for (int c = 0; c <= STEREO; ++c)
                {
                    int base_idx = (i * 2 * (STEREO + 1)) + (c * 2);
                    int s = (channels + (c * 5))[4 - i];
                    dest[base_idx] = (byte)s;
                    dest[base_idx + 1] = (byte)(s >> 8);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe int adpcm_expand_nibble(int* channel, int nibble)
        {
            int nibbleSign = nibble - (((nibble & 0x08) != 0) ? 0x10 : 0);
            int predictor = ((channel[SAMPLE1] * channel[COEFF1]) + (channel[SAMPLE2] * channel[COEFF2])) / 256 + (nibbleSign * channel[DELTA]);

            if (predictor < -32768)
                predictor = -32768;
            else if (predictor > 32767)
                predictor = 32767;

            channel[SAMPLE2] = channel[SAMPLE1];
            channel[SAMPLE1] = predictor;

            int delta = (ADAPTATION_TABLE[nibble] * channel[DELTA]) / 256;
            if (delta < 16)
                delta = 16;

            channel[DELTA] = delta;

            return predictor;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void adpcm_write_payload_samples(byte* dest, int dest_offset, int* channels, int nibbles, int STEREO)
        {
            int sample1 = adpcm_expand_nibble(channels, nibbles >> 4);
            int sample2 = adpcm_expand_nibble(channels + (STEREO * 5), nibbles & 0x0f);

            if (((IntPtr)dest) == IntPtr.Zero)
                return;

            dest += dest_offset;
            dest[0] = (byte)sample1;
            dest[1] = (byte)(sample1 >> 8);
            dest[2] = (byte)sample2;
            dest[3] = (byte)(sample2 >> 8);
        }

        private static unsafe int rtap_river_read_adpcm_helper(RTAPRiver* _this, byte* dest, int dest_size)
        {
            RTAPSpring* spring = (RTAPSpring*)(_this->spring);

            int STEREO = ((spring->format & FLAG_STEREO) == 0) ? 0 : 1;
            int HEADER_SIZE = (STEREO + 1) + ((STEREO + 1) << 1) * 3;
            int HEADER_DBYTES = (STEREO + 1) << 2;

            int* channels = stackalloc int[10];

            channels[COEFF1] = _this->l_coeff1;
            channels[COEFF2] = _this->l_coeff2;
            channels[DELTA] = _this->l_delta;
            channels[SAMPLE1] = _this->l_sample1;
            channels[SAMPLE2] = _this->l_sample2;

            if (STEREO != 0)
            {
                int* right = channels + 5;
                right[COEFF1] = _this->r_coeff1;
                right[COEFF2] = _this->r_coeff2;
                right[DELTA] = _this->r_delta;
                right[SAMPLE1] = _this->r_sample1;
                right[SAMPLE2] = _this->r_sample2;
            }

            byte* cache = stackalloc byte[8];
            int cache_size = 0;

            byte* buffer = (byte*)(spring->data);
            int block_align = spring->block_align;

            int read_head = _this->read_head;
            int data_size = spring->data_size;

            while (read_head < data_size && dest_size > 0)
            {
                int block_size = block_align;
                {
                    int read_block = read_head / block_align;
                    int remaining = data_size - (read_block * block_align);
                    if (remaining < block_size)
                        block_size = remaining;
                    if (((block_size >> STEREO) - 7) < 0)
                        break;
                }

                int block_offset = read_head % block_align;

                if (block_offset == 0)
                {
                    for (int c = 0; c <= STEREO; ++c)
                    {
                        int block_predictor = buffer[read_head + c];
                        if (block_predictor > 6)
                            block_predictor = 6;
                        int* channel = channels + (c * 5);
                        channel[COEFF1] = ADAPTATION_COEFF1[block_predictor];
                        channel[COEFF2] = ADAPTATION_COEFF2[block_predictor];
                    }

                    for (int i = 0; i < 3; ++i)
                    {
                        for (int c = 0; c <= STEREO; ++c)
                        {
                            int base_idx = read_head + (STEREO + 1) + ((STEREO + 1) << 1) * i + (c << 1);
                            int s = buffer[base_idx];
                            s |= buffer[base_idx + 1] << 8;
                            if ((s & 0x8000) != 0)
                                s -= 0x10000;
                            (channels + (c * 5))[i + 2] = s;
                        }
                    }

                    read_head += HEADER_SIZE;
                    block_offset = HEADER_SIZE;

                    if (dest_size >= HEADER_DBYTES)
                    {
                        adpcm_write_header_samples(dest, channels, STEREO);
                        if (((IntPtr)dest) != IntPtr.Zero)
                            dest += HEADER_DBYTES;
                        dest_size -= HEADER_DBYTES;
                    }
                    else
                    {
                        adpcm_write_header_samples(cache, channels, STEREO);
                        cache_size = HEADER_DBYTES;
                        break;
                    }
                }

                block_size -= block_offset;

                int iterations = dest_size >> 2;
                if (iterations > block_size)
                    iterations = block_size;

                for (int i = 0; i < iterations; ++i)
                    adpcm_write_payload_samples(dest, (i << 2), channels, buffer[read_head + i], STEREO);

                read_head += iterations;
                if (((IntPtr)dest) != IntPtr.Zero)
                    dest += (iterations << 2);
                dest_size -= (iterations << 2);

                if (iterations < block_size && (dest_size & 0x3) != 0)
                {
                    adpcm_write_payload_samples(cache, 0, channels, buffer[read_head], STEREO);
                    cache_size = 4;
                    read_head++;
                    break;
                }
            }

            _this->read_head = read_head;
            _this->cache_size = cache_size;
            _this->l_coeff1 = channels[COEFF1];
            _this->l_coeff2 = channels[COEFF2];
            _this->l_delta = channels[DELTA];
            _this->l_sample1 = channels[SAMPLE1];
            _this->l_sample2 = channels[SAMPLE2];
            if (STEREO != 0)
            {
                int* right = channels + 5;
                _this->r_coeff1 = right[COEFF1];
                _this->r_coeff2 = right[COEFF2];
                _this->r_delta = right[DELTA];
                _this->r_sample1 = right[SAMPLE1];
                _this->r_sample2 = right[SAMPLE2];
            }
            byte* river_cache = ((byte*)_this) + RIVER_CACHE_OFFSET;
            for (int i = 0; i < cache_size; ++i)
                river_cache[i] = cache[i];

            return dest_size;
        }
    }
}