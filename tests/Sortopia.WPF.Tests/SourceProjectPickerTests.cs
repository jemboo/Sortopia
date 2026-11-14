using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Sortopia.Functions;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Sortopia.WPF.Views.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class SourceProjectPickerTests
{
    [Fact]
    public void Incompatible_projects_stay_visible_with_reasons_and_become_selectable_when_the_function_supports_them()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { CheckPicker(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static void CheckPicker()
    {
        WorkspaceSourceProject Source(string name, string kind, string key, string location)
        {
            var settings = new projectSettingsDto {
                resultsSortopiaDataTypes = [new() { kind = kind, value = "" }],
                resultsDataTypes = [new() { dataType = new() { kind = kind, value = "" }, queryParameterKeys = [key] }]
            };
            return new(Guid.NewGuid(), name, true, settings, new WorkspaceTreeNode(WorkspaceEntityKind.Project, name).Editor!, Location: location);
        }
        var matching = Source("Standard tests", "SortableTests", "ChannelCount", "SortableTests / Standard");
        var prefix = Source("smallCC", "SortableTests", "PrefixLibId", "SortableTests / Prefix");
        var models = Source("smallCC", "SorterModelSet", "ChannelCount", "SorterModelSets / Standard / Msce");
        var model = new WorkspaceConfigurationViewModel(WorkspaceEntityKind.Module, true, () => null, () => "Msce", () => [matching, prefix, models]);
        model.SetupFunctionName = SetupFunctionNames.evaluationSource; model.ResultsFunctionName = ResultsFunctionNames.sorterEval;
        model.SetupDataTypesOverride.IsOverridden = true;
        var view = new WorkspaceConfigurationEditor { DataContext = model, ActiveItemKey = "setupOutputs" };
        var window = new Window { Content = view, Width = 620, Height = 900, Left = -20000, Top = -20000,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        window.Show();
        try
        {
            void Update() { view.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); window.UpdateLayout(); }
            ComboBox Picker()
            {
                Update(); var entries = (ItemsControl)view.FindName("SetupDataTypesList");
                var row = model.SetupDataTypes.Single(output => output.DataType.Kind == "SortableTests");
                var presenter = (ContentPresenter)entries.ItemContainerGenerator.ContainerFromItem(row);
                return (ComboBox)entries.ItemTemplate.FindName("SetupSourceProjectInput", presenter);
            }
            var picker = Picker(); picker.IsDropDownOpen = true; Update();
            var popup = (Popup)picker.Template.FindName("PART_Popup", picker); ((FrameworkElement)popup.Child).UpdateLayout();
            Assert.Equal(3, picker.Items.Count);
            var choice = picker.Items.Cast<WorkspaceSourceProjectChoice>().Single(item => item.Id == prefix.Id);
            var item = (ComboBoxItem)picker.ItemContainerGenerator.ContainerFromItem(choice);
            Assert.False(item.IsEnabled); Assert.Contains("PrefixLibId", choice.CompatibilityText);
            Assert.Equal(choice.CompatibilityText, AutomationProperties.GetHelpText(item));
            Assert.Equal(choice.CompatibilityText, item.ToolTip); Assert.True(ToolTipService.GetShowOnDisabled(item));
            Assert.True(((ComboBoxItem)picker.ItemContainerGenerator.ContainerFromIndex(0)).IsEnabled);
            if (Environment.GetEnvironmentVariable("SORTOPIA_SOURCE_PICKER_SNAPSHOT") is { Length: > 0 } path)
            {
                var content = (FrameworkElement)popup.Child;
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                var drawing = new DrawingVisual();
                using (var dc = drawing.RenderOpen()) dc.DrawRectangle(new VisualBrush(content), null, new Rect(content.RenderSize));
                bitmap.Render(drawing);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(path); encoder.Save(output);
            }
            picker.IsDropDownOpen = false;
            picker.SetCurrentValue(ComboBox.SelectedValueProperty, matching.Id); Update();
            Assert.Equal(matching.Id, model.SetupDataTypes.Single(output => output.DataType.Kind == "SortableTests").SourceProjectId);
            model.SetupFunctionName = SetupFunctionNames.evaluationSourcePrefix;
            picker = Picker(); picker.IsDropDownOpen = true; Update();
            choice = picker.Items.Cast<WorkspaceSourceProjectChoice>().Single(item => item.Id == prefix.Id);
            item = (ComboBoxItem)picker.ItemContainerGenerator.ContainerFromItem(choice);
            Assert.True(item.IsEnabled);
            picker.SetCurrentValue(ComboBox.SelectedValueProperty, prefix.Id); picker.IsDropDownOpen = false; Update();
            Assert.Equal(prefix.Id, model.SetupDataTypes.Single(output => output.DataType.Kind == "SortableTests").SourceProjectId);
            // A previously saved incompatible assignment remains visible for correction.
            model.SetupFunctionName = SetupFunctionNames.evaluationSource; Update();
            var selected = model.SetupDataTypes.Single(output => output.DataType.Kind == "SortableTests");
            Assert.Equal(prefix.Id, selected.SourceProjectId); Assert.Contains("PrefixLibId", selected.Error);
            Assert.Equal(prefix.Id, Picker().SelectedValue);
        }
        finally { window.Close(); }
    }
}
