using System;
using System.Collections;
using System.Collections.Generic;

namespace KTSG.Server;

public enum BindableListChangeType
{
    Add,
    AddRange,
    Insert,
    InsertRange,
    RemoveAt,
    RemoveRange,
    Clear,
    Sort,
    Reset,
    Replace,
}

public readonly struct BindableListChange<T>
{
    public BindableListChangeType Type { get; }
    public int Index { get; }
    public int ToIndex { get; }
    public int Count { get; }
    public T Item { get; }
    public IReadOnlyList<T>? Items { get; }

    public BindableListChange(
        BindableListChangeType type,
        int index = -1,
        int count = 0,
        T item = default!,
        IReadOnlyList<T>? items = null,
        int toIndex = -1)
    {
        Type = type;
        Index = index;
        Count = count;
        Item = item;
        Items = items;
        ToIndex = toIndex;
    }
}

public sealed class BindableList<T> : IReadOnlyList<T>
{
    private readonly List<T> _items = new();
    private Action<BindableListChange<T>>? _onChanged;

    public BindableList()
    {
    }

    public BindableList(IEnumerable<T> items)
    {
        _items.AddRange(items);
    }

    public int Count => _items.Count;

    public T this[int index]
    {
        get => _items[index];
        set => Replace(index, value);
    }

    public IUnRegister RegisterOnChanged(Action<BindableListChange<T>> action)
    {
        _onChanged += action;
        return PureClassPool.Get<BindableListUnRegister<T>>(true).Init(this, action);
    }

    public void Add(T item)
    {
        _items.Add(item);
        NotifyChanged(new BindableListChange<T>(BindableListChangeType.Add, _items.Count - 1, 1, item));
    }

    public void AddRange(IEnumerable<T> items)
    {
        if (items == null)
        {
            return;
        }

        List<T> addedItems = new(items);
        if (addedItems.Count == 0)
        {
            return;
        }

        int startIndex = _items.Count;
        _items.AddRange(addedItems);
        NotifyChanged(new BindableListChange<T>(BindableListChangeType.AddRange, startIndex, addedItems.Count, default!, addedItems));
    }

    public void Insert(int index, T item)
    {
        if (index < 0 || index > _items.Count)
        {
            return;
        }

        _items.Insert(index, item);
        NotifyChanged(new BindableListChange<T>(BindableListChangeType.Insert, index, 1, item));
    }

    public void InsertRange(int index, IEnumerable<T> items)
    {
        if (index < 0 || index > _items.Count || items == null)
        {
            return;
        }

        List<T> insertedItems = new(items);
        if (insertedItems.Count == 0)
        {
            return;
        }

        _items.InsertRange(index, insertedItems);
        NotifyChanged(new BindableListChange<T>(BindableListChangeType.InsertRange, index, insertedItems.Count, default!, insertedItems));
    }

    public bool Remove(T item)
    {
        int index = _items.IndexOf(item);
        if (index < 0)
        {
            return false;
        }

        RemoveAt(index);
        return true;
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            return;
        }

        T removedItem = _items[index];
        _items.RemoveAt(index);
        NotifyChanged(new BindableListChange<T>(BindableListChangeType.RemoveAt, index, 1, removedItem));
    }

    public void RemoveRange(int index, int count)
    {
        if (index < 0 || count <= 0 || index + count > _items.Count)
        {
            return;
        }

        List<T> removedItems = _items.GetRange(index, count);
        _items.RemoveRange(index, count);
        NotifyChanged(new BindableListChange<T>(BindableListChangeType.RemoveRange, index, count, default!, removedItems));
    }

    public void Clear()
    {
        if (_items.Count == 0)
        {
            return;
        }

        int removedCount = _items.Count;
        _items.Clear();
        NotifyChanged(new BindableListChange<T>(BindableListChangeType.Clear, 0, removedCount));
    }

    public void Sort(Comparison<T> comparison)
    {
        if (comparison == null || _items.Count <= 1)
        {
            return;
        }

        _items.Sort(comparison);
        NotifyChanged(new BindableListChange<T>(BindableListChangeType.Sort, 0, _items.Count));
    }

    public void Reset(IEnumerable<T>? items)
    {
        _items.Clear();

        List<T> resetItems = new();
        if (items != null)
        {
            resetItems.AddRange(items);
            _items.AddRange(resetItems);
        }

        NotifyChanged(new BindableListChange<T>(BindableListChangeType.Reset, 0, _items.Count, default!, resetItems));
    }

    public void Replace(int index, T item)
    {
        if (index < 0 || index >= _items.Count)
        {
            return;
        }

        _items[index] = item;
        NotifyChanged(new BindableListChange<T>(BindableListChangeType.Replace, index, 1, item));
    }

    public IEnumerator<T> GetEnumerator()
    {
        return _items.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private void NotifyChanged(BindableListChange<T> change)
    {
        _onChanged?.Invoke(change);
    }

    public void UnRegisterOnChanged(Action<BindableListChange<T>> action)
    {
        _onChanged -= action;
    }
}

public sealed class BindableListUnRegister<T> : IUnRegister
{
    private BindableList<T>? _bindableList;
    private Action<BindableListChange<T>>? _unregisterAction;
    private bool _recycled;

    public BindableListUnRegister<T> Init(BindableList<T> bindableList, Action<BindableListChange<T>> action)
    {
        _recycled = false;
        _bindableList = bindableList;
        _unregisterAction = action;
        return this;
    }

    public void UnRegister()
    {
        if (_recycled)
        {
            return;
        }

        _recycled = true;
        if (_bindableList != null && _unregisterAction != null)
        {
            _bindableList.UnRegisterOnChanged(_unregisterAction);
        }

        _bindableList = null;
        _unregisterAction = null;
        PureClassPool.Return(this);
    }
}
