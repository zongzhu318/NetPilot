namespace NetPilot.Core.Logging;

public sealed class RingBuffer<T>
{
    private readonly T[] _items;
    private int _start;
    private int _count;

    public RingBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _items = new T[capacity];
    }

    public int Capacity => _items.Length;
    public int Count => _count;

    public void Add(T item)
    {
        if (_count < _items.Length)
        {
            _items[(_start + _count) % _items.Length] = item;
            _count++;
            return;
        }

        _items[_start] = item;
        _start = (_start + 1) % _items.Length;
    }

    public IReadOnlyList<T> Snapshot()
    {
        var result = new T[_count];
        for (var i = 0; i < _count; i++)
        {
            result[i] = _items[(_start + i) % _items.Length];
        }

        return result;
    }

    public void Clear()
    {
        Array.Clear(_items);
        _start = 0;
        _count = 0;
    }
}
