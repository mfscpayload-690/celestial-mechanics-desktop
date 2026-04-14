using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CelestialMechanics.Desktop.Models;
using CelestialMechanics.Desktop.ViewModels;
using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Desktop.Views.Panels;

public partial class BodyTypePalette : UserControl
{
    public BodyTypePalette()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WireCategoryToggle(StarToggle, BodyType.Star);
        WireCategoryToggle(PlanetToggle, BodyType.Planet);
        WireCategoryToggle(GasGiantToggle, BodyType.GasGiant);
        WireCategoryToggle(RockyToggle, BodyType.RockyPlanet);
        WireCategoryToggle(MoonToggle, BodyType.Moon);
        WireCategoryToggle(AsteroidToggle, BodyType.Asteroid);
        WireCategoryToggle(NeutronToggle, BodyType.NeutronStar);
        WireCategoryToggle(BlackHoleToggle, BodyType.BlackHole);
        WireCategoryToggle(CometToggle, BodyType.Comet);

        PopulateSubtypeButtons(StarSubtypes, BodyType.Star);
        PopulateSubtypeButtons(PlanetSubtypes, BodyType.Planet);
        PopulateSubtypeButtons(GasGiantSubtypes, BodyType.GasGiant);
        PopulateSubtypeButtons(RockySubtypes, BodyType.RockyPlanet);
        PopulateSubtypeButtons(MoonSubtypes, BodyType.Moon);
        PopulateSubtypeButtons(AsteroidSubtypes, BodyType.Asteroid);
        PopulateSubtypeButtons(NeutronSubtypes, BodyType.NeutronStar);
        PopulateSubtypeButtons(BlackHoleSubtypes, BodyType.BlackHole);
        PopulateSubtypeButtons(CometSubtypes, BodyType.Comet);
    }

    private void WireCategoryToggle(ToggleButton toggle, BodyType type)
    {
        toggle.Checked -= OnAnyToggleChecked;
        toggle.Checked += OnAnyToggleChecked;

        toggle.Click += (_, _) =>
        {
            if (DataContext is SimulationViewModel vm)
            {
                vm.SelectBodyType(type);
            }
        };
    }

    private void OnAnyToggleChecked(object sender, RoutedEventArgs e)
    {
        if (sender == StarToggle) PlanetToggle.IsChecked = GasGiantToggle.IsChecked = RockyToggle.IsChecked = MoonToggle.IsChecked = AsteroidToggle.IsChecked = NeutronToggle.IsChecked = BlackHoleToggle.IsChecked = CometToggle.IsChecked = false;
        if (sender == PlanetToggle) StarToggle.IsChecked = GasGiantToggle.IsChecked = RockyToggle.IsChecked = MoonToggle.IsChecked = AsteroidToggle.IsChecked = NeutronToggle.IsChecked = BlackHoleToggle.IsChecked = CometToggle.IsChecked = false;
        if (sender == GasGiantToggle) StarToggle.IsChecked = PlanetToggle.IsChecked = RockyToggle.IsChecked = MoonToggle.IsChecked = AsteroidToggle.IsChecked = NeutronToggle.IsChecked = BlackHoleToggle.IsChecked = CometToggle.IsChecked = false;
        if (sender == RockyToggle) StarToggle.IsChecked = PlanetToggle.IsChecked = GasGiantToggle.IsChecked = MoonToggle.IsChecked = AsteroidToggle.IsChecked = NeutronToggle.IsChecked = BlackHoleToggle.IsChecked = CometToggle.IsChecked = false;
        if (sender == MoonToggle) StarToggle.IsChecked = PlanetToggle.IsChecked = GasGiantToggle.IsChecked = RockyToggle.IsChecked = AsteroidToggle.IsChecked = NeutronToggle.IsChecked = BlackHoleToggle.IsChecked = CometToggle.IsChecked = false;
        if (sender == AsteroidToggle) StarToggle.IsChecked = PlanetToggle.IsChecked = GasGiantToggle.IsChecked = RockyToggle.IsChecked = MoonToggle.IsChecked = NeutronToggle.IsChecked = BlackHoleToggle.IsChecked = CometToggle.IsChecked = false;
        if (sender == NeutronToggle) StarToggle.IsChecked = PlanetToggle.IsChecked = GasGiantToggle.IsChecked = RockyToggle.IsChecked = MoonToggle.IsChecked = AsteroidToggle.IsChecked = BlackHoleToggle.IsChecked = CometToggle.IsChecked = false;
        if (sender == BlackHoleToggle) StarToggle.IsChecked = PlanetToggle.IsChecked = GasGiantToggle.IsChecked = RockyToggle.IsChecked = MoonToggle.IsChecked = AsteroidToggle.IsChecked = NeutronToggle.IsChecked = CometToggle.IsChecked = false;
        if (sender == CometToggle) StarToggle.IsChecked = PlanetToggle.IsChecked = GasGiantToggle.IsChecked = RockyToggle.IsChecked = MoonToggle.IsChecked = AsteroidToggle.IsChecked = NeutronToggle.IsChecked = BlackHoleToggle.IsChecked = false;
    }

    private void PopulateSubtypeButtons(ItemsControl host, BodyType bodyType)
    {
        host.Items.Clear();

        foreach (var subtype in BodyCatalog.GetSubtypes(bodyType))
        {
            var button = new Button
            {
                Content = subtype.Name,
                Margin = new Thickness(2),
                Padding = new Thickness(10, 5, 10, 5),
                Tag = subtype,
            };

            if (TryFindResource("ModalSecondaryButtonStyle") is Style style)
            {
                button.Style = style;
            }

            button.Click += (_, _) => SelectSubtype(subtype);
            host.Items.Add(button);
        }
    }

    private void SelectSubtype(BodySubtype subtype)
    {
        if (DataContext is SimulationViewModel vm)
        {
            vm.SelectSubtype(subtype);
            if (!vm.IsAddMode)
            {
                vm.EnterAddModeCommand.Execute(null);
            }
        }

        StarToggle.IsChecked = false;
        PlanetToggle.IsChecked = false;
        GasGiantToggle.IsChecked = false;
        RockyToggle.IsChecked = false;
        MoonToggle.IsChecked = false;
        AsteroidToggle.IsChecked = false;
        NeutronToggle.IsChecked = false;
        BlackHoleToggle.IsChecked = false;
        CometToggle.IsChecked = false;
    }
}
