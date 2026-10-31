namespace Sortopia.Sorting.Inputs

open System.Runtime.Intrinsics
open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Inputs

module GrayVectorGenerator =

    /// Seeds exactly 512 Gray code sequences into bit-packed vectors.
    /// This always generates a full 512-lane block.
    let private generateInitialSeedBlock (sw: int<channelCount>) : Vector512<uint64>[] =
        let width = %sw
        let vecs = Array.init width (fun _ -> Array.zeroCreate<uint64> 8)
        
        for i = 0 to 511 do
            let gray = uint64 i ^^^ (uint64 i >>> 1)
            let lane = i / 64
            let bit = i % 64
            for wire = 0 to width - 1 do
                if (gray &&& (1uL <<< wire)) <> 0uL then
                    vecs.[wire].[lane] <- vecs.[wire].[lane] ||| (1uL <<< bit)
        
        vecs |> Array.map (fun v -> 
            Vector512.Create(v.[0], v.[1], v.[2], v.[3], v.[4], v.[5], v.[6], v.[7]))

    /// Yields a sequence of bit-packed blocks covering exactly 2^n cases.
    let getAllSortBlockBitv512ForChannelCount (sw: int<channelCount>) : seq<sortBlockBitv512> =
        let width = %sw
        if width < 0 || width > 63 then invalidArg "sw" "Channel count must be between 0 and 63."
        let totalSequences = 1uL <<< width
        
        seq {
            let seedVectors = generateInitialSeedBlock sw
            let firstBlock = sortBlockBitv512.createFromVectors seedVectors 512

            if totalSequences < 512uL then
                // CASE 1: The entire 0-1 space is smaller than one block.
                // We generate the seed and truncate it immediately.
                yield SortBlockBitv512.truncate firstBlock (int totalSequences)
            else
                // CASE 2: The 0-1 space is one or more full 512-lane blocks.
                let totalBlocks = totalSequences >>> 9
                let mutable currentVectors = seedVectors
                
                for blockIdx = 0uL to totalBlocks - 1uL do
                    // Yield the current full block
                    yield sortBlockBitv512.createFromVectors (Array.copy currentVectors) 512

                    // Compute flip for next block
                    if blockIdx < totalBlocks - 1uL then
                        let mutable c = blockIdx + 1uL
                        let mutable wireToFlip = 9 
                        while (c &&& 1uL) = 0uL && wireToFlip < width do
                            c <- c >>> 1
                            wireToFlip <- wireToFlip + 1
                        
                        if wireToFlip < width then
                            // XOR with AllBitsSet flips all 512 bits for this wire
                            currentVectors.[wireToFlip] <- currentVectors.[wireToFlip] ^^^ Vector512<uint64>.AllBitsSet
        }
