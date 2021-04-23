using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class Bits
{
    /// <summary>
    /// Set the bit at the input index and return result
    /// </summary>
    /// <param name="val"></param>
    /// <param name="index"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    public static int SetBit(int val, int index, bool value)
    {
        return value ? val | (1 << index) : val & ~(1 << index);
    }

    /// <summary>
    /// set the bit at the input index and return the result
    /// </summary>
    /// <param name="val"></param>
    /// <param name="index"></param>
    /// <returns></returns>
    public static bool GetBit(int val, int index)
    {
        return (val & (1 << index)) != 0;
    }
}
