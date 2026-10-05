using CommonUtils.Models;
using TraceLensService.Contexts;
using TraceLensService.Models.DbModels;
using TraceLensService.Models.Internal;
using TraceLensService.Models.Options;
using TraceLensService.Models.Responses.Reports;
using TraceLensService.Models.Responses.Traces;
using TraceLensService.Utils;
using static TraceLensService.Common.GlobalConsts;
using Grouping = TraceLensService.Contexts.ReportQueries.Grouping;

namespace TraceLensService.Business
{
    /// <summary>Raporlar: bugün / dün / son 7 gün / son 30 gün, hemen önceki eşit dönemle karşılaştırmalı.</summary>
    public class ReportBusiness(IServiceProvider serviceProvider)
        : BusinessBase(serviceProvider.GetRequiredService<ILogger<ReportBusiness>>())
    {
        private readonly ReportQueries _reports = serviceProvider.GetRequiredService<ReportQueries>();
        private readonly AlertQueries _alerts = serviceProvider.GetRequiredService<AlertQueries>();
        private readonly ThresholdStore _thresholds = serviceProvider.GetRequiredService<ThresholdStore>();

        private static readonly Dictionary<string, int> PeriodDays = new() { ["today"] = 1, ["yesterday"] = 1, ["7d"] = 7, ["30d"] = 30 };

        /// <param name="period">today | yesterday | 7d | 30d | custom (custom için from/to günleri, en fazla DailyRetentionDays gün)</param>
        public Task<DataResponse<ReportResponse>> GetReport(string? period, DateOnly? customFrom, DateOnly? customTo)
            => Guard(async () =>
            {
                DataResponse<ReportResponse> response = new();
                TimeZoneInfo tz = ResolveTimeZone(_reports.TimeZone);
                DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTime.UtcNow, tz));

                // Dönem: "Dün" dışındakiler bugünü (henüz bitmemiş) de içerir; önceki dönem hemen öncesindeki eşit gün sayısı
                string key;
                DateOnly from, to;
                if (period == "custom" && customFrom is DateOnly cf && customTo is DateOnly ct)
                {
                    key = "custom";
                    to = ct > today ? today : ct;
                    from = cf > to ? to : cf;
                    if (to.DayNumber - from.DayNumber >= DailyRetentionDays) from = to.AddDays(-(DailyRetentionDays - 1));
                }
                else
                {
                    key = period is not null && PeriodDays.ContainsKey(period) ? period : "7d";
                    to = key == "yesterday" ? today.AddDays(-1) : today;
                    from = to.AddDays(-(PeriodDays[key] - 1));
                }
                int days = to.DayNumber - from.DayNumber + 1;
                DateOnly previousTo = from.AddDays(-1), previousFrom = previousTo.AddDays(-(days - 1));

                var curOps = _reports.GetAsync(from, to, Grouping.Operation);
                var prevOps = _reports.GetAsync(previousFrom, previousTo, Grouping.Operation);
                var curDays = _reports.GetAsync(from, to, Grouping.Day);
                var prevDays = _reports.GetAsync(previousFrom, previousTo, Grouping.Day);
                var curDayOps = _reports.GetAsync(from, to, Grouping.DayOperation);
                var prevDayOps = _reports.GetAsync(previousFrom, previousTo, Grouping.DayOperation);
                var curTotal = _reports.GetAsync(from, to, Grouping.Total);
                var prevTotal = _reports.GetAsync(previousFrom, previousTo, Grouping.Total);
                var firstDay = _reports.GetFirstDayAsync();
                DateTimeOffset periodStart = StartOfDayUtc(from, tz), periodEnd = StartOfDayUtc(to.AddDays(1), tz);
                var alerts = _alerts.GetOverlappingAsync(periodStart, periodEnd);
                await Task.WhenAll(curOps, prevOps, curDays, prevDays, curDayOps, prevDayOps, curTotal, prevTotal, firstDay, alerts);

                ThresholdOptions thresholds = _thresholds.Current;
                long Slow(DailyRow r) => ReportQueries.SlowCount(r.Histogram, thresholds.For(r.Service, r.Operation));

                Dictionary<(string, string, string), DailyRow> previousByOp = prevOps.Result
                    .ToDictionary(r => (r.App.ToString(), r.Service, r.Operation));

                response.Success(new ReportResponse
                {
                    Period = key,
                    IncludesToday = to == today,
                    TimeZone = tz.Id,
                    From = from,
                    To = to,
                    PreviousFrom = previousFrom,
                    PreviousTo = previousTo,
                    FromUtc = periodStart.UtcDateTime,
                    ToUtc = periodEnd.UtcDateTime,
                    PreviousFromUtc = StartOfDayUtc(previousFrom, tz).UtcDateTime,
                    DataSince = firstDay.Result,
                    Totals = Totals(curTotal.Result, curOps.Result.Sum(Slow), curDays.Result.Count),
                    PreviousTotals = Totals(prevTotal.Result, prevOps.Result.Sum(Slow), prevDays.Result.Count),
                    Daily = Daily(curDays.Result, curDayOps.Result, Slow, tz),
                    PreviousDaily = Daily(prevDays.Result, prevDayOps.Result, Slow, tz),
                    Operations = curOps.Result.Select(r =>
                    {
                        previousByOp.TryGetValue((r.App.ToString(), r.Service, r.Operation), out DailyRow? prev);
                        return new ReportOperationResponse
                        {
                            App = r.App,
                            Service = r.Service,
                            Operation = r.Operation,
                            ThresholdMs = thresholds.For(r.Service, r.Operation),
                            Count = r.Requests,
                            AvgMs = Avg(r),
                            P95Ms = r.P95Ms,
                            SlowCount = Slow(r),
                            ErrorCount = r.Errors,
                            ErrorRate = Rate(r.Errors, r.Requests),
                            PreviousCount = prev?.Requests,
                            PreviousAvgMs = prev is null ? null : Avg(prev),
                            PreviousErrorRate = prev is null ? null : Rate(prev.Errors, prev.Requests)
                        };
                    }).OrderByDescending(o => o.Count).ToList(),
                    Alerts = Alerts(alerts.Result, periodStart, periodEnd)
                });
                return response;
            }, GeneralConsts.ReportNotRetrieved);

        private static ReportTotalsResponse Totals(List<DailyRow> total, long slow, int dayCount)
        {
            DailyRow? t = total.FirstOrDefault();
            if (t is null || t.Requests == 0) return new ReportTotalsResponse();
            return new ReportTotalsResponse
            {
                RequestCount = t.Requests,
                AvgMs = Avg(t),
                P50Ms = t.P50Ms,
                P95Ms = t.P95Ms,
                ErrorCount = t.Errors,
                ErrorRate = Rate(t.Errors, t.Requests),
                SlowCount = slow,
                SlowRate = Rate(slow, t.Requests),
                DayCount = dayCount
            };
        }

        private static List<TimeBucketResponse> Daily(List<DailyRow> days, List<DailyRow> dayOps, Func<DailyRow, long> slow, TimeZoneInfo tz)
        {
            Dictionary<DateOnly, long> slowByDay = dayOps.GroupBy(r => r.Day!.Value).ToDictionary(g => g.Key, g => g.Sum(slow));
            return days.Where(d => d.Day is not null).OrderBy(d => d.Day).Select(d => new TimeBucketResponse
            {
                Time = StartOfDayUtc(d.Day!.Value, tz).UtcDateTime,
                Count = d.Requests,
                AvgMs = Avg(d),
                P50Ms = d.P50Ms,
                P90Ms = d.P90Ms,
                P95Ms = d.P95Ms,
                P99Ms = d.P99Ms,
                SlowCount = slowByDay.GetValueOrDefault(d.Day!.Value),
                ErrorCount = d.Errors
            }).ToList();
        }

        private static ReportAlertsResponse Alerts(List<AlertRecord> alerts, DateTimeOffset start, DateTimeOffset end)
        {
            // Sadece dönem içinde kalan kısım sayılır; hâlâ açıksa şimdiye kadar
            double Minutes(AlertRecord a)
            {
                DateTime from = a.FiredAt > start.UtcDateTime ? a.FiredAt : start.UtcDateTime;
                DateTime until = a.ResolvedAt ?? DateTime.UtcNow;
                if (until > end.UtcDateTime) until = end.UtcDateTime;
                return Math.Max(0, Math.Round((until - from).TotalMinutes, 1));
            }

            return new ReportAlertsResponse
            {
                Count = alerts.Count,
                TotalMinutes = Math.Round(alerts.Sum(Minutes), 1),
                Longest = alerts.OrderByDescending(Minutes).Take(10).Select(a => new ReportAlertResponse
                {
                    App = a.App,
                    Service = a.Service,
                    Operation = a.Operation,
                    PeakValueMs = a.PeakValueMs,
                    ThresholdMs = a.ThresholdMs,
                    FiredAt = a.FiredAt,
                    ResolvedAt = a.ResolvedAt,
                    Minutes = Minutes(a)
                }).ToList()
            };
        }

        private static double Avg(DailyRow r) => r.Requests == 0 ? 0 : Math.Round(r.DurationSumMs / r.Requests, 2);
        private static double Rate(long part, long total) => total == 0 ? 0 : (double)part / total;

        private static DateTimeOffset StartOfDayUtc(DateOnly day, TimeZoneInfo tz) =>
            new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), tz), TimeSpan.Zero);

        private static TimeZoneInfo ResolveTimeZone(string id)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        }
    }
}
