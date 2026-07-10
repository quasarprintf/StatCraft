using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls;
using StatCraft.Models;
using StatCraft.Services;
using StatCraft.ViewModels;

namespace StatCraft.Views
{
    public partial class DataPage : UserControl
    {
        private readonly AccountRepository _accountRepository;
        private readonly TokenProtector _tokenProtector;
        private readonly BattleNetAuthService _authService;
        private readonly StarCraft2ProfileService _sc2ProfileService;

        private DataPageViewModel ViewModel => (DataPageViewModel)DataContext!;

        public DataPage()
        {
            InitializeComponent();

            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "StatCraft");
            var dbPath = Path.Combine(appDataDir, "statcraft.db");

            _accountRepository = new AccountRepository(dbPath);
            _accountRepository.Initialize();

            _tokenProtector = new TokenProtector(Path.Combine(appDataDir, "statcraft.key"));
            _tokenProtector.Initialize();

            _authService = new BattleNetAuthService(new HttpClient());
            _sc2ProfileService = new StarCraft2ProfileService(new HttpClient());

            var vm = new DataPageViewModel();
            vm.SessionRequested += async () => await OnSessionRequestedAsync();
            DataContext = vm;
        }

        private async Task OnSessionRequestedAsync()
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;

            var pickerVm = new AccountPickerViewModel(_accountRepository);
            var pickerResult = await new AccountPickerWindow(pickerVm).ShowDialog<AccountPickerResult?>(owner);

            if (pickerResult is { Outcome: AccountPickerOutcome.AccountSelected })
            {
                ViewModel.SetActiveAccount(pickerResult.Account);
            }
            else if (pickerResult is { Outcome: AccountPickerOutcome.LinkNew })
            {
                var linkVm = new LinkAccountViewModel(_accountRepository, _tokenProtector, _authService, _sc2ProfileService);
                var linkedAccount = await new LinkAccountWindow(linkVm).ShowDialog<BattleNetAccount?>(owner);
                if (linkedAccount is not null)
                    ViewModel.SetActiveAccount(linkedAccount);
            }
        }
    }
}
