namespace Sortopia.Evaluation

open FSharp.UMX

module internal SimdBatch =
    let comparatorChannels (network: ceBlock) =
        let ces = network.CeArray
        {| Lows = ces |> Array.map (fun ce -> int ce.LowChannel)
           Highs = ces |> Array.map (fun ce -> int ce.HighChannel) |}

    // Raw block streams carry width on each block. Typed datasets validate their blocks at construction.
    let prepareBlocks (widthOf: 'block -> int) prefix (networks: ceBlock[]) blocks =
        if isNull networks then nullArg "ceBlocks"
        let inputs = Seq.toArray blocks
        if inputs.Length > 0 && networks.Length > 0 then
            let width = widthOf inputs[0]
            CeBlock.validateBatchWidth (UMX.tag width) prefix networks
            if inputs |> Array.exists (fun block -> widthOf block <> width) then
                invalidArg "simdSortableBlocks" "All input blocks must have the same width."
        inputs
