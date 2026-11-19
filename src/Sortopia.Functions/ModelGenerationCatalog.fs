namespace Sortopia.Functions

/// Expand the supported variants once for each phase, using the shared definitions.
module internal ModelGenerationCatalog =
    let private variants group create = [
        for algorithm in ModelGenerators.algorithms do
            for modelName, constructor in ModelGenerators.constructors do
                yield $"{group}.{algorithm.Name}.{modelName}", create algorithm constructor ]

    let setup () =
        variants "sorter-model-set.uniform" ModelGenerationDefinitions.setupModels
        @ variants "sorter-eval-inputs.standard" ModelGenerationDefinitions.setupStandardInputs

    let results () =
        variants "sorter-model-set.uniform" (ModelGenerationDefinitions.resultsModels modelPrefixMode.NoPrefix)
        @ variants "sorter-model-set.prefix" (ModelGenerationDefinitions.resultsModels modelPrefixMode.CatalogPrefix)
        @ variants "sorter-eval-inputs.standard" ModelGenerationDefinitions.resultsStandardInputs
