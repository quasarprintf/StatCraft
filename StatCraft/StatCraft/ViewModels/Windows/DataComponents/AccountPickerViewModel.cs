using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models.Battlenet;
using StatCraft.Services.DatabaseRepository;

namespace StatCraft.ViewModels
{
    public enum AccountPickerOutcome { AccountSelected, LinkNew, Cancelled }

    public record AccountPickerResult(AccountPickerOutcome Outcome, Sc2Profile? Profile);

    public partial class AccountPickerViewModel : ViewModelBase
    {
        public AccountPickerViewModel(AccountRepository accountRepository)
        {
            Profiles = new ObservableCollection<Sc2Profile>(accountRepository.GetAllProfiles());
        }

        public ObservableCollection<Sc2Profile> Profiles { get; }

        [ObservableProperty] private Sc2Profile? _selectedProfile;

        public event Action<AccountPickerResult>? Closed;

        [RelayCommand]
        private void SelectAccount() => SelectAccount(SelectedProfile);
        public void SelectAccount(Sc2Profile? profile) => Closed?.Invoke(new AccountPickerResult(AccountPickerOutcome.AccountSelected, profile));

        [RelayCommand]
        private void LinkNewAccount() => Closed?.Invoke(new AccountPickerResult(AccountPickerOutcome.LinkNew, null));

        [RelayCommand]
        private void Cancel() => Closed?.Invoke(new AccountPickerResult(AccountPickerOutcome.Cancelled, null));
    }
}
