using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IPAbuyer.Models
{
    public class SearchResult : INotifyPropertyChanged
    {
        private string? _bundleId;
        private string? _id;
        private string? _name;
        private string? _developer;
        private string? _artworkUrl;
        private string? _price;
        private string? _version;
        private string? _purchased;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string? bundleId
        {
            get => _bundleId;
            set => SetProperty(ref _bundleId, value);
        }

        public string? id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        public string? name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string? developer
        {
            get => _developer;
            set => SetProperty(ref _developer, value);
        }

        public string? artworkUrl
        {
            get => _artworkUrl;
            set => SetProperty(ref _artworkUrl, value);
        }

        public string? price
        {
            get => _price;
            set => SetProperty(ref _price, value);
        }

        public string? version
        {
            get => _version;
            set => SetProperty(ref _version, value);
        }

        public string? purchased
        {
            get => _purchased;
            set => SetProperty(ref _purchased, value);
        }

        private void SetProperty(ref string? storage, string? value, [CallerMemberName] string? propertyName = null)
        {
            if (storage == value)
            {
                return;
            }

            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
