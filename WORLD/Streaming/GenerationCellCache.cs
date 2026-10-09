// Keeps completed generation decisions while protecting cells used by loaded chunks.
// Only unprotected records can be evicted; empty decisions are valid cache records.
using Godot;
using System;
using System.Collections.Generic;

public sealed class GenerationLease : IDisposable
{
    #region State
    private Action _release;
    #endregion

    #region Lifecycle
    // =========================================================
    // Store one release action shared by ordinary scope and node-lifetime leases.
    public GenerationLease(Action release)
    {
        _release = release;
    }

    // =========================================================
    // Release once, even if multiple cleanup paths dispose the same lease.
    public void Dispose()
    {
        Action release = _release;
        _release = null;
        release?.Invoke();
    }
    #endregion
}

public sealed class GenerationCellCache<T> where T : class
{
    #region Records
    private sealed class Record
    {
        public T Value;
        public LinkedListNode<Vector2I> Eviction;
    }
    #endregion

    #region State
    private readonly int _target;
    private readonly Action<T> _removed;
    private readonly Dictionary<Vector2I, Record> _records = new();
    private readonly Dictionary<Vector2I, int> _pins = new();
    private readonly LinkedList<Vector2I> _evictable = new();
    #endregion

    #region Construction
    // =========================================================
    // Set the target for unused data; live working sets take priority over eviction.
    public GenerationCellCache(int target, Action<T> removed)
    {
        _target = Math.Max(1, target);
        _removed = removed;
    }
    #endregion

    #region Access
    // =========================================================
    // Distinguish a completed empty decision from an unchecked cell.
    public bool TryGetValue(Vector2I cell, out T value)
    {
        if (_records.TryGetValue(cell, out Record record))
        {
            value = record.Value;
            return true;
        }

        value = null;
        return false;
    }

    // =========================================================
    // Check whether a cell has a completed generation decision.
    public bool ContainsKey(Vector2I cell)
    {
        return _records.ContainsKey(cell);
    }

    // =========================================================
    // Publish a completed decision without replacing an existing live record.
    public bool TryAdd(Vector2I cell, T value)
    {
        if (_records.ContainsKey(cell)) return false;

        Record record = new() { Value = value };
        _records.Add(cell, record);

        if (!_pins.ContainsKey(cell))
            record.Eviction = _evictable.AddLast(cell);

        Trim();
        return true;
    }
    #endregion

    #region Protection
    // =========================================================
    // Protect an inclusive cell rectangle, including cells not generated yet.
    public IDisposable Pin(Vector2I first, Vector2I last)
    {
        List<Vector2I> cells = new();

        for (int y = first.Y; y <= last.Y; y++)
        for (int x = first.X; x <= last.X; x++)
        {
            Vector2I cell = new(x, y);
            cells.Add(cell);

            _pins.TryGetValue(cell, out int count);
            _pins[cell] = count + 1;

            if (_records.TryGetValue(cell, out Record record) &&
                record.Eviction != null)
            {
                _evictable.Remove(record.Eviction);
                record.Eviction = null;
            }
        }

        return new GenerationLease(() =>
        {
            foreach (Vector2I cell in cells)
            {
                int count = _pins[cell] - 1;

                if (count > 0)
                {
                    _pins[cell] = count;
                    continue;
                }

                _pins.Remove(cell);

                if (_records.TryGetValue(cell, out Record record))
                    record.Eviction = _evictable.AddLast(cell);
            }

            Trim();
        });
    }

    // =========================================================
    // Evict unused records without scanning or deleting protected records.
    private void Trim()
    {
        while (_records.Count > _target && _evictable.First != null)
        {
            Vector2I cell = _evictable.First.Value;
            _evictable.RemoveFirst();

            Record record = _records[cell];
            _records.Remove(cell);
            _removed?.Invoke(record.Value);
        }
    }
    #endregion
}