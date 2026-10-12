using System.Collections.Concurrent;

namespace NanoBot.Util;

/// <summary>
/// Bounded, thread-safe buffer for images uploaded while no live realtime session can take them.
/// Owned by the service so it survives agent rebuilds; drained by the agent once a session is up.
/// When full, the oldest image is dropped.
/// </summary>
public class PendingImageQueue
{
    private const int MaxImages = 3;
    private readonly ConcurrentQueue<(byte[] Data, string MimeType)> _queue = new();

    public void Enqueue(byte[] data, string mimeType)
    {
        _queue.Enqueue((data, mimeType));
        while (_queue.Count > MaxImages && _queue.TryDequeue(out _)) { }
    }

    public bool TryPeek(out (byte[] Data, string MimeType) image) => _queue.TryPeek(out image);

    public void RemoveFirst() => _queue.TryDequeue(out _);

    public int Count => _queue.Count;
}
