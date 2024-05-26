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

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct RTAPPond
    {
        public IntPtr data;
        public int data_size;
        public int format;
        public int sample_rate;
        public int block_align;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct RTAPRiver
    {
        public IntPtr pond;
        public int read_head;
        public int block_size;
        public int left_delta;
        public int left_sample1;
        public int left_sample2;
        public int left_coeff1;
        public int left_coeff2;
        public int right_delta;
        public int right_sample1;
        public int right_sample2;
        public int right_coeff1;
        public int right_coeff2;
        public byte sample0;
        public byte sample1;
        public byte sample2;
        public byte sample3;
        public byte sample4;
        public byte sample5;
        public byte sample6;
        public byte sample7;
    }

    internal static class RTAP
    {
        internal const int FLAG_16 = 1;
        internal const int FLAG_STEREO = 1 << 1;
        internal const int FLAG_ADPCM = 1 << 2;

        internal static int ALLOC_SIZE_POND;
        internal static int ALLOC_SIZE_RIVER;

        static RTAP()
        {
            ALLOC_SIZE_POND = rtap_alloc_size_for_pond();
            ALLOC_SIZE_RIVER = rtap_alloc_size_for_river();
        }

        internal static int rtap_alloc_size_for_pond()
        {
            return Marshal.SizeOf(typeof(RTAPPond));
        }

        internal static int rtap_alloc_size_for_river()
        {
            return Marshal.SizeOf(typeof(RTAPRiver));
        }

        internal unsafe static void rtap_pond_init(IntPtr _this, IntPtr data_ptr, int data_size, int format, int sample_rate, int block_align)
        {
            if (_this == IntPtr.Zero)
                throw new InvalidOperationException("rtap_pond_init(...) called on a NULL pointer.");

            RTAPPond* pond = (RTAPPond*)_this;
            pond->data = data_ptr;
            pond->data_size = data_size;
            pond->format = format;
            pond->sample_rate = sample_rate;
            pond->block_align = block_align;
        }

        internal unsafe static int rtap_pond_get_length(IntPtr _this)
        {
            if (_this == IntPtr.Zero)
                throw new InvalidOperationException("rtap_get_length(...) called on a NULL pointer.");

            RTAPPond* pond = (RTAPPond*)_this;
            if ((pond->format & FLAG_ADPCM) == 0)
                return pond->data_size;

            int full_block_samples = ((pond->block_align - 7) << 1) + 2;
            int partial_block_extra = (pond->data_size % pond->block_align);
            int partial_block_samples = 0;
            if (partial_block_extra > 0)
                partial_block_samples = ((partial_block_extra - 7) << 1) + 2;

            int total_samples = ((pond->data_size / pond->block_align) * full_block_samples) + partial_block_samples;

            return total_samples * sizeof(short);
        }

        internal unsafe static void rtap_river_init(IntPtr _this, IntPtr pond_ptr)
        {
            if (_this == IntPtr.Zero)
                throw new InvalidOperationException("rtap_river_init(...) called on a NULL pointer.");

            rtap_river_set_pond(_this, pond_ptr);
        }

        internal unsafe static void rtap_river_reset(IntPtr _this)
        {
            if (_this == IntPtr.Zero)
                throw new InvalidOperationException("rtap_river_reset(...) called on a NULL pointer.");

            rtap_river_set_pond(_this, IntPtr.Zero);
        }

        internal unsafe static void rtap_river_set_pond(IntPtr _this, IntPtr pond_ptr)
        {
            if (_this == IntPtr.Zero)
                throw new InvalidOperationException("rtap_river_set_pond(...) called on a NULL pointer.");

            byte* memory = (byte*)_this;
            for (int i = 0; i < ALLOC_SIZE_RIVER; ++i)
                memory[i] = (byte)0x0;

            RTAPRiver* river = (RTAPRiver*)_this;
            river->pond = pond_ptr;
        }

        internal unsafe static int rtap_river_read_into(IntPtr _this, IntPtr buffer_ptr, int start_idx, int length)
        {
            if (_this == IntPtr.Zero)
            {
                throw new InvalidOperationException("rtap_river_read_into(...) called on a NULL pointer.");
                return -1;
            }

            RTAPRiver* river = (RTAPRiver*)_this;
            if (river->pond == IntPtr.Zero)
            {
                throw new InvalidOperationException("rtap_river_read_into(...) called on a river with a NULL pond pointer.");
                return -1;
            }

            RTAPPond* pond = (RTAPPond*)(river->pond);
            if ((pond->format & FLAG_ADPCM) == 0)
            {
                Buffer.MemoryCopy((void*)(((byte*)pond->data) + start_idx), (void*)buffer_ptr, length, length);
            }
            else
            {
                
            }

            return 0;
        }
    }

    public sealed class RtapPond : IDisposable
    {
        private static readonly int PondSizeInBytes;

        private bool disposed = false;

        internal IntPtr PondPtr;
        internal IntPtr DataPtr;

        public readonly int Length;
        public readonly RtapFormat Format;
        public readonly int SampleRate;
        public readonly double Duration;

        static RtapPond()
        {
            PondSizeInBytes = RTAP.rtap_alloc_size_for_pond();
        }

        public RtapPond(byte[] data, RtapFormat format, int sampleRate, int blockAlignment)
        {
            Format = format;
            SampleRate = sampleRate;

            DataPtr = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, DataPtr, data.Length);

            PondPtr = Marshal.AllocHGlobal(PondSizeInBytes);
            RTAP.rtap_pond_init(PondPtr, DataPtr, data.Length, (int)format, sampleRate, blockAlignment);

            Length = RTAP.rtap_pond_get_length(PondPtr);
            
            Duration = 0.0;
        }

        ~RtapPond()
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
                Marshal.FreeHGlobal(PondPtr);
                Marshal.FreeHGlobal(DataPtr);

                PondPtr = IntPtr.Zero;
                DataPtr = IntPtr.Zero;

                disposed = true;
            }
        }
    }

    public sealed class RtapRiver : IDisposable
    {
        private static readonly int RiverSizeInBytes;

        private bool disposed = false;

        public RtapPond Pond { get; private set; }

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

        public void SetPond(RtapPond pond)
        {
            if (RiverPtr == IntPtr.Zero)
                return;

            RTAP.rtap_river_set_pond(RiverPtr, ((pond is null) ? IntPtr.Zero : pond.PondPtr));

            Pond = pond;
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
