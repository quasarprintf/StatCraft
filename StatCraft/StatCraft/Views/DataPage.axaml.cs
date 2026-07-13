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

            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "StatCraft");
            string dbPath = Path.Combine(appDataDir, "statcraft.db");

            _accountRepository = new AccountRepository(dbPath);
            _accountRepository.Initialize();

            _tokenProtector = new TokenProtector(Path.Combine(appDataDir, "statcraft.key"));
            _tokenProtector.Initialize();

            _authService = new BattleNetAuthService(new HttpClient());
            _sc2ProfileService = new StarCraft2ProfileService(new HttpClient());

            DataPageViewModel vm = new DataPageViewModel();
            vm.SessionRequested += async () => await OnSessionRequestedAsync();
            DataContext = vm;
        }

        private async Task OnSessionRequestedAsync()
        {
            if (!(TopLevel.GetTopLevel(this) is Window owner)) return;

            AccountPickerViewModel pickerVm = new AccountPickerViewModel(_accountRepository);
            AccountPickerResult? pickerResult = await new AccountPickerWindow(pickerVm).ShowDialog<AccountPickerResult?>(owner);

            if (pickerResult?.Outcome == AccountPickerOutcome.AccountSelected)
            {
                ViewModel.SetActiveAccount(pickerResult.Account);
            }
            else if (pickerResult?.Outcome == AccountPickerOutcome.LinkNew)
            {
                LinkAccountViewModel linkVm = new LinkAccountViewModel(_accountRepository, _tokenProtector, _authService, _sc2ProfileService);
                BattleNetAccount? linkedAccount = await new LinkAccountWindow(linkVm).ShowDialog<BattleNetAccount?>(owner);
                if (linkedAccount != null)
                    ViewModel.SetActiveAccount(linkedAccount);
            }
        }
    }
}
