namespace Sortopia.Workspaces.Serialization

open FSharp.UMX
open Sortopia.Runs
open Sortopia.Functions
open Sortopia.Workspaces

/// Typed settings are resolved only at an explicit validation or execution boundary.
module WorkspaceDefinitionResolution =
    let moduleConfiguration parent (value: ModuleDefinition) =
        WorkspaceDefinitionValidation.moduleDefinition value |> ignore
        let kind = WorkspaceDefinitionCodec.kind value.moduleKind
        let captured = ModuleSettingsDto.toDomain kind value.inheritedDefaults
        ModuleDefaults.resolve (Option.defaultValue captured parent) (ModuleOverridesDto.toDomain kind value.localOverrides)

    let projectConfiguration (parent: projectConfiguration option) (value: ProjectDefinition) =
        WorkspaceDefinitionValidation.project value |> ignore
        let kind = WorkspaceDefinitionCodec.kind value.projectKind
        if parent |> Option.exists (fun inherited -> inherited.HasSetup <> (kind = hierarchyKind.Branch)) then
            invalidArg "parent" "Project settings must match the inherited setup phase."
        let configuration =
            match value.state with
            | Sealed(snapshot, _) -> SettingsDto.toDomain kind snapshot
            | Draft(local, captured, _) ->
                let inherited = Option.defaultValue (SettingsDto.toDomain kind captured) parent
                ProjectResolution.resolve inherited (ProjectLocalSettingsDto.toDomain inherited local)
        ProjectConfiguration.validate configuration
        if value.isSealed then FunctionConfiguration.validateResults configuration.Results
        configuration.Setup |> Option.iter (FunctionConfiguration.validateSetup true)
        configuration

    /// A candidate snapshot does not seal the document; generation persists it before writes.
    let projectSnapshot (value: ProjectDefinition) =
        let configuration = projectConfiguration None value
        ProjectSnapshot.create (UMX.tag value.id) (UMX.tag value.projectName) configuration
