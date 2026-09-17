using System.Windows.Controls;
using EngineLayer.DatabaseLoading;
using GuiFunctions;

namespace MetaMorpheusGUI
{
    /// <summary>
    /// Interaction logic for FastaHeaderParsingControl.xaml. The panel is bound to
    /// <see cref="FastaHeaderParsingViewModel"/>; the two methods below exist so the five task windows
    /// keep their one-line round trip.
    /// </summary>
    public partial class FastaHeaderParsingControl : UserControl
    {
        public FastaHeaderParsingControl()
        {
            InitializeComponent();
            DataContext = ViewModel;
        }

        public FastaHeaderParsingViewModel ViewModel { get; } = new();

        public void SetFromParameters(FastaHeaderParsingParameters parameters) =>
            ViewModel.SetFromParameters(parameters);

        public FastaHeaderParsingParameters ToParameters() => ViewModel.ToParameters();
    }
}
