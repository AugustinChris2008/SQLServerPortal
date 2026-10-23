# SQL-Vertrag des Portals

Die Anwendung liest ausschließlich aus drei Views im Schema `portal`. Die vorhandenen Tabellen können beliebig aufgebaut sein; nur die View-Ausgabe muss diesen Vertrag erfüllen.

Das Portal behandelt alle Werte als zeitversetzte Snapshots. Es leitet aus alten oder fehlenden Datensätzen ausdrücklich keinen Online-/Offline-Status ab. `CollectedAtUtc` muss deshalb immer den tatsächlichen Erfassungszeitpunkt enthalten.

## `portal.vServerOverview`

| Spalte | SQL-Typ | Null | Bedeutung |
| --- | --- | --- | --- |
| `Id` | `int` | nein | stabile technische ID |
| `Name` | `nvarchar(128)` | nein | Anzeigename des Servers |
| `InstanceName` | `nvarchar(256)` | nein | vollständiger Instanzname |
| `Environment` | `nvarchar(50)` | nein | z. B. Produktion, Abnahme, Entwicklung |
| `Version` | `nvarchar(100)` | nein | SQL-Server-Version |
| `Edition` | `nvarchar(100)` | nein | Edition |
| `DatabaseCount` | `int` | nein | Anzahl Datenbanken |
| `MemoryGb` | `decimal(12,2)` | ja | installierter beziehungsweise verfügbarer RAM des Servers in GB |
| `CollectedAtUtc` | `datetimeoffset` | nein | Erfassungszeitpunkt in UTC |

## `portal.vConfigurationCheck`

Diese View bildet die fachliche Verbindung zwischen der SOLL-Tabelle und den je Server erfassten IST-Werten. Das Portal übernimmt den fertigen Status aus der View und verändert keine Daten.

| Spalte | SQL-Typ | Null | Bedeutung |
| --- | --- | --- | --- |
| `Id` | `int` | nein | stabile, eindeutige ID der Prüfung |
| `ServerId` | `int` | nein | Verweis auf die Server-ID |
| `ServerName` | `nvarchar(128)` | nein | Anzeigename des Servers |
| `Environment` | `nvarchar(50)` | nein | Umgebung |
| `Category` | `nvarchar(80)` | nein | z. B. Sicherheit, Parallelität oder TempDB |
| `ParameterName` | `nvarchar(256)` | nein | eindeutiger Name des Parameters |
| `ExpectedValue` | `nvarchar(256)` | nein | aufbereiteter SOLL-Wert |
| `ActualValue` | `nvarchar(256)` | ja | aufbereiteter IST-Wert |
| `Unit` | `nvarchar(30)` | ja | optionale Einheit, z. B. MB oder Dateien |
| `Status` | `nvarchar(30)` | nein | `Konform`, `Abweichung` oder `Nicht ermittelt` |
| `Deviation` | `nvarchar(500)` | ja | optionale Erläuterung der Abweichung |
| `CollectedAtUtc` | `datetimeoffset` | nein | Erfassungszeitpunkt des IST-Werts in UTC |

Ein mögliches Grundmuster für die Zuordnung ist:

```sql
CREATE OR ALTER VIEW portal.vConfigurationCheck
AS
SELECT
    Id              = CAST((server.ServerId * 100000) + target.ParameterId AS int),
    ServerId        = CAST(server.ServerId AS int),
    ServerName      = CAST(server.ServerName AS nvarchar(128)),
    Environment     = CAST(server.Environment AS nvarchar(50)),
    Category        = CAST(target.Category AS nvarchar(80)),
    ParameterName   = CAST(target.ParameterName AS nvarchar(256)),
    ExpectedValue   = CAST(target.ExpectedValue AS nvarchar(256)),
    ActualValue     = CAST(actual.ActualValue AS nvarchar(256)),
    Unit            = CAST(target.Unit AS nvarchar(30)),
    Status          = CAST(CASE
        WHEN actual.ActualValue IS NULL THEN N'Nicht ermittelt'
        WHEN LTRIM(RTRIM(actual.ActualValue)) = LTRIM(RTRIM(target.ExpectedValue)) THEN N'Konform'
        ELSE N'Abweichung'
    END AS nvarchar(30)),
    Deviation       = CAST(CASE
        WHEN actual.ActualValue IS NULL THEN N'Kein aktueller IST-Wert vorhanden'
        WHEN LTRIM(RTRIM(actual.ActualValue)) <> LTRIM(RTRIM(target.ExpectedValue))
            THEN CONCAT(N'IST ', actual.ActualValue, N', erwartet ', target.ExpectedValue)
        ELSE NULL
    END AS nvarchar(500)),
    CollectedAtUtc  = CAST(COALESCE(actual.CollectedAtUtc, server.CollectedAtUtc) AS datetimeoffset)
FROM dbo.YourConfigurationTarget AS target
CROSS JOIN dbo.YourManagedServer AS server
LEFT JOIN dbo.YourConfigurationActual AS actual
    ON actual.ServerId = server.ServerId
   AND actual.ParameterName = target.ParameterName;
GO
```

Tabellen- und Spaltennamen sowie die ID-Bildung sind anzupassen. Falls Parameter numerisch, boolesch oder mit Toleranz verglichen werden, sollte die Normalisierung und Bewertung ebenfalls in dieser View erfolgen.

## `portal.vDatabaseOverview`

| Spalte | SQL-Typ | Null | Bedeutung |
| --- | --- | --- | --- |
| `Id` | `int` | nein | stabile technische ID |
| `ServerId` | `int` | nein | Verweis auf die Server-ID |
| `ServerName` | `nvarchar(128)` | nein | redundanter Anzeigename für Listen |
| `Name` | `nvarchar(128)` | nein | Datenbankname |
| `Environment` | `nvarchar(50)` | nein | Umgebung |
| `Status` | `nvarchar(20)` | nein | `Gesund`, `Warnung` oder `Kritisch` |
| `State` | `nvarchar(60)` | nein | z. B. `ONLINE` oder `RECOVERY_PENDING` |
| `RecoveryModel` | `nvarchar(20)` | nein | `FULL`, `SIMPLE` oder `BULK_LOGGED` |
| `CompatibilityLevel` | `int` | nein | Compatibility Level |
| `SizeGb` | `decimal(18,2)` | nein | Gesamtgröße |
| `DataUsedPercent` | `decimal(5,2)` | ja | belegter Datenbereich |
| `LogUsedPercent` | `decimal(5,2)` | ja | belegtes Transaktionslog |
| `LastFullBackupAtUtc` | `datetimeoffset` | ja | letztes Full Backup |
| `LastLogBackupAtUtc` | `datetimeoffset` | ja | letztes Log Backup |
| `Owner` | `nvarchar(128)` | nein | Datenbank-Owner |
| `CollectedAtUtc` | `datetimeoffset` | nein | Erfassungszeitpunkt in UTC |

## Grundgerüst

```sql
IF SCHEMA_ID(N'portal') IS NULL
    EXEC(N'CREATE SCHEMA portal AUTHORIZATION dbo;');
GO

-- Beispiel: Inventarspalten auf die vorhandene Tabelle abbilden.
CREATE OR ALTER VIEW portal.vServerOverview
AS
SELECT
    Id              = CAST(source.ServerId AS int),
    Name            = CAST(source.ServerName AS nvarchar(128)),
    InstanceName    = CAST(source.InstanceName AS nvarchar(256)),
    Environment     = CAST(source.Environment AS nvarchar(50)),
    Version         = CAST(source.ProductVersion AS nvarchar(100)),
    Edition         = CAST(source.Edition AS nvarchar(100)),
    DatabaseCount   = CAST(source.DatabaseCount AS int),
    MemoryGb        = CAST(source.MemoryGb AS decimal(12,2)),
    CollectedAtUtc  = CAST(source.CollectedAtUtc AS datetimeoffset)
FROM dbo.YourServerSource AS source;
GO
```

Das Beispiel ist vor der Ausführung an die echten Tabellen- und Spaltennamen anzupassen. Für die Datenbank-View gilt dasselbe Prinzip.

## Read-only-Rolle

```sql
IF DATABASE_PRINCIPAL_ID(N'portal_reader') IS NULL
    CREATE ROLE portal_reader;
GO

GRANT SELECT ON SCHEMA::portal TO portal_reader;
DENY INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::portal TO portal_reader;
GO

-- Den zuvor angelegten Datenbankbenutzer der Rolle zuordnen:
-- ALTER ROLE portal_reader ADD MEMBER [DOMAIN\sqlportal-service];
```

Die Anwendung benötigt keine Rechte auf den zugrunde liegenden Tabellen, sofern die Ownership Chain nicht unterbrochen wird.

