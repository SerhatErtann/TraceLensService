# tracelens-service

Servisler ve scheduler'lar için istek süresi takibi, yavaş istek alarmı ve Jaeger tarzı trace detayı (OpenTelemetry).
Arayüz ayrı repoda: **tracelens-frontend**.

## Nasıl çalışır?

```
 Sizin servisleriniz            OTel Collector        ClickHouse            TraceLensService         tracelens-frontend
 (+ TraceLens.Instrumentation)  (Docker, 4317)        (Docker, 8123)        (bu repo, 5100)          (Vue, 5180)
 "bu istek 410ms sürdü"  ───→   ölçümleri alır  ───→  saklar         ←───   sorgular, alarm   ←───   tarayıcıda gösterir
                                                                            üretir, Teams'e yazar
```

1. Servise istek gelir. **TraceLens.Instrumentation** paketi isteğin ve içindeki her adımın (SQL, başka servise çağrı, metod) süresini ölçer.
2. Ölçümleri **OTel Collector**'e gönderir; Collector bunları **ClickHouse**'a yazar.
3. **TraceLensService** ClickHouse'tan özet çıkarır, her dakika eşik kontrolü yapar ve gerekirse Teams/Slack'e bildirim atar.
4. **tracelens-frontend** TraceLensService'ten veriyi alıp gösterir.

## Klasörler

```
tracelens-service/
├─ tracelens-service.slnx        Visual Studio ile bunu açın
├─ docker-compose.yml            ClickHouse + OTel Collector
├─ otel-collector/               Collector ayarı (otel-collector.yaml) ve Dockerfile
├─ TraceLensService/             API
│   ├─ Business/                 TraceBusiness, AlertBusiness (Guard → DataResponse)
│   ├─ Common/                   GlobalConsts, TraceLensRouteUrls
│   ├─ Config/                   appsettings.json
│   ├─ Contexts/                 ClickHouseContext + sorgular (TraceQueries, AlertQueries)
│   ├─ Endpoints/                TraceLensEndpoints : IEndpoint
│   ├─ Enums/
│   ├─ Models/                   Requests, Responses, DbModels, Options, Internal
│   └─ Utils/                    TraceAnalyzer, AlertWorker, AlertNotifier, ActiveAlertCache
├─ TraceLens.Instrumentation/    Diğer servislerin ekleyeceği NuGet paketi
└─ Samples/                      Sadece test verisi üretmek için demo servisler
    ├─ Sample.OrderService       (5101)  → PaymentService'i çağırır, bilerek N+1 sorgu ve yavaş metod içerir
    ├─ Sample.PaymentService     (5102)  değişken gecikme, ara sıra 502
    └─ Sample.Scheduler                  5 sn'de bir OrderService'e istek atan job'lar
```

## Çalıştırma

1. **Docker Desktop**'ı açın, repo kökünde:
   ```
   docker compose up -d
   ```
2. **Visual Studio**'da `tracelens-service.slnx`'i açın → solution'a sağ tık → **Configure Startup Projects → Multiple startup projects**:
   - `TraceLensService` → Start (Swagger açılır: http://localhost:5100/swagger)
   - Test verisi istiyorsanız `Sample.PaymentService`, `Sample.OrderService`, `Sample.Scheduler` → Start
3. Arayüz için **tracelens-frontend** reposunun README'sine bakın.

Durdurmak: Visual Studio'da Stop, Docker için `docker compose down` (veriler silinmez).

## ClickHouse'taki verilere bakmak

- Tarayıcıda **http://localhost:8123/play**, kullanıcı/şifre `tracelens` / `tracelens` (yalnızca yerel geliştirme).
- Veya DBeaver → ClickHouse bağlantısı, host `localhost`, port `8123`, veritabanı `otel`.

```sql
-- Tablolar
SELECT name, total_rows FROM system.tables WHERE database = 'otel';

-- Son 20 HTTP isteği
SELECT Timestamp, ServiceName, SpanName, Duration / 1e6 AS ms, StatusCode
FROM otel.otel_traces WHERE SpanKind = 'Server'
ORDER BY Timestamp DESC LIMIT 20;

-- Bir trace'in tüm adımları
SELECT Timestamp, ServiceName, SpanName, Duration / 1e6 AS ms
FROM otel.otel_traces WHERE TraceId = '...' ORDER BY Timestamp;

-- Alarmlar
SELECT Service, Operation, PeakValueMs, FiredAt, ResolvedAt FROM otel.tracelens_alerts FINAL ORDER BY FiredAt DESC;
```

| Tablo | Ne | Saklama |
|---|---|---|
| `otel_traces` | Her span (istek, SQL, HTTP çağrısı, metod) bir satır. Collector oluşturur | 7 gün |
| `tracelens_alerts` | Alarmlar. TraceLensService açılışta oluşturur | 90 gün |
| `tracelens_thresholds` | Eşikler (dashboard'dan yönetilir). TraceLensService açılışta oluşturur | Süresiz |

## API

Tüm yanıtlar `DataResponse<T>` formatındadır: `{ isSuccess, message, messageCode, data }`. Tam liste Swagger'da.

| Uç | Açıklama |
|---|---|
| `GET api/v1/{service\|scheduler}/services` | Filtre listesi |
| `GET api/v1/{service\|scheduler}/summary` | Operasyon bazında sayı, ortalama, p95, eşiği aşan, hata |
| `GET api/v1/{service\|scheduler}/totals` | KPI kartları |
| `GET api/v1/{service\|scheduler}/timeseries` | Zaman grafiği |
| `GET api/v1/{service\|scheduler}/requests` | Sayfalı istek listesi |
| `GET api/v1/traces/{traceId}` | Waterfall + kök neden ipuçları |
| `GET api/v1/alerts` | Açık ve kapanan alarmlar |
| `POST api/v1/alerts/test-notification` | Webhook'u dener |
| `GET api/v1/settings` | Eşik ve alarm ayarları |
| `GET api/v1/thresholds` | Varsayılan eşik + özel eşikler |
| `PUT api/v1/thresholds/default` | `{ thresholdMs }` varsayılan eşiği değiştirir |
| `PUT api/v1/thresholds` | `{ service, operation, thresholdMs }` özel eşik ekler/günceller |
| `DELETE api/v1/thresholds?service=&operation=` | Özel eşiği kaldırır |

Ortak filtreler: `range` (15m, 1h, 24h, 7d) veya `from`/`to`, `service`, `operation`, `minDurationMs`, `onlyErrors`, `onlySlow`.

## Eşikler, alarmlar, bildirimler

**Eşikler dashboard'dan yönetilir** (Ayarlar sayfası veya endpoint tablosundaki ✎). Değişiklik anında geçerli olur:
grafik, "eşiği aşan" sayıları ve alarmlar yeni eşiğe göre hesaplanır. Eşikler `otel.tracelens_thresholds` tablosunda tutulur.
`appsettings.json`'daki `Thresholds` bölümü yalnızca **ilk açılışta**, tablo boşken bir kez aktarılır; sonrasında dikkate alınmaz.
Birden fazla TraceLensService örneği çalışıyorsa bir örnekteki değişiklik diğerlerine en geç 60 sn içinde yansır.

`TraceLensService/Config/appsettings.json`:

```json
"Thresholds":    { "DefaultMs": 200, "Overrides": { "order-scheduler|JOB ReportExport": 2000 } },
"Alerts":        { "WindowMinutes": 5, "MinRequestCount": 5, "Metric": "avg", "ResolveRatio": 0.9 },
"Notifications": { "WebhookUrl": "", "Format": "teams", "DashboardUrl": "http://localhost:5180" }
```

- Her dakika son 5 dakikaya bakılır; ortalama (veya p95) eşiği aşarsa alarm açılır.
- Açık alarm, değer eşiğin %90'ının altına inince kapanır (eşik etrafında gidip gelen değerler bildirim yağdırmasın).
- `Format`: `teams` (Teams Workflows → *"Post to a channel when a webhook request is received"*), `slack` veya `generic`.
- **WebhookUrl gizlidir**; dosyaya değil ortam değişkenine yazın: `Notifications__WebhookUrl`.

## Bir servise TraceLens eklemek

```csharp
builder.Services.AddTraceLens(builder.Configuration);
// ...
app.UseTraceLens();   // request/response body boyutlarını kaydeder
```

```json
"TraceLens": {
  "ServiceName": "help-center-service",
  "AppType": "Service",              // scheduler ise "Scheduler"
  "OtlpEndpoint": "http://localhost:4317",
  "UseEntityFrameworkCore": true,
  "UseSqlClient": false              // Dapper/ADO.NET kullanılıyorsa true
}
```

Scheduler job'ları: `TracedBackgroundService`'ten türetin ya da mevcut job'u `IJobTracer.RunAsync("JobAdi", ct => ...)` ile sarın.
Kritik bir metodun süresini ayrıca görmek için: `using var span = TraceLensTracer.StartMethod();`

Otomatik yakalananlar: gelen HTTP istekleri, giden HttpClient çağrıları (servisler arası `traceparent` taşınır), EF Core/SqlClient sorguları, exception'lar.
