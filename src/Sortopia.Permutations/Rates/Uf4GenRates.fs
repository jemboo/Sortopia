namespace Sortopia.Permutations

open Sortopia.Core

open System
open FSharp.UMX

[<Struct; StructuralEquality; NoComparison>]
type uf4GenRates = 
    private { 
        order: int
        seedOpsGenRates: opsGenRates
        opsGenRatesArray: opsGenRatesArray
    }

    static member create (order: int) (seedOpsGenRates: opsGenRates) (opsGenRatesArray: opsGenRatesArray) : uf4GenRates =
        if order < 4 || order % 4 <> 0 then
            failwith $"Order must be at least 4 and divisible by 4, got {order}"
        { order = order; seedOpsGenRates = seedOpsGenRates; opsGenRatesArray = opsGenRatesArray }

    member this.Order with get() = this.order
    member this.SeedOpsGenRates with get() = this.seedOpsGenRates
    member this.OpsGenRatesArray with get() = this.opsGenRatesArray

    interface IStableSerializable with
        member this.WriteStableBytes writer =
            writer.Write("uf4GenRates")
            writer.Write(this.order)
            GuidUtils.writeStableValue writer this.seedOpsGenRates
            GuidUtils.writeStableValue writer this.opsGenRatesArray

module Uf4GenRates =

    let makeUniform (order: int) : uf4GenRates =
        let genRatesArrayLength = MathUtils.exactLog2 (order / 4)
        let genRatesArray = Array.init genRatesArrayLength (fun _ -> opsGenRates.createUniform())
        uf4GenRates.create 
            order 
            (opsGenRates.createUniform())
            (opsGenRatesArray.create genRatesArray)
