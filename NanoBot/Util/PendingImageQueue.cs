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
    private readonly ConcurrentQueue<StoredImage> _queue = new();

    public void Enqueue(StoredImage image)
    {
        _queue.Enqueue(image);
        while (_queue.Count > MaxImages && _queue.TryDequeue(out _)) { }
    }

    public bool TryPeek(out StoredImage image) => _queue.TryPeek(out image);

    public void RemoveFirst() => _queue.TryDequeue(out _);

    public StoredImage[] Snapshot() => _queue.ToArray();

    public int Count => _queue.Count;
}
