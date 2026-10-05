using CommonUtils.Models;
using TraceLensService.Contexts;
using TraceLensService.Models.Requests;
using TraceLensService.Models.Responses.Thresholds;
using TraceLensService.Utils;
using static TraceLensService.Common.GlobalConsts;

namespace TraceLensService.Business
{
    /// <summary>Eşik yönetimi: varsayılan eşik ve operasyon bazlı özel eşikler. Değişiklik anında geçerli olur.</summary>
    public class ThresholdBusiness(IServiceProvider serviceProvider)
        : BusinessBase(serviceProvider.GetRequiredService<ILogger<ThresholdBusiness>>())
    {
        private readonly ThresholdQueries _thresholdQueries = serviceProvider.GetRequiredService<ThresholdQueries>();
        private readonly ThresholdStore _thresholdStore = serviceProvider.GetRequiredService<ThresholdStore>();

        public Task<DataResponse<ThresholdListResponse>> GetThresholds()
            => Guard(() =>
            {
                DataResponse<ThresholdListResponse> response = new();
                response.Success(BuildList());
                return Task.FromResult(response);
            }, GeneralConsts.ThresholdsNotRetrieved);

        public Task<DataResponse<ThresholdListResponse>> SetDefault(DefaultThresholdRequest request)
            => Guard(async () =>
            {
                DataResponse<ThresholdListResponse> response = new();
                ValidateRange(request.ThresholdMs);

                await _thresholdQueries.UpsertAsync(string.Empty, string.Empty, request.ThresholdMs);
                await _thresholdStore.ReloadAsync();

                response.Success(BuildList());
                return response;
            }, GeneralConsts.ThresholdNotSaved);

        public Task<DataResponse<ThresholdListResponse>> SetOverride(ThresholdRequest request)
            => Guard(async () =>
            {
                DataResponse<ThresholdListResponse> response = new();
                string service = request.Service.Trim();
                string operation = request.Operation.Trim();
                if (service.Length == 0 || operation.Length == 0)
                    throw new FriendlyException(GeneralConsts.ThresholdOperationRequired);
                ValidateRange(request.ThresholdMs);

                await _thresholdQueries.UpsertAsync(service, operation, request.ThresholdMs);
                await _thresholdStore.ReloadAsync();

                response.Success(BuildList());
                return response;
            }, GeneralConsts.ThresholdNotSaved);

        public Task<DataResponse<ThresholdListResponse>> DeleteOverride(string? service, string? operation)
            => Guard(async () =>
            {
                DataResponse<ThresholdListResponse> response = new();
                string svc = service?.Trim() ?? string.Empty;
                string op = operation?.Trim() ?? string.Empty;
                if (svc.Length == 0 || op.Length == 0)
                    throw new FriendlyException(GeneralConsts.ThresholdOperationRequired);
                if (!_thresholdStore.Overrides.Any(o => o.Service == svc && o.Operation == op))
                    throw new FriendlyException(GeneralConsts.ThresholdOverrideNotFound);

                await _thresholdQueries.DeleteAsync(svc, op);
                await _thresholdStore.ReloadAsync();

                response.Success(BuildList());
                return response;
            }, GeneralConsts.ThresholdNotDeleted);

        private ThresholdListResponse BuildList() => new()
        {
            DefaultMs = _thresholdStore.Current.DefaultMs,
            Overrides = _thresholdStore.Overrides
                .Select(o => new ThresholdOverrideResponse
                {
                    Service = o.Service,
                    Operation = o.Operation,
                    ThresholdMs = o.ThresholdMs,
                    UpdatedAt = o.UpdatedAt
                })
                .ToList()
        };

        private static void ValidateRange(double thresholdMs)
        {
            if (double.IsNaN(thresholdMs) || thresholdMs < MinThresholdMs || thresholdMs > MaxThresholdMs)
                throw new FriendlyException(GeneralConsts.ThresholdOutOfRange);
        }
    }
}
