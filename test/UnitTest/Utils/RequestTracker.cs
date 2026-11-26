using System.Collections.Generic;

namespace Altinn.Platform.Storage.UnitTest.Utils;

/// <summary>
/// Counts the requests that mocks receive, per test host. Every host built by
/// <see cref="Fixture.TestApplicationFactory{TEntryPoint}"/> has its own tracker, so tests running
/// in parallel don't count each other's requests.
/// </summary>
public sealed class RequestTracker
{
    private readonly Dictionary<string, List<object>> _tracker = [];

    private readonly object _dataLock = new();

    /// <summary>
    /// Gets the number of requests recorded for the key.
    /// </summary>
    public int GetRequestCount(string requestKey)
    {
        lock (_dataLock)
        {
            return _tracker.TryGetValue(requestKey, out List<object>? value) ? value.Count : 0;
        }
    }

    /// <summary>
    /// Records a request for the key.
    /// </summary>
    public void AddRequest(string requestKey, object request)
    {
        lock (_dataLock)
        {
            if (!_tracker.TryGetValue(requestKey, out List<object>? value))
            {
                value = [];
                _tracker.Add(requestKey, value);
            }

            value.Add(request);
        }
    }

    /// <summary>
    /// Removes all recorded requests.
    /// </summary>
    public void Clear()
    {
        lock (_dataLock)
        {
            _tracker.Clear();
        }
    }
}
