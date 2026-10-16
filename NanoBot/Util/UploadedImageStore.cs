namespace NanoBot.Util;

/// <summary>An uploaded image and the short label the model uses to refer to it (e.g. "img-2").</summary>
public sealed record StoredImage(string Label, byte[] Data, string MimeType);

/// <summary>
/// Holds the images a user has uploaded during the current realtime session so tools (e.g. PowerAI)
/// can fetch them by label. The realtime model is told each image's label when the image is sent.
/// </summary>
public interface IUploadedImageStore
{
    /// <summary>Stores an image and assigns it a new label. When full, the oldest image is dropped.</summary>
    StoredImage Add(byte[] data, string mimeType);

    bool TryGet(string label, out StoredImage image);

    /// <summary>Labels currently available, oldest first.</summary>
    IReadOnlyList<string> Labels();

    /// <summary>Drops every image except those in <paramref name="keep"/> (images not yet delivered to a session).</summary>
    void Clear(IEnumerable<StoredImage> keep = null);
}

public sealed class UploadedImageStore : IUploadedImageStore
{
    private const int MaxImages = 5;

    private readonly object _lock = new();
    private readonly List<StoredImage> _images = new();

    // Never reset, so a label is not reused after a session ends and a stale reference can't alias a newer image.
    private int _nextNumber = 1;

    public StoredImage Add(byte[] data, string mimeType)
    {
        lock (_lock)
        {
            var image = new StoredImage($"img-{_nextNumber++}", data, mimeType);
            _images.Add(image);
            while (_images.Count > MaxImages)
                _images.RemoveAt(0);
            return image;
        }
    }

    public bool TryGet(string label, out StoredImage image)
    {
        label = label?.Trim();
        lock (_lock)
        {
            image = _images.FirstOrDefault(i => string.Equals(i.Label, label, StringComparison.OrdinalIgnoreCase));
            return image != null;
        }
    }

    public IReadOnlyList<string> Labels()
    {
        lock (_lock)
            return _images.Select(i => i.Label).ToList();
    }

    public void Clear(IEnumerable<StoredImage> keep = null)
    {
        var keepSet = keep?.ToHashSet() ?? new HashSet<StoredImage>();
        lock (_lock)
            _images.RemoveAll(i => !keepSet.Contains(i));
    }
}
