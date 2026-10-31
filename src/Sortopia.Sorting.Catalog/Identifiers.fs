namespace Sortopia.Sorting.Catalog

open System
open System.Globalization
open System.Text.RegularExpressions
open FSharp.UMX
open Sortopia.Core
open Sortopia.Sorting

module internal IdText =
    let positive name value =
        if value <= 0 then invalidArg name "Value must be positive."

    let read pattern (text: string) =
        if isNull text then nullArg "text"
        let matched = Regex.Match(text, "\\A\\s*" + pattern + "\\s*\\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1.0))
        if not matched.Success then raise (FormatException("Invalid catalog identifier."))
        matched.Groups

    let number (group: Group) = Int32.Parse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture)

/// Stable names identify alternative networks at a given width.
type sorterLibVariant = VariantA | VariantB | VariantC | Batcher | BatcherBitonic

module SorterLibVariant =
    let toString = function VariantA -> "VariantA" | VariantB -> "VariantB" | VariantC -> "VariantC" | Batcher -> "Batcher" | BatcherBitonic -> "BatcherBitonic"
    let fromString = function
        | "VariantA" -> VariantA
        | "VariantB" -> VariantB
        | "VariantC" -> VariantC
        | "Batcher" -> Batcher
        | "BatcherBitonic" -> BatcherBitonic
        | _ -> raise (FormatException("Unknown sorting-network variant."))

type prefixLibVariant = PrefixA | PrefixB | PrefixC

module PrefixLibVariant =
    let toString = function PrefixA -> "PrefixA" | PrefixB -> "PrefixB" | PrefixC -> "PrefixC"
    let fromString = function
        | "PrefixA" -> PrefixA
        | "PrefixB" -> PrefixB
        | "PrefixC" -> PrefixC
        | _ -> raise (FormatException("Unknown prefix variant."))

/// A valid key need not have an entry in the catalog; use SorterLib.tryGet to check availability.
type sorterLibId =
    private { channelCount: int<channelCount>; sorterVariant: sorterLibVariant }

    static member create (channelCount: int<channelCount>) sorterVariant =
        IdText.positive "channelCount" (int channelCount)
        { channelCount = channelCount; sorterVariant = sorterVariant }

    member this.ChannelCount = this.channelCount
    member this.SorterVariant = this.sorterVariant

    interface IStableSerializable with
        member this.WriteStableBytes writer =
            writer.Write("Sortopia.Sorting.Catalog.SorterLibId/v1")
            writer.Write(int this.channelCount)
            writer.Write(SorterLibVariant.toString this.sorterVariant)

module SorterLibId =
    /// Canonical, culture-independent text for project parameters and persisted identifiers.
    let toString (key: sorterLibId) =
        FormattableString.Invariant($"SorterLibId(channelCount={int key.ChannelCount}, Variant={SorterLibVariant.toString key.SorterVariant})")

    /// Reads the complete labeled form; malformed text and nonpositive widths are rejected.
    let fromString text =
        let fields = IdText.read @"SorterLibId\(channelCount=([0-9]+),\s*Variant=(Variant[ABC]|Batcher|BatcherBitonic)\)" text
        sorterLibId.create (UMX.tag (IdText.number fields.[1])) (SorterLibVariant.fromString fields.[2].Value)

/// A key for a partial sorting network with a declared stage count.
type prefixLibId =
    private { channelCount: int<channelCount>; stageLength: int<stageLength>; variant: prefixLibVariant }

    static member create (channelCount: int<channelCount>) (stageLength: int<stageLength>) variant =
        IdText.positive "channelCount" (int channelCount)
        IdText.positive "stageLength" (int stageLength)
        { channelCount = channelCount; stageLength = stageLength; variant = variant }

    member this.ChannelCount = this.channelCount
    member this.StageLength = this.stageLength
    member this.PrefixLibVariant = this.variant

    interface IStableSerializable with
        member this.WriteStableBytes writer =
            writer.Write("Sortopia.Sorting.Catalog.PrefixLibId/v1")
            writer.Write(int this.channelCount)
            writer.Write(int this.stageLength)
            writer.Write(PrefixLibVariant.toString this.variant)

module PrefixLibId =
    let toString (key: prefixLibId) =
        FormattableString.Invariant($"PrefixLibId(channelCount={int key.ChannelCount}, stageLength={int key.StageLength}, Variant={PrefixLibVariant.toString key.PrefixLibVariant})")

    let fromString text =
        let fields = IdText.read @"PrefixLibId\(channelCount=([0-9]+),\s*stageLength=([0-9]+),\s*Variant=(Prefix[ABC])\)" text
        prefixLibId.create (UMX.tag (IdText.number fields.[1])) (UMX.tag (IdText.number fields.[2])) (PrefixLibVariant.fromString fields.[3].Value)

/// Identifies a prefix that sorts equal contiguous segments independently, preparing them for a merge.
type mergeLibId =
    private { channelCount: int<channelCount>; mergeDimension: int<mergeDimension>; variant: sorterLibVariant }

    static member create (channelCount: int<channelCount>) (mergeDimension: int<mergeDimension>) variant =
        IdText.positive "channelCount" (int channelCount)
        IdText.positive "mergeDimension" (int mergeDimension)
        if int channelCount % int mergeDimension <> 0 then
            invalidArg "mergeDimension" "Channel count must be divisible by merge dimension."
        { channelCount = channelCount; mergeDimension = mergeDimension; variant = variant }

    member this.ChannelCount = this.channelCount
    member this.MergeDimension = this.mergeDimension
    member this.SorterLibVariant = this.variant
    member this.FactorChannelCount = this.channelCount / int this.mergeDimension

    interface IStableSerializable with
        member this.WriteStableBytes writer =
            writer.Write("Sortopia.Sorting.Catalog.MergeLibId/v1")
            writer.Write(int this.channelCount)
            writer.Write(int this.mergeDimension)
            writer.Write(SorterLibVariant.toString this.variant)
