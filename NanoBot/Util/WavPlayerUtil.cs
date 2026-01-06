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

    private static WavFileInfo GetWavFileInfo(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("File path cannot be null or empty.");
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The specified file was not found.");
        }

        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var binaryReader = new BinaryReader(fileStream);

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

        // Parse all chunks
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

                // Skip any extra format bytes
                if (subChunkSize > 16)
                    binaryReader.BaseStream.Seek(subChunkSize - 16, SeekOrigin.Current);
            }
            else if (subChunkId == "data")
            {
                dataOffset = binaryReader.BaseStream.Position;
                break; // Found data chunk, we're done
            }
            else
            {
                // Skip unknown chunks
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

    public static async Task PlayAsync(string inputFilePath, CancellationToken cancellationToken = default)
    {
        var wavInfo = GetWavFileInfo(inputFilePath);

        using var speaker = new PvSpeaker(sampleRate: wavInfo.SampleRate, bitsPerSample: wavInfo.BitsPerSample, bufferSizeSecs: 60);

        await using var registration = cancellationToken.Register(() => speaker.Stop());

        speaker.Start();

        await using var fileStream = new FileStream(inputFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        // Seek directly to the audio data
        fileStream.Seek(wavInfo.DataOffset, SeekOrigin.Begin);

        // Stream audio data in chunks until end of file
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
}