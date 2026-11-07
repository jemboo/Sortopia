# Workspace presentation

The shared theme provides a consistent presentation across Sortopia, following
[Fluent 2 principles](https://fluent2.microsoft.design/design-principles),
[typography](https://fluent2.microsoft.design/typography), and
[layout guidance](https://fluent2.microsoft.design/layout).

- Use charcoal surfaces for the canvas, panels, and inputs. Reserve blue for selection and primary actions.
- Keep spectral parameters, network usage, and graph annotation colors meaningful.
- Use Segoe UI, sentence-case headings, aligned labels and values, and compact navigation.
- Prefer 4, 8, 12, 16, and 24 pixel spacing. Keep editable values tall enough for their text and padding.
- Keep hover, focus, selected, disabled, and Stop states distinct; preserve keyboard and automation behavior.
- Retain viewports and splitters for dense scientific controls.

`WorkspaceTheme.xaml` is the application theme. The older `Navigation.xaml` and
`NetworkWorkspace.xaml` resource paths forward to it so existing consumers share the same styles.
Home, Workspace, catalog playback, random networks, and SGD use these tokens.

`Views/Pages/Sgd/SgdPage.xaml` hosts the Setup, Sampling, and Evolution subpages from
`Views/Pages/Sgd/SubPages`, using the retained `ViewModels/Pages/Sgd/SgdPageViewModel.cs`
instance. Workflow behavior belongs in the matching page and subpage view models.
Sampling and Evolution share `Views/Shared/Controls/SgdResultsView.xaml` and its
annotation/history view models in `ViewModels/Shared/Controls`. Reusable visual controls,
including the graph, network diagram, and parameter editor, live in `Views/Shared/Controls`.
`Views/Pages/Workspace/WorkspacePage.xaml` groups Save and Discard beside the workspace name, with creation/import actions above the hierarchy and entity editor. Retained folders appear in a compact expandable notice after saving.
