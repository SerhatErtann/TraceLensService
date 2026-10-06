# tracelens-service

Servisler ve scheduler'lar için istek süresi takibi, yavaş istek alarmı ve Jaeger tarzı trace detayı (OpenTelemetry).
Arayüz ayrı repoda: **tracelens-frontend**.

## Nasıl çalışır?

```
 Sizin servisleriniz            OTel Collector        ClickHouse            TraceLensService         tracelens-frontend
 (+ OpenTelemetry paketleri)    (Docker, 4317)        (Docker, 8123)        (bu repo, 5100)          (Vue, 5180)
 "bu istek 410ms sürdü"  ───→   ölçümleri alır  ───→  saklar         ←───   sorgular, alarm   ←───   tarayıcıda gösterir
                                                                            üretir, Teams'e yazar
```

1. Servise istek gelir. Servise kurulu resmi **OpenTelemetry** paketleri isteğin ve içindeki her adımın (SQL, başka servise çağrı, metod) süresini ölçer (bkz. "Bir servisi TraceLens'e bağlamak").
2. Ölçümleri **OTel Collector**'e gönderir; Collector bunları **ClickHouse**'a yazar.
3. **TraceLensService** ClickHouse'tan özet çıkarır, her dakika eşik kontrolü yapar ve gerekirse Teams/Slack'e bildirim atar.
4. **tracelens-frontend** TraceLensService'ten veriyi alıp gösterir.

## Klasörler

```
tracelens-service/
├─ tracelens-service.slnx        Visual Studio ile bunu açın
├─ docker-compose.yml            Geliştirme: ClickHouse + Collector · Sunucu (--profile app): + API + dashboard
├─ .env.example                  Şifreler/adresler için örnek; ".env" olarak kopyalanır (git'e girmez)
├─ nuget.config                  Paket kaynakları (CommonUtils Finoku feed'inde)
├─ otel-collector/               Collector ayarı (otel-collector.yaml) ve Dockerfile
├─ TraceLensService/             API (+ Dockerfile)
│   ├─ Business/                 TraceBusiness, AlertBusiness (Guard → DataResponse)
│   ├─ Common/                   GlobalConsts, TraceLensRouteUrls
│   ├─ Config/                   appsettings.json
│   ├─ Contexts/                 ClickHouseContext + sorgular (TraceQueries, AlertQueries)
│   ├─ Endpoints/                TraceLensEndpoints : IEndpoint
│   ├─ Enums/
│   ├─ Models/                   Requests, Responses, DbModels, Options, Internal
│   └─ Utils/                    TraceAnalyzer, AlertWorker, AlertNotifier, ActiveAlertCache
└─ Samples/                      Test verisi üreten demo servisler; aynı zamanda "servisi bağlama" rehberinin örneği
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

## Sunucuya kurulum (Docker)

Sunucuda Docker ve Docker Compose yeterli; .NET veya Node kurmaya gerek yok.

1. İki repoyu **yan yana** klonlayın:
   ```
   git clone https://github.com/SerhatErtann/TraceLensService.git tracelens-service
   git clone https://github.com/SerhatErtann/TraceLensFrontend.git tracelens-frontend
   ```
2. `tracelens-service` içinde `.env.example`'ı `.env` olarak kopyalayın ve doldurun. En az şunlar:
   - `AUTH_PASSWORD`: dashboard giriş şifresi (**zorunlu**; boşsa API açılmaz). Kullanıcı adı `AUTH_USERNAME`, varsayılan `admin`
   - `CLICKHOUSE_PASSWORD`: güçlü bir şifre
   - `DASHBOARD_URL`: dashboard'un dışarıdan erişilen adresi (bildirimlerdeki link için)
   - `NOTIFICATIONS_WEBHOOK_URL`: Teams/Slack bildirimi isteniyorsa
3. Hepsini başlatın:
   ```
   docker compose --profile app up -d --build
   ```
4. Dashboard: `http://<sunucu>:8080` (port `.env`'deki `DASHBOARD_PORT`).
5. Servisleriniz ölçümleri `http://<sunucu>:4317` adresine göndersin (`TraceLens:OtlpEndpoint`).

| Container | Dışarıya açık mı | Not |
|---|---|---|
| `tracelens-dashboard` | Evet, `DASHBOARD_PORT` (8080) | nginx; `/api` isteklerini API'ye yönlendirir |
| `otel-collector` | Evet, 4317 / 4318 | Servisler buraya gönderir |
| `tracelens-service` | Hayır | Sadece dashboard üzerinden erişilir; Swagger kapalı |
| `clickhouse` | Sadece sunucunun kendisinden (`127.0.0.1:8123`) | Veriler `clickhouse-data` volume'unda kalıcı |

Güncellemek için: `git pull` (iki repoda da) → `docker compose --profile app up -d --build`.

**Dikkat:**
- `CLICKHOUSE_USER`/`CLICKHOUSE_PASSWORD` ClickHouse'un **ilk açılışında** kullanıcıyı oluşturur. Sonradan `.env`'de değiştirmek mevcut kullanıcının şifresini değiştirmez; önce ClickHouse'ta `ALTER USER` ile değiştirin.
- Şifrede `;` karakteri kullanmayın (bağlantı cümlesinde ayraçtır).
- **Alarm kontrolünü yalnızca bir TraceLensService örneği yapmalı.** Aynı veritabanına bağlı ikinci bir örnek (ör. sunucudaki container + yerelde Visual Studio'daki) çalışıyorsa, ikincisinde `Alerts__Enabled=false` verin; yoksa aynı alarm için iki bildirim gider.

## Dashboard girişi

Tek kullanıcılı, kullanıcı adı + şifre ile giriş. Kullanıcı adı ve şifre ortam değişkeninden okunur (`Auth__Username`, `Auth__Password`; Docker'da `.env` → `AUTH_USERNAME`, `AUTH_PASSWORD`).

| Durum | Davranış |
|---|---|
| Şifre tanımlı | Dashboard giriş ekranı açar; `auth/login`, `auth/logout`, `auth/me` ve `/health` dışındaki tüm API uçları oturum ister (yoksa 401) |
| Şifre tanımlı değil (yerel geliştirme) | Giriş kapalı, herkes erişir; açılışta uyarı loglanır |
| `Auth__Required=true` ve şifre yok | API **açılmaz** (docker-compose'da açık; sunucuda yanlışlıkla korumasız yayını engeller) |

- Oturum cookie'si `HttpOnly` ve `SameSite=Strict`; 8 saat kullanılmazsa düşer.
- Aynı IP'den dakikada en fazla 5 giriş denemesi (başarılılar dahil); fazlası 429 döner.
- Container yeniden başlayınca oturumlar düşer, yeniden giriş gerekir.

## ClickHouse'taki verilere bakmak

- Tarayıcıda **http://localhost:8123/play**, kullanıcı/şifre `.env`'deki değerler (yoksa `tracelens` / `tracelens`). ClickHouse yalnızca çalıştığı makineden erişilebilir; sunucudaki veriye bakmak için sunucuya SSH tüneliyle bağlanın.
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
| `POST api/v1/auth/login` · `POST api/v1/auth/logout` · `GET api/v1/auth/me` | Giriş (bkz. "Dashboard girişi") |
| `GET api/v1/overview` | Genel Bakış: tüm servis/scheduler özetleri, kart grafikleri (24 nokta), açık sorun ve alarm sayısı, önceki dönem toplamları ve grafiği |
| `GET api/v1/issues` | Sorunlar: ortalaması eşiği aşan veya hata oranı %5'i geçen endpoint/job'lar, en sık hata ve alarm bilgisiyle (`service` ile filtrelenebilir) |
| `GET api/v1/{service\|scheduler}/services` | Filtre listesi |
| `GET api/v1/{service\|scheduler}/summary` | Operasyon bazında sayı, ortalama, p95, eşiği aşan, hata |
| `GET api/v1/{service\|scheduler}/totals` | KPI kartları |
| `GET api/v1/{service\|scheduler}/timeseries` | Zaman grafiği (ortalama, p50/p90/p95/p99, istek, eşiği aşan, hatalı) |
| `GET api/v1/{service\|scheduler}/requests` | Sayfalı istek listesi |
| `GET api/v1/{service\|scheduler}/services/{service}/breakdown` | Servis Detayı: süre dağılımı (kendi kodu / dış çağrı / DB) ve metod, DB sorgusu, dış çağrı grupları |
| `GET api/v1/{service\|scheduler}/services/{service}/spans?category=&name=&target=` | Bir metod / DB sorgusu / dış çağrının en yavaş 10 örneği (`category`: method, db, call) |
| `GET api/v1/{service\|scheduler}/services/{service}/anatomy?operation=` | İstek anatomisi: bir endpoint/job'un son 300 isteğinde her adım istek başına kaç kez ve ne kadar sürüyor |
| `GET api/v1/{service\|scheduler}/histogram` | Süre dağılımı (logaritmik aralıklar) + p50/p90/p99 |
| `GET api/v1/{service\|scheduler}/outcomes` | Durum kodu (görevlerde başarılı/başarısız) dağılımı ve hata türleri |
| `GET api/v1/{service\|scheduler}/instances` | Servisin çalışan kopyaları (service.instance.id) ayrı ayrı |
| `GET api/v1/live?since=&app=&service=&onlySlow=&onlyErrors=` | Canlı: since'ten sonra gelen istekler (en yeni üstte, 30 sn geri bakarak; tekrarlar spanId ile ayıklanır) ve son 60 saniyenin özeti (veri gecikmesi nedeniyle 7 sn geriden) |
| `GET api/v1/reports?period=today|yesterday|7d|30d|custom&from=&to=` | Rapor: dönem toplamları, gün gün, endpoint/görev bazında önceki dönemle karşılaştırma, alarmlar. Günlük özet tablosundan (tracelens_daily, 90 gün) |
| `GET api/v1/service-map` | Servis haritası: servisler, görevler, veritabanları ve aralarındaki çağrılar |
| `GET api/v1/traces/{traceId}` | Waterfall + kök neden ipuçları |
| `GET api/v1/alerts` | Açık ve kapanan alarmlar; filtreler: `days` veya `from`/`to`, `app`, `service`, `operation` (içinde geçen), `kind` (slow \| error), `minPeakMs`, `status` (500 \| 5xx) |
| `POST api/v1/alerts/test-notification` | Webhook'u dener |
| `GET api/v1/settings` | Eşik ve alarm ayarları |
| `GET api/v1/thresholds` | Varsayılan eşik + özel eşikler |
| `PUT api/v1/thresholds/default` | `{ thresholdMs }` varsayılan eşiği değiştirir |
| `PUT api/v1/thresholds` | `{ service, operation, thresholdMs }` özel eşik ekler/günceller |
| `DELETE api/v1/thresholds?service=&operation=` | Özel eşiği kaldırır |

Ortak filtreler: `range` (15m, 1h, 6h, 24h, 7d) veya `from`/`to` (ISO tarih-saat; dashboard'da "Özel aralık"), `service`, `operation`, `minDurationMs`, `onlyErrors`, `onlySlow`.

## Eşikler, alarmlar, bildirimler

**Eşikler dashboard'dan yönetilir** (Ayarlar sayfası veya endpoint tablosundaki ✎). Değişiklik anında geçerli olur:
grafik, "eşiği aşan" sayıları ve alarmlar yeni eşiğe göre hesaplanır. Eşikler `otel.tracelens_thresholds` tablosunda tutulur.
`appsettings.json`'daki `Thresholds` bölümü yalnızca **ilk açılışta**, tablo boşken bir kez aktarılır; sonrasında dikkate alınmaz.
Birden fazla TraceLensService örneği çalışıyorsa bir örnekteki değişiklik diğerlerine en geç 60 sn içinde yansır.

`TraceLensService/Config/appsettings.json`:

```json
"Thresholds":    { "DefaultMs": 200, "Overrides": { "order-scheduler|JOB ReportExport": 2000 } },
"Alerts":        { "WindowMinutes": 5, "MinRequestCount": 5, "Metric": "avg", "ResolveRatio": 0.9,
                   "ErrorAlertsEnabled": true, "ErrorRate": 0.05, "MinErrorCount": 3 },
"Notifications": { "WebhookUrl": "", "Format": "teams", "DashboardUrl": "http://localhost:5180" }
```

- Her dakika son 5 dakikaya bakılır. İki tür alarm var: **yavaşlık** (ortalama veya p95 eşiği aşarsa) ve **hata** (hata oranı `ErrorRate`'i, yani varsayılan %5'i geçerse ve en az `MinErrorCount` hatalı istek varsa). Hata alarmı en sık hata kodunu (HTTP 500, 502 ya da görevde exception tipi) kaydeder; aynı endpoint için ikisi aynı anda açık olabilir.
- Alarmlar sayfasında tarih aralığı, servis/görev, uygulama, operasyon, alarm türü, en yüksek süre (ör. ≥ 200 ms) ve hata koduna (500 ya da 5xx) göre filtrelenir.
- Açık alarm, değer eşiğin (hata alarmında oran sınırının) %90'ının altına inince kapanır (sınır etrafında gidip gelen değerler bildirim yağdırmasın).
- `Format`: `teams` (Teams Workflows → *"Post to a channel when a webhook request is received"*), `slack` veya `generic`.
- **WebhookUrl gizlidir**; dosyaya değil ortam değişkenine yazın: `Notifications__WebhookUrl`.

## Bir servisi TraceLens'e bağlamak

Ayrı bir TraceLens paketi yok; servisler **nuget.org'daki resmi OpenTelemetry paketlerini** doğrudan kurar.
`Samples/` klasöründeki üç proje bu rehberin birebir uygulanmış halidir.

### 1. Paketler

```
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
dotnet add package OpenTelemetry.Instrumentation.AspNetCore        # web servisleri için
dotnet add package OpenTelemetry.Instrumentation.Http              # başka servislere HttpClient çağrıları
dotnet add package OpenTelemetry.Instrumentation.EntityFrameworkCore --prerelease   # EF Core kullanıyorsa
dotnet add package OpenTelemetry.Instrumentation.SqlClient         # EF yoksa (Dapper/ADO.NET); EF ile birlikte eklemeyin
```

### 2. Ayar (`appsettings.json`)

```json
"TraceLens": {
  "ServiceName": "help-center-service",
  "OtlpEndpoint": "http://localhost:4317"
}
```

`ServiceName` dashboard'da görünecek addır. `OtlpEndpoint` OTel Collector'ün adresidir (sunucuda `http://<sunucu>:4317`).

### 3. `Program.cs`, web servisi

```csharp
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService(builder.Configuration["TraceLens:ServiceName"]!)
        .AddAttributes([new("tracelens.app_type", "service")]))      // Services sayfasında listelenir
    .WithTracing(t => t
        .AddSource("TraceLens")                                        // kendi metod ölçümleriniz (adım 5)
        .AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()                       // veya .AddSqlClientInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(builder.Configuration["TraceLens:OtlpEndpoint"]!)));
```

### 4. Scheduler

`Program.cs`'te aynı kurulum. Tek farkı `"tracelens.app_type", "scheduler"` olması; scheduler'da `AddAspNetCoreInstrumentation` gerekmez.
Sonra [`Samples/Sample.Scheduler/JobTracing.cs`](Samples/Sample.Scheduler/JobTracing.cs) dosyasını projenize kopyalayın ve her job çalıştırmasını sarın:

```csharp
await JobTracing.RunAsync("OrderSync", ct => DoWorkAsync(ct), stoppingToken);
```

Bu sarmalayıcı her çalıştırmayı ayrı bir trace yapar ve `job.name` etiketini ekler; Schedulers sayfası job'ları bu etiketten tanır.
Hangfire, Quartz veya şirketin Scheduler kütüphanesi kullanılıyorsa job'un çalıştığı metodun içini aynı şekilde sarmak yeterli.

### 5. Kritik bir metodun süresini ayrıca görmek (opsiyonel)

```csharp
static readonly ActivitySource Source = new("TraceLens");

using var span = Source.StartActivity("OrderReportService.BuildAsync");
span?.SetTag("order.id", id);   // trace detayında görünür
```

### Otomatik gelenler

Gelen HTTP istekleri, giden HttpClient çağrıları (servisler arası `traceparent` taşınır, A → B → C tek trace'te birleşir),
EF Core/SQL sorguları ve exception'lar. DB ve HTTP çağrısı adları dashboard'da okunur hale getirilir
(`main` → `SELECT Orders`, `GET` → `GET payment-service/payments/5/status`).
