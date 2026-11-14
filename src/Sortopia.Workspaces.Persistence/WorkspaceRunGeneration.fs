namespace Sortopia.Workspaces.Persistence

open System
open System.Threading
open FSharp.UMX
open Sortopia.FileDb
open Sortopia.Functions
open Sortopia.Workspaces
open Sortopia.Workspaces.Serialization

type WorkspaceRunGenerationResult = {
    Document: WorkspaceDocument
    Scope: runWorkspace
    Report: runParameterGenerationReport
}

/// Both UI and CLI use the same saved-draft to generated-snapshot transition.
module WorkspaceRunGeneration =
    let generate root (saved: WorkspaceDocument) (runId: Guid) (progress: IProgress<runExecutionProgress>) (token: CancellationToken) =
        let baseline = WorkspaceDisk.documentBaseline saved
        let scope = WorkspaceRunDocument.resolveByRunId baseline runId
        let mutable document = saved
        let beforeWrite () =
            let parent, _ = WorkspaceProjectState.find (UMX.untag scope.Parent.Id) baseline
            if not parent.Definition.isSealed then
                let next = WorkspaceProjectState.seal scope.Parent baseline
                document <- WorkspaceFileStore.SaveDocument(root, next, saved, token).Definition
        let report = RunParameterExecution.generateInScopeBeforeWrite scope root beforeWrite progress token
        { Document = document; Scope = scope; Report = report }
