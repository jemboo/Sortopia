namespace Sortopia.Workspaces.Persistence

open System
open System.Threading
open FSharp.UMX
open Sortopia.FileDb
open Sortopia.Functions
open Sortopia.Workspaces
open Sortopia.Workspaces.Serialization

type WorkspaceRunGenerationResult = {
    Workspace: SavedWorkspace
    Scope: RunExecutionScope
    Report: runParameterGenerationReport
}

/// Both UI and CLI use the same saved-draft to generated-snapshot transition.
module WorkspaceRunGeneration =
    let generate (saved: SavedWorkspace) (runId: Guid) (progress: IProgress<runExecutionProgress>) (token: CancellationToken) =
        let baseline = saved.Baseline
        let scope = saved.ResolveRun(runId)
        let mutable updated = saved
        let beforeWrite () =
            let parent, _ = WorkspaceProjectState.find (UMX.untag scope.Parent.Id) baseline
            if not parent.Definition.isSealed then
                let next = WorkspaceProjectState.seal scope.Parent baseline
                updated <- WorkspaceFileStore.SaveDocument(next, saved, token).Workspace
        let report = RunParameterExecution.generateInScopeBeforeWrite scope saved.RootFolder beforeWrite progress token
        { Workspace = updated; Scope = scope; Report = report }
