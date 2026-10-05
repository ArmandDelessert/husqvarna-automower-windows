namespace HusqaCockpit.Core.Api;

/// <summary>Serializes requests and keeps a minimum interval between them (the API allows 1 request/second).</summary>
internal sealed class RequestThrottle(TimeSpan minimumInterval, TimeProvider time) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;

    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wait = _lastRequest + minimumInterval - time.GetUtcNow();
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, time, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            _gate.Release();
            throw;
        }
        return new Lease(this);
    }

    public void Dispose() => _gate.Dispose();

    private void Release()
    {
        _lastRequest = time.GetUtcNow();
        _gate.Release();
    }

    private sealed class Lease(RequestThrottle owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Release();
            }
        }
    }
}
