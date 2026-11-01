namespace Sortopia.Sorting.Serialization.Inputs

open MessagePack
open Sortopia.Sorting.Inputs
open Sortopia.Sorting.Networks
open Sortopia.Sorting.Serialization.Networks

[<MessagePackObject>]
type sortableTestsWithPrefixDto =
    { [<Key(0)>] Tests: sortableTestsDto
      [<Key(1)>] Prefix: ceDto array }

module SortableTestsWithPrefixDto =
    let private validatePrefix tests (prefix: ce array) =
        if isNull prefix then nullArg "prefix"
        let width = int (SortableTests.getChannelCount tests)
        if prefix |> Array.exists (fun c -> int c.HighChannel >= width) then
            invalidArg "prefix" "Prefix endpoints must be within the dataset width."

    let fromDomain (tests: sortableTests, prefix: ce array) : sortableTestsWithPrefixDto =
        validatePrefix tests prefix
        { Tests = SortableTestsDto.fromDomain tests
          Prefix = prefix |> Array.map CeDto.fromDomain }

    let toDomain (dto: sortableTestsWithPrefixDto) : sortableTests * ce array =
        if obj.ReferenceEquals(dto, null) then nullArg "dto"
        if isNull dto.Prefix then nullArg "dto.Prefix"
        let tests = SortableTestsDto.toDomain dto.Tests
        let prefix = dto.Prefix |> Array.map CeDto.toDomain
        validatePrefix tests prefix
        tests, prefix
