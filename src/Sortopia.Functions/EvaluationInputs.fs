namespace Sortopia.Functions

open Sortopia.Models.Sorting

/// Compatibility rules shared by copied inputs and both evaluation functions.
/// Callers keep their existing validation order and storage boundaries.
module internal EvaluationInputs =
    let private requireWidth message channels model =
        if SorterModel.getChannelCount model <> channels then invalidArg "inputs" message

    let private requirePrefix message prefix model =
        let modelPrefix = SorterModel.getPrefix model
        if not (Array.isEmpty prefix) && not (Array.isEmpty modelPrefix) && modelPrefix <> prefix then
            invalidArg "inputs" message

    let validateCopiedModels channels prefix (models: sorterModelSet) =
        for model in models.SorterModels do
            requireWidth "Every source sorter model must have the same channel count as the source SortableTests." channels model
            requirePrefix "The source sorter model prefix must match the source SortableTests prefix." prefix model

    let validateEvaluationWidths channels models =
        for model in models do
            requireWidth "The models and tests must have the same channel count." channels model

    let validateEvaluationPrefixes prefix (models: sorterModelSet) =
        for model in models.SorterModels do
            requirePrefix "The model prefix must match the source SortableTests prefix. Regenerate models using the associated tests." prefix model
