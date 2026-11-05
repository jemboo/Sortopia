# Sandbox SGD presentation

Sandbox follows [Fluent 2 design principles](https://fluent2.microsoft.design/design-principles),
[typography](https://fluent2.microsoft.design/typography), and
[layout guidance](https://fluent2.microsoft.design/layout): clear hierarchy, familiar controls,
consistent spacing, and visible interaction states.

- Use charcoal surfaces for the canvas, panels, and inputs. Reserve blue for selection and primary actions.
- Keep the shared spectral parameter, network usage, and graph annotation colors meaningful.
- Use Segoe UI, sentence-case headings, and a small set of text sizes. Align labels and values consistently.
- Prefer 4, 8, 12, 16, and 24 pixel spacing. Keep editable values tall enough for their text and padding.
- Keep focus, hover, selected, disabled, and Stop states distinct. Preserve keyboard and automation behavior.
- Retain the viewports and splitters so dense scientific controls fit narrower windows.

`SandboxWorkspace.xaml` is scoped to Sandbox. Do not merge it into the application resources:
the original SGD presentation must retain its existing appearance.

`SandboxSgdPage.xaml` and `SgdPage.xaml` contain presentation only. Both inherit the interaction
handlers in `SgdWorkspacePage`, and the shell supplies the same `SgdPageViewModel` instance to
both routes. Add workflow behavior to the shared model or interaction base, not either view.
Both use the existing parameter, network, and graph controls.
