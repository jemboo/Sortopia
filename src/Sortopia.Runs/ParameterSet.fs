namespace Sortopia.Runs

open FSharp.UMX

type parameterSet =
    private { paramMap : Map<string<paramKeyName>, string> }

    static member create paramMap = { paramMap = paramMap }

    member this.ParamMap = this.paramMap

    member this.TryFind key = Map.tryFind key this.paramMap

    /// Some adds or replaces the value. None removes the key.
    member this.WithValue(key, value) =
        { paramMap = Map.change key (fun _ -> value) this.paramMap }

    /// Missing keys and values the key cannot parse both yield None.
    member this.TryFind(key: paramKey<'value>) =
        Map.tryFind key.Name this.paramMap |> Option.bind key.TryParse

    member this.WithValue(key: paramKey<'value>, value: 'value option) =
        this.WithValue(key.Name, Option.map key.Format value)
