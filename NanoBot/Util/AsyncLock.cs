namespace NanoBot.Util;

internal class AsyncLock
{
    private readonly SemaphoreSlim _semaphore;
    private readonly Task<Releaser> _releaser;

    public AsyncLock()
    {
        _semaphore = new SemaphoreSlim(1);
        _releaser = Task.FromResult(new Releaser(this));
    }

    public Task<Releaser> LockAsync()
    {
        var wait = _semaphore.WaitAsync();
        return wait.IsCompleted ?
            _releaser :
            wait.ContinueWith((_, state) => new Releaser((AsyncLock)state),
                this, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public Releaser Lock()
    {
        _semaphore.Wait(); // Wait synchronously
        return new Releaser(this);
    }

    internal struct Releaser : IDisposable
    {
        private readonly AsyncLock _mutex;

        internal Releaser(AsyncLock mutex) { _mutex = mutex; }

        public void Dispose()
        {
            if (_mutex != null)
                _mutex._semaphore.Release();
        }
    }
}