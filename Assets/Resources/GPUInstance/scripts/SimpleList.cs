using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Basically the same as System.List but this has the underlying array exposed. Mainly done so that you can get ref to struct objects.
/// </summary>
/// <typeparam name="T"></typeparam>
public class SimpleList<T> : IEnumerable<T>
{
    public IEnumerator<T> GetEnumerator()
    {
        return AllItems;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return AllItems;
    }

    public IEnumerator<T> AllItems
    {
        get
        {
            for (int i = 0; i < Count; i++)
                yield return Array[i];
        }
    }

    /// <summary>
    /// number of items in the array
    /// </summary>
    public int Count { get; private set; }

    public int Capacity { get { return this.Array.Length; } }

    /// <summary>
    /// Manually adjust count. Be careful not to messup the list
    /// </summary>
    /// <param name="new_count"></param>
    public void SetCount(int new_count) { Count = new_count; }

    /// <summary>
    /// array for this set
    /// </summary>
    public T[] Array;

    public T this[int i]
    {
        get { return this.Array[i]; }
        set { this.Array[i] = value; }
    }

    public SimpleList()
    {
        Array = new T[2];
        Count = 0;
    }
    public SimpleList(int capacity = 2)
    {
        Array = new T[capacity];
        Count = 0;
    }

    public SimpleList(IEnumerable<T> collection, int capacity = 2)
    {
        Array = new T[capacity < 2 ? 2 : capacity];
        Count = 0;
        foreach (var t in collection)
            Add(t);
    }

    public SimpleList(T[] data)
    {
        if (data == null)
            throw new System.Exception("Error, data should not be null");

        this.Array = data;
        this.Count = data.Length;
    }

    /// <summary>
    /// Add a value and return its index
    /// </summary>
    /// <param name="value"></param>
    /// <param name="pool"></param>
    /// <returns></returns>
    public int Add(in T value)
    {
        //get new array if needed
        if (Array.Length <= Count)
        {
            var new_array = new T[(Array.Length + 1) * 2];
            System.Array.Copy(Array, new_array, Array.Length);
            Array = new_array;
        }

        int index = Count;
        Array[index] = value;
        Count++;
        return index;
    }

    public void Resize(in int new_size)
    {
        if (new_size < 0)
            throw new System.Exception("Error, size too small");

        var new_array = new T[new_size];
        var length = System.Math.Min(this.Array.Length, new_size);
        for (int i = 0; i < length; i++)
            new_array[i] = this.Array[i];
        this.Array = new_array;
        this.Count = System.Math.Min(new_size, this.Count);
    }

    public void Append(SimpleList<T> data, int start_index, int length)
    {
        var stop = start_index + length;
        for (int i = start_index; i < stop; i++)
            Add(data.Array[i]);
    }

    /// <summary>
    /// Sets all elements up to 'Count' to default. Also sets 'Count' to 0
    /// </summary>
    public void Clear()
    {
        if (Array != null)
        {
            for (int i = 0; i < Count; i++)
                Array[i] = default(T);
            Count = 0;
        }
    }
}
public static class SimpleListExtension
{
    /// <summary>
    /// Trys to find the input item and removes it. Also swaps the last element with the removed element if it was removed.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="list"></param>
    /// <param name="obj"></param>
    /// <returns></returns>
    public static bool RemoveSwap<T>(this SimpleList<T> list, T obj) where T : System.IEquatable<T>
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list.Array[i].Equals(obj))
            {
                list.Array[i] = list.Array[list.Count - 1]; // Swap
                list.Array[list.Count - 1] = default(T); // Remove item at the end
                list.SetCount(list.Count - 1);
                return true;
            }
        }
        return false;
    }
    /// <summary>
    /// Trys to find the input item and removes it. Also swaps the last element with the removed element if it was removed.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="list"></param>
    /// <param name="obj"></param>
    /// <returns></returns>
    public static bool RemoveSwapReverseSearch<T>(this SimpleList<T> list, T obj) where T : System.IEquatable<T>
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list.Array[i].Equals(obj))
            {
                list.Array[i] = list.Array[list.Count - 1]; // Swap
                list.Array[list.Count - 1] = default(T); // Remove item at the end
                list.SetCount(list.Count - 1);
                return true;
            }
        }
        return false;
    }
    /// <summary>
    /// Try to find the index of the input item.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="list"></param>
    /// <param name="obj"></param>
    /// <param name="index"></param>
    /// <returns></returns>
    public static bool TryFind<T>(this SimpleList<T> list, T obj, out int index) where T : System.IEquatable<T>
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list.Array[i].Equals(obj))
            {
                index = i;
                return true;
            }
        }
        index = -1;
        return false;
    }
    /// <summary>
    /// Remove content from the list on the left side.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="list"></param>
    /// <param name="count"></param>
    public static void PopLeft<T>(this SimpleList<T> list, int shift)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var src = i + shift;
            if (src >= list.Count)
            {
                list.Array[i] = default(T);
            }
            else
            {
                list.Array[i] = list.Array[src];
            }
        }
        list.SetCount(System.Math.Max(list.Count - shift, 0));
    }
    /// <summary>
    /// Remove item at the specified index & swap it with the item at the final index
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="list"></param>
    /// <param name="index"></param>
    public static void RemoveSwap<T>(this SimpleList<T> list, in int index)
    {
        if (index < list.Count)
        {
            list.Array[index] = list.Array[list.Count - 1];
            list.Array[list.Count - 1] = default(T);
            list.SetCount(list.Count - 1);
        }
    }
    /// <summary>
    /// Removes range of items (sets internal array to defailt(T) for unallocated space)
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="list"></param>
    /// <param name="index"></param>
    /// <param name="count"></param>
    public static void RemoveRange<T>(this SimpleList<T> list, in int index, in int count)
    {
        if (index + count > list.Count || index < 0 || count < 0)
            throw new System.Exception("Error, invalid range specified!");

        for (int i = index, j = index + count; i < list.Count; i++, j++)
        {
            if (j < list.Count)
            {
                list.Array[i] = list.Array[j];
            }
            else
            {
                list.Array[i] = default(T);
            }
        }
;
        list.SetCount(list.Count - count);
    }
}
