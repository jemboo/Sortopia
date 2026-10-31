namespace Sortopia.Permutations

open Sortopia.Core

[<Struct; StructuralEquality; NoComparison>]
type uf4MutationRates =
    private
        {
            order: int
            seedOpsTransitionRates: opsTransitionRates
            twoOrbitPairOpsTransitionRates: opsTransitionRatesArray
        }

        static member create 
                (order: int) 
                (seedOpsTransitionRates: opsTransitionRates) 
                (twoOrbitPairOpsTransitionRates: opsTransitionRatesArray) : uf4MutationRates =
            if order < 4 || order % 4 <> 0 then
                failwith $"Order must be at least 4 and divisible by 4, got {order}"
            { 
                order = order; 
                seedOpsTransitionRates = seedOpsTransitionRates; 
                twoOrbitPairOpsTransitionRates = twoOrbitPairOpsTransitionRates }

        static member createUniform 
                            (order: int) 
                            (seedRates:opsTransitionRates) 
                            (rates:opsTransitionRates) : uf4MutationRates =
            let genRatesArrayLength = MathUtils.exactLog2 (order / 4)
            let genRatesArray = Array.init genRatesArrayLength (fun _ -> rates)
            uf4MutationRates.create 
                order
                seedRates
                (opsTransitionRatesArray.create genRatesArray)

        member this.Order with get() = this.order
        member this.SeedOpsTransitionRates with get() = this.seedOpsTransitionRates
        member this.TwoOrbitPairOpsTransitionRates with get() = this.twoOrbitPairOpsTransitionRates

        interface IStableSerializable with
            member this.WriteStableBytes writer =
                writer.Write("uf4MutationRates")
                writer.Write(this.order)
                GuidUtils.writeStableValue writer this.seedOpsTransitionRates
                GuidUtils.writeStableValue writer this.twoOrbitPairOpsTransitionRates

module Uf4MutationRates =


    let makeV1 (order: int) 
               (seedModificationRate: float)
               (modificationRate: float)
               (orthoRate: float)
               (paraRate: float) 
               (selfSymRate: float) : uf4MutationRates =

        let opsSeedActionRates = opsActionRates.createMod seedModificationRate orthoRate paraRate selfSymRate
        let opsActionRates = opsActionRates.createMod modificationRate orthoRate paraRate selfSymRate
        let opsSeedTransitionRates = opsTransitionRates.createUniformFromRates opsSeedActionRates
        let opsTransitionRates = opsTransitionRates.createUniformFromRates opsActionRates
        uf4MutationRates.createUniform order opsSeedTransitionRates opsTransitionRates


    let makeV2 (order: int) 
               (seedModificationRate: float)
               (modificationRate: float)
               (orthoRate: float)
               (paraRate: float) 
               (selfSymRate: float) : uf4MutationRates =

        // make the seed opsTransitionRates
        let opsSeedActionRates = opsActionRates.createMod seedModificationRate orthoRate paraRate selfSymRate
        let opsSeedTransitionRates = opsTransitionRates.createUniformFromRates opsSeedActionRates

        // make the unfolding opsTransitionRates
        if order < 4 || order % 4 <> 0 then
            failwith $"Order must be at least 4 and divisible by 4, got {order}"
        let mutRatesArrayLength = MathUtils.exactLog2 (order / 4)
        let mutRatesArray = MathUtils.arrayInterpolationU seedModificationRate modificationRate mutRatesArrayLength
        let opsActionRatesArray = mutRatesArray |> Array.map (fun rate -> opsActionRates.createMod rate orthoRate paraRate selfSymRate)
        let opsTransitionRatesArray = opsTransitionRatesArray.create (opsActionRatesArray |> Array.map opsTransitionRates.createUniformFromRates)
        uf4MutationRates.create order opsSeedTransitionRates opsTransitionRatesArray


