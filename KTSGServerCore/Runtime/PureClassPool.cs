using System;
using System.Collections.Generic;

namespace KTSG.Server;

public interface IStaticPool
{
    int Count { get; }
    int UsingCount { get; }
    void CleanUp();
    bool Update(float deltaTime);
    object SpawnAny();
    void ReturnAny(object instance);
}

public static class PureClassPool
{
    private static readonly Dictionary<Type, IStaticPool> Pools = new();
    private static readonly List<Type> CleanupCache = new();

    public static void Update(float deltaTime)
    {
        if (Pools.Count == 0)
        {
            return;
        }

        CleanupCache.Clear();
        foreach (var kv in Pools)
        {
            if (kv.Value.Update(deltaTime))
            {
                CleanupCache.Add(kv.Key);
            }
        }

        for (int i = 0; i < CleanupCache.Count; i++)
        {
            Pools.Remove(CleanupCache[i]);
        }

        CleanupCache.Clear();
    }

    public static void Register<T>(
        Func<T> factory,
        Action<T>? onGet = null,
        Action<T>? onReturn = null,
        int preloadCount = 0,
        int maxCount = 0,
        float autoCleanInterval = 30f,
        bool canAutoClear = false)
        where T : class
    {
        Type type = typeof(T);
        if (Pools.ContainsKey(type))
        {
            return;
        }

        Pools[type] = new Pool<T>(factory, onGet, onReturn, preloadCount, maxCount, autoCleanInterval, canAutoClear);
    }

    public static T Get<T>(
        bool autoRegister = false,
        int preloadCount = 0,
        Action<T>? onGet = null,
        Action<T>? onReturn = null)
        where T : class, new()
    {
        Type type = typeof(T);
        if (!Pools.TryGetValue(type, out IStaticPool? pool))
        {
            if (!autoRegister)
            {
                return null!;
            }

            Register(() => new T(), onGet, onReturn, preloadCount, 0, 30f, true);
            pool = Pools[type];
        }

        return ((Pool<T>)pool).Get();
    }

    public static void Return<T>(T instance) where T : class
    {
        if (instance == null)
        {
            return;
        }

        if (Pools.TryGetValue(typeof(T), out IStaticPool? pool))
        {
            ((Pool<T>)pool).Return(instance);
        }
    }

    public static void Return(object instance)
    {
        if (instance == null)
        {
            return;
        }

        Type type = instance.GetType();
        if (Pools.TryGetValue(type, out IStaticPool? pool))
        {
            pool.ReturnAny(instance);
        }
    }

    public static void ClearAll()
    {
        foreach (var pool in Pools.Values)
        {
            pool.CleanUp();
        }

        Pools.Clear();
    }

    private sealed class Pool<T> : IStaticPool where T : class
    {
        private readonly List<T> _idleItems;
        private readonly Func<T> _factory;
        private readonly Action<T>? _onGet;
        private readonly Action<T>? _onReturn;

        private float _autoCleanupTimer;
        private float _sinceLastGet;
        private int _intervalGet;
        private int _intervalNew;
        private readonly bool _canAutoClear;
        private readonly bool _canAutoClean;

        public int Count => _idleItems.Count;
        public int UsingCount { get; private set; }
        public int MaxCount { get; }
        public float AutoCleanInterval { get; }

        public Pool(
            Func<T> factory,
            Action<T>? onGet,
            Action<T>? onReturn,
            int preloadCount,
            int maxCount,
            float autoCleanInterval,
            bool canAutoClear)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _onGet = onGet;
            _onReturn = onReturn;
            _idleItems = new List<T>(Math.Max(0, preloadCount));
            MaxCount = maxCount <= 0 ? Math.Max(8, preloadCount * 4) : maxCount;
            AutoCleanInterval = autoCleanInterval;
            _canAutoClear = canAutoClear;
            _canAutoClean = autoCleanInterval > 0f;

            for (int i = 0; i < preloadCount; i++)
            {
                T item = _factory();
                if (item != null)
                {
                    _idleItems.Add(item);
                }
            }
        }

        public T Get()
        {
            _intervalGet++;
            _sinceLastGet = 0f;

            T item;
            int lastIndex = _idleItems.Count - 1;
            if (lastIndex >= 0)
            {
                item = _idleItems[lastIndex];
                _idleItems.RemoveAt(lastIndex);
            }
            else
            {
                item = _factory();
                _intervalNew++;
            }

            UsingCount++;
            _onGet?.Invoke(item);
            return item;
        }

        public void Return(T item)
        {
            if (item == null)
            {
                return;
            }

            _onReturn?.Invoke(item);
            UsingCount = Math.Max(0, UsingCount - 1);

            if (Count + UsingCount < MaxCount)
            {
                _idleItems.Add(item);
                return;
            }

            if (item is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        public object SpawnAny()
        {
            return Get();
        }

        public void ReturnAny(object instance)
        {
            Return(instance as T);
        }

        public bool Update(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return false;
            }

            _sinceLastGet += deltaTime;

            if (_canAutoClear && AutoCleanInterval > 0f && _sinceLastGet > 3f * AutoCleanInterval)
            {
                CleanUp();
                return Count == 0 && UsingCount == 0;
            }

            if (_canAutoClean)
            {
                _autoCleanupTimer += deltaTime;
                if (_autoCleanupTimer >= AutoCleanInterval)
                {
                    _autoCleanupTimer = 0f;
                    PerformAudit();
                }
            }

            return false;
        }

        private void PerformAudit()
        {
            if (_intervalNew == 0 && _idleItems.Count > 0)
            {
                int safeCount = Math.Max(0, (int)Math.Ceiling(_intervalGet * 0.4f) + 1);
                if (_idleItems.Count > safeCount)
                {
                    int removeCount = _idleItems.Count - safeCount;
                    for (int i = 0; i < removeCount; i++)
                    {
                        int lastIndex = _idleItems.Count - 1;
                        T item = _idleItems[lastIndex];
                        _idleItems.RemoveAt(lastIndex);
                        if (item is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }
                    }
                }
            }

            _intervalGet = 0;
            _intervalNew = 0;
        }

        public void CleanUp()
        {
            for (int i = _idleItems.Count - 1; i >= 0; i--)
            {
                T item = _idleItems[i];
                if (item is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }

            _idleItems.Clear();
            _autoCleanupTimer = 0f;
            _sinceLastGet = 0f;
            _intervalGet = 0;
            _intervalNew = 0;
        }
    }
}
