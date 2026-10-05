using CommonUtils.Models;
using TraceLensService.Common;

namespace TraceLensService.Business
{
    /// <summary>
    /// İş katmanı sınıfları için ortak taban. Tüm public iş metotları <see cref="Guard{TResponse}"/> içinden
    /// çalışır; yanıt sözleşmesi her zaman 200 + BaseResponse/DataResponse kalır.
    /// </summary>
    public abstract class BusinessBase(ILogger logger)
    {
        protected readonly ILogger _logger = logger;

        /// <summary>
        /// Merkezî hata sarmalayıcı. Beklenen iş hatası (<see cref="FriendlyException"/>) → mesajı korunur (Warning log);
        /// beklenmeyen sistem hatası → <paramref name="systemErrorMessage"/> döner (Error log).
        /// </summary>
        protected async Task<TResponse> Guard<TResponse>(Func<Task<TResponse>> action, string systemErrorMessage = GlobalConsts.GeneralConsts.SystemError)
            where TResponse : BaseResponse, new()
        {
            try
            {
                return await action();
            }
            catch (FriendlyException fex)
            {
                _logger.LogWarning(fex, "İş hatası: {Message}", fex.Message);
                TResponse response = new();
                response.Failure(fex.Message);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Beklenmeyen hata: {Message}", systemErrorMessage);
                TResponse response = new();
                response.Failure(systemErrorMessage);
                return response;
            }
        }
    }
}
