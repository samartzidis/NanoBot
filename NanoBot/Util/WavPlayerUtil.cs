using Pv;

namespace NanoBot.Util;

public static class WavPlayerUtil
{
    private class WavFileInfo
    {
        public int SampleRate { get; init; }
        public int BitsPerSample { get; init; }
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

        var formatChunkId = new string(binaryReader.ReadChars(4));
        if (formatChunkId != "fmt ")
            throw new InvalidDataException("Invalid WAV file format.");

        var formatChunkSize = binaryReader.ReadInt32();
        var audioFormat = binaryReader.ReadInt16();
        var channels = binaryReader.ReadInt16();
        var sampleRate = binaryReader.ReadInt32();
        var byteRate = binaryReader.ReadInt32();
        var blockAlign = binaryReader.ReadInt16();
        var bitsPerSample = binaryReader.ReadInt16();

        if (formatChunkSize > 16)
            binaryReader.BaseStream.Seek(formatChunkSize - 16, SeekOrigin.Current);

        if (channels != 1)
            throw new InvalidDataException("WAV file must have a single channel (MONO)");

        var dataChunkId = new string(binaryReader.ReadChars(4));
        if (dataChunkId != "data")
            throw new InvalidDataException("Invalid WAV file data chunk.");

        return new WavFileInfo
        {
            SampleRate = sampleRate,
            BitsPerSample = bitsPerSample,
        };
    }

    public static async Task PlayAsync(string inputFilePath, CancellationToken cancellationToken = default)
    {
        var wavInfo = GetWavFileInfo(inputFilePath);

        using var speaker = new PvSpeaker(sampleRate: wavInfo.SampleRate, bitsPerSample: wavInfo.BitsPerSample);
        speaker.Start();

        await using var fileStream = new FileStream(inputFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(fileStream);

        // Skip RIFF header
        reader.BaseStream.Seek(12, SeekOrigin.Begin);

        while (reader.BaseStream.Position < reader.BaseStream.Length && !cancellationToken.IsCancellationRequested)
        {
            var subChunkId = new string(reader.ReadChars(4));
            var subChunkSize = reader.ReadInt32();

            if (subChunkId == "data")
            {
                // Stream audio data until EOF or cancellation
                var buffer = new byte[512]; // Adjust buffer size as needed
                int bytesRead;
                while ((bytesRead = await reader.BaseStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    speaker.Write(buffer.Take(bytesRead).ToArray());
                }
                break;
            }
            else
            {
                // Skip other chunks
                reader.BaseStream.Seek(subChunkSize, SeekOrigin.Current);
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            speaker.Stop();
        }
        else
        {
            speaker.Flush();
        }
    }
}
