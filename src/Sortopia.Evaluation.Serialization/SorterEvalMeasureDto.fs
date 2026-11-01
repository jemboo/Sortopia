namespace Sortopia.Evaluation.Serialization

open MessagePack
open FSharp.UMX
open MessagePack
open Sortopia.Evaluation

// --- Individual Case DTOs ---

[<MessagePackObject>]
type ceLengthMeasureDto = {
    [<Key(0)>] FilterUnsorted: bool
    [<Key(1)>] FilterReflectionSymmetric: bool
}

[<MessagePackObject>]
type stageLengthMeasureDto = {
    [<Key(0)>] FilterUnsorted: bool
    [<Key(1)>] FilterReflectionSymmetric: bool
}

[<MessagePackObject>]
type unsortedCountMeasureDto = {
    [<Key(0)>] FilterReflectionSymmetric: bool
}

[<MessagePackObject>]
type ceStMeasureDto = {
    [<Key(0)>] StageWeight: float
    [<Key(1)>] FilterUnsorted: bool
    [<Key(2)>] FilterReflectionSymmetric: bool
    [<Key(3)>] StageCrossingWeight: float
}

[<MessagePackObject>]
type ceStUcMeasureDto = {
    [<Key(0)>] StageWeight: float
    [<Key(1)>] UnsortedWeight: float
    [<Key(2)>] FilterReflectionSymmetric: bool
}

// --- Main Measure Union DTO ---

type sorterEvalMeasureDto =
    | CeLength of ceLengthMeasureDto
    | StageLength of stageLengthMeasureDto
    | UnsortedCount of unsortedCountMeasureDto
    | CeSt of ceStMeasureDto
    | CeStUc of ceStUcMeasureDto

// --- Mapping Module ---

module SorterEvalMeasureDto =

    let fromDomain (domain: sorterEvalMeasure) : sorterEvalMeasureDto =
        match domain with
        | sorterEvalMeasure.CeLength m ->
            CeLength { 
                FilterUnsorted = %m.FilterUnsorted
                FilterReflectionSymmetric = %m.FilterReflectionSymmetric 
            }
        | sorterEvalMeasure.StageLength m ->
            StageLength { 
                FilterUnsorted = %m.FilterUnsorted
                FilterReflectionSymmetric = %m.FilterReflectionSymmetric 
            }
        | sorterEvalMeasure.UnsortedCount m ->
            UnsortedCount { 
                FilterReflectionSymmetric = %m.FilterReflectionSymmetric 
            }
        | sorterEvalMeasure.CeSt m ->
            CeSt { 
                StageWeight = %m.StageWeight
                FilterUnsorted = %m.FilterUnsorted
                FilterReflectionSymmetric = %m.FilterReflectionSymmetric 
                StageCrossingWeight = %m.StageCrossingWeight
            }
        | sorterEvalMeasure.CeStUc m ->
            CeStUc { 
                StageWeight = %m.StageWeight
                UnsortedWeight = %m.UnsortedWeight
                FilterReflectionSymmetric = %m.FilterReflectionSymmetric 
            }

    let toDomain (dto: sorterEvalMeasureDto) : sorterEvalMeasure =
        match dto with
        | CeLength d ->
            sorterEvalMeasure.CeLength (ceLengthMeasure.create d.FilterUnsorted d.FilterReflectionSymmetric)
        | StageLength d ->
            sorterEvalMeasure.StageLength (stageLengthMeasure.create d.FilterUnsorted d.FilterReflectionSymmetric)
        | UnsortedCount d ->
            sorterEvalMeasure.UnsortedCount (unsortedCountMeasure.create d.FilterReflectionSymmetric)
        | CeSt d ->            
            sorterEvalMeasure.CeSt (ceStMeasure.create {
                StageWeight = d.StageWeight |> UMX.tag
                FilterUnsorted = d.FilterUnsorted |> UMX.tag
                FilterReflectionSymmetric = d.FilterReflectionSymmetric |> UMX.tag
                StageCrossingWeight = d.StageCrossingWeight |> UMX.tag
            })
        | CeStUc d ->
            sorterEvalMeasure.CeStUc (ceStUcMeasure.create {
                StageWeight = d.StageWeight |> UMX.tag
                UnsortedWeight = d.UnsortedWeight |> UMX.tag
                FilterReflectionSymmetric = d.FilterReflectionSymmetric |> UMX.tag
            })

    let pack (domain: sorterEvalMeasure) : byte[] =
        domain |> fromDomain |> MessagePackSerializer.Serialize

    let unpack (bytes: byte[]) : sorterEvalMeasure =
        MessagePackSerializer.Deserialize<sorterEvalMeasureDto>(bytes) |> toDomain
