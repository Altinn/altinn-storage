using System.Collections.Generic;

namespace Altinn.Platform.Storage.UnitTest.Fixture;

internal sealed class ConcurrentList<T> : IList<T>, IReadOnlyList<T>
{
    private readonly List<T> _inner;

    public ConcurrentList()
    {
        _inner = new List<T>();
    }

    public T this[int index]
    {
        get
        {
            lock (_inner)
            {
                return _inner[index];
            }
        }
        set
        {
            lock (_inner)
            {
                _inner[index] = value;
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_inner)
            {
                return _inner.Count;
            }
        }
    }

    public bool IsReadOnly
    {
        get
        {
            lock (_inner)
            {
                return ((IList<T>)_inner).IsReadOnly;
            }
        }
    }

    public void Add(T item)
    {
        lock (_inner)
        {
            _inner.Add(item);
        }
    }

    public void Clear()
    {
        lock (_inner)
        {
            _inner.Clear();
        }
    }

    public bool Contains(T item)
    {
        lock (_inner)
        {
            return _inner.Contains(item);
        }
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        lock (_inner)
        {
            _inner.CopyTo(array, arrayIndex);
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        List<T> copy;
        lock (_inner)
        {
            copy = [.. _inner];
        }

        return copy.GetEnumerator();
    }

    public int IndexOf(T item)
    {
        lock (_inner)
        {
            return _inner.IndexOf(item);
        }
    }

    public void Insert(int index, T item)
    {
        lock (_inner)
        {
            _inner.Insert(index, item);
        }
    }

    public bool Remove(T item)
    {
        lock (_inner)
        {
            return _inner.Remove(item);
        }
    }

    public void RemoveAt(int index)
    {
        lock (_inner)
        {
            _inner.RemoveAt(index);
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();
}
