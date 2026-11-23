namespace Sortopia.Functions

open System
open FSharp.UMX
open Sortopia.Runs

module FunctionConfiguration =
    let private version name saved current =
        if String.IsNullOrWhiteSpace saved then invalidArg "functionVersion" $"Function '{name}' needs a saved version."
        if saved <> current then invalidOp $"Function '{name}' is fixed at version {saved} in this project, but the installed version is {current}. Create a new project version to execute the current function. Existing saved data remains readable."

    let validateOutputs (definition: functionDefinition<'executor>) (outputs: Map<sortopiaDataType, string<paramKeyName> list>) =
        if (outputs |> Map.toList |> List.map fst) <> definition.SortopiaDataTypes then
            invalidArg "dataTypes" "Saved data types, including their text, must exactly match the selected function."
        let required = (if definition.ParametersFromSources then ParamKeys.all else definition.RequiredParameters) |> List.map (fun key -> key.Name) |> Set.ofList
        for dataType in definition.SortopiaDataTypes do
            let actual = outputs[dataType]
            if not (Set.isSubset (Set.ofList actual) required) || actual.Length <> (Set.ofList actual).Count then
                invalidArg "queryParameterKeys" $"Query keys for '{dataType}' must be unique required parameters of the selected function."

    // A sealed snapshot remains readable when the installed implementation changes.
    let validateResults (config: resultsConfig) =
        let definition = ResultsFunctionCatalog.require config.FunctionName
        if String.IsNullOrWhiteSpace config.FunctionVersion then invalidArg "resultsFunctionVersion" "A results function version is required."
        if config.FunctionVersion = definition.Version then
            validateOutputs definition config.DataTypes
            FunctionValidation.validateDefaults definition config.ParameterDefaults

    let requiresSource name dataType =
        let definition = SetupFunctionCatalog.require name
        List.contains dataType definition.SourceDataTypes

    let validateSetupSource (config: setupConfig) (entry: setupDataTypeConfig) (source: sourceProjectSnapshot) =
        match SetupFunctionCatalog.functions |> Map.tryFind config.FunctionName with
        | Some definition when definition.ParametersFromSources && config.FunctionVersion = definition.Version ->
            let contract = SourceContracts.resolve source.Results.DataTypes[entry.DataType]
            SourceContracts.requireParameters contract |> ignore
            let outside = SourceContracts.missingQueryKeys entry.QueryParameterKeys contract
            if not outside.IsEmpty then
                let names = System.String.Join(", ", outside)
                invalidArg "queryParameterKeys" $"Setup query keys for '{entry.DataType}' must be a subset of source project '{source.ProjectName}' results query keys. Unsupported keys: {names}."
        | _ -> ()

    let validateSetup requireSources (config: setupConfig) =
        let definition = SetupFunctionCatalog.require config.FunctionName
        if String.IsNullOrWhiteSpace config.FunctionVersion then invalidArg "setupFunctionVersion" "A setup function version is required."
        if config.FunctionVersion = definition.Version then
            validateOutputs definition config.DataTypes
            FunctionValidation.validateDefaults definition config.ParameterDefaults
            if definition.ParametersFromSources then
                let parameters = parameterSet.fromBindings config.ParameterDefaults
                FunctionValidation.validate definition parameters
            if requireSources then
                for dataType in definition.SourceDataTypes do
                    if config.Outputs |> List.exists (fun entry -> entry.DataType = dataType && UMX.untag entry.SourceProjectId <> Guid.Empty) |> not then
                        invalidArg "sourceProjectId" $"Choose a source project for '{dataType}'; the selected setup function consumes this input."

    let private validateInputs executable (value: run) =
        let results = ResultsFunctionCatalog.require value.ResultsFunctionName
        value.Reporting |> Option.iter (fun reporting ->
            SgdReporting.validate reporting |> ignore
            if not results.SupportsEvolutionReporting then invalidArg "reporting" "The selected results function does not support evolution reporting."
            let counts = seq {
                for binding in value.ResultsParameterDefaults do
                    if binding.KeyName = ParamKeys.generationCountKey.Name then yield binding.FormattedValue
                for span in value.ResultsParameterSpans do
                    if span.KeyName = ParamKeys.generationCountKey.Name then yield! span.Values }
            if value.Sgd.IsNone then SgdReporting.validateGenerationCounts reporting counts)
        value.Sgd |> Option.iter (fun controls ->
            SgdRun.validate controls |> ignore
            if not results.SupportsEvolutionReporting then invalidArg "sgd" "Only SGD runs support snapshot controls.")
        let config = value.ProjectConfiguration.Results
        if executable && not (config.FunctionName = ResultsFunctionNames.standardMsuf4SimpleSgd && config.FunctionVersion = "6") then
            version (string config.FunctionName) config.FunctionVersion results.Version
        let results = if executable then ResultsFunctionCatalog.requireVersion config.FunctionName config.FunctionVersion else results
        if executable && results.SupportsEvolutionReporting && results.Version = "7" && value.Sgd.IsNone then
            invalidArg "sgd" "Set SnapshotInterval and SnapshotCount on the SGD run."
        if config.FunctionVersion = results.Version then
            FunctionValidation.validateInputs results value.ResultsQueryParameterKeys value.ResultsParameterDefaults value.ResultsParameterSpans
        match value.Setup with
        | None ->
            if executable && not results.SourceDataTypes.IsEmpty then invalidArg "resultsFunctionName" "This results function requires a branch run with setup data."
        | Some settings ->
            let spans = value.SetupParameterSpans.Value
            let setup = SetupFunctionCatalog.require settings.FunctionName
            if executable then version (string settings.FunctionName) settings.FunctionVersion setup.Version
            if settings.FunctionVersion = setup.Version then
                let setup =
                    if not setup.ParametersFromSources then setup else
                    let resolved = setup.SourceDataTypes |> List.forall (fun dataType -> Map.containsKey dataType value.ResolvedSources)
                    if executable && not resolved then invalidArg "sourceProjectId" "Resolve both source projects before executing the copy setup."
                    let contract =
                        if resolved then
                            settings.Outputs |> List.map (fun entry ->
                                let contract = SourceContracts.resolve value.ResolvedSources[entry.DataType].Results.DataTypes[entry.DataType]
                                let outside = SourceContracts.missingQueryKeys entry.QueryParameterKeys contract
                                let outsideNames = System.String.Join(", ", outside)
                                if not outside.IsEmpty then invalidArg "queryParameterKeys" $"Setup query keys for '{entry.DataType}' must be a subset of its source results query keys. Unsupported keys: {outsideNames}."
                                contract) |> SourceContracts.combine
                        else (settings.ParameterDefaults |> List.map _.KeyName) @ (spans |> List.map _.KeyName)
                             |> SourceContracts.resolve
                    { setup with RequiredParameters = SourceContracts.requireParameters contract }
                FunctionValidation.validateInputs setup
                    (settings.Outputs |> List.collect (fun output -> output.QueryParameterKeys)) settings.ParameterDefaults spans
            if executable && results.SourceDataTypes |> List.exists (fun input -> not (List.contains input setup.SortopiaDataTypes)) then
                invalidArg "functions" "The setup function does not provide all data types required by the results function."
    let validateRunInputs value = validateInputs true value
    let validateSavedRunInputs value = validateInputs false value
