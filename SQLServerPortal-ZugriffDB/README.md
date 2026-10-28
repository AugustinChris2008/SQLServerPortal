# SQL Operations Portal

Read-only-Webportal für den zentralen Überblick über zeitversetzt erfasste Microsoft-SQL-Server- und Datenbankdaten. Das Portal ist bewusst als Snapshot-Inventar aufgebaut und behauptet weder Live-Verfügbarkeit noch aktuelle CPU-Auslastung. Im SQL-Modus liest es per `SELECT` aus `[ZZImportTest].[dbo].[zol_ready4import]` sowie für die Konfigurationsprüfung aus `[Backupparameter].[dbo].[ComplianceRules]` und `[Backupparameter].[dbo].[ComplianceResults]`; im Demo-Modus bleibt die bestehende Oberfläche ohne Datenbank erreichbar.

## Enthalten

- Dashboard mit Bestands-, RAM- und Datenalter-Kennzahlen
- filter- und sortierbare Serverübersicht mit installierter RAM-Kapazität
- filter- und sortierbare Datenbankübersicht
- Konfigurationsprüfung mit SOLL-/IST-Vergleich, Compliance-Kennzahlen und Serverbewertung
- serverseitiges Paging und ausschließlich parametrisierte Filter
- Demo- und SQL-Server-Datenquelle
- Liveness- und Readiness-Endpunkte
- responsives React-/TypeScript-Frontend
- ASP.NET-Core-10-API
- Multi-Stage-Docker-Build
- Kubernetes-Basismanifeste mit restriktivem Security Context

## Schnellstart mit Docker

```powershell
docker compose up --build
```

Danach ist das Portal unter `http://localhost:8080` erreichbar. Standardmäßig werden Demo-Daten angezeigt.

## Lokale Entwicklung

Voraussetzungen: .NET 10 SDK und Node.js 24 oder neuer.

Terminal 1:

```powershell
dotnet run --project src/SqlPortal.Api
```

Terminal 2:

```powershell
Set-Location src/SqlPortal.Web
npm.cmd install
npm.cmd run dev
```

Das Frontend läuft dann auf `http://localhost:5173` und leitet API-Aufrufe an `http://localhost:5080` weiter.

## SQL Server anschließen

1. Den lokalen SQL-Login `SqlPortal` mit ausschließlich `SELECT` auf `[ZZImportTest].[dbo].[zol_ready4import]`, `[Backupparameter].[dbo].[ComplianceRules]` und `[Backupparameter].[dbo].[ComplianceResults]` verwenden.
2. `.env.example` als `.env` kopieren und `SQLPORTAL_PASSWORD` durch das echte Kennwort ersetzen.
3. Die Konfiguration verwendet SQL-Authentifizierung und `ApplicationIntent=ReadOnly`:

```powershell
$env:DataSource__Mode = "SqlServer"
$env:ConnectionStrings__PortalDatabase = "Server=tcp:DEZANT101V,1433;Database=ZZImportTest;User ID=SqlPortal;Password=CHANGE_ME;Encrypt=True;TrustServerCertificate=True;ApplicationIntent=ReadOnly;Application Name=SQL Operations Portal"
```

Für Docker wird die lokale, von Git ignorierte `.env` verwendet:

```powershell
Copy-Item .env.example .env
docker compose up --detach --build
```

`.env` wird von Git ignoriert. Das Kennwort gehört weder in das Image noch in Git. Für den ersten internen Verbindungstest ist in `.env.example` `TrustServerCertificate=True` gesetzt; nach Bereitstellung der Unternehmens-CA sollte dies auf `False` geändert werden.

`ApplicationIntent=ReadOnly` ersetzt keine Datenbankberechtigungen. Der Login muss auf SQL-Server-Seite auf `SELECT` für die drei benötigten Tabellen begrenzt bleiben. Die Anwendung enthält im aktiven Repository ausschließlich feste, parametrisierte `SELECT`-Abfragen.

## API

| Endpunkt | Zweck |
| --- | --- |
| `GET /api/v1/overview` | aggregierte Dashboard-Daten |
| `GET /api/v1/servers` | Server mit Suche, Filtern, Sortierung und Paging |
| `GET /api/v1/servers/filters` | verfügbare SQL-Versionen und Editionen |
| `GET /api/v1/servers/{serverId}` | Serverparameter, Datenbanken und Konfigurationsprüfungen |
| `GET /api/v1/databases` | Datenbanken mit Suche, Filtern, Sortierung und Paging |
| `GET /api/v1/databases/{databaseId}` | sämtliche erfassten Parameter und Compliance-Daten einer Datenbank |
| `GET /api/v1/configuration/overview` | Compliance-Kennzahlen und Bewertung je Server |
| `GET /api/v1/configuration/checks` | SOLL-/IST-Prüfungen mit Suche, Filtern, Sortierung und Paging |
| `GET /health/live` | Prozess-Liveness |
| `GET /health/ready` | Readiness inklusive SQL-Verbindung im SQL-Modus |
| `GET /openapi/v1.json` | OpenAPI-Beschreibung in der Development-Umgebung |

Listenendpunkte akzeptieren `search`, `environment`, `page`, `pageSize`, `sortBy` und `sortDirection`. Der Serverendpunkt unterstützt zusätzlich `version` und `edition`. Datenbank- und Konfigurationsendpunkt unterstützen außerdem `status`; beim Konfigurationsendpunkt filtert `category` den Servertyp.

## Kubernetes

Die Basis liegt unter `deploy/kubernetes/base`. Vor dem Deployment müssen mindestens Image-Name, Ingress-Host, Datenquellenmodus und Secret-Verwaltung an den Zielcluster angepasst werden. Der Connection String mit dem SQL-Kennwort muss aus einem Kubernetes Secret beziehungsweise einem zentralen Secret Store kommen.

```powershell
kubectl apply -k deploy/kubernetes/base
```

`secret.example.yaml` ist nur eine Vorlage und wird nicht durch die Kustomization installiert. Für Produktion sollte ein vorhandener Secret Store beziehungsweise eine verschlüsselte Secret-Verwaltung verwendet werden.

## Noch bewusst offen

- Unternehmens-Login über OIDC/Entra ID; dafür werden Tenant, Client-ID und DBA-Gruppe benötigt.
- fachliche Ausgestaltung der einzelnen Tabs über die heute verfügbare Inventartabelle hinaus.
- Backup-, SQL-Agent- und Hochverfügbarkeitsmodule als nächste fachliche Ausbaustufen.

Vor einem zentralen Rollout muss die Authentifizierung aktiviert werden. Die jetzige Version ist für lokale Entwicklung beziehungsweise ein bereits vollständig abgeschottetes Testnetz gedacht.

