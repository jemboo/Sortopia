namespace Sortopia.Models.Sorting


type simpleSorterModelType =
    | Msce
    | Mssi
    | Msrs
    | Msuf4
    | Msuf6


module SimpleSorterModelType =

    let toString (model:simpleSorterModelType) : string =
        match model with
        | Msce -> "Msce"
        | Mssi -> "Mssi"
        | Msrs -> "Msrs"
        | Msuf4 -> "Msuf4"
        | Msuf6 -> "Msuf6"

    let fromString (s:string) : simpleSorterModelType =
        match s with
        | "Msce" -> Msce
        | "Mssi" -> Mssi
        | "Msrs" -> Msrs
        | "Msuf4" -> Msuf4
        | "Msuf6" -> Msuf6
        | _ -> failwithf "Unknown SorterModelType: %s" s

    /// Match the channel-count constraints of the uniform model generators.
    let supportsChannelCount channelCount model =
        match model with
        | Msce -> channelCount >= 1
        | Mssi | Msrs -> channelCount >= 2
        | Msuf4 -> channelCount > 0 && (channelCount &&& (channelCount - 1)) = 0
        | Msuf6 -> channelCount >= 6 && channelCount % 2 = 0

    let withMsuf4 () : simpleSorterModelType list =
        [ Msce; Mssi; Msrs; Msuf4 ]

    let withMsuf6 () : simpleSorterModelType list =
        [ Msce; Mssi; Msrs; Msuf6 ]


type sorterModelType =
    | Simple of simpleSorterModelType
    | SimpleR of simpleSorterModelType
    | SimpleS of simpleSorterModelType
    | Unknown


module SorterModelType =

    let toString (model:sorterModelType) : string =
        match model with
        | Simple sms -> sms |> SimpleSorterModelType.toString
        | SimpleR sms -> (SimpleSorterModelType.toString sms) + "R"
        | SimpleS sms -> (SimpleSorterModelType.toString sms) + "S"
        | Unknown -> "Unknown"

    let fromString (s:string) : sorterModelType =
        match s with
        | "Unknown" -> Unknown
        | "Msce" -> Simple Msce
        | "Mssi" -> Simple Mssi
        | "Msrs" -> Simple Msrs
        | "Msuf4" -> Simple Msuf4
        | "Msuf6" -> Simple Msuf6
        | "MsceR" -> SimpleR Msce
        | "MsceS" -> SimpleS Msce
        | "MssiR" -> SimpleR Mssi
        | "MssiS" -> SimpleS Mssi
        | "MsrsR" -> SimpleR Msrs
        | "MsrsS" -> SimpleS Msrs
        | "Msuf4R" -> SimpleR Msuf4
        | "Msuf4S" -> SimpleS Msuf4
        | "Msuf6R" -> SimpleR Msuf6
        | "Msuf6S" -> SimpleS Msuf6
        | _ -> failwithf "Unknown SorterModelType: %s" s
