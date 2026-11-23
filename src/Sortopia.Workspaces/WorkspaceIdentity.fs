namespace Sortopia.Workspaces

open System

/// Display names are independent of file-system names.
module EntityName =
    let validate (name: string) =
        if String.IsNullOrWhiteSpace name || name <> name.Trim() || (name |> Seq.exists Char.IsControl) then
            invalidArg "name" "Use a nonblank name without leading/trailing whitespace or control characters."
        name

module EntityId =
    let validate (id: System.Guid) =
        if id = System.Guid.Empty then invalidArg "id" "Entity ID must not be empty."
    let internal unique argument (ids: System.Guid seq) =
        let seen = Collections.Generic.HashSet<System.Guid>()
        for id in ids do
            validate id
            if not (seen.Add id) then invalidArg argument $"Duplicate ID in {argument}: {id}"

type hierarchyKind =
    | Root = 0
    | Branch = 1
