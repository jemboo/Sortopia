module Sortopia.Sorting.Catalog.Tests.Identifiers

open System
open System.Globalization
open System.Collections.Generic
open FSharp.UMX
open Xunit
open Sortopia.Core
open Sortopia.Sorting
open Sortopia.Sorting.Catalog

[<Fact>]
let all_identifiers_round_trip_under_other_cultures () =
    let original = CultureInfo.CurrentCulture
    try
        for culture in ["en-US"; "fr-FR"; "ar-SA"] do
            CultureInfo.CurrentCulture <- CultureInfo.GetCultureInfo culture
            for id in SorterLib.ids () do
                Assert.Equal(id, id |> SorterLibId.toString |> SorterLibId.fromString)
            for id in PrefixLib.ids () do
                Assert.Equal(id, id |> PrefixLibId.toString |> PrefixLibId.fromString)
            for width in [24; 32; 768] do
                for id in MergeLibId.availableForChannelCount (UMX.tag width) do
                    Assert.Equal(id, id |> MergeLibId.toString |> MergeLibId.fromString)
            Assert.Equal("SorterLibId(channelCount=24, Variant=VariantB)", SorterLibId.toString (sorterLibId.create 24<channelCount> VariantB))
            Assert.Equal("PrefixLibId(channelCount=24, stageLength=4, Variant=PrefixB)", PrefixLibId.toString (prefixLibId.create 24<channelCount> 4<stageLength> PrefixB))
            Assert.Equal("MergeLibId(channelCount=24, mergeDimension=3, Variant=VariantB)", MergeLibId.toString (mergeLibId.create 24<channelCount> 3<mergeDimension> VariantB))
    finally
        CultureInfo.CurrentCulture <- original

[<Fact>]
let constructors_reject_invalid_dimensions () =
    for width in [0; -1] do
        Assert.Throws<ArgumentException>(fun () -> sorterLibId.create (UMX.tag width) VariantA |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> prefixLibId.create (UMX.tag width) 3<stageLength> PrefixA |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> mergeLibId.create (UMX.tag width) 2<mergeDimension> VariantA |> ignore) |> ignore
    for count in [0; -1] do
        Assert.Throws<ArgumentException>(fun () -> prefixLibId.create 24<channelCount> (UMX.tag count) PrefixA |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> mergeLibId.create 24<channelCount> (UMX.tag count) VariantA |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> mergeLibId.create 24<channelCount> 5<mergeDimension> VariantA |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> MergeLibId.fromString "MergeLibId(channelCount=24, mergeDimension=0, Variant=VariantA)" |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> MergeLibId.fromString "MergeLibId(channelCount=24, mergeDimension=5, Variant=VariantA)" |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> SorterLibId.fromString "SorterLibId(channelCount=0, Variant=VariantA)" |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> PrefixLibId.fromString "PrefixLibId(channelCount=24, stageLength=0, Variant=PrefixA)" |> ignore) |> ignore

[<Theory>]
[<InlineData("")>]
[<InlineData("24,VariantA")>]
[<InlineData("SorterLibId(channelCount=24,, Variant=VariantA)")>]
[<InlineData("SorterLibId(channelCount=24, Variant=VariantA")>]
[<InlineData("SorterLibId(channelCount=-24, Variant=VariantA)")>]
[<InlineData("SorterLibId(channelCount=24, Variant=VariantD)")>]
[<InlineData("SorterLibId(channelCount=24, Variant=VariantA)junk")>]
let sorter_identifier_requires_the_complete_labeled_form text =
    Assert.Throws<FormatException>(fun () -> SorterLibId.fromString text |> ignore) |> ignore

[<Fact>]
let all_identifier_readers_reject_null_wrong_labels_overflow_and_extra_fields () =
    for parse, valid in
        [ (SorterLibId.fromString >> box), "SorterLibId(channelCount=24, Variant=VariantA)"
          (PrefixLibId.fromString >> box), "PrefixLibId(channelCount=24, stageLength=3, Variant=PrefixA)"
          (MergeLibId.fromString >> box), "MergeLibId(channelCount=24, mergeDimension=3, Variant=VariantA)" ] do
        Assert.Throws<ArgumentNullException>(fun () -> parse null |> ignore) |> ignore
        Assert.Throws<FormatException>(fun () -> parse (valid.Replace("channelCount", "width")) |> ignore) |> ignore
        Assert.Throws<FormatException>(fun () -> parse (valid.Replace(")", ", extra=1)")) |> ignore) |> ignore
        Assert.Throws<OverflowException>(fun () -> parse (valid.Replace("24", "2147483648")) |> ignore) |> ignore
        Assert.Equal(parse valid, parse (" \n" + valid.Replace(", ", ",\n ") + "\t"))
    Assert.Throws<FormatException>(fun () -> SorterLibVariant.fromString "varianta" |> ignore) |> ignore
    Assert.Throws<FormatException>(fun () -> PrefixLibVariant.fromString "PrefixD" |> ignore) |> ignore
    for variant in [VariantA; VariantB; VariantC; Batcher; BatcherBitonic] do
        Assert.Equal(variant, variant |> SorterLibVariant.toString |> SorterLibVariant.fromString)
    for variant in [PrefixA; PrefixB; PrefixC] do
        Assert.Equal(variant, variant |> PrefixLibVariant.toString |> PrefixLibVariant.fromString)

[<Fact>]
let stable_id_contract_has_fixed_versioned_encodings () =
    let cases =
        [ box (sorterLibId.create 24<channelCount> VariantB), "f3dc9bd1-90e9-bf03-ed44-34283854a381"
          box (prefixLibId.create 24<channelCount> 4<stageLength> PrefixB), "546ff5e3-fb2a-83d4-6f55-c60d5e226ce5"
          box (mergeLibId.create 24<channelCount> 3<mergeDimension> VariantB), "e0e9763a-32b7-eaac-2dbe-e4bf7bc29e12" ]
    for value, expected in cases do
        Assert.Equal(Guid.Parse expected, GuidUtils.guidFromObjs [value])

[<Fact>]
let equality_and_stable_identity_include_all_fields () =
    let values =
        [ for width in [24; 32] do
            for variant in [VariantA; VariantB; VariantC; Batcher; BatcherBitonic] do
                yield box (sorterLibId.create (UMX.tag width) variant)
                for dimension in [1; 2; 4] do
                    yield box (mergeLibId.create (UMX.tag width) (UMX.tag dimension) variant)
            for variant in [PrefixA; PrefixB; PrefixC] do
                for stages in [2; 3; 4] do
                    yield box (prefixLibId.create (UMX.tag width) (UMX.tag stages) variant) ]
    Assert.Equal(values.Length, HashSet<obj>(values).Count)
    Assert.Equal(values.Length, values |> List.map (fun value -> GuidUtils.guidFromObjs [value]) |> Set.ofList |> Set.count)
    for id in SorterLib.ids () do
        let restored = id |> SorterLibId.toString |> SorterLibId.fromString
        Assert.True(EqualityComparer<sorterLibId>.Default.Equals(id, restored))
        Assert.Equal(id.GetHashCode(), restored.GetHashCode())
