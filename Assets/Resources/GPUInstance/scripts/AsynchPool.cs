using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// A pool that uses keys (ids) and read-only thread items to allow multi threading
/// </summary>
/// <typeparam name="T"></typeparam>
public class AsynchPool<T> where T : new()
{
    public int MaxSize { get; private set; } = 100;
    Queue<T> _queue = new Queue<T>(); // ideally we could use concurrentqueue.. but I have found some serious slow downs when using it (specifically when requresting the count).. Just using basic locking for now...

    /// <summary>
    /// Current number of objects in the pool.
    /// </summary>
    public int Count
    {
        get
        {
            lock (this._queue)
            {
                return this._queue.Count;
            }
        }
    }

    public AsynchPool(int maxSize = int.MaxValue)
    {
        this.MaxSize = maxSize;
    }

    /// <summary>
    /// Get a new item from the pool. Uses default constructor.
    /// </summary>
    /// <returns></returns>
    public T Get()
    {
        lock (this._queue)
        {
            if (this._queue.Count > 0)
            {
                return this._queue.Dequeue();
            }
            else
            {
                return new T();
            }
        }
    }

    /// <summary>
    /// Add an item to the pool
    /// </summary>
    /// <param name="item"></param>
    public void Add(T item)
    {
        lock (this._queue)
        {
            if (this._queue.Count < this.MaxSize) // Note* it is possible for the pool to go past max size- we dont really care though
            {
                this._queue.Enqueue(item);
            }
        }
    }

    /// <summary>
    /// Clear the pool. Clearing the pool to empty is not guarenteed if other threads are adding to it concurrently.
    /// </summary>
    public void Clear()
    {
        lock (this._queue)
        {
            this._queue.Clear();
        }
    }
}


/// <summary>
/// A pool that uses keys (ids) and read-only thread items to allow multi threading. Same as AsynchPool but uses a delegate for objetc creation.
/// </summary>
/// <typeparam name="T"></typeparam>
public class AsynchPoolNoNew<T>
{
    System.Func<T> MakeNew;
    public int MaxSize { get; private set; } = 100;
    Queue<T> _queue = new Queue<T>();

    /// <summary>
    /// Current number of objects in the pool.
    /// </summary>
    public int Count
    {
        get
        {
            lock (this._queue)
            {
                return this._queue.Count;
            }
        }
    }

    public AsynchPoolNoNew(System.Func<T> MakeNew, int maxSize = int.MaxValue)
    {
        this.MaxSize = maxSize;
        this.MakeNew = MakeNew;
    }

    /// <summary>
    /// Get a new item from the pool. Uses default constructor.
    /// </summary>
    /// <returns></returns>
    public T Get()
    {
        lock (this._queue)
        {
            if (this._queue.Count > 0)
            {
                return this._queue.Dequeue();
            }
            else
            {
                return MakeNew();
            }
        }
    }

    /// <summary>
    /// Add an item to the pool
    /// </summary>
    /// <param name="item"></param>
    public void Add(T item)
    {
        lock (this._queue)
        {
            if (this._queue.Count < this.MaxSize) // Note* it is possible for the pool to go past max size- we dont really care though
            {
                this._queue.Enqueue(item);
            }
        }
    }

    /// <summary>
    /// Clear the pool. Clearing the pool to empty is not guarenteed if other threads are adding to it concurrently.
    /// </summary>
    public void Clear()
    {
        lock (this._queue)
        {
            this._queue.Clear();
        }
    }
}