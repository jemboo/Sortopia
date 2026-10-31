namespace Sortopia.Models.Sorting.Simple.Ce

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting

[<Struct; CustomEquality; NoComparison>]
type msceRandMutate =
    private
        {
          id : Guid<sorterModelMutatorId>
          rngFactory: rngFactory
          indelRates: indelRates
          excludeSelfCe: bool<excludeSelfCe>
          mutVariant: mutatorVariant
        }
    with
    static member create
            (rngFactory: rngFactory)
            (indelRates: indelRates) // Single value
            (excludeSelfCe: bool<excludeSelfCe>)
            (mutVariant: mutatorVariant) : msceRandMutate =

        let id =
            [
                box "Sortopia.Models.Sorting/msceRandMutate/v1"
                box (MutatorVariant.toString mutVariant)
                box rngFactory
                box indelRates
                box (excludeSelfCe |> UMX.untag)
            ] |> GuidUtils.guidFromObjs |> UMX.tag<sorterModelMutatorId>

        {
            id = id
            rngFactory = rngFactory
            indelRates = indelRates
            excludeSelfCe = excludeSelfCe
            mutVariant = mutVariant
        }

    member this.Id with get () = this.id
    member this.MutatorVariant = this.mutVariant
    member this.RngFactory with get () = this.rngFactory
    member this.IndelRates with get () = this.indelRates
    member this.ExcludeSelfCe with get () = this.excludeSelfCe

    override this.Equals(obj) =
        match obj with
        | :? msceRandMutate as other -> this.Id = other.Id
        | _ -> false

    override this.GetHashCode() =
        hash (this.Id)

    interface IEquatable<msceRandMutate> with
        member this.Equals(other) = this.Id = other.Id

    member this.MakeSorterModelId
                (parent: msce)
                (index: int<mutationIndex>)
                (modd: int<mutationMod>) : Guid<sorterModelId> =
        CommonMutator.makeSorterModelId parent.Id this.Id index modd


    member this.MakeSorterModelFromId
                            (parent: msce)
                            (id: Guid<sorterModelId>)
                            : msce =
        let rng = this.RngFactory.Create %id
        let excludeSelfCe = this.ExcludeSelfCe
        let channelCount = %parent.ChannelCount

        let ceCodeInserter = fun () -> Ce.generateCeCode %excludeSelfCe channelCount (rng.NextIndex)
        let ceCodeMutator = fun _ -> Ce.generateCeCode %excludeSelfCe channelCount (rng.NextIndex)

        let ceCodes = IndelRates.mutate
                        this.IndelRates
                        ceCodeInserter
                        ceCodeMutator
                        (rng.NextFloat)
                        parent.CeCodes

        msce.create id parent.ChannelCount ceCodes


    member this.MakeSorterModelFromIndexAndMod
                    (parent: msce)
                    (index: int<mutationIndex>)
                    (modd: int<mutationMod>) : msce =
        let id = this.MakeSorterModelId parent index modd
        this.MakeSorterModelFromId parent id