namespace Sortopia.Core

open System
open System.Globalization
open FSharp.UMX

module StringUtils = 

    let getTimestampString () =
        let now = DateTime.Now
        // MM-dd for month-day
        // HH:mm:ss.f for hours (24h):minutes:seconds.tenths
        now.ToString("MM-dd HH:mm:ss.fff")


module CompactStringParser =

    /// Extracts a key-value token handling nested balanced parentheses.
    let getValue (key: string) (input: string) : string =
        let prefix = key + "="
        let idx = input.IndexOf(prefix)
        if idx < 0 then
            failwithf "Key '%s' not found in compact string '%s'" key input
            
        let startIdx = idx + prefix.Length
        let mutable parenCount = 0
        let mutable endIdx = startIdx
        let mutable found = false

        while endIdx < input.Length && not found do
            let ch = input.[endIdx]
            match ch with
            | '(' -> 
                parenCount <- parenCount + 1
                endIdx <- endIdx + 1
            | ')' -> 
                if parenCount > 0 then
                    parenCount <- parenCount - 1
                    endIdx <- endIdx + 1
                else
                    found <- true
            | ',' when parenCount = 0 -> 
                found <- true
            | _ -> 
                endIdx <- endIdx + 1

        input.Substring(startIdx, endIdx - startIdx).Trim()

    let parseBool<[<Measure>] 'm> (key: string) (input: string) : bool<'m> =
        let raw = getValue key input
        match Boolean.TryParse(raw) with
        | true, v -> UMX.tag<'m> v
        | _ -> failwithf "Failed to parse bool for key '%s' in '%s'" key input

    let parseFloat<[<Measure>] 'm> (key: string) (input: string) : float<'m> =
        let raw = getValue key input
        match Double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture) with
        | true, v -> UMX.tag<'m> v
        | _ -> failwithf "Failed to parse float for key '%s' in '%s'" key input