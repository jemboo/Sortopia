namespace Sortopia.Sorting.Inputs

open System
open System.Collections.Generic
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Sorting.Networks

module BinaryIntArrays =

    let getAllBinaryIntArraysForChannelCount (channelCount: int<channelCount>) : sortableIntArray[] =
        if channelCount < 0<channelCount> || channelCount > 30<channelCount> then
            invalidArg "channelCount" "An eager binary input array requires a channel count between 0 and 30."
        let count = pown 2 (int channelCount)
        let result = Array.zeroCreate count
        for i = 0 to count - 1 do
            let z1Vals = Array.init (int channelCount) (fun j -> (i >>> j) &&& 1)
            result.[i] <- sortableIntArray.create(z1Vals, channelCount, 2 |> UMX.tag<symbolSetSize>)
        result

