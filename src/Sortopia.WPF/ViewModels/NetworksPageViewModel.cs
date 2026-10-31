using Sortopia.WPF.Models;
namespace Sortopia.WPF.ViewModels;
public sealed class NetworksPageViewModel : NetworkPlaybackViewModel
{
    public NetworksPageViewModel(Action returnHome) : this(returnHome, CatalogNetwork.LoadAll()) { }
    private NetworksPageViewModel(Action returnHome, IReadOnlyList<CatalogNetwork> networks)
        : base(returnHome, networks.First(n => n.Width == 8)) => Networks = networks;
    public IReadOnlyList<CatalogNetwork> Networks { get; }
    public new CatalogNetwork SelectedNetwork { get => (CatalogNetwork)base.SelectedNetwork; set => base.SelectedNetwork = value; }
}
