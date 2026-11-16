namespace Sortopia.Functions

open Sortopia.Runs
open Sortopia.Models.Sorting

/// Expand the supported variants once for each phase, using the shared definitions.
module internal ModelGenerationCatalog =
    // These tokens are persisted function identities, independent of display/type names.
    let private algorithmNames = Map.ofList [
        simpleSorterModelType.Msce, "msce"
        simpleSorterModelType.Mssi, "mssi"
        simpleSorterModelType.Msrs, "msrs"
        simpleSorterModelType.Msuf4, "msuf4"
        simpleSorterModelType.Msuf6, "msuf6" ]
    let private familyNames = Map.ofList [
        modelFamily.Simple, "simple"
        modelFamily.SimpleR, "simple-r"
        modelFamily.SimpleS, "simple-s" ]

    let private name (definition: functionDefinition<'executor>) =
        let metadata = definition.ModelGeneration |> Option.defaultWith (fun () ->
            invalidOp "Model-generation catalog entries require model-generation metadata.")
        let group =
            match Map.containsKey (sortopiaDataType.SortableTests "") definition.DataTypes, metadata.PrefixMode with
            | false, modelPrefixMode.NoPrefix -> "sorter-model-set.uniform"
            | false, modelPrefixMode.CatalogPrefix -> "sorter-model-set.prefix"
            | true, modelPrefixMode.NoPrefix -> "sorter-eval-inputs.standard"
            | true, modelPrefixMode.CatalogPrefix -> invalidOp "Combined catalog generators require models without a fixed prefix."
        $"{group}.{algorithmNames[metadata.Algorithm]}.{familyNames[metadata.Family]}"

    let private variants create = [
        for KeyValue(algorithm, _) in algorithmNames do
            for KeyValue(family, _) in familyNames do
                let definition = create algorithm family
                yield name definition, definition ]

    let setup () =
        variants ModelGenerationDefinitions.setupModels
        @ variants ModelGenerationDefinitions.setupStandardInputs

    let results () =
        variants (ModelGenerationDefinitions.resultsModels modelPrefixMode.NoPrefix)
        @ variants (ModelGenerationDefinitions.resultsModels modelPrefixMode.CatalogPrefix)
        @ variants ModelGenerationDefinitions.resultsStandardInputs
