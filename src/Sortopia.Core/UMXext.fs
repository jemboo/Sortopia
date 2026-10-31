namespace Sortopia.Core

open System.Globalization
open FSharp.UMX

module UmxExt =


    let boolToRaw (v: bool<'u>) : string = (UMX.untag v).ToString()
    
    /// Converts a UMX-tagged float to a string using G17 precision and InvariantCulture
    let floatToRaw (v: float<'u>) : string = 
        (UMX.untag v).ToString("G17", CultureInfo.InvariantCulture)
        
    let intToRaw (v: int<'u>) : string = (UMX.untag v).ToString()
    let uint64ToRaw (v: uint64<'u>) : string = (UMX.untag v).ToString()
    let stringToRaw (v: string<'u>) : string = %v
    let guidToRaw (v: Guid<'u>) : string = (UMX.untag v).ToString()


    /// Converts any UMX-tagged int option to a string
    let intOptionToString (v: int<'u> option) : string =
        match v with
        | Some value -> (UMX.untag value).ToString()
        | None -> "None"

    /// Converts any UMX-tagged uint64 option to a string
    let uint64OptionToString (v: uint64<'u> option) : string =
        match v with
        | Some value -> (UMX.untag value).ToString()
        | None -> "None"


    /// Converts any UMX-tagged float option to a string using G17 precision and InvariantCulture
    let floatOptionToString (v: float<'u> option) : string =
        match v with
        | Some value -> (UMX.untag value).ToString("G17", CultureInfo.InvariantCulture)
        | None -> "None"

    let guidOptionToString (v: Guid<'u> option) : string =
        match v with
        | Some value -> (UMX.untag value).ToString()
        | None -> "None"
