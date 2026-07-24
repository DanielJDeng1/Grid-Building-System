using System.Collections.Generic;

/// <summary>
/// Minimal array-based binary min-heap, used by AStarPathfinder's open set.
/// Supports lazy deletion (duplicate pushes for the same item are tolerated
/// - the caller skips stale pops via its own closed set) rather than a full
/// decrease-key implementation. Simpler to get right, and correct for A*'s
/// specific access pattern (we only ever care about popping the minimum).
/// </summary>
public class BinaryHeap<T>
{
    private readonly List<(T item, float priority)> _items = new();

    public int Count => _items.Count;

    public void Clear() => _items.Clear();

    public void Push(T item, float priority)
    {
        _items.Add((item, priority));
        SiftUp(_items.Count - 1);
    }

    public T Pop()
    {
        var root = _items[0];
        int last = _items.Count - 1;
        _items[0] = _items[last];
        _items.RemoveAt(last);
        if (_items.Count > 0)
            SiftDown(0);
        return root.item;
    }

    private void SiftUp(int index)
    {
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (_items[index].priority >= _items[parent].priority)
                break;
            (_items[index], _items[parent]) = (_items[parent], _items[index]);
            index = parent;
        }
    }

    private void SiftDown(int index)
    {
        int count = _items.Count;
        while (true)
        {
            int left = index * 2 + 1;
            int right = index * 2 + 2;
            int smallest = index;

            if (left < count && _items[left].priority < _items[smallest].priority)
                smallest = left;
            if (right < count && _items[right].priority < _items[smallest].priority)
                smallest = right;

            if (smallest == index)
                break;

            (_items[index], _items[smallest]) = (_items[smallest], _items[index]);
            index = smallest;
        }
    }
}
