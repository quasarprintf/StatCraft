using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models;
using StatCraft.Services;

namespace StatCraft.ViewModels
{
    public enum AccountPickerOutcome { AccountSelected, LinkNew, Cancelled }

    public record AccountPickerResult(AccountPickerOutcome Outcome, BattleNetAccount? Account);

    public partial class AccountPickerViewModel : ViewModelBase
    {
        public AccountPickerViewModel(AccountRepository accountRepository)
        {
            Accounts = new ObservableCollection<BattleNetAccount>(accountRepository.GetAllAccounts());
        }

        public ObservableCollection<BattleNetAccount> Accounts { get; }

        [ObservableProperty] private BattleNetAccount? _selectedAccount;

        public event Action<AccountPickerResult>? Closed;

        [RelayCommand]
        private void SelectAccount() => SelectAccount(SelectedAccount);
        public void SelectAccount(BattleNetAccount? account) => Closed?.Invoke(new AccountPickerResult(AccountPickerOutcome.AccountSelected, account));

        [RelayCommand]
        private void LinkNewAccount() => Closed?.Invoke(new AccountPickerResult(AccountPickerOutcome.LinkNew, null));

        [RelayCommand]
        private void Cancel() => Closed?.Invoke(new AccountPickerResult(AccountPickerOutcome.Cancelled, null));
    }
}
