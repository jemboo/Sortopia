namespace Sortopia.Runs

open FSharp.UMX

/// A set of typed parameter bindings. Text is produced only by explicit formatting operations.
type parameterSet =
    private { paramMap : Map<string<paramKeyName>, parameterBinding> }

    static member create (paramMap: Map<string<paramKeyName>, parameterBinding>) =
        if isNull (box paramMap) then nullArg "paramMap"
        for KeyValue(key, binding) in paramMap do
            ParameterBinding.validate binding
            if key <> binding.KeyName then invalidArg "paramMap" "Each map key must match its parameter binding's key."
        { paramMap = paramMap }

    /// Build a set directly from typed bindings, without a project or run.
    static member fromBindings (bindings: parameterBinding seq) =
        if isNull (box bindings) then nullArg "bindings"
        let values = Seq.toArray bindings
        ParameterBinding.validateUnique values
        { paramMap = values |> Array.map (fun binding -> binding.KeyName, binding) |> Map.ofArray }

    member this.ParamMap = this.paramMap

    /// Find a binding without formatting its value.
    member this.TryFind(key: string<paramKeyName>) = Map.tryFind key this.paramMap

    /// Some adds or replaces a typed value. None removes the key.
    member this.WithValue(key: string<paramKeyName>, value: parameterValue option) =
        if System.String.IsNullOrWhiteSpace (UMX.untag key) then invalidArg "key" "Enter a parameter key."
        match value with
        | Some value -> this.WithBinding(ParameterBinding.fromValue key value)
        | None -> { paramMap = Map.remove key this.paramMap }

    /// Add or replace a typed binding, preserving all other parameters.
    member this.WithBinding(binding: parameterBinding) =
        ParameterBinding.validate binding
        { paramMap = Map.add binding.KeyName binding this.paramMap }

    /// Missing keys and stored values of a different type both yield None.
    member this.TryFind(key: paramKey<'value>) =
        if isNull (box key) then nullArg "key"
        Map.tryFind key.Name this.paramMap |> Option.bind (fun binding -> ParameterValue.tryGet<'value> binding.Value)

    member this.WithValue(key: paramKey<'value>, value: 'value option) =
        if isNull (box key) then nullArg "key"
        match value with
        | Some value -> this.WithBinding(ParameterBinding.create key value)
        | None -> { paramMap = Map.remove key.Name this.paramMap }

    /// Format a single value for a text-based consumer such as a result query.
    member this.TryFindText(key: string<paramKeyName>) =
        Map.tryFind key this.paramMap |> Option.map (fun binding -> binding.FormattedValue)

    /// Explicitly project all bindings to canonical, culture-independent strings.
    member this.ToStringMap() =
        this.paramMap |> Map.map (fun _ binding -> binding.FormattedValue)
