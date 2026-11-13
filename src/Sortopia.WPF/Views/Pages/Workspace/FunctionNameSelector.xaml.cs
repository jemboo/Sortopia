using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sortopia.WPF.ViewModels.Pages.Workspace;

namespace Sortopia.WPF.Views.Pages.Workspace;

public partial class FunctionNameSelector : UserControl
{
    public static readonly DependencyProperty ChoicesProperty = DependencyProperty.Register(nameof(Choices), typeof(IEnumerable<string>),
        typeof(FunctionNameSelector), new PropertyMetadata(Array.Empty<string>(), InputsChanged));
    public static readonly DependencyProperty SelectedFunctionProperty = DependencyProperty.Register(nameof(SelectedFunction), typeof(string),
        typeof(FunctionNameSelector), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, InputsChanged));
    public static readonly DependencyProperty IsSetupCatalogProperty = DependencyProperty.Register(nameof(IsSetupCatalog), typeof(bool),
        typeof(FunctionNameSelector), new PropertyMetadata(false, InputsChanged));

    private string[] names = [];
    private FunctionNameNode[] roots = [];
    private FunctionNameNode? candidate;
    public IEnumerable<string> Choices { get => (IEnumerable<string>)GetValue(ChoicesProperty); set => SetValue(ChoicesProperty, value); }
    public string SelectedFunction { get => (string)GetValue(SelectedFunctionProperty); set => SetValue(SelectedFunctionProperty, value); }
    public bool IsSetupCatalog { get => (bool)GetValue(IsSetupCatalogProperty); set => SetValue(IsSetupCatalogProperty, value); }

    public FunctionNameSelector()
    {
        InitializeComponent();
        IsEnabledChanged += (_, _) => { if (!IsEnabled) PickerPopup.IsOpen = false; };
        IsVisibleChanged += (_, _) => { if (!IsVisible) PickerPopup.IsOpen = false; };
        DataContextChanged += (_, _) => PickerPopup.IsOpen = false;
        Unloaded += (_, _) => PickerPopup.IsOpen = false;
        Refresh();
    }

    private static void InputsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((FunctionNameSelector)sender).Refresh();
    private static IEnumerable<FunctionNameNode> Descendants(IEnumerable<FunctionNameNode> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Descendants(node.Children)));
    private void Refresh()
    {
        if (FunctionTree is null) return;
        var available = (Choices ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (!names.SequenceEqual(available))
        {
            names = available;
            roots = FunctionNameNode.Build(names);
            FunctionTree.ItemsSource = roots;
        }
        CurrentName.Text = string.IsNullOrEmpty(SelectedFunction) ? "Choose function…" : SelectedFunction;
        SelectCurrent();
    }

    private void SelectCurrent()
    {
        candidate = null;
        foreach (var node in Descendants(roots))
        {
            node.IsSelected = node.IsFunction && node.Path == SelectedFunction;
            if (SelectedFunction?.StartsWith(node.Path + ".", StringComparison.Ordinal) == true) node.IsExpanded = true;
        }
        candidate = Descendants(roots).FirstOrDefault(node => node.IsSelected);
        ShowCandidate();
    }
    private void ShowCandidate()
    {
        ChooseFunction.IsEnabled = candidate?.IsFunction == true && IsEnabled;
        CandidateName.Text = candidate?.IsFunction == true ? candidate.Path
            : names.Length == 0 ? "No compatible functions are available." : "Select a complete function name.";
        var preview = candidate?.IsFunction == true ? FunctionPreview.Find(candidate.Path, IsSetupCatalog) : null;
        FunctionDetails.DataContext = preview;
        FunctionDetails.Visibility = preview is null ? Visibility.Collapsed : Visibility.Visible;
        PreviewPlaceholder.Visibility = preview is null ? Visibility.Visible : Visibility.Collapsed;
        PreviewPlaceholder.Text = candidate?.IsFunction == true ? "Catalog details are unavailable for this function."
            : "Highlight a function to preview its description, required parameters, and output SortopiaDataTypes.";
        DetailsViewport.ScrollToTop();
    }
    private void OpenPicker_Click(object sender, RoutedEventArgs e)
    {
        SelectCurrent();
        PickerPopup.IsOpen = !PickerPopup.IsOpen;
    }
    private void PickerPopup_Opened(object? sender, EventArgs e) => FunctionTree.Focus();
    private void FunctionTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        candidate = e.NewValue as FunctionNameNode;
        ShowCandidate();
    }
    private void CommitSelection()
    {
        if (!IsEnabled || candidate?.IsFunction != true || !names.Contains(candidate.Path, StringComparer.Ordinal)) return;
        SetCurrentValue(SelectedFunctionProperty, candidate.Path);
        ClosePicker();
    }
    private void ClosePicker() { PickerPopup.IsOpen = false; OpenPicker.Focus(); }
    private void ChooseFunction_Click(object sender, RoutedEventArgs e) => CommitSelection();
    private void Cancel_Click(object sender, RoutedEventArgs e) => ClosePicker();
    private void FunctionTree_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { ClosePicker(); e.Handled = true; }
        else if (e.Key == Key.Enter && candidate?.IsFunction == true) { CommitSelection(); e.Handled = true; }
    }
    private void FunctionTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(FunctionTree, e.OriginalSource as DependencyObject) is TreeViewItem { DataContext: FunctionNameNode { IsFunction: true } node })
        {
            candidate = node;
            CommitSelection(); e.Handled = true;
        }
    }
}
