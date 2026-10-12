using System;
using System.IO;
using System.Text;

namespace Clippy.Core.Services
{
    // 16 kHz, 16-bit mono. Bounds memory even if a device produces data faster than real time.
    public sealed class PcmRecordingBuffer : IDisposable
    {
        public const int MaximumSeconds = 60;
        public const int MaximumBytes = 16000 * 2 * MaximumSeconds;
        private readonly MemoryStream pcm = new();
        public bool Append(byte[] bytes, int count)
        {
            if (count < 0 || count > bytes.Length || count % 2 != 0) throw new ArgumentException("Invalid PCM buffer.");
            var accepted = Math.Min(count, MaximumBytes - (int)pcm.Length);
            pcm.Write(bytes, 0, accepted);
            return pcm.Length == MaximumBytes;
        }
        public byte[] ToWave()
        {
            using var output = new MemoryStream();
            using var writer = new BinaryWriter(output, Encoding.ASCII, true);
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + (int)pcm.Length);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(16000); writer.Write(32000);
            writer.Write((short)2); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write((int)pcm.Length);
            pcm.Position = 0; pcm.CopyTo(output);
            return output.ToArray();
        }
        public void Dispose() => pcm.Dispose();
    }
}
