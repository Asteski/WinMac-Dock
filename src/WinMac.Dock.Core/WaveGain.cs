using System.Buffers.Binary;

namespace WinMac.Dock.Core;

public static class WaveGain
{
    // The Recycle Bin WAVs use 16-bit PCM. Change samples rather than
    // endpoint volume, allowing gain above 100% without changing other apps.
    public static byte[] Apply(byte[] wave, int percent)
    {
        var result = (byte[])wave.Clone();
        if (result.Length < 12 || !result.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            || !result.AsSpan(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("Invalid WAV file.");
        bool pcm16 = false;
        for (int offset = 12; offset <= result.Length - 8;)
        {
            int size = BinaryPrimitives.ReadInt32LittleEndian(result.AsSpan(offset + 4, 4));
            int start = offset + 8;
            if (size < 0 || size > result.Length - start) throw new InvalidDataException("Invalid WAV chunk.");
            if (result.AsSpan(offset, 4).SequenceEqual("fmt "u8))
                pcm16 = size >= 16 && BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(start, 2)) == 1
                    && BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(start + 14, 2)) == 16;
            if (result.AsSpan(offset, 4).SequenceEqual("data"u8))
            {
                if (!pcm16) throw new InvalidDataException("Recycle Bin sound must use 16-bit PCM WAV audio.");
                double gain = Math.Clamp(percent, 0, 600) / 100d;
                for (int i = start; i + 1 < start + size; i += 2)
                {
                    var sample = BinaryPrimitives.ReadInt16LittleEndian(result.AsSpan(i, 2));
                    var amplified = (short)Math.Clamp(Math.Round(sample * gain), short.MinValue, short.MaxValue);
                    BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(i, 2), amplified);
                }
                return result;
            }
            if (size == result.Length - start) break;
            offset = start + size + (size & 1);
        }
        throw new InvalidDataException("WAV audio data was not found.");
    }
}
