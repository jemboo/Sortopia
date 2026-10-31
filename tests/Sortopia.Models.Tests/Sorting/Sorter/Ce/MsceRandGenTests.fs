namespace Sortopia.Models.Tests.Sorter.Ce

open System
open FSharp.UMX
open Xunit
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Core.Rando
open Sortopia.Sorting
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple
open Sortopia.Models.Sorting.Simple.Ce

type MsceRandGenTests() =

    // Helper to create a Model_Ce
    let createModelCe (id: Guid<sorterModelId>) (width: int<channelCount>) (ceCodes: int array) : msce =
        msce.create id width ceCodes

    // Helper function to create a mock random number generator
    let createMockRando (indices: int list) (floats: float list) =
        fun _ _ -> new MockRando(floats, indices) :> IRando
