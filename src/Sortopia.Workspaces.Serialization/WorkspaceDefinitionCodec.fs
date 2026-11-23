namespace Sortopia.Workspaces.Serialization

open System
open Sortopia.Runs
open Sortopia.Workspaces
open Sortopia.Core.Serialization
open MessagePack

[<CLIMutable>]
type childReferenceDto = { id: Guid; name: string }
[<CLIMutable>]
type inheritedFromDto = { moduleId: Guid; moduleName: string }
[<CLIMutable>]
type workspaceDefinitionDto = {
    schemaVersion: int; id: Guid; workspaceName: string; folderName: string
    modules: childReferenceDto array
}
[<CLIMutable>]
type moduleDefinitionDto = {
    schemaVersion: int; id: Guid; moduleKind: string; moduleName: string
    localOverrides: moduleOverridesDto
    inheritedDefaults: moduleSettingsDto
    inheritedFrom: inheritedFromDto
    modules: childReferenceDto array
    projects: childReferenceDto array
}
/// Editing text is preserved verbatim; only a sealed snapshot is executable.
type projectStateDto =
    | Draft of LocalSettings: projectLocalSettingsDto * InheritedDefaults: projectSettingsDto * InheritedFrom: inheritedFromDto
    | Sealed of Configuration: projectSettingsDto * EditingSettings: projectLocalSettingsDto option

module ProjectLifecycle =
    let localSettings = function Draft(local, _, _) -> local | Sealed(_, local) -> Option.defaultValue Unchecked.defaultof<_> local
    let configuration = function Sealed(snapshot, _) -> snapshot | Draft _ -> Unchecked.defaultof<_>
    let inheritedDefaults = function Draft(_, inherited, _) -> inherited | Sealed _ -> Unchecked.defaultof<_>
    let inheritedFrom = function Draft(_, _, origin) -> origin | Sealed _ -> Unchecked.defaultof<_>
    let withOrigin origin = function Draft(local, inherited, _) -> Draft(local, inherited, origin) | state -> state
    let seal snapshot = function
        | Draft(local, _, _) -> Sealed(snapshot, Some local)
        | Sealed(_, local) -> Sealed(snapshot, local)
    /// Reference remapping preserves both execution and retained editing state.
    let map settings local origin = function
        | Draft(values, inherited, parent) -> Draft(local values, settings inherited, origin parent)
        | Sealed(snapshot, editing) -> Sealed(settings snapshot, editing |> Option.map local)

/// Document definitions contain only their own editable values. Children belong to documents.
[<CLIMutable>]
type WorkspaceDefinition = { id: Guid; workspaceName: string; folderName: string }
[<CLIMutable>]
type ModuleDefinition = {
    id: Guid; moduleKind: string; moduleName: string
    localOverrides: moduleOverridesDto
    inheritedDefaults: moduleSettingsDto
    inheritedFrom: inheritedFromDto
}
[<CLIMutable>]
type ProjectDefinition = {
    id: Guid; projectKind: string; projectName: string
    state: projectStateDto
} with
    [<IgnoreMember>] member this.isSealed = this.state.IsSealed
    [<IgnoreMember>] member this.configuration = ProjectLifecycle.configuration this.state
    [<IgnoreMember>] member this.localSettings = ProjectLifecycle.localSettings this.state
    [<IgnoreMember>] member this.inheritedDefaults = ProjectLifecycle.inheritedDefaults this.state
    [<IgnoreMember>] member this.inheritedFrom = ProjectLifecycle.inheritedFrom this.state

[<CLIMutable>]
type projectDefinitionDto = {
    schemaVersion: int; id: Guid; projectKind: string; projectName: string
    state: projectStateDto
    runs: Guid array
} with
    [<IgnoreMember>] member this.isSealed = this.state.IsSealed
    [<IgnoreMember>] member this.configuration = ProjectLifecycle.configuration this.state
    [<IgnoreMember>] member this.localSettings = ProjectLifecycle.localSettings this.state
    [<IgnoreMember>] member this.inheritedDefaults = ProjectLifecycle.inheritedDefaults this.state
    [<IgnoreMember>] member this.inheritedFrom = ProjectLifecycle.inheritedFrom this.state

/// Structural validation leaves unfinished configuration text untouched.
module WorkspaceDefinitionValidation =
    let kind = function "root" -> hierarchyKind.Root | "branch" -> hierarchyKind.Branch | _ -> invalidArg "kind" "Kind must be root or branch."
    let private identity id name folder =
        EntityId.validate id
        EntityName.validate name |> ignore
        FolderName.validate folder |> ignore
    let private origin (value: inheritedFromDto) =
        if not (isNull (box value)) then
            EntityId.validate value.moduleId
            EntityName.validate value.moduleName |> ignore
    let private required name value = if isNull (box value) then invalidArg name $"Missing {name}."
    let workspace (value: WorkspaceDefinition) =
        required "workspace definition" value
        identity value.id value.workspaceName value.folderName
        value
    let moduleDefinition (value: ModuleDefinition) =
        required "module definition" value
        identity value.id value.moduleName value.moduleName
        kind value.moduleKind |> ignore
        required "inherited defaults" value.inheritedDefaults
        required "local overrides" value.localOverrides
        origin value.inheritedFrom
        value
    let project (value: ProjectDefinition) =
        required "project definition" value
        identity value.id value.projectName value.projectName
        kind value.projectKind |> ignore
        required "project state" value.state
        if value.isSealed then required "sealed configuration" value.configuration
        else
            required "inherited defaults" value.inheritedDefaults
            required "local settings" value.localSettings
            origin value.inheritedFrom
        value

/// Versioned, shallow entity files. References are validated on read and derived on write.
module WorkspaceDefinitionCodec =
    let schemaVersion = 22
    let supportsVersion value = value = schemaVersion
    let kind value = WorkspaceDefinitionValidation.kind value
    let private version value =
        if not (supportsVersion value) then invalidArg "schemaVersion" $"Unsupported entity definition version: {value}. Create a new workspace in the current format."
    let private required name value = if isNull (box value) then invalidArg name $"Missing {name}."
    let private references name (values: childReferenceDto array) =
        if isNull values then invalidArg name "Required child references are missing."
        let ids = Collections.Generic.HashSet<Guid>()
        let folders = Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
        for value in values do
            if isNull (box value) then invalidArg name "A child reference is missing."
            EntityId.validate value.id
            FolderName.validate value.name |> ignore
            if not (ids.Add value.id) || not (folders.Add value.name) then invalidArg name "Duplicate child ID or folder reference."

    let workspaceDefinition (value: workspaceDefinitionDto) : WorkspaceDefinition =
        { id = value.id; workspaceName = value.workspaceName; folderName = value.folderName }
    let moduleDefinition (value: moduleDefinitionDto) : ModuleDefinition =
        { id = value.id; moduleKind = value.moduleKind; moduleName = value.moduleName
          localOverrides = value.localOverrides; inheritedDefaults = value.inheritedDefaults; inheritedFrom = value.inheritedFrom }
    let projectDefinition (value: projectDefinitionDto) : ProjectDefinition =
        { id = value.id; projectKind = value.projectKind; projectName = value.projectName; state = value.state }

    let validateWorkspace (value: workspaceDefinitionDto) =
        required "workspace definition" value
        version value.schemaVersion
        workspaceDefinition value |> WorkspaceDefinitionValidation.workspace |> ignore
        references "modules" value.modules
        value
    let validateModule (value: moduleDefinitionDto) =
        required "module definition" value
        version value.schemaVersion
        moduleDefinition value |> WorkspaceDefinitionValidation.moduleDefinition |> ignore
        references "modules" value.modules
        references "projects" value.projects
        FolderName.unique "module and project children" (Seq.append (value.modules |> Seq.map _.name) (value.projects |> Seq.map _.name))
        value
    let validateProject (value: projectDefinitionDto) =
        required "project definition" value
        version value.schemaVersion
        projectDefinition value |> WorkspaceDefinitionValidation.project |> ignore
        if isNull value.runs then invalidArg "runs" "Required run references are missing."
        value.runs |> Array.iter EntityId.validate
        if (Array.distinct value.runs).Length <> value.runs.Length then invalidArg "runs" "Duplicate run reference."
        value

    let decodeWorkspace bytes = bytes |> MessagePackSetup.deserialize<workspaceDefinitionDto> |> validateWorkspace
    let decodeModule bytes = bytes |> MessagePackSetup.deserialize<moduleDefinitionDto> |> validateModule
    let decodeProject bytes = bytes |> MessagePackSetup.deserialize<projectDefinitionDto> |> validateProject
