using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using IPAbuyer.Data;

namespace IPAbuyer.Views
{
    public sealed partial class PurchasedListPage : Page
    {
        public class PurchasedAppView
        {
            public string bundleID { get; set; }
            public string name { get; set; }
            public string version { get; set; }
        }

        public PurchasedListPage()
        {
            this.InitializeComponent();
            LoadPurchasedApps();
        }

        private void LoadPurchasedApps()
        {
            var list = new List<PurchasedAppView>();
            var dbList = PurchasedAppDb.GetPurchasedApps();
            foreach (var item in dbList)
            {
                list.Add(new PurchasedAppView
                {
                    bundleID = item.bundleID,
                    name = item.name,
                    version = item.version
                });
            }
            PurchasedList.ItemsSource = list;
        }

        private void BackToSearchButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(SearchPage));
        }
    }
}
