using IPAbuyer.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Globalization;

namespace IPAbuyer.Views
{
    public sealed partial class IpatoolPage : Page
    {
        private static readonly ResourceLoader Loader = new();
        private const string IpatoolGitRevision = "dcddce4650d49d64aaff1b0785d76de01f5227af";
        private bool _isInitializing;

        public IpatoolPage()
        {
            InitializeComponent();
            InitializeMainGitRevision();
            InitializeSelection();
        }

        private void InitializeMainGitRevision()
        {
            string shortRevision = IpatoolGitRevision[..7];
            MainGitRevisionTextBlock.Text = LF("IpatoolPage/Main/GitRevisionShortFormat", shortRevision);
            ToolTipService.SetToolTip(
                MainGitRevisionTextBlock,
                LF("IpatoolPage/Main/GitRevisionTooltipFormat", IpatoolGitRevision));
        }

        private void InitializeSelection()
        {
            _isInitializing = true;
            try
            {
                string flavor = KeychainConfig.GetIpatoolFlavor();
                MainRadioButton.IsChecked = string.Equals(flavor, KeychainConfig.IpatoolFlavorMain, StringComparison.OrdinalIgnoreCase);
                LegacyRadioButton.IsChecked = !MainRadioButton.IsChecked;
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private void IpatoolFlavorRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || sender is not RadioButton radioButton || radioButton.Tag is not string flavor)
            {
                return;
            }

            KeychainConfig.SaveIpatoolFlavor(flavor);
        }

        private static string L(string key)
        {
            return Loader.GetString(key);
        }

        private static string LF(string key, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, L(key), args);
        }
    }
}
