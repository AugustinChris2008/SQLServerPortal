# SQL Operations Portal

Read-only-Webportal für den zentralen Überblick über zeitversetzt erfasste Microsoft-SQL-Server- und Datenbankdaten. Das Portal ist bewusst als Snapshot-Inventar aufgebaut und behauptet weder Live-Verfügbarkeit noch aktuelle CPU-Auslastung. Das Projekt startet standardmäßig mit realistischen Demo-Daten und kann über drei stabile SQL-Views an eine bestehende Reporting-Datenbank angeschlossen werden.

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

1. Die drei Views aus [database/README.md](database/README.md) auf die vorhandenen Tabellen abbilden.
2. Einen Benutzer mit ausschließlich `SELECT` auf dem Schema `portal` verwenden.
3. Die Konfiguration setzen:

```powershell
$env:DataSource__Mode = "SqlServer"
$env:ConnectionStrings__PortalDatabase = "Server=tcp:SERVER,1433;Database=DB;User ID=sqlportal_reader;Password=...;Encrypt=True;TrustServerCertificate=False;ApplicationIntent=ReadOnly;Application Name=SQL Operations Portal"
```

Für Docker kann `.env.example` nach `.env` kopiert und lokal angepasst werden. `.env` wird von Git ignoriert.

`ApplicationIntent=ReadOnly` ersetzt keine Datenbankberechtigungen. Der Login muss auf SQL-Server-Seite auf `SELECT` begrenzt werden.

## API

| Endpunkt | Zweck |
| --- | --- |
| `GET /api/v1/overview` | aggregierte Dashboard-Daten |
| `GET /api/v1/servers` | Server mit Suche, Filtern, Sortierung und Paging |
| `GET /api/v1/databases` | Datenbanken mit Suche, Filtern, Sortierung und Paging |
| `GET /api/v1/configuration/overview` | Compliance-Kennzahlen und Bewertung je Server |
| `GET /api/v1/configuration/checks` | SOLL-/IST-Prüfungen mit Suche, Filtern, Sortierung und Paging |
| `GET /health/live` | Prozess-Liveness |
| `GET /health/ready` | Readiness inklusive SQL-Verbindung im SQL-Modus |
| `GET /openapi/v1.json` | OpenAPI-Beschreibung in der Development-Umgebung |

Listenendpunkte akzeptieren `search`, `environment`, `page`, `pageSize`, `sortBy` und `sortDirection`. Datenbank- und Konfigurationsendpunkt unterstützen zusätzlich `status`, der Konfigurationsendpunkt außerdem `category`.

## Kubernetes

Die Basis liegt unter `deploy/kubernetes/base`. Vor dem Deployment müssen mindestens Image-Name, Ingress-Host, Datenquellenmodus und Secret-Verwaltung an den Zielcluster angepasst werden.

```powershell
kubectl apply -k deploy/kubernetes/base
```

`secret.example.yaml` ist nur eine Vorlage und wird nicht durch die Kustomization installiert. Für Produktion sollte ein vorhandener Secret Store beziehungsweise eine verschlüsselte Secret-Verwaltung verwendet werden.

## Noch bewusst offen

- Unternehmens-Login über OIDC/Entra ID; dafür werden Tenant, Client-ID und DBA-Gruppe benötigt.
- konkrete View-Zuordnung; dafür wird das vorhandene Tabellenschema benötigt.
- Backup-, SQL-Agent- und Hochverfügbarkeitsmodule als nächste fachliche Ausbaustufen.

Vor einem zentralen Rollout muss die Authentifizierung aktiviert werden. Die jetzige Version ist für lokale Entwicklung beziehungsweise ein bereits vollständig abgeschottetes Testnetz gedacht.


