using System;
using System.IO;
using System.Runtime.InteropServices;
using Hoodrich.Core;

namespace Hoodrich.Voice
{
    /// <summary>
    /// One voice line, played out of Windows, with nothing in between.
    ///
    /// winmm.dll ships with the operating system, so this adds no dependency of any kind --
    /// which is the whole reason it is done the hard way. Every managed audio library that
    /// would make this ten lines shorter is a DLL in the player's scripts folder that can lose
    /// a version fight with somebody else's mod, and this project has never put one there.
    ///
    /// ONE SOUND AT A TIME, deliberately. Dialogue is turn-taking by nature -- two characters
    /// talking over each other is a bug, not a feature -- and a single device with a single
    /// buffer is far easier to prove correct than a mixer. It also means the volume call can
    /// be used for panning, since it applies to the whole device and the whole device is this
    /// one line.
    ///
    /// Nothing here throws. A voice layer that can take the script down with it is worse than
    /// no voice layer, and every entry point is wrapped for that reason.
    /// </summary>
    internal static class WaveOut
    {
        // ---- the parts of winmm we need ----------------------------------------

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct WaveFormatEx
        {
            public short FormatTag;
            public short Channels;
            public int SamplesPerSec;
            public int AvgBytesPerSec;
            public short BlockAlign;
            public short BitsPerSample;
            public short Size;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveHdr
        {
            public IntPtr Data;
            public int BufferLength;
            public int BytesRecorded;
            public IntPtr User;
            public int Flags;
            public int Loops;
            public IntPtr Next;
            public IntPtr Reserved;
        }

        private const int WhdrDone = 0x00000001;
        private const int WaveMapper = -1;

        [DllImport("winmm.dll")]
        private static extern int waveOutOpen(out IntPtr h, int device, ref WaveFormatEx fmt,
                                              IntPtr callback, IntPtr instance, int flags);

        [DllImport("winmm.dll")]
        private static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);

        [DllImport("winmm.dll")]
        private static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);

        [DllImport("winmm.dll")]
        private static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, int size);

        [DllImport("winmm.dll")]
        private static extern int waveOutReset(IntPtr h);

        [DllImport("winmm.dll")]
        private static extern int waveOutClose(IntPtr h);

        [DllImport("winmm.dll")]
        private static extern int waveOutSetVolume(IntPtr h, uint volume);

        // ---- what is open right now ---------------------------------------------

        private static IntPtr _device;
        private static IntPtr _header;
        private static IntPtr _buffer;

        private static int _openRate;
        private static short _openChannels;

        /// <summary>Whether a line is still coming out of the speakers.</summary>
        public static bool Playing { get; private set; }

        /// <summary>
        /// Plays one clip, replacing whatever was already going.
        ///
        /// The device is kept open BETWEEN lines when the format matches, because opening and
        /// closing it per line is the expensive part and dialogue arrives in bursts -- a
        /// conversation is a dozen lines in the same voice at the same rate.
        /// </summary>
        public static bool Play(byte[] pcm, int rate, short channels, short bits)
        {
            if (pcm == null || pcm.Length == 0) return false;
            if (bits != 16) return false;              // see Read: this reads 16-bit only

            try
            {
                Stop();

                if (_device == IntPtr.Zero || _openRate != rate || _openChannels != channels)
                {
                    Shutdown();

                    var fmt = new WaveFormatEx
                    {
                        FormatTag = 1,                 // WAVE_FORMAT_PCM
                        Channels = channels,
                        SamplesPerSec = rate,
                        BitsPerSample = bits,
                        BlockAlign = (short)(channels * bits / 8),
                        Size = 0
                    };
                    fmt.AvgBytesPerSec = rate * fmt.BlockAlign;

                    if (waveOutOpen(out _device, WaveMapper, ref fmt,
                                    IntPtr.Zero, IntPtr.Zero, 0) != 0)
                    {
                        _device = IntPtr.Zero;
                        return false;
                    }

                    _openRate = rate;
                    _openChannels = channels;
                }

                _buffer = Marshal.AllocHGlobal(pcm.Length);
                Marshal.Copy(pcm, 0, _buffer, pcm.Length);

                var hdr = new WaveHdr { Data = _buffer, BufferLength = pcm.Length };

                _header = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WaveHdr)));
                Marshal.StructureToPtr(hdr, _header, false);

                var size = Marshal.SizeOf(typeof(WaveHdr));

                if (waveOutPrepareHeader(_device, _header, size) != 0) { Release(); return false; }
                if (waveOutWrite(_device, _header, size) != 0)
                {
                    waveOutUnprepareHeader(_device, _header, size);
                    Release();
                    return false;
                }

                Playing = true;
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Voice: could not play a clip: " + ex.Message);
                Release();
                return false;
            }
        }

        /// <summary>
        /// Frees the buffer once Windows has finished with it.
        ///
        /// Called every frame while something is playing, and this is not optional bookkeeping:
        /// unpreparing a header before the device has finished with it FAILS, and never
        /// unpreparing it at all leaks the buffer for the life of the process. Polling the DONE
        /// flag is the callback-free way to know, and callback-free is what a script wants --
        /// a delegate handed to unmanaged code has to outlive a hot reload, and this one would
        /// not.
        /// </summary>
        public static void Poll()
        {
            if (!Playing || _header == IntPtr.Zero) return;

            try
            {
                var hdr = (WaveHdr)Marshal.PtrToStructure(_header, typeof(WaveHdr));
                if ((hdr.Flags & WhdrDone) == 0) return;

                waveOutUnprepareHeader(_device, _header, Marshal.SizeOf(typeof(WaveHdr)));
                Release();
            }
            catch (Exception ex)
            {
                Log.Debug("Voice: poll failed: " + ex.Message);
                Release();
            }
        }

        /// <summary>Cuts whatever is playing. The device stays open for the next line.</summary>
        public static void Stop()
        {
            if (_device == IntPtr.Zero) return;

            try
            {
                waveOutReset(_device);

                if (_header != IntPtr.Zero)
                {
                    waveOutUnprepareHeader(_device, _header, Marshal.SizeOf(typeof(WaveHdr)));
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Voice: stop failed: " + ex.Message);
            }

            Release();
        }

        /// <summary>
        /// Left and right, each 0 to 1. Volume and pan in the one call.
        ///
        /// The low word is the left channel and the high word the right, which is what makes
        /// this both the volume control and the panner. It applies to the whole device, and
        /// the whole device is one line of dialogue, so that is exactly the scope wanted.
        /// </summary>
        public static void Volume(float left, float right)
        {
            if (_device == IntPtr.Zero) return;

            try
            {
                var l = (uint)(Math.Max(0f, Math.Min(1f, left)) * 0xFFFF);
                var r = (uint)(Math.Max(0f, Math.Min(1f, right)) * 0xFFFF);

                waveOutSetVolume(_device, (r << 16) | l);
            }
            catch
            {
                // A line at the wrong volume still says the words.
            }
        }

        /// <summary>
        /// Closes the device and lets go of everything.
        ///
        /// THIS MUST RUN ON UNLOAD. An open device with a prepared header survives a script
        /// reload -- the managed side goes away and the handle does not -- and after a few
        /// reloads there are no free devices left and audio stops working across the whole
        /// game, with nothing in any log to say why.
        /// </summary>
        public static void Shutdown()
        {
            Stop();

            if (_device == IntPtr.Zero) return;

            try { waveOutClose(_device); }
            catch (Exception ex) { Log.Debug("Voice: close failed: " + ex.Message); }

            _device = IntPtr.Zero;
            _openRate = 0;
            _openChannels = 0;
        }

        private static void Release()
        {
            if (_header != IntPtr.Zero) { Marshal.FreeHGlobal(_header); _header = IntPtr.Zero; }
            if (_buffer != IntPtr.Zero) { Marshal.FreeHGlobal(_buffer); _buffer = IntPtr.Zero; }

            Playing = false;
        }

        // ---- reading the file ----------------------------------------------------

        /// <summary>
        /// A RIFF/WAVE file, far enough to find the samples.
        ///
        /// Walks the chunk list rather than assuming the data starts at byte 44. Plenty of
        /// tools write a LIST or fact chunk before it, and everything that assumes a fixed
        /// offset plays those files as a burst of noise.
        ///
        /// 16-bit PCM only, which is what to export. Anything else is reported and skipped
        /// rather than guessed at.
        /// </summary>
        public static bool Read(string path, out byte[] pcm, out int rate,
                                out short channels, out short bits)
        {
            pcm = null; rate = 0; channels = 0; bits = 0;

            try
            {
                var raw = File.ReadAllBytes(path);
                if (raw.Length < 12) return false;

                if (raw[0] != 'R' || raw[1] != 'I' || raw[2] != 'F' || raw[3] != 'F') return false;
                if (raw[8] != 'W' || raw[9] != 'A' || raw[10] != 'V' || raw[11] != 'E') return false;

                var i = 12;
                var gotFmt = false;

                while (i + 8 <= raw.Length)
                {
                    var id = new string(new[] { (char)raw[i], (char)raw[i + 1],
                                                (char)raw[i + 2], (char)raw[i + 3] });
                    var size = BitConverter.ToInt32(raw, i + 4);
                    var body = i + 8;

                    if (size < 0 || body + size > raw.Length) size = raw.Length - body;

                    if (id == "fmt ")
                    {
                        if (size < 16) return false;

                        var tag = BitConverter.ToInt16(raw, body);
                        channels = BitConverter.ToInt16(raw, body + 2);
                        rate = BitConverter.ToInt32(raw, body + 4);
                        bits = BitConverter.ToInt16(raw, body + 14);

                        // 1 is PCM, 0xFFFE is WAVE_FORMAT_EXTENSIBLE, which for 16-bit
                        // integer samples is the same bytes with a longer header.
                        if (tag != 1 && tag != unchecked((short)0xFFFE)) return false;

                        gotFmt = true;
                    }
                    else if (id == "data" && gotFmt)
                    {
                        pcm = new byte[size];
                        Buffer.BlockCopy(raw, body, pcm, 0, size);
                        return bits == 16 && channels > 0 && rate > 0;
                    }

                    // Chunks are word-aligned; an odd size is followed by a pad byte.
                    i = body + size + (size & 1);
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Debug("Voice: could not read " + path + ": " + ex.Message);
                return false;
            }
        }
    }
}
