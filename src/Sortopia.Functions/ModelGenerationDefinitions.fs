namespace Sortopia.Functions

open FSharp.UMX
open Sortopia.Runs

/// One generation contract and save routine, adapted to either execution phase.
module internal ModelGenerationDefinitions =
    let private modelsType = sortopiaDataType.SorterModelSet ""
    let private testsType = sortopiaDataType.SortableTests ""

    type private specification = {
        ParameterSupport: functionParameterSupport
        RequiredParameters: paramKeyDescriptor list
        Outputs: (sortopiaDataType * string<paramKeyName> list) list
        InputError: parameterBinding list -> parameterSpan list -> string
        Description: runExecutionPhase -> string
        Save: runHost -> unit
    }

    let private create includeTests source (algorithm: simpleModelAlgorithm) constructor =
        let modelKeys = ModelGenerators.keys source algorithm constructor
        let modelOutput = modelsType, InputGenerators.queryKeys modelKeys
        let description = algorithm.Description source constructor
        let support = algorithm.ParameterSupport includeTests source constructor
        let modelInputError defaults spans =
            let error = algorithm.InputError source defaults spans
            if error <> "" then error
            else
                try
                    constructor.Validate defaults spans
                    ""
                with error -> error.Message
        let defaults (context: parameterDefaultContext) =
            // Constructors own their defaults; expose only requested missing keys, in descriptor order.
            let proposed = support.Defaults context @ constructor.Defaults context
                           |> List.map (fun binding -> binding.KeyName, binding) |> Map.ofList
            context.Missing |> List.choose (fun key -> Map.tryFind key.Name proposed)
        { ParameterSupport = { support with Defaults = defaults }
          RequiredParameters =
              if includeTests then InputGenerators.standardKeys @ modelKeys |> List.distinctBy (fun key -> key.Name)
              else modelKeys
          Outputs =
              if includeTests then [testsType, InputGenerators.queryKeys InputGenerators.standardKeys; modelOutput]
              else [modelOutput]
          InputError =
              if includeTests then InputGenerators.evaluationInputError InputGenerators.standardKeys modelInputError
              else modelInputError
          Description = fun phase ->
              match includeTests, phase with
              | true, runExecutionPhase.Setup -> $"Generate full binary sortable tests. {description} Save both inputs in the setup database."
              | true, _ -> $"Generate full binary sortable tests. {description} Save both outputs in the results database for use by other projects."
              | false, runExecutionPhase.Setup -> description + " Save the models in the setup database."
              | false, _ -> description + " Save the models in the results database."
          Save = fun host ->
              // Keep each factory lazy and independent; models are published before tests.
              host.Database.Save(host.Outputs.Output(modelsType, []), fun () -> ModelGenerators.models source algorithm constructor host.Parameters)
              if includeTests then
                  host.Database.Save(host.Outputs.Output(testsType, []), fun () -> InputGenerators.standardTests host.Parameters) }

    let private describe phase spec definition =
        definition
        |> FunctionDefinition.describe (spec.Description phase)
        |> FunctionDefinition.withParameterSupport spec.ParameterSupport

    let private setup includeTests algorithm constructor =
        let spec = create includeTests modelPrefixMode.NoPrefix algorithm constructor
        FunctionDefinition.setupValidated algorithm.Version spec.RequiredParameters spec.Outputs spec.InputError
            (fun host _ -> spec.Save host)
        |> describe runExecutionPhase.Setup spec

    let private results includeTests source algorithm constructor =
        let spec = create includeTests source algorithm constructor
        FunctionDefinition.resultsValidated algorithm.Version spec.RequiredParameters spec.Outputs spec.InputError
            spec.Save
        |> describe runExecutionPhase.Results spec

    let setupModels = setup false
    let resultsModels = results false
    let setupStandardInputs = setup true
    let resultsStandardInputs = results true modelPrefixMode.NoPrefix
