using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models;
using StatCraft.Services;

namespace StatCraft.ViewModels
{
    public enum LinkAccountStage { EnterCredentials, Connecting, Failed }

    public partial class LinkAccountViewModel : ViewModelBase
    {
        private const string ClientIdSettingKey = "BlizzardClientId";
        private const string ClientSecretSettingKey = "BlizzardClientSecretEncryptedB64";

        private readonly AccountRepository _accountRepository;
        private readonly TokenProtector _tokenProtector;
        private readonly BattleNetAuthService _authService;
        private CancellationTokenSource? _linkCts;

        public LinkAccountViewModel(AccountRepository accountRepository, TokenProtector tokenProtector, BattleNetAuthService authService)
        {
            _accountRepository = accountRepository;
            _tokenProtector = tokenProtector;
            _authService = authService;
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsEnterCredentials), nameof(IsConnecting), nameof(IsFailed))]
        private LinkAccountStage _stage;

        public bool IsEnterCredentials => Stage == LinkAccountStage.EnterCredentials;
        public bool IsConnecting => Stage == LinkAccountStage.Connecting;
        public bool IsFailed => Stage == LinkAccountStage.Failed;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SubmitCredentialsCommand))]
        private string _clientId = "";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SubmitCredentialsCommand))]
        private string _clientSecret = "";

        [ObservableProperty] private string _statusMessage = "";

        public BattleNetAccount? LinkedAccount { get; private set; }

        public event Action<bool>? Closed;

        public async Task InitializeAsync()
        {
            var clientId = _accountRepository.GetSetting(ClientIdSettingKey);
            var encryptedSecretB64 = _accountRepository.GetSetting(ClientSecretSettingKey);

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
                var result = await _authService.LinkAccountAsync(ClientId, ClientSecret, _linkCts.Token);

                var encryptedAccessToken = _tokenProtector.Encrypt(result.AccessToken);
                var encryptedRefreshToken = result.RefreshToken is null ? null : _tokenProtector.Encrypt(result.RefreshToken);

                var existing = _accountRepository.FindByAccountSub(result.AccountSub);
                if (existing is not null)
                {
                    _accountRepository.UpdateAccountTokens(existing.Id, encryptedAccessToken, encryptedRefreshToken, result.ExpiresAtUtc, result.BattleTag);
                    existing.BattleTag = result.BattleTag;
                    existing.EncryptedAccessToken = encryptedAccessToken;
                    existing.EncryptedRefreshToken = encryptedRefreshToken;
                    existing.TokenExpiresAtUtc = result.ExpiresAtUtc;
                    LinkedAccount = existing;
                }
                else
                {
                    var account = new BattleNetAccount
                    {
                        BattleTag = result.BattleTag,
                        AccountSub = result.AccountSub,
                        EncryptedAccessToken = encryptedAccessToken,
                        EncryptedRefreshToken = encryptedRefreshToken,
                        TokenExpiresAtUtc = result.ExpiresAtUtc,
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    };
                    _accountRepository.InsertAccount(account);
                    LinkedAccount = account;
                }

                Closed?.Invoke(true);
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
