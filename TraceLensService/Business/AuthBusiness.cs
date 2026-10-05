using CommonUtils.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using TraceLensService.Models.Options;
using TraceLensService.Models.Requests;
using TraceLensService.Models.Responses.Auth;
using static TraceLensService.Common.GlobalConsts;

namespace TraceLensService.Business
{
    /// <summary>Tek kullanıcılı dashboard girişi (kullanıcı adı + şifre, cookie oturumu).</summary>
    public class AuthBusiness(IServiceProvider serviceProvider)
        : BusinessBase(serviceProvider.GetRequiredService<ILogger<AuthBusiness>>())
    {
        private readonly IHttpContextAccessor _httpContextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
        private readonly IOptions<AuthOptions> _authOptions = serviceProvider.GetRequiredService<IOptions<AuthOptions>>();

        private HttpContext Context => _httpContextAccessor.HttpContext!;

        public Task<DataResponse<AuthStatusResponse>> Login(LoginRequest request)
            => Guard(async () =>
            {
                DataResponse<AuthStatusResponse> response = new();
                AuthOptions opts = _authOptions.Value;
                if (!opts.Enabled)
                    throw new FriendlyException(GeneralConsts.AuthDisabled);

                // Sabit süreli karşılaştırma: yanıt süresinden şifrenin doğru kısmı tahmin edilemesin.
                bool valid = FixedTimeEquals(request.Username, opts.Username) & FixedTimeEquals(request.Password, opts.Password!);
                if (!valid)
                {
                    _logger.LogWarning("Hatalı giriş denemesi: kullanıcı '{Username}', IP {Ip}", request.Username, Context.Connection.RemoteIpAddress);
                    throw new FriendlyException(GeneralConsts.InvalidCredentials);
                }

                ClaimsIdentity identity = new([new Claim(ClaimTypes.Name, opts.Username)], CookieAuthenticationDefaults.AuthenticationScheme);
                await Context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

                response.Success(new AuthStatusResponse { AuthEnabled = true, Authenticated = true, Username = opts.Username });
                return response;
            }, GeneralConsts.LoginFailed);

        public Task<BaseResponse> Logout()
            => Guard(async () =>
            {
                BaseResponse response = new();
                await Context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                response.Success();
                return response;
            });

        public Task<DataResponse<AuthStatusResponse>> GetStatus()
            => Guard(() =>
            {
                DataResponse<AuthStatusResponse> response = new();
                bool enabled = _authOptions.Value.Enabled;
                bool authenticated = Context.User.Identity?.IsAuthenticated == true;
                response.Success(new AuthStatusResponse
                {
                    AuthEnabled = enabled,
                    Authenticated = !enabled || authenticated,
                    Username = authenticated ? Context.User.Identity!.Name : null
                });
                return Task.FromResult(response);
            });

        private static bool FixedTimeEquals(string a, string b) =>
            CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(a ?? string.Empty)),
                SHA256.HashData(Encoding.UTF8.GetBytes(b)));
    }
}
