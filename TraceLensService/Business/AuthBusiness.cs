using CommonUtils.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.RegularExpressions;
using TraceLensService.Models.DbModels;
using TraceLensService.Models.Options;
using TraceLensService.Models.Requests;
using TraceLensService.Models.Responses.Auth;
using TraceLensService.Utils;
using static TraceLensService.Common.GlobalConsts;

namespace TraceLensService.Business
{
    /// <summary>
    /// Dashboard girişi ve kullanıcılar (cookie oturumu). Hiç kullanıcı yokken ilk hesap giriş sayfasından açılır;
    /// sonrasında yeni kullanıcıyı ya giriş yapmış biri ekler ya da Auth:AllowRegistration açıksa kişi kendisi kayıt olur.
    /// </summary>
    public partial class AuthBusiness(IServiceProvider serviceProvider)
        : BusinessBase(serviceProvider.GetRequiredService<ILogger<AuthBusiness>>())
    {
        private readonly IHttpContextAccessor _httpContextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
        private readonly IOptions<AuthOptions> _authOptions = serviceProvider.GetRequiredService<IOptions<AuthOptions>>();
        private readonly UserStore _users = serviceProvider.GetRequiredService<UserStore>();

        private HttpContext Context => _httpContextAccessor.HttpContext!;
        private string? CurrentUsername => Context.User.Identity?.IsAuthenticated == true ? Context.User.Identity.Name : null;

        [GeneratedRegex("^[a-z0-9._-]{3,32}$")]
        private static partial Regex UsernamePattern();

        public Task<DataResponse<AuthStatusResponse>> Login(LoginRequest request)
            => Guard(async () =>
            {
                DataResponse<AuthStatusResponse> response = new();
                UserRecord? user = await _users.FindAsync(request.Username);

                // Kullanıcı yoksa da şifre özeti hesaplanır: yanıt süresinden kullanıcı adının var olup olmadığı anlaşılmasın
                if (!PasswordHasher.Verify(request.Password ?? string.Empty, user?.PasswordHash) || user is null)
                {
                    _logger.LogWarning("Hatalı giriş denemesi: kullanıcı '{Username}', IP {Ip}", request.Username, Context.Connection.RemoteIpAddress);
                    throw new FriendlyException(GeneralConsts.InvalidCredentials);
                }

                await SignIn(user);
                response.Success(await BuildStatus(user.Username));
                return response;
            }, GeneralConsts.LoginFailed);

        /// <summary>Giriş sayfasından kayıt: ilk hesap her zaman, sonrakiler yalnızca Auth:AllowRegistration açıksa. Kayıt olan oturum açar.</summary>
        public Task<DataResponse<AuthStatusResponse>> Register(LoginRequest request)
            => Guard(async () =>
            {
                DataResponse<AuthStatusResponse> response = new();
                (string username, string password) = Validate(request);

                bool firstAccount = (await _users.GetAllAsync()).Count == 0;
                if (!firstAccount && !_authOptions.Value.AllowRegistration)
                    throw new FriendlyException(GeneralConsts.RegistrationClosed);

                (AddUserResult result, UserRecord? user) = await _users.AddAsync(username, password, onlyIfFirst: firstAccount && !_authOptions.Value.AllowRegistration);
                ThrowIfNotAdded(result);

                _logger.LogInformation("Yeni hesap: {Username} (kayıt, IP {Ip})", username, Context.Connection.RemoteIpAddress);
                await SignIn(user!);
                response.Success(await BuildStatus(user!.Username));
                return response;
            }, GeneralConsts.RegisterFailed);

        public Task<BaseResponse> Logout()
            => Guard(async () =>
            {
                BaseResponse response = new();
                await Context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                response.Success();
                return response;
            });

        public Task<DataResponse<AuthStatusResponse>> GetStatus()
            => Guard(async () =>
            {
                DataResponse<AuthStatusResponse> response = new();
                response.Success(await BuildStatus(CurrentUsername));
                return response;
            });

        public Task<DataResponse<List<UserResponse>>> GetUsers()
            => Guard(async () =>
            {
                DataResponse<List<UserResponse>> response = new();
                response.Success(await BuildUserList());
                return response;
            }, GeneralConsts.UsersNotRetrieved);

        /// <summary>Giriş yapmış kullanıcı başka birine hesap açar (oturum değişmez).</summary>
        public Task<DataResponse<List<UserResponse>>> AddUser(LoginRequest request)
            => Guard(async () =>
            {
                DataResponse<List<UserResponse>> response = new();
                (string username, string password) = Validate(request);
                (AddUserResult result, _) = await _users.AddAsync(username, password, onlyIfFirst: false);
                ThrowIfNotAdded(result);

                _logger.LogInformation("Yeni hesap: {Username} ({By} ekledi)", username, CurrentUsername);
                response.Success(await BuildUserList());
                return response;
            }, GeneralConsts.UserNotSaved);

        public Task<DataResponse<List<UserResponse>>> DeleteUser(string username)
            => Guard(async () =>
            {
                DataResponse<List<UserResponse>> response = new();
                UserRecord user = await _users.FindAsync(username) ?? throw new FriendlyException(GeneralConsts.UserNotFound);
                // Kendini silmek engellenir; böylece en az bir kullanıcı (silen kişi) her zaman kalır
                if (user.Username == CurrentUsername)
                    throw new FriendlyException(GeneralConsts.CannotDeleteSelf);

                await _users.DeleteAsync(user);
                _logger.LogInformation("Hesap silindi: {Username} ({By} sildi)", user.Username, CurrentUsername);
                response.Success(await BuildUserList());
                return response;
            }, GeneralConsts.UserNotDeleted);

        /// <summary>Kendi şifresini değiştirir; diğer cihazlardaki oturumlar düşer, bu oturum yenilenir.</summary>
        public Task<BaseResponse> ChangePassword(ChangePasswordRequest request)
            => Guard(async () =>
            {
                BaseResponse response = new();
                UserRecord user = await _users.FindAsync(CurrentUsername) ?? throw new FriendlyException(GeneralConsts.LoginRequired);
                if (!PasswordHasher.Verify(request.CurrentPassword ?? string.Empty, user.PasswordHash))
                    throw new FriendlyException(GeneralConsts.CurrentPasswordWrong);
                if (!IsValidPassword(request.NewPassword))
                    throw new FriendlyException(GeneralConsts.PasswordInvalid);

                UserRecord updated = await _users.ChangePasswordAsync(user, request.NewPassword);
                await SignIn(updated);
                response.Success();
                return response;
            }, GeneralConsts.PasswordNotChanged);

        private async Task SignIn(UserRecord user)
        {
            ClaimsIdentity identity = new(
                [new Claim(ClaimTypes.Name, user.Username), new Claim(PasswordStampClaim, PasswordHasher.Stamp(user.PasswordHash))],
                CookieAuthenticationDefaults.AuthenticationScheme);
            await Context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        }

        private async Task<AuthStatusResponse> BuildStatus(string? username)
        {
            bool setupRequired = (await _users.GetAllAsync()).Count == 0;
            return new AuthStatusResponse
            {
                Authenticated = username is not null,
                Username = username,
                SetupRequired = setupRequired,
                RegistrationOpen = setupRequired || _authOptions.Value.AllowRegistration
            };
        }

        private async Task<List<UserResponse>> BuildUserList() =>
            (await _users.GetAllAsync())
                .OrderBy(u => u.Username)
                .Select(u => new UserResponse { Username = u.Username, CreatedAt = u.CreatedAt, IsMe = u.Username == CurrentUsername })
                .ToList();

        private static (string Username, string Password) Validate(LoginRequest request)
        {
            string username = UserStore.Normalize(request.Username);
            if (!UsernamePattern().IsMatch(username))
                throw new FriendlyException(GeneralConsts.UsernameInvalid);
            if (!IsValidPassword(request.Password))
                throw new FriendlyException(GeneralConsts.PasswordInvalid);
            return (username, request.Password);
        }

        private static bool IsValidPassword(string? password) =>
            password is { Length: >= MinPasswordLength and <= MaxPasswordLength };

        private static void ThrowIfNotAdded(AddUserResult result)
        {
            if (result == AddUserResult.UsernameTaken)
                throw new FriendlyException(GeneralConsts.UsernameTaken);
            // İlk hesap için yarışı başka biri kazandı
            if (result == AddUserResult.NotFirst)
                throw new FriendlyException(GeneralConsts.RegistrationClosed);
        }
    }
}
