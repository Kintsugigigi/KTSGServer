using System;
using System.Collections.Generic;

namespace KTSG.Core
{
    /// <summary>
    /// 极简高性能对象池 (非线程安全)
    /// </summary>
    public class SimplePool<T> where T : class
    {
        private readonly Queue<T> _pool;
        
        private readonly Func<T> _factory;       // 创建新对象
        private readonly Action<T> _onGet;       // 取出时调用 (Reset)
        private readonly Action<T> _onReturn;    // 归还时调用 (Clean)
        private readonly Action<T> _onDestroy;   // 销毁时调用 (Dispose)
        
        public int Count => _pool.Count;         
        public int MaxCapacity { get; }         
        
        public SimplePool(Func<T> factory, 
                          Action<T> onGet = null, 
                          Action<T> onReturn = null, 
                          Action<T> onDestroy = null,
                          int initCapacity = 16, 
                          int maxCapacity = 100,
                          int preloadCount = 0)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (maxCapacity <= 0) throw new ArgumentException("Max capacity must be greater than 0");

            _factory = factory;
            _onGet = onGet;
            _onReturn = onReturn;
            _onDestroy = onDestroy;
            MaxCapacity = maxCapacity;

            _pool = new Queue<T>(Math.Max(initCapacity, preloadCount));

            if (preloadCount > 0)
            {
                for (int i = 0; i < preloadCount; i++)
                {
                    _pool.Enqueue(_factory());
                }
            }
        }

        /// <summary>
        /// 从池中获取一个对象
        /// </summary>
        public T Get()
        {
            T item;
            if (_pool.Count > 0)
            {
                item = _pool.Dequeue();
            }
            else
            {
                item = _factory();
            }

            _onGet?.Invoke(item);
            return item;
        }

        /// <summary>
        /// 将对象归还回池
        /// </summary>
        public void Return(T item)
        {
            if (item == null) return;

            _onReturn?.Invoke(item);

            if (_pool.Count < MaxCapacity)
            {
                _pool.Enqueue(item);
            }
            else
            {
                _onDestroy?.Invoke(item);
            }
        }

        /// <summary>
        /// 清空池中所有对象
        /// </summary>
        public void Clear()
        {
            if (_onDestroy != null)
            {
                foreach (var item in _pool)
                {
                    _onDestroy(item);
                }
            }
            _pool.Clear();
        }
    }
}