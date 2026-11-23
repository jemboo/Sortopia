namespace Sortopia.Workspaces

open FSharp.UMX
open Sortopia.Runs
open Sortopia.Functions

/// Shared inheritance operations; independent of module/project identity and storage.
module ConfigurationInheritance =
    let resultsRequirements name = ResultsFunctionCatalog.functions |> Map.tryFind name |> Option.map _.RequiredParameters
    let setupRequirements name = SetupFunctionCatalog.functions |> Map.tryFind name |> Option.bind (fun value -> if value.ParametersFromSources then None else Some value.RequiredParameters)
    let defaultsFor requirements values =
        match requirements with
        | None -> values
        | Some keys -> values |> List.filter (fun (value: parameterBinding) -> keys |> List.exists (fun (key: paramKeyDescriptor) -> key.Name = value.KeyName))

    let validateParameters requirements defaults queryKeys =
        let overlap = ParameterSets.overlapError (defaults |> List.map (fun (binding: parameterBinding) -> binding.KeyName)) queryKeys
        if overlap <> "" then invalidArg "queryParameterKeys" ("Query keys must be supplied by spans, not defaults. " + overlap)
        match requirements with
        | None -> ()
        | Some keys ->
            for binding: parameterBinding in defaults do
                match keys |> List.tryFind (fun (key: paramKeyDescriptor) -> key.Name = binding.KeyName) with
                | None -> invalidArg "parameterDefaults" $"Parameter '{binding.KeyName}' is not required by the selected function. Remove this default or choose a compatible module function."
                | Some key when key.ValueType <> binding.ValueType -> invalidArg "parameterDefaults" $"Parameter '{binding.KeyName}' must have type {ParameterValue.label key.ValueType}."
                | _ -> ()
            for name in queryKeys do
                if not (keys |> List.exists (fun key -> key.Name = name)) then
                    invalidArg "queryParameterKeys" $"Query key '{name}' is not a required input of the selected function."

    /// Local bindings replace matching keys; unrelated inherited bindings remain in order.
    let mergeParameterDefaults (inherited: parameterBinding list) (local: parameterBinding list) =
        ParameterBinding.validateUnique inherited
        ParameterBinding.validateUnique local
        let replacements = local |> List.map (fun binding -> binding.KeyName, binding) |> Map.ofList
        let inheritedKeys = inherited |> List.map _.KeyName |> Set.ofList
        (inherited |> List.map (fun binding -> Map.tryFind binding.KeyName replacements |> Option.defaultValue binding))
        @ (local |> List.filter (fun binding -> not (Set.contains binding.KeyName inheritedKeys)))

    let resolveDefaults inherited local excluded =
        mergeParameterDefaults inherited (Option.defaultValue [] local)
        |> List.filter (fun binding -> not (List.contains binding.KeyName excluded))

    /// An explicit empty ID clears a source; omitted types inherit independently.
    let mergeSources (inherited: setupSourceConfig list) (local: setupSourceConfig list) =
        let replaced = local |> List.map _.DataType |> Set.ofList
        (inherited |> List.filter (fun entry -> not (Set.contains entry.DataType replaced))) @ local

    let validateDescription value = if isNull value then nullArg "description"
    let validateDefaultOverrides argument defaults (excluded: string<paramKeyName> list) =
        defaults |> Option.iter ParameterBinding.validateUnique
        if excluded |> List.exists (UMX.untag >> System.String.IsNullOrWhiteSpace) then invalidArg argument "Excluded default keys must not be blank."
        if (List.distinct excluded).Length <> excluded.Length then invalidArg argument "Excluded default keys must be unique."
    let validateSources (sources: setupSourceConfig list) =
        if (sources |> List.map _.DataType |> List.distinct).Length <> sources.Length then invalidArg "setupSources" "Choose each source data type only once."
        for entry in sources do SortopiaDataType.validate entry.DataType
