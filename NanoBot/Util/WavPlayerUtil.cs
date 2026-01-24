#if false
using Pv;

namespace NanoBot.Util;

public static class WavPlayerUtil
{
    private class WavFileInfo
    {
        public int SampleRate { get; init; }
        public int BitsPerSample { get; init; }
        public long DataOffset { get; init; }
    }

    private static WavFileInfo ParseWavHeader(BinaryReader binaryReader)
    {
        var chunkId = new string(binaryReader.ReadChars(4));
        if (chunkId != "RIFF")
            throw new InvalidDataException("Not a valid WAV file.");

        binaryReader.BaseStream.Seek(4, SeekOrigin.Current);
        var format = new string(binaryReader.ReadChars(4));
        if (format != "WAVE")
            throw new InvalidDataException("Not a valid WAV file.");

        int sampleRate = 0;
        int bitsPerSample = 0;
        long dataOffset = 0;

        while (binaryReader.BaseStream.Position < binaryReader.BaseStream.Length)
        {
            var subChunkId = new string(binaryReader.ReadChars(4));
            var subChunkSize = binaryReader.ReadInt32();

            if (subChunkId == "fmt ")
            {
                var audioFormat = binaryReader.ReadInt16();
                var channels = binaryReader.ReadInt16();
                sampleRate = binaryReader.ReadInt32();
                var byteRate = binaryReader.ReadInt32();
                var blockAlign = binaryReader.ReadInt16();
                bitsPerSample = binaryReader.ReadInt16();

                if (channels != 1)
                    throw new InvalidDataException("WAV file must have a single channel (MONO)");

                if (subChunkSize > 16)
                    binaryReader.BaseStream.Seek(subChunkSize - 16, SeekOrigin.Current);
            }
            else if (subChunkId == "data")
            {
                dataOffset = binaryReader.BaseStream.Position;
                break;
            }
            else
            {
                binaryReader.BaseStream.Seek(subChunkSize, SeekOrigin.Current);
            }
        }

        if (dataOffset == 0)
            throw new InvalidDataException("No data chunk found in WAV file.");

        return new WavFileInfo
        {
            SampleRate = sampleRate,
            BitsPerSample = bitsPerSample,
            DataOffset = dataOffset,
        };
    }

    private static WavFileInfo GetWavFileInfo(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty.");

        if (!File.Exists(filePath))
            throw new FileNotFoundException("The specified file was not found.");

        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var binaryReader = new BinaryReader(fileStream);

        return ParseWavHeader(binaryReader);
    }

    public static async Task PlayAsync(string inputFilePath, CancellationToken cancellationToken = default)
    {
        var wavInfo = GetWavFileInfo(inputFilePath);

        using var speaker = new PvSpeaker(sampleRate: wavInfo.SampleRate, bitsPerSample: wavInfo.BitsPerSample, bufferSizeSecs: 60);

        await using var registration = cancellationToken.Register(() => speaker.Stop());

        speaker.Start();

        await using var fileStream = new FileStream(inputFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        fileStream.Seek(wavInfo.DataOffset, SeekOrigin.Begin);

        byte[] buffer = new byte[16384];
        int bytesRead;
        while (!cancellationToken.IsCancellationRequested &&
               (bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            byte[] chunk = new byte[bytesRead];
            Array.Copy(buffer, chunk, bytesRead);
            speaker.Write(chunk);
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            speaker.Flush();
        }
    }

    public static async Task PlayAsync(byte[] wavData, CancellationToken cancellationToken = default)
    {
        using var memoryStream = new MemoryStream(wavData);
        using var binaryReader = new BinaryReader(memoryStream);

        var wavInfo = ParseWavHeader(binaryReader);

        using var speaker = new PvSpeaker(sampleRate: wavInfo.SampleRate, bitsPerSample: wavInfo.BitsPerSample, bufferSizeSecs: 60);

        await using var registration = cancellationToken.Register(() => speaker.Stop());

        speaker.Start();

        memoryStream.Seek(wavInfo.DataOffset, SeekOrigin.Begin);

        byte[] buffer = new byte[16384];
        int bytesRead;
        while (!cancellationToken.IsCancellationRequested &&
               (bytesRead = await memoryStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            byte[] chunk = new byte[bytesRead];
            Array.Copy(buffer, chunk, bytesRead);
            speaker.Write(chunk);
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            speaker.Flush();
        }
    }
}
#endif