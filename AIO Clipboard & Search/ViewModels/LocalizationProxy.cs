using AIO_Hybrid_Clipboard.Services;
using System.ComponentModel;

namespace AIO_Hybrid_Clipboard.ViewModels
{
    /// <summary>
    /// Bindable indexer over the localized string table, e.g.
    /// <c>{Binding Loc[HideOnCopy]}</c>. Call <see cref="Refresh"/> after a
    /// language change to re-evaluate every bound label at once.
    /// </summary>
    public sealed class LocalizationProxy : INotifyPropertyChanged
    {
        private readonly SettingsService _settings;

        internal LocalizationProxy(SettingsService settings) => _settings = settings;

        public string this[string key] => _settings.T(key);

        public event PropertyChangedEventHandler? PropertyChanged;

        internal void Refresh() =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}
