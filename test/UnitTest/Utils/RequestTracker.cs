using System.Collections.Generic;
using System.Linq;

namespace Altinn.Platform.Storage.UnitTest.Utils;

public static class RequestTracker
{
    private static Dictionary<string, List<object>> _tracker =
        new Dictionary<string, List<object>>();

    private static readonly object DataLock = new object();

    public static int GetRequestCount(string requestKey)
    {
        if (_tracker.TryGetValue(requestKey, out List<object> value))
        {
            return value.Count;
        }

        return 0;
    }

    public static void AddRequest(string requestKey, object request)
    {
        lock (DataLock)
        {
            if (!_tracker.TryGetValue(requestKey, out List<object> value))
            {
                value = new List<object>();
                _tracker.Add(requestKey, value);
            }

            value.Add(request);
        }
    }

    public static void Clear()
    {
        lock (DataLock)
        {
            _tracker = new Dictionary<string, List<object>>();
        }
    }
}
