namespace Sortopia.Models.Inputs

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Sorting.Inputs
open Sortopia.Sorting.Catalog

[<Struct; CustomEquality; NoComparison>]
type msasPfx =
    private
        { id: Guid<sorterTestModelID>
          pfxLibId: prefixLibId }

    static member create
            (pfxLibId: prefixLibId) : msasPfx =
            if PrefixLib.tryGet pfxLibId |> Option.isNone then invalidArg "pfxLibId" "Prefix is not available in the catalog."
            let id =
                [
                    "MsasPfx" :> obj
                    pfxLibId :> obj
                ] |> GuidUtils.guidFromObjs |> UMX.tag<sorterTestModelID>

            { id = id; pfxLibId = pfxLibId;}

    member this.Id with get() = this.id
    member this.PfxLibId with get() = this.pfxLibId
    member this.ChannelCount with get() = this.pfxLibId.ChannelCount
    member this.StageLength with get() = this.pfxLibId.StageLength

    override this.Equals(obj) =
        match obj with
        | :? msasPfx as other ->
            this.id = other.id
        | _ -> false

    override this.GetHashCode() =
        hash (this.id, this.pfxLibId)

    interface IEquatable<msasPfx> with
        member this.Equals(other) =
            this.id = other.id

    member this.MakeSortableBoolTest
            (sorterTestId: Guid<sortableTestsId>) : sortableBinaryTests =
        let ceArray = (SorterDataParse.getCeArrayFromPrefixLib this.PfxLibId).Value
        let bArrays = SortableBoolArray.getAllPossibleResultsFromCeArray
                        ceArray
                        this.ChannelCount
                      |> Seq.toArray
        sortableBinaryTests.create
                sorterTestId
                this.ChannelCount
                bArrays

    member this.MakeSortableBitv512TestWithPrefix(sorterTestId: Guid<sortableTestsId>) =
        let ceArray = (SorterDataParse.getCeArrayFromPrefixLib this.PfxLibId).Value
        let bArrays = SortableBoolArray.getAllPossibleResultsFromCeArray
                        ceArray
                        this.ChannelCount
                      |> Seq.toArray
        SortableBitv512Tests.fromBoolArrays sorterTestId this.ChannelCount bArrays, ceArray

    member this.MakeSortableBitv512Test(sorterTestId: Guid<sortableTestsId>) =
        this.MakeSortableBitv512TestWithPrefix(sorterTestId) |> fst


module MsasPfx = ()

