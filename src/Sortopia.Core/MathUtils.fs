namespace Sortopia.Core

open System

module MathUtils = 

    /// Computes  (toM)^power, where toM is an integer and power is a non-negative integer.
    let integerPower (toM:int) (power:int) =
        pown toM power

    /// Verifies if an integer is an exact power of two and returns its base-2 logarithm.
    /// Throws ArgumentException if n <= 0, or if n is not an exact power of 2.
    let exactLog2 (n: int) : int =
        if n <= 0 then
            invalidArg "n" "Input must be positive"
        // Check if n is a power of two: n & (n - 1) = 0
        if n &&& (n - 1) = 0 then
            // Compute log2 by counting trailing zeros (position of the set bit)
            let rec countTrailingZeros num count =
                if num = 0 then count
                else countTrailingZeros (num >>> 1) (count + 1)

            countTrailingZeros (n >>> 1) 0
        else
            invalidArg "n" "Input must be an exact power of 2"


    /// Performs linear interpolation between two float values, with the max endpoint included
    /// total array length of k.
    let arrayInterpolationU (x0: float) (x1: float) (k: int) : float [] =
        if k < 2 then
            invalidArg "k" "k must be at least 2 for interpolation"
        let step = (x1 - x0) / float k
        Array.init k (fun i -> x0 + step * float (i + 1))
