using System.Collections;

namespace LaptopThermalHelper.Core.Collections;

public sealed class FixedRingBuffer<T> : IReadOnlyList<T>
{
    private readonly T[] _items;
    private int _start;

    public FixedRingBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "容量必须大于零。");
        }

        _items = new T[capacity];
    }

    public int Capacity => _items.Length;

    public int Count { get; private set; }

    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _items[(_start + index) % Capacity];
        }
    }

    public void Add(T item)
    {
        int index = (_start + Count) % Capacity;
        if (Count == Capacity)
        {
            _items[_start] = item;
            _start = (_start + 1) % Capacity;
            return;
        }

        _items[index] = item;
        Count++;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (int index = 0; index < Count; index++)
        {
            yield return _items[(_start + index) % Capacity];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
