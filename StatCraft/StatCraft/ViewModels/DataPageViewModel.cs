using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models;

namespace StatCraft.ViewModels
{
    public partial class DataPageViewModel : ViewModelBase
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ActiveAccountLabel))]
        private BattleNetAccount? _activeAccount;

        public string ActiveAccountLabel => ActiveAccount is null ? "No active session" : ActiveAccount.DisplayName;

        public event Action? SessionRequested;

        [RelayCommand]
        private void BeginSession() => SessionRequested?.Invoke();

        public void SetActiveAccount(BattleNetAccount? account) => ActiveAccount = account;
    }
}
