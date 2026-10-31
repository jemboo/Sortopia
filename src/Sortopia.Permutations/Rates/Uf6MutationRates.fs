namespace Sortopia.Permutations

open Sortopia.Core

[<Struct; StructuralEquality; NoComparison>]
type uf6MutationRates = 
    private { 
        order: int
        seed6TransitionRates: seed6TransitionRates
        opsTransitionRates: opsTransitionRatesArray
    }

    static member create 
            (order: int) 
            (seed6TransitionRates: seed6TransitionRates) 
            (opsTransitionRates: opsTransitionRatesArray) : uf6MutationRates =
        if order < 6 || order % 6 <> 0 then
            failwith $"Order must be at least 6 and divisible by 6, got {order}"
        { order = order; seed6TransitionRates = seed6TransitionRates; opsTransitionRates = opsTransitionRates }

    static member createUniform 
            (order: int) 
            (seed6TransitionRates: seed6TransitionRates) 
            (rates: opsTransitionRates) : uf6MutationRates =
        let genRatesArrayLength = MathUtils.exactLog2 (order / 6)
        let genRatesArray = Array.init genRatesArrayLength (fun _ -> rates)
        uf6MutationRates.create 
            order 
            seed6TransitionRates 
            (opsTransitionRatesArray.create genRatesArray)

    member this.Order with get() = this.order
    member this.Seed6TransitionRates with get() = this.seed6TransitionRates
    member this.OpsTransitionRates with get() = this.opsTransitionRates

    interface IStableSerializable with
        member this.WriteStableBytes writer =
            writer.Write("uf6MutationRates")
            writer.Write(this.order)
            GuidUtils.writeStableValue writer this.seed6TransitionRates
            GuidUtils.writeStableValue writer this.opsTransitionRates

module Uf6MutationRates =

    let makeUniform (order: int) (seed6MutationRates: float) (twoOrbitMutationRate: float) 
         : uf6MutationRates =
        let mutRatesArrayLength = MathUtils.exactLog2 (order / 6)
        let mutRatesArray = Array.init mutRatesArrayLength (fun _ -> opsTransitionRates.createUniformFromFloat(twoOrbitMutationRate))
        uf6MutationRates.create 
            order 
            (seed6TransitionRates.createUniform(seed6MutationRates))
            (opsTransitionRatesArray.create mutRatesArray)

    let makeUniformFromRates 
            (order: int) 
            (seed6TransitionRates: seed6TransitionRates) 
            (rates: opsTransitionRates) : uf6MutationRates =
        uf6MutationRates.createUniform order seed6TransitionRates rates
