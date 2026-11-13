namespace Sortopia.Runs

open System

/// Folder names are single portable path segments, compared case-insensitively among siblings.
module FolderName =
    let validate (name: string) =
        if String.IsNullOrWhiteSpace name || name <> name.Trim() || name.EndsWith('.') ||
           name.IndexOfAny("<>:\"/\\|?*".ToCharArray()) >= 0 || (name |> Seq.exists Char.IsControl) then
            invalidArg "folderName" "Use a nonblank folder name without path separators, reserved characters, or trailing spaces/periods."
        let stem = (name.Split('.') |> Array.head).ToUpperInvariant()
        let reserved = [ "CON"; "PRN"; "AUX"; "NUL"; "CLOCK$" ] @
                       [ for i in 1..9 do yield $"COM{i}"; yield $"LPT{i}" ]
        if List.contains stem reserved then invalidArg "folderName" "This folder name is reserved by Windows."
        if name.StartsWith(".sortopia-", StringComparison.OrdinalIgnoreCase) then
            invalidArg "folderName" "Folder names beginning with '.sortopia-' are reserved for workspace operations."
        name

    let unique argument (names: string seq) =
        let seen = Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
        for name in names do
            validate name |> ignore
            if not (seen.Add name) then invalidArg argument $"Duplicate folder name in {argument}: {name}"
