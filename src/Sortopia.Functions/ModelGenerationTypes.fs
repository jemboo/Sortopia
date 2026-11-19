namespace Sortopia.Functions

open FSharp.UMX
open Sortopia.Core
open Sortopia.Runs
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple

/// A supported Simple-model constructor, with the inputs and text needed by its functions.
/// This is a generation recipe, not an exhaustive classification of sorterModel cases.
type internal simpleModelConstructor = {
    Name: string
    Parameters: paramKeyDescriptor list
    Description: string
    Guidance: string
    ValidateCombinations: parameterBinding list -> parameterSpan list -> unit
    Defaults: parameterDefaultContext -> parameterBinding list
    EditorRules: Map<string<paramKeyName>, parameterEditorRule>
    Create: parameterSet -> simpleSorterModelGen -> sorterModelGen
}

[<RequireQualifiedAccess>]
type internal modelPrefixMode = NoPrefix | CatalogPrefix

/// Executable catalog policy; shared builders do not identify algorithms by name or model type.
type internal simpleModelAlgorithm = {
    Name: string
    Parameters: paramKeyDescriptor list
    Version: string
    Description: modelPrefixMode -> simpleModelConstructor -> string
    CombinationError: modelPrefixMode -> parameterBinding list -> parameterSpan list -> string
    ParameterSupport: bool -> modelPrefixMode -> string -> functionParameterSupport
    SuffixStages: modelPrefixMode -> parameterSet -> int<stageLength>
    Create: rngFactory -> int<channelCount> -> ce array -> int<stageLength> -> parameterSet -> simpleSorterModelGen
}
