namespace Sortopia.Runs

/// Expanded inputs for a run. Root runs have no setup array.
type runParameterSets = {
    Results: parameterSet array
    Setup: parameterSet array option
}

module RunParameterSets =
    /// Expand each function's defaults and spans independently, preserving typed bindings.
    /// Defaults and spans have disjoint keys; defaults appear in every combination.
    /// No spans produces one set containing the defaults, even when that set is empty.
    let create (value: run) : runParameterSets =
        if isNull (box value) then nullArg "value"
        match value with
        | run.RootRun root ->
            { Results = RootRun.makeResultsParameterSets root; Setup = None }
        | run.BranchRun branch ->
            { Results = BranchRun.makeResultsParameterSets branch
              Setup = Some (BranchRun.makeSetupParameterSets branch) }
