using Microsoft.UI.Xaml.Navigation;

namespace IPAbuyer
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window window = Window.Current;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
            IPAbuyer.Data.PurchasedAppDb.InitDb();
            IPAbuyer.Data.AccountHistoryDb.InitDb();
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            window ??= new Window();

            if (window.Content is not Frame rootFrame)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                window.Content = rootFrame;
            }

            // 检查本地登录状态
            bool isLoggedIn = IPAbuyer.Data.AccountHistoryDb.GetAccounts().Count > 0 && !IPAbuyer.Data.AccountHistoryDb.IsLogoutFlag();
            if (isLoggedIn)
            {
                rootFrame.Navigate(typeof(IPAbuyer.Views.SearchPage), e.Arguments);
            }
            else
            {
                rootFrame.Navigate(typeof(IPAbuyer.Views.LoginPage), e.Arguments);
            }
            window.Activate();
        }

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation failure</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }
    }
}
