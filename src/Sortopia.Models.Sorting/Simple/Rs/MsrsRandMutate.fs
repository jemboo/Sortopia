namespace Sortopia.Models.Sorting.Simple.Rs

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Models.Sorting

[<Struct; CustomEquality; NoComparison>]
type msrsRandMutate =
    private
        {
          id : Guid<sorterModelMutatorId>
          rngFactory: rngFactory
          opsActionRates: opsActionRates
          mutVariant: mutatorVariant
        }
    with
    static member create
            (rngFactory: rngFactory)
            (opsActionRates: opsActionRates)
            (mutVariant: mutatorVariant) : msrsRandMutate =

        let id =
            [
                box "msuf6RandMutate"
                box (MutatorVariant.toString mutVariant)
                box rngFactory
                box opsActionRates
            ] |> GuidUtils.guidFromObjs |> UMX.tag<sorterModelMutatorId>

        {
            id = id
            rngFactory = rngFactory
            opsActionRates = opsActionRates
            mutVariant = mutVariant
        }

    member this.Id with get () = this.id
    member this.MutatorVariant = this.mutVariant
    member this.RngFactory with get () = this.rngFactory
    member this.OpsActionRates with get () = this.opsActionRates

    override this.Equals(obj) =
        match obj with
        | :? msrsRandMutate as other -> this.Id = other.Id
        | _ -> false

    override this.GetHashCode() =
        hash (this.Id)

    interface IEquatable<msrsRandMutate> with
        member this.Equals(other) = this.Id = other.Id

    member this.MakeSorterModelId
                    (parent: msrs)
                    (index: int<mutationIndex>)
                    (modd: int<mutationMod>) : Guid<sorterModelId> =
        CommonMutator.makeSorterModelId parent.Id this.Id index modd

    member this.MakeSorterModelFromId (parent: msrs) (id: Guid<sorterModelId>) : msrs =
        let rng = this.RngFactory.Create %id

        // Define specific mutation behaviors using Perm_RsOps
        let orthoMutator = fun rsPerm -> PermRsOps.mutatePerm_Rs (rng.NextIndex) opsActionMode.Ortho rsPerm
        let paraMutator = fun rsPerm -> PermRsOps.mutatePerm_Rs (rng.NextIndex) opsActionMode.Para rsPerm
        let selfSymMutator = fun rsPerm -> PermRsOps.mutatePerm_Rs (rng.NextIndex) opsActionMode.SelfRefl rsPerm

        // Perform the mutation using the uniform rate module
        let mutated = OpsActionRates.mutate
                        this.OpsActionRates
                        orthoMutator
                        paraMutator
                        selfSymMutator
                        (rng.NextFloat)
                        parent.Perm_Rss

        msrs.create id parent.ChannelCount mutated

    member this.MakeSorterModelFromIndexAndMod
                            (parent: msrs)
                            (index: int<mutationIndex>)
                            (modd: int<mutationMod>) : msrs =
        let id = this.MakeSorterModelId parent index modd
        this.MakeSorterModelFromId parent id