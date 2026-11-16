namespace Sortopia.Functions

open FSharp.UMX
open Sortopia.Runs
open Sortopia.Models.Sorting

/// One generation contract and save routine, adapted to either execution phase.
module internal ModelGenerationDefinitions =
    let private modelsType = sortopiaDataType.SorterModelSet ""
    let private testsType = sortopiaDataType.SortableTests ""

    type private specification = {
        Metadata: modelGenerationMetadata
        RequiredParameters: paramKeyDescriptor list
        Outputs: (sortopiaDataType * string<paramKeyName> list) list
        InputError: parameterBinding list -> parameterSpan list -> string
        Description: runExecutionPhase -> string
        Save: IRunDatabase -> parameterSet -> unit
    }

    // Keep the algorithm's input keys and generator together for both phases.
    let private algorithm source = function
        | simpleSorterModelType.Msce -> ModelGenerators.msceKeys source, ModelGenerators.msce source
        | simpleSorterModelType.Mssi -> ModelGenerators.mssiKeys source, ModelGenerators.mssi source
        | simpleSorterModelType.Msrs -> ModelGenerators.msrsKeys source, ModelGenerators.msrs source
        | simpleSorterModelType.Msuf4 -> ModelGenerators.msuf4Keys source, ModelGenerators.msuf4 source
        | simpleSorterModelType.Msuf6 -> ModelGenerators.msuf6Keys source, ModelGenerators.msuf6 source

    let private create includeTests source modelType family =
        let keys, makeModels = algorithm source modelType
        let modelKeys = ModelGenerators.familyKeys family keys
        let modelOutput = modelsType, InputGenerators.queryKeys modelKeys
        let description = ModelGenerators.familyDescription source modelType family
        { Metadata = { Algorithm = modelType; Family = family; PrefixMode = source }
          RequiredParameters =
              if includeTests then InputGenerators.standardKeys @ modelKeys |> List.distinctBy (fun key -> key.Name)
              else modelKeys
          Outputs =
              if includeTests then [testsType, InputGenerators.queryKeys InputGenerators.standardKeys; modelOutput]
              else [modelOutput]
          InputError =
              if includeTests then InputGenerators.evaluationInputError InputGenerators.standardKeys modelType
              else ModelGenerators.inputError source modelType
          Description = fun phase ->
              match includeTests, phase with
              | true, runExecutionPhase.Setup -> $"Generate full binary sortable tests. {description} Save both inputs in the setup database."
              | true, _ -> $"Generate full binary sortable tests. {description} Save both outputs in the results database for use by other projects."
              | false, runExecutionPhase.Setup -> description + " Save the models in the setup database."
              | false, _ -> description + " Save the models in the results database."
          Save = fun database parameters ->
              // Keep each factory lazy and independent; models are published before tests.
              database.SaveIfMissing(modelsType, [], fun () -> makeModels family parameters)
              if includeTests then
                  database.SaveIfMissing(testsType, [], fun () -> InputGenerators.standardTests parameters) }

    let private describe phase spec definition =
        definition
        |> FunctionDefinition.describe (spec.Description phase)
        |> FunctionDefinition.generatesModels spec.Metadata.Algorithm spec.Metadata.Family spec.Metadata.PrefixMode

    let private setup includeTests modelType family =
        let spec = create includeTests modelPrefixMode.NoPrefix modelType family
        FunctionDefinition.setupValidated (ModelGenerators.version modelType) spec.RequiredParameters spec.Outputs spec.InputError
            (fun host _ -> spec.Save host.SetupDb host.Parameters)
        |> describe runExecutionPhase.Setup spec

    let private results includeTests source modelType family =
        let spec = create includeTests source modelType family
        FunctionDefinition.resultsValidated (ModelGenerators.version modelType) spec.RequiredParameters spec.Outputs spec.InputError
            (fun host -> spec.Save host.ResultsDb host.Parameters)
        |> describe runExecutionPhase.Results spec

    let setupModels = setup false
    let resultsModels = results false
    let setupStandardInputs = setup true
    let resultsStandardInputs = results true modelPrefixMode.NoPrefix
