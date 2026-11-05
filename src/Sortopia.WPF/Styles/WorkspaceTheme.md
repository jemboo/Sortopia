# Workspace presentation

The shared theme extends the Sandbox design across Sortopia, following
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
Home, catalog playback, random networks, project runs, and both SGD presentations use these tokens.

`SgdPage.xaml` is the promoted Sandbox presentation. Its layout and `SandboxSgdPage.xaml` share
the interaction handlers in `SgdWorkspacePage` and the same `SgdPageViewModel` instance.
Workflow behavior belongs in the shared model or interaction base. Both reuse the parameter,
network, and graph controls.
