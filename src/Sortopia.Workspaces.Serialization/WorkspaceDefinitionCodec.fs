namespace Sortopia.Workspaces.Serialization

open System
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Workspaces
open Sortopia.Core.Serialization

[<CLIMutable>]
type childFolderReferenceDto = { id: Guid; folderName: string }
[<CLIMutable>]
type inheritedFromDto = { moduleId: Guid; moduleName: string }
[<CLIMutable>]
type workspaceDefinitionDto = {
    schemaVersion: int; id: Guid; workspaceName: string; folderName: string
    modules: childFolderReferenceDto array
}
[<CLIMutable>]
type moduleDefinitionDto = {
    schemaVersion: int; id: Guid; moduleKind: string; moduleName: string; folderName: string
    localOverrides: moduleOverridesDto
    inheritedDefaults: moduleSettingsDto
    inheritedFrom: inheritedFromDto
    modules: childFolderReferenceDto array
    projects: childFolderReferenceDto array
}
[<CLIMutable>]
type projectDefinitionDto = {
    schemaVersion: int; id: Guid; projectKind: string; projectName: string; folderName: string; isSealed: bool
    // Sealed projects own their fixed settings; drafts own overrides and parent context.
    configuration: projectSettingsDto
    localOverrides: projectOverridesDto
    inheritedDefaults: projectSettingsDto
    inheritedFrom: inheritedFromDto
    runs: Guid array
}

/// Versioned, shallow entity files. Child references are authoritative and ordered.
/// File resolution and hierarchy assembly belong to Workspaces.Persistence.
module WorkspaceDefinitionCodec =
    let schemaVersion = 15
    let private kindName = function hierarchyKind.Root -> "root" | hierarchyKind.Branch -> "branch" | _ -> invalidArg "kind" "Unknown entity family."
    let kind = function "root" -> hierarchyKind.Root | "branch" -> hierarchyKind.Branch | _ -> invalidArg "kind" "Kind must be root or branch."
    let private validate version id name folder =
        if version <> schemaVersion then invalidArg "schemaVersion" $"Unsupported entity definition version: {version}. Create a new workspace in the current format."
        EntityId.validate id
        EntityName.validate name |> ignore
        FolderName.validate folder |> ignore
    let private references name (values: childFolderReferenceDto array) =
        if isNull values then invalidArg name "Required child references are missing."
        let ids = Collections.Generic.HashSet<Guid>()
        let folders = Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
        for value in values do
            if isNull (box value) then invalidArg name "A child reference is missing."
            EntityId.validate value.id
            FolderName.validate value.folderName |> ignore
            if not (ids.Add value.id) || not (folders.Add value.folderName) then invalidArg name "Duplicate child ID or folder reference."
    let private origin (value: inheritedFromDto) =
        if not (isNull (box value)) then
            EntityId.validate value.moduleId
            EntityName.validate value.moduleName |> ignore
    let private required name value = if isNull (box value) then invalidArg name $"Missing {name}."
    let private moduleRef (value: workspaceModule) = { id = UMX.untag value.Id; folderName = value.FolderName }
    let private projectRef (value: project) = { id = UMX.untag value.Id; folderName = value.FolderName }

    let workspace (value: workspace) : workspaceDefinitionDto = {
        schemaVersion = schemaVersion; id = UMX.untag value.Id; workspaceName = string value.WorkspaceName; folderName = value.FolderName
        modules = value.Modules |> Array.map moduleRef }
    let moduleWithOrigin inheritedFrom (value: workspaceModule) : moduleDefinitionDto = {
        schemaVersion = schemaVersion; id = UMX.untag value.Id; moduleName = string value.ModuleName; folderName = value.FolderName; moduleKind = kindName value.Kind
        localOverrides = ModuleOverridesDto.fromDomain value.LocalOverrides; inheritedDefaults = ModuleSettingsDto.fromDomain value.InheritedDefaults
        inheritedFrom = inheritedFrom; modules = value.Modules |> Array.map moduleRef; projects = value.Projects |> Array.map projectRef }
    let projectWithOrigin inheritedFrom (value: project) : projectDefinitionDto = {
        schemaVersion = schemaVersion; id = UMX.untag value.Id; projectName = string value.ProjectName; folderName = value.FolderName; projectKind = kindName value.Kind
        isSealed = value.IsSealed
        configuration = if value.IsSealed then SettingsDto.fromDomain value.Configuration else Unchecked.defaultof<_>
        localOverrides = if value.IsSealed then Unchecked.defaultof<_> else OverridesDto.fromDomain value.LocalOverrides
        inheritedDefaults = if value.IsSealed then Unchecked.defaultof<_> else SettingsDto.fromDomain value.InheritedDefaults
        inheritedFrom = if value.IsSealed then Unchecked.defaultof<_> else inheritedFrom
        runs = value.Runs |> Array.map (fun value -> UMX.untag value.Id) }
    let encodeWorkspace value = workspace value |> MessagePackSetup.serialize
    let encodeModule value = moduleWithOrigin Unchecked.defaultof<_> value |> MessagePackSetup.serialize
    let encodeProject value = projectWithOrigin Unchecked.defaultof<_> value |> MessagePackSetup.serialize

    let decodeWorkspace bytes =
        let value = MessagePackSetup.deserialize<workspaceDefinitionDto> bytes
        required "workspace definition" value
        validate value.schemaVersion value.id value.workspaceName value.folderName
        references "modules" value.modules
        value
    let decodeModule bytes =
        let value = MessagePackSetup.deserialize<moduleDefinitionDto> bytes
        required "module definition" value
        validate value.schemaVersion value.id value.moduleName value.folderName
        kind value.moduleKind |> ignore
        required "inherited defaults" value.inheritedDefaults
        required "local overrides" value.localOverrides
        origin value.inheritedFrom
        references "modules" value.modules
        references "projects" value.projects
        value
    let decodeProject bytes =
        let value = MessagePackSetup.deserialize<projectDefinitionDto> bytes
        required "project definition" value
        validate value.schemaVersion value.id value.projectName value.folderName
        kind value.projectKind |> ignore
        if isNull value.runs then invalidArg "runs" "Required run references are missing."
        value.runs |> Array.iter EntityId.validate
        if (Array.distinct value.runs).Length <> value.runs.Length then invalidArg "runs" "Duplicate run reference."
        if value.isSealed then required "sealed configuration" value.configuration
        else
            if value.runs.Length > 0 then invalidArg "runs" "Projects containing saved runs must be sealed."
            required "inherited defaults" value.inheritedDefaults
            required "local overrides" value.localOverrides
            origin value.inheritedFrom
        value

    let moduleHeader parent (value: moduleDefinitionDto) =
        let kind = kind value.moduleKind
        let captured = ModuleSettingsDto.toDomain kind value.inheritedDefaults
        workspaceModule.createInherited (UMX.tag value.id) (UMX.tag value.moduleName) value.folderName
            (Option.defaultValue captured parent) (ModuleOverridesDto.toDomain kind value.localOverrides) [||] [||]
    let projectHeader parent (value: projectDefinitionDto) =
        let kind = kind value.projectKind
        let configuration, local =
            if value.isSealed then SettingsDto.toDomain kind value.configuration, Defaults.inherited (kind = hierarchyKind.Branch)
            else
                let captured = SettingsDto.toDomain kind value.inheritedDefaults
                Option.defaultValue captured parent, OverridesDto.toDomain kind value.localOverrides
        project.restore (UMX.tag value.id) (UMX.tag value.projectName) value.folderName configuration local value.isSealed [||]
