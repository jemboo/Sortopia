namespace Sortopia.Runs

open FSharp.UMX
open Sortopia.Storage

[<Measure>] type databaseName
[<Measure>] type projectName
[<Measure>] type runName


/// A run whose execution has no dependencies on other runs.
type rootRun =
    private
        {
          dataBaseName: string<databaseName>
          projName: string<projectName>
          runName: string<runName>
          description: string
          parameterSpans: parameterSpan list
          executeFunctionName: string
          outputDataTypes: outputDataType list
        }

    member this.DatabaseName = this.dataBaseName
    member this.ProjectName = this.projName
    member this.RunName = this.runName
    member this.Description = this.description
    member this.ParameterSpans = this.parameterSpans
    member this.ExecuteFunctionName = this.executeFunctionName
    member this.OutputDataTypes = this.outputDataTypes

module RootRun =
    let create databaseName projectName runName description parameterSpans executeFunctionName outputDataTypes =
        {
          dataBaseName = databaseName
          projName = projectName
          runName = runName
          description = description
          parameterSpans = parameterSpans
          executeFunctionName = executeFunctionName
          outputDataTypes = outputDataTypes
        }

type run =
    | RootRun of rootRun

    member this.DatabaseName = let (RootRun value) = this in value.DatabaseName
    member this.ProjectName = let (RootRun value) = this in value.ProjectName
    member this.RunName = let (RootRun value) = this in value.RunName
    member this.Description = let (RootRun value) = this in value.Description
    member this.ParameterSpans = let (RootRun value) = this in value.ParameterSpans
    member this.ExecuteFunctionName = let (RootRun value) = this in value.ExecuteFunctionName
    member this.OutputDataTypes = let (RootRun value) = this in value.OutputDataTypes
