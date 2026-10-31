namespace Sortopia.Permutations.Serialization.Tests

open System
open FsUnit.Xunit
open Xunit
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Permutations.Serialization.RatesAndOps

type IndelRatesTests() =
    let epsilon = 1e-10 // Tolerance for floating-point comparisons

    [<Fact>]
    let ``IndelRatesDto toIndelRates should correctly map to domain type`` () =
        let dto = { mutationThresh = 0.2; insertionThresh = 0.5; deletionThresh = 0.9 }
        let rates = IndelRatesDto.toDomain dto
        rates.MutationRate |> should (equalWithin epsilon) 0.2
        rates.InsertionRate |> should (equalWithin epsilon) 0.3 // 0.5 - 0.2
        rates.DeletionRate |> should (equalWithin epsilon) 0.4 // 0.9 - 0.5
        rates.NoActionRate |> should (equalWithin epsilon) 0.1 // 1.0 - 0.9

    [<Fact>]
    let ``IndelRatesDto fromIndelRates should correctly map to Dto`` () =
        let rates = indelRates.create (0.2, 0.3, 0.4)
        let dto = IndelRatesDto.fromDomain rates
        dto.mutationThresh |> should (equalWithin epsilon) 0.2
        dto.insertionThresh |> should (equalWithin epsilon) 0.5 // 0.2 + 0.3
        dto.deletionThresh |> should (equalWithin epsilon) 0.9 // 0.2 + 0.3 + 0.4

    [<Fact>]
    let ``Roundtrip conversion through IndelRatesDto should preserve rates`` () =
        let original = indelRates.create (0.2, 0.3, 0.4)
        let dto = IndelRatesDto.fromDomain original
        let converted = IndelRatesDto.toDomain dto
        converted.MutationRate |> should (equalWithin epsilon) original.MutationRate
        converted.InsertionRate |> should (equalWithin epsilon) original.InsertionRate
        converted.DeletionRate |> should (equalWithin epsilon) original.DeletionRate
        converted.NoActionRate |> should (equalWithin epsilon) original.NoActionRate