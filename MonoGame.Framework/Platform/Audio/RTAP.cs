using System;
using System.Runtime.InteropServices;

namespace RTAudioProcessing
{
    public enum RtapFormat
    {
        Mono8 = 0x0,
        Mono16 = RTAP.FLAG_16,
        Stereo8 = RTAP.FLAG_STEREO,
        Stereo16 = RTAP.FLAG_STEREO | RTAP.FLAG_16,
        MonoMSAdpcm = RTAP.FLAG_ADPCM | RTAP.FLAG_16,
        StereoMSAdpcm = RTAP.FLAG_ADPCM | RTAP.FLAG_STEREO | RTAP.FLAG_16
    }

    internal static partial class RTAP
    {
        internal const int FLAG_16 = 1;
        internal const int FLAG_STEREO = 1 << 1;
        internal const int FLAG_ADPCM = 1 << 2;

        private static int ALLOC_SIZE_SPRING;
        private static int ALLOC_SIZE_RIVER;

        private static int RIVER_CACHE_OFFSET;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct RTAPSpring
        {
            public IntPtr data;
            public int data_size;
            public int format;
            public int sample_rate;
            public int block_align;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct RTAPRiver
        {
            public IntPtr spring;
            public int read_head;
            public int cache_size;
            public int l_coeff1;
            public int l_coeff2;
            public int l_delta;
            public int l_sample1;
            public int l_sample2;
            public int r_coeff1;
            public int r_coeff2;
            public int r_delta;
            public int r_sample1;
            public int r_sample2;
            public byte cache0;
            public byte cache1;
            public byte cache2;
            public byte cache3;
            public byte cache4;
            public byte cache5;
            public byte cache6;
            public byte cache7;
        }

        static RTAP()
        {
            ALLOC_SIZE_SPRING = rtap_alloc_size_for_spring();
            ALLOC_SIZE_RIVER = rtap_alloc_size_for_river();
            
            RIVER_CACHE_OFFSET = (int)Marshal.OffsetOf(typeof(RTAPRiver), "cache0");
        }

        internal static int rtap_alloc_size_for_spring()
        {
            return Marshal.SizeOf(typeof(RTAPSpring));
        }

        internal static int rtap_alloc_size_for_river()
        {
            return Marshal.SizeOf(typeof(RTAPRiver));
        }

        internal unsafe static void rtap_spring_init(IntPtr _this, IntPtr data_ptr, int data_size, int format, int sample_rate, int block_align)
        {
            if (_this == IntPtr.Zero)
                throw new InvalidOperationException("rtap_spring_init(...) called on a NULL pointer.");

            RTAPSpring* spring = (RTAPSpring*)_this;
            spring->data = data_ptr;
            spring->data_size = data_size;
            spring->format = format;
            spring->sample_rate = sample_rate;
            spring->block_align = block_align;
        }

        internal unsafe static int rtap_spring_get_length(IntPtr _this)
        {
            if (_this == IntPtr.Zero)
                throw new InvalidOperationException("rtap_spring_get_length(...) called on a NULL pointer.");

            RTAPSpring* spring = (RTAPSpring*)_this;
            if ((spring->format & FLAG_ADPCM) == 0)
                return spring->data_size;

            int stereo = ((spring->format & FLAG_STEREO) == 0) ? 0 : 1;

            int full_block_samples = (((spring->block_align >> stereo) - 7) << 1) + 2;
            int partial_block_bytes = (spring->data_size % spring->block_align);
            int partial_block_samples = 0;
            if (partial_block_bytes > 0)
                partial_block_samples = (((partial_block_bytes >> stereo) - 7) << 1) + 2;
            if (partial_block_samples < 2)
                partial_block_samples = 0;

            int total_samples = ((spring->data_size / spring->block_align) * full_block_samples) + partial_block_samples;

            return total_samples * sizeof(short) * (stereo + 1);
        }

        internal unsafe static double rtap_spring_get_duration(IntPtr _this)
        {
            if (_this == IntPtr.Zero)
                throw new InvalidOperationException("rtap_spring_get_duration(...) called on a NULL pointer.");

            RTAPSpring* spring = (RTAPSpring*)_this;

            double divisor = spring->sample_rate;
            if ((spring->format & FLAG_16) != 0)
                divisor *= 2.0f;
            if ((spring->format & FLAG_STEREO) != 0)
                divisor *= 2.0f;

            if (divisor <= 0.00001)
                return 0.0f;

            return ((double)spring->data_size) / divisor;
        }

        internal unsafe static void rtap_river_init(IntPtr _this, IntPtr spring_ptr)
        {
            if (_this == IntPtr.Zero)
                throw new InvalidOperationException("rtap_river_init(...) called on a NULL pointer.");

            rtap_river_set_spring(_this, spring_ptr);
        }

        internal unsafe static void rtap_river_reset(IntPtr _this)
        {
            if (_this == IntPtr.Zero)
                throw new InvalidOperationException("rtap_river_reset(...) called on a NULL pointer.");

            rtap_river_set_spring(_this, IntPtr.Zero);
        }

        internal unsafe static void rtap_river_set_spring(IntPtr _this, IntPtr spring_ptr)
        {
            if (_this == IntPtr.Zero)
                throw new InvalidOperationException("rtap_river_set_spring(...) called on a NULL pointer.");

            byte* memory = (byte*)_this;
            for (int i = 0; i < ALLOC_SIZE_RIVER; ++i)
                memory[i] = (byte)0x0;

            RTAPRiver* river = (RTAPRiver*)_this;
            river->spring = spring_ptr;
            river->read_head = 0;
        }

        internal unsafe static int rtap_river_read_into(IntPtr _this, IntPtr buffer_ptr, int start_idx, int length)
        {
            if (_this == IntPtr.Zero)
            {
                throw new InvalidOperationException("rtap_river_read_into(...) called on a NULL pointer.");
                return -1;
            }

            RTAPRiver* river = (RTAPRiver*)_this;
            if (river->spring == IntPtr.Zero)
            {
                throw new InvalidOperationException("rtap_river_read_into(...) called on a river with a NULL spring pointer.");
                return -1;
            }

            if (length <= 0)
            {
                throw new ArgumentException("rtap_river_read_into(...) called with a length of 0.");
                return -1;
            }

            RTAPSpring* spring = (RTAPSpring*)(river->spring);
            if ((spring->format & FLAG_ADPCM) == 0)
            {
                Buffer.MemoryCopy((void*)(((byte*)spring->data) + start_idx), (void*)buffer_ptr, length, length);
            }
            else
            {
                rtap_river_read_adpcm(river, buffer_ptr, start_idx, length);
            }

            return 0;
        }

        private unsafe static void rtap_river_read_adpcm(RTAPRiver* _this, IntPtr buffer_ptr, int start_idx, int length)
        {
            // This method is only called internally, so we perform no validation
            RTAPSpring* spring = (RTAPSpring*)(_this->spring);
            byte* dest = (byte*)buffer_ptr;
            byte* river_cache = ((byte*)_this) + RIVER_CACHE_OFFSET;

            int dest_size = length;

            int stereo = ((spring->format & FLAG_STEREO) == 0) ? 0 : 1;
            int read_head = _this->read_head;
            int block_align = spring->block_align;
            int data_size = spring->data_size;

            int samples_per_block = (((block_align >> stereo) - 7) << 1) + 2;
            int dbytes_per_block = (samples_per_block << stereo) * sizeof(short);

            int request_block = start_idx / dbytes_per_block;
            int request_offset = start_idx - (request_block * dbytes_per_block);

            int cache_size;

            _this->read_head = request_block * block_align;
            if (request_offset != 0)
            {
                // To-Myuu: Add optimization when requesting the same block that we're currently in

                _this->read_head = request_block * block_align;
                int remaining = rtap_river_read_adpcm_helper(_this, (byte*)(IntPtr.Zero), request_offset);
                read_head = _this->read_head;

                cache_size = _this->cache_size;
                int current_block = read_head / block_align;
                int current_offset = read_head - (current_block * block_align);
                int current_offset_samples = ((current_offset - (7 << stereo)) << 1) + (2 << stereo);
                if (current_offset_samples < 0)
                    current_offset_samples = 0;
                int current_offset_dbytes = current_offset_samples * sizeof(short);
                int read_head_dbytes = (current_block * dbytes_per_block) + current_offset_dbytes;
                int cache_request = read_head_dbytes - start_idx;
                if (cache_request < 0)
                {
                    throw new InvalidOperationException("rtap_river_read_adpcm(...) somehow has a start_idx beyond the read_head.");
                }
                else
                if (cache_request > 0 && cache_request <= cache_size)
                {
                    byte* cache = river_cache + (cache_size - cache_request);
                    for (int i = 0; i < cache_request; ++i)
                        dest[i] = cache[i];

                    dest += cache_request;
                    dest_size -= cache_request;
                }
            }

            if (dest_size <= 0)
                return;

            dest_size = rtap_river_read_adpcm_helper(_this, dest, dest_size);
            if (dest_size <= 0)
                return;

            cache_size = _this->cache_size;
            if (cache_size > 0)
            {
                int cache_request = dest_size;
                if (cache_request > cache_size)
                    cache_request = cache_size;

                byte* cache = river_cache;
                for (int i = 0; i < cache_request; ++i)
                    dest[i] = cache[i];

                dest += cache_request;
                dest_size -= cache_request;
            }

            for (int i = 0; i < dest_size; ++i)
                dest[i] = (byte)0x0;
        }
    }

    public sealed class RtapSpring : IDisposable
    {
        private static readonly int SpringSizeInBytes;

        private bool disposed = false;

        internal IntPtr SpringPtr;
        internal IntPtr DataPtr;

        public readonly int Length;
        public readonly RtapFormat Format;
        public readonly int SampleRate;
        public readonly double Duration;

        static RtapSpring()
        {
            SpringSizeInBytes = RTAP.rtap_alloc_size_for_spring();
        }

        public RtapSpring(byte[] data, RtapFormat format, int sampleRate, int blockAlignment)
        {
            Format = format;
            SampleRate = sampleRate;

            DataPtr = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, DataPtr, data.Length);

            SpringPtr = Marshal.AllocHGlobal(SpringSizeInBytes);
            RTAP.rtap_spring_init(SpringPtr, DataPtr, data.Length, (int)format, sampleRate, blockAlignment);

            Length = RTAP.rtap_spring_get_length(SpringPtr);
            
            Duration = RTAP.rtap_spring_get_duration(SpringPtr);
        }

        ~RtapSpring()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Dispose(bool disposing)
        {
            if (!disposed)
            {
                Marshal.FreeHGlobal(SpringPtr);
                Marshal.FreeHGlobal(DataPtr);

                SpringPtr = IntPtr.Zero;
                DataPtr = IntPtr.Zero;

                disposed = true;
            }
        }
    }

    public sealed class RtapRiver : IDisposable
    {
        private static readonly int RiverSizeInBytes;

        private bool disposed = false;

        public RtapSpring Spring { get; private set; }

        internal IntPtr RiverPtr;

        static RtapRiver()
        {
            RiverSizeInBytes = RTAP.rtap_alloc_size_for_river();
        }

        public RtapRiver()
        {
            RiverPtr = Marshal.AllocHGlobal(RiverSizeInBytes);
            RTAP.rtap_river_reset(RiverPtr);
        }

        ~RtapRiver()
        {
            Dispose(false);
        }

        public void Reset()
        {
            if (RiverPtr == IntPtr.Zero)
                return;

            RTAP.rtap_river_reset(RiverPtr);
        }

        public void SetSpring(RtapSpring spring)
        {
            if (RiverPtr == IntPtr.Zero)
                return;

            RTAP.rtap_river_set_spring(RiverPtr, ((spring == null) ? IntPtr.Zero : spring.SpringPtr));

            Spring = spring;
        }

        public void ReadInto(IntPtr destination, int startIndex, int length)
        {
            if (RiverPtr == IntPtr.Zero)
                throw new InvalidOperationException("River::ReadInto(...) called with RiverPtr == IntPtr.Zero (likely already disposed)");

            RTAP.rtap_river_read_into(RiverPtr, destination, startIndex, length);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Dispose(bool disposing)
        {
            if (!disposed)
            {
                Marshal.FreeHGlobal(RiverPtr);

                RiverPtr = IntPtr.Zero;

                disposed = true;
            }
        }
    }
}
