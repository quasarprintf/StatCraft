using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models;
using StatCraft.Services;

namespace StatCraft.ViewModels
{
    public enum LinkAccountStage { EnterCredentials, Connecting, SelectingProfile, Failed }

    public partial class LinkAccountViewModel : ViewModelBase
    {
        private const string ClientIdSettingKey = "BlizzardClientId";
        private const string ClientSecretSettingKey = "BlizzardClientSecretEncryptedB64";

        private readonly AccountRepository _accountRepository;
        private readonly TokenProtector _tokenProtector;
        private readonly BattleNetAuthService _authService;
        private readonly StarCraft2ProfileService _sc2ProfileService;
        private CancellationTokenSource? _linkCts;

        public LinkAccountViewModel(
            AccountRepository accountRepository,
            TokenProtector tokenProtector,
            BattleNetAuthService authService,
            StarCraft2ProfileService sc2ProfileService)
        {
            _accountRepository = accountRepository;
            _tokenProtector = tokenProtector;
            _authService = authService;
            _sc2ProfileService = sc2ProfileService;
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsEnterCredentials), nameof(IsConnecting), nameof(IsSelectingProfile), nameof(IsFailed))]
        private LinkAccountStage _stage;

        public bool IsEnterCredentials => Stage == LinkAccountStage.EnterCredentials;
        public bool IsConnecting => Stage == LinkAccountStage.Connecting;
        public bool IsSelectingProfile => Stage == LinkAccountStage.SelectingProfile;
        public bool IsFailed => Stage == LinkAccountStage.Failed;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SubmitCredentialsCommand))]
        private string _clientId = "";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SubmitCredentialsCommand))]
        private string _clientSecret = "";

        [ObservableProperty] private string _statusMessage = "";

        public ObservableCollection<Sc2Profile> Sc2Profiles { get; } = [];

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConfirmProfileCommand))]
        private Sc2Profile? _selectedSc2Profile;

        public Sc2Profile? LinkedProfile { get; private set; }

        public event Action<bool>? Closed;

        public async Task InitializeAsync()
        {
            string? clientId = _accountRepository.GetSetting(ClientIdSettingKey);
            string? encryptedSecretB64 = _accountRepository.GetSetting(ClientSecretSettingKey);

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(encryptedSecretB64))
            {
                Stage = LinkAccountStage.EnterCredentials;
                return;
            }

            ClientId = clientId;
            ClientSecret = _tokenProtector.Decrypt(Convert.FromBase64String(encryptedSecretB64));
            await StartLinkingAsync();
        }

        private bool CanSubmitCredentials() => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

        [RelayCommand(CanExecute = nameof(CanSubmitCredentials))]
        private async Task SubmitCredentials()
        {
            _accountRepository.SetSetting(ClientIdSettingKey, ClientId);
            _accountRepository.SetSetting(ClientSecretSettingKey, Convert.ToBase64String(_tokenProtector.Encrypt(ClientSecret)));
            await StartLinkingAsync();
        }

        private async Task StartLinkingAsync()
        {
            Stage = LinkAccountStage.Connecting;
            _linkCts = new CancellationTokenSource();

            try
            {
                BattleNetTokenResult result = await _authService.LinkAccountAsync(ClientId, ClientSecret, _linkCts.Token);
                List<Sc2Profile> fetchedProfiles = await _sc2ProfileService.GetProfilesAsync(result.AccountSub, result.AccessToken, _linkCts.Token);

                if (fetchedProfiles.Count == 0)
                {
                    StatusMessage = "No StarCraft II profiles were found on this Battle.net account.";
                    Stage = LinkAccountStage.Failed;
                    return;
                }

                byte[] encryptedAccessToken = _tokenProtector.Encrypt(result.AccessToken);
                byte[]? encryptedRefreshToken = result.RefreshToken == null ? null : _tokenProtector.Encrypt(result.RefreshToken);

                BattleNetAccount? account = _accountRepository.FindByAccountSub(result.AccountSub);
                if (account == null)
                {
                    account = new BattleNetAccount
                    {
                        BattleTag = result.BattleTag,
                        AccountSub = result.AccountSub,
                        EncryptedAccessToken = encryptedAccessToken,
                        EncryptedRefreshToken = encryptedRefreshToken,
                        TokenExpiresAtUtc = result.ExpiresAtUtc,
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    };
                    _accountRepository.InsertAccount(account);
                }
                else
                {
                    _accountRepository.UpdateAccountTokens(account.Id, encryptedAccessToken, encryptedRefreshToken, result.ExpiresAtUtc, result.BattleTag);
                    account.BattleTag = result.BattleTag;
                    account.EncryptedAccessToken = encryptedAccessToken;
                    account.EncryptedRefreshToken = encryptedRefreshToken;
                    account.TokenExpiresAtUtc = result.ExpiresAtUtc;
                }

                Sc2Profiles.Clear();
                foreach (Sc2Profile profile in fetchedProfiles)
                {
                    profile.BattleNetAccountId = account.Id;
                    profile.Account = account;
                    _accountRepository.UpsertProfile(profile);
                    Sc2Profiles.Add(profile);
                }

                SelectedSc2Profile = Sc2Profiles[0];
                Stage = LinkAccountStage.SelectingProfile;
            }
            catch (BattleNetAuthException ex) when (ex.Reason == AuthFailureReason.UserCancelled)
            {
                Stage = LinkAccountStage.EnterCredentials;
            }
            catch (BattleNetAuthException ex)
            {
                StatusMessage = ex.Message;
                Stage = LinkAccountStage.Failed;
            }
            catch (Exception)
            {
                StatusMessage = "An unexpected error occurred while linking the account.";
                Stage = LinkAccountStage.Failed;
            }
        }

        private bool CanConfirmProfile() => SelectedSc2Profile != null;

        [RelayCommand(CanExecute = nameof(CanConfirmProfile))]
        private void ConfirmProfile()
        {
            ConfirmProfile(SelectedSc2Profile);
        }

        public void ConfirmProfile(Sc2Profile? profile)
        {
            if (profile == null) return;
            LinkedProfile = profile;
            Closed?.Invoke(true);
        }

        [RelayCommand]
        private void CancelLinking()
        {
            _linkCts?.Cancel();
        }

        [RelayCommand]
        private async Task Retry() => await StartLinkingAsync();

        [RelayCommand]
        private void Close() => Closed?.Invoke(false);
    }
}
