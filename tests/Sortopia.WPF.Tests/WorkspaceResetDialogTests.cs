using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Sortopia.WPF.Views.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceResetDialogTests
{
    [Fact]
    public void Confirmation_identifies_scope_has_copyable_path_and_defaults_to_cancel()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var request = new WorkspaceResetRequest("GuiSpace / SorterSgd / Simple_msuf4-cc16-prefix / best-source-pools", false, 1, 1);
                var dialog = new WorkspaceResetDialog(request);
                var content = (FrameworkElement)dialog.Content;
                dialog.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                var border = new Border { Width = 560, Background = dialog.Background, Resources = dialog.Resources };
                System.Windows.Documents.TextElement.SetForeground(border, dialog.Foreground);
                dialog.Content = null; border.Child = content; content.DataContext = request;
                border.Measure(new Size(560, double.PositiveInfinity));
                border.Arrange(new Rect(0, 0, 560, border.DesiredSize.Height)); border.UpdateLayout();
                var cancel = (Button)dialog.FindName("CancelResetButton");
                var reset = (Button)dialog.FindName("ConfirmResetButton");
                var path = (TextBox)dialog.FindName("ResetPath");
                Assert.True(cancel.IsCancel); Assert.True(cancel.IsDefault); Assert.False(reset.IsDefault);
                Assert.True(path.IsReadOnly); Assert.True(path.IsEnabled); Assert.Equal(request.Path, path.Text);
                Assert.Contains("1 project, 1 run", request.Description);
                Assert.Contains("cannot be undone", request.EditingNotice);
                Assert.True(reset.ActualWidth >= 155);
                if (Environment.GetEnvironmentVariable("SORTOPIA_RESET_SCREENSHOT") is { Length: > 0 } output)
                {
                    var bitmap = new RenderTargetBitmap(560, (int)Math.Ceiling(border.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(border);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(output); encoder.Save(stream);
                }
                dialog.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
