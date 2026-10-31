namespace Sortopia.Permutations

open Sortopia.Core

open System
open FSharp.UMX

[<Struct; StructuralEquality; NoComparison>]
type uf6GenRates = 
    private { 
        order: int
        seedGenRatesUf6: seed6GenRates
        opsGenRatesArray: opsGenRatesArray
    }

    static member create (order: int) (seedGenRatesUf6: seed6GenRates) (opsGenRatesArray: opsGenRatesArray) : uf6GenRates =
        if order < 6 || order % 6 <> 0 then
            failwith $"Order must be at least 6 and divisible by 6, got {order}"
        { order = order; seedGenRatesUf6 = seedGenRatesUf6; opsGenRatesArray = opsGenRatesArray }

    member this.Order with get() = this.order
    member this.SeedGenRatesUf6 with get() = this.seedGenRatesUf6
    member this.OpsGenRatesArray with get() = this.opsGenRatesArray

    interface IStableSerializable with
        member this.WriteStableBytes writer =
            writer.Write("uf6GenRates")
            writer.Write(this.order)
            GuidUtils.writeStableValue writer this.seedGenRatesUf6
            GuidUtils.writeStableValue writer this.opsGenRatesArray

module Uf6GenRates =

    let makeUniform (order: int) : uf6GenRates =
        let genRatesArrayLength = MathUtils.exactLog2 (order / 6)
        let genRatesArray = Array.init genRatesArrayLength (fun _ -> opsGenRates.createUniform())
        uf6GenRates.create 
            order 
            (seed6GenRates.createUniform())
            (opsGenRatesArray.create genRatesArray)
