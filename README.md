# Burgan Azure DevOps Aggregator

Azure DevOps Work Item olaylarını (durum değişimi, yorum vb.) kurallara göre değerlendiren, otomatik aksiyonlar (yorum ekleme, state değiştirme, XML tabanlı hesaplama, risk hesaplama) çalıştıran ve Elasticsearch/Kibana için yapısal log üreten **.NET 6 Web API + React Yönetim Paneli**.

---

## İçindekiler
- [Öne Çıkanlar](#öne-çıkanlar)
- [Mimari & Akış](#mimari--akış)
- [Depo Yapısı](#depo-yapısı)
- [Gereksinimler](#gereksinimler)
- [Yapılandırma](#yapılandırma)
- [Lokalde Çalıştırma](#lokalde-çalıştırma)
- [Docker (Backend)](#docker-backend)
- [Kubernetes (Frontend)](#kubernetes-frontend)
- [API Uçları](#api-uçları)
- [Aksiyon Handler’ları](#aksiyon-handlerları)
- [Loglama & Kibana](#loglama--kibana)
- [Geliştirme Notları](#geliştirme-notları)
- [CI / GitHub Actions](#ci--github-actions)
- [Güvenlik & Sırlar](#güvenlik--sırlar)
- [Yol Haritası](#yol-haritası)
- [Lisans](#lisans)

---

## Öne Çıkanlar
- **Backend:** ASP.NET Core 6 (Web API), EF Core (SQL Server), RulesEngine, Roslyn C# Scripting, Elastic APM, App.Metrics
- **Frontend:** React admin panel (Ant Design, Tailwind), Nginx (unprivileged), K8s manifestleri
- **Kurallar:** XML → JSON dönüştürme, doğrulama, yürütme
- **Aksiyonlar:** Yorum ekle, state değiştir, XML hesaplama, risk hesaplama
- **Gözlemlenebilirlik:** ES/Kibana için detaylı JSON yürütme logları, Elastic APM, App.Metrics
- **Dev UX:** Swagger/OpenAPI (Development), CORS (React’a izinli)

---

## Mimari & Akış
1. **Girdi:** Work Item payload’ı (manuel tetikleme veya webhook).
2. **Kural Motoru:** `RulesEngineService` → kurallar payload’a uygulanır.
3. **Aksiyon Yürütme:** `ActionExecutor` uygun handler’ı çağırır:
   - `AddCommentActionHandler` (Azure DevOps’a yorum)
   - `ChangeStateActionHandler` (state geçişi)
   - `ExecuteXmlCalculationActionHandler` (XML tabanlı hesap)
   - `RiskCalculationActionHandler` (risk/severity normalize)
4. **Loglama:** `RuleExecutionJsonLogger` yürütme oturumunu ayrıntılı JSON olarak yazar (ES’e uygun).
5. **Yönetim Paneli:** Kurallar/incelemeler/şablonlar/özetler için React dashboard.

---

## Depo Yapısı
```
BurganAzureDevopsAggregator/
├─ kibana-json-example.json                  # Kibana/Elasticsearch örnek dökümanı
└─ BurganAzureDevopsAggregator/
   ├─ BurganAzureDevopsAggregator.sln
   ├─ BurganAzureDevopsAggregator.csproj     # net6.0; EF Core, RulesEngine, Roslyn, Elastic APM...
   ├─ Dockerfile                             # Backend container image
   ├─ Program.cs                             # Host/DI/Swagger/CORS/APM
   ├─ appsettings*.json                      # (Örnekleri repo dışında tutun)
   ├─ Actions/
   │  ├─ ActionExecutor.cs
   │  ├─ AddCommentActionHandler.cs
   │  ├─ ChangeStateActionHandler.cs
   │  ├─ ExecuteXmlCalculationActionHandler.cs
   │  └─ RiskCalculationActionHandler.cs
   ├─ Business/
   │  ├─ AzureDevOpsClient.cs                # ADO REST (work item comments vs.)
   │  ├─ RulesEngineService.cs
   │  ├─ RulesService.cs
   │  ├─ XmlRuleImportService.cs
   │  ├─ XmlToJsonConverter.cs
   │  └─ RuleExecutionJsonLogger.cs
   ├─ Controllers/
   │  ├─ AdminDashboardController.cs
   │  ├─ ManualReviewController.cs
   │  ├─ RulesController.cs
   │  └─ RulesExecuteController.cs
   ├─ Database/
   │  └─ ApplicationDbContext.cs             # EF Core (SQL Server)
   ├─ Frontend/admin-dashboard/              # React + Nginx + K8s
   │  ├─ package.json, Dockerfile, nginx.conf
   │  └─ k8s/deployment.yaml, service.yaml, ingress.yaml
   ├─ Helper /                               # RulesHelper, RuleMapper, RuleValidator
   └─ Models/
      ├─ RuleModel.cs (RuleModel, RuleAction, RuleActionParameter)
      ├─ WorkItemModel.cs, FlatWorkItemModel.cs
      ├─ RuleDto.cs, XmlImportRequest.cs
      └─ ...
```

---

## Gereksinimler
- **Backend:** .NET 6 SDK
- **Veritabanı:** SQL Server (EF Core)
- **Frontend:** Node.js 18+ / npm
- **Dış Sistem:** Azure DevOps PAT (WIT yorum/state için)
- **Opsiyonel:** Elastic APM, App.Metrics toplayıcı

NuGet (özet): `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.SqlServer`, `RulesEngine`, `Microsoft.CodeAnalysis.CSharp.Scripting`, `Elastic.Apm.NetCoreAll`, `Newtonsoft.Json`.

---

## Yapılandırma
`appsettings.json` yerine **env var** veya **user-secrets** kullanın. Repoya **yalnızca** `appsettings.example.json` ekleyin.

**`appsettings.example.json`**
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOUR_SQL;Database=YOUR_DB;User Id=YOUR_USER;Password=YOUR_PASSWORD;"
  },
  "AzureDevOps": {
    "Organization": "https://dev.azure.com/YOUR_ORG",
    "Project": "YOUR_PROJECT",
    "PersonalAccessToken": "SET_FROM_ENV_OR_USERSECRETS"
  },
  "MetricsOptions": { "DefaultContextLabel": "BurganAzureDevopsAggregator", "Enabled": true },
  "MetricsWebTrackingOptions": { "ApdexTrackingEnabled": true, "ApdexTSeconds": 0.1, "IgnoredRoutesRegexPatterns": [], "OAuth2TrackingEnabled": false },
  "MetricsEndpointsOptions": { "MetricsEndpointEnabled": true, "MetricsTextEndpointEnabled": true, "EnvironmentInfoEndpointEnabled": true },
  "ElasticApm": {
    "SecretToken": "SET_FROM_ENV",
    "ServerUrl": "https://apm.example.com",
    "ServiceName": "BurganAzureDevopsAggregator",
    "Environment": "Development",
    "TransactionSampleRate": 1.0,
    "CaptureBody": "all",
    "CaptureBodyContentTypes": "application/json*, text/*"
  },
  "AllowedHosts": "*"
}
```

**Örnek env değişkenleri**
```bash
export ASPNETCORE_ENVIRONMENT=Development
export AzureDevOps__Organization="https://dev.azure.com/<org>"
export AzureDevOps__Project="<project>"
export AzureDevOps__PersonalAccessToken="<pat>"
export ConnectionStrings__DefaultConnection="Server=...;Database=...;User Id=...;Password=...;"
```

---

## Lokalde Çalıştırma

### Backend (API)
```bash
cd BurganAzureDevopsAggregator/BurganAzureDevopsAggregator
dotnet restore
dotnet build
dotnet run
# Development'ta Swagger UI açık olur: http(s)://localhost:<port>/swagger
```

### Frontend (Dashboard)
```bash
cd BurganAzureDevopsAggregator/BurganAzureDevopsAggregator/Frontend/admin-dashboard
npm ci
npm start        # dev server
npm run build    # prod build
```

---

## Docker (Backend)
```bash
cd BurganAzureDevopsAggregator/BurganAzureDevopsAggregator
docker build -t devops-aggregator-backend .
docker run -d --name aggregator   -e ASPNETCORE_ENVIRONMENT=Prod   -e AzureDevOps__Organization="https://dev.azuredevops.com/<org>"   -e AzureDevOps__Project="<project>"   -e AzureDevOps__PersonalAccessToken="<pat>"   -e ConnectionStrings__DefaultConnection="Server=...;Database=...;User Id=...;Password=...;"   -p 5000:80 devops-aggregator-backend
```

---

## Kubernetes (Frontend)
`Frontend/admin-dashboard/k8s/` altındaki manifestleri özelleştirip uygulayın:
- `deployment.yaml` – image, replika, env
- `service.yaml` – 80 → 80 servis
- `ingress.yaml` – TLS edge termination, `/dashboard` route

> Namespace, host ve imagePullSecrets değerlerini ortamına göre güncelle.

---

## API Uçları

**Base:** `/api`

**RulesController** (`/api/rules`)
- `GET /health` – sağlık kontrolü  
- `POST /convert-xml-to-json` – XML kuralları JSON DTO’ya çevir

**RulesExecuteController** (`/api/rulesexecute`)
- `GET /{ruleset}` – ruleset’e göre kuralları getir  
- `POST /execute` – gönderilen payload üzerinde kuralları çalıştır  
- `POST /save` – kural(lar)ı kaydet  
- `POST /import-xml` – toplu XML import  
- `POST /convert-xml-to-json` – pratik dönüşüm ucu

**ManualReviewController** (`/api/manualreview`)
- `GET /pending` – inceleme bekleyenler  
- `GET /{ruleId}/suggestions` – kural için şablon önerileri  
- `POST /{ruleId}/complete` – inceleme sonrası onay/kayıt  
- `POST /{ruleId}/reject` – reddet

**AdminDashboardController** (`/api/admindashboard`)
- `GET /rules` – listeleme (sayfalama/filtre)  
- `GET /overview` – kural/öncelik/kurals et/neden özetleri  
- `GET /rule-editor/{ruleId}` – editör verisi + validasyon yardımcıları  
- `POST /validate-expression` – ifade doğrulama  
- `GET /templates` – örnek ifadeler/şablonlar  
- `POST /bulk-approve` – toplu onay

> **CORS:** `ReactApp` politikası açık. İzinli origin’leri ortam bazlı kısıtlayın.

---

## Aksiyon Handler’ları

- **AddCommentActionHandler**  
  Parametre: `CommentText` → ADO `workItems/{id}/comments`
- **ChangeStateActionHandler**  
  Parametre: `NewState` (+ opsiyonel reason) → Work Item state geçişi
- **ExecuteXmlCalculationActionHandler**  
  Parametre: `XmlRuleName`, `TargetField` → XML hesapla, alanı güncelle
- **RiskCalculationActionHandler**  
  Girdi: `Microsoft_VSTS_Common_Risk` + `Microsoft_VSTS_Common_Severity` → normalize edip alan set eder

Yeni handler eklemek için `IActionHandler`’ı implement edin; DI ile kaydedin.

---

## Loglama & Kibana
- `RuleExecutionJsonLogger`:  
  - **Oturum özeti:** toplam/passed/failed/skipped, süreler  
  - **Kural detayları:** expression, status, süre, hata, aksiyonlar  
- `kibana-json-example.json`: index şeması ve dashboard için örnektir.

---

## Geliştirme Notları
- **Swagger/OpenAPI:** Development’ta açık.  
- **Elastic APM:** `ElasticApm__ServerUrl`, `__SecretToken` ile aktifleştir.  
- **App.Metrics:** JSON/metrics endpointleri `MetricsEndpointsOptions` ile yönetilir.  
- **Kod Stili:** `.editorconfig` ekleyebilir, PR öncesi `dotnet format` önerilir.

---

## CI / GitHub Actions
Örnek workflow (`.github/workflows/build.yml`) ile:
- Backend: .NET restore/build
- Frontend: npm ci/build

Gerekli **GitHub Secrets** (örnek):
- `AZDO_PAT`
- `SQL_CONN`
- `ELASTIC_APM_SERVER_URL`
- `ELASTIC_APM_TOKEN`

---

## Güvenlik & Secrets
- **Asla** gerçek `appsettings.json` commit’lemeyin.  
- **Env var** / `dotnet user-secrets` / **GitHub Secrets** kullanın.  
- Loglarda iş/kişi verisini maskeleyin.  
- Token sızıntısında hemen revoke + rotate yapın.

## Lisans
Varsayılan olarak kurum içi kullanım. Açık kaynak yapılacaksa `MIT` veya `Apache-2.0` ekleyin.
