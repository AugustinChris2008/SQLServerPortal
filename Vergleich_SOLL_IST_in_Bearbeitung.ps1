# =================================================================================================
# SQL Server Compliance Soll-Ist-Vergleich
# =================================================================================================


# =================================================================================================
# KONFIGURATION
# =================================================================================================

# Zentrales Repository
$RepositoryServer   = "DEZANT101V\KATHI"
$RepositoryDatabase = "Backupparameter"

# Tabellen im Repository
$ServerTable        = "dbo.SqlInstances"
$RulesTable         = "dbo.ComplianceRules"
$CurrentValuesTable = "dbo.ComplianceCurrentValues"
$ResultsTable       = "dbo.ComplianceResults"


# =================================================================================================
# DBATOOLS LADEN
# =================================================================================================

try {
    Import-Module dbatools -ErrorAction Stop
}
catch {
    Write-Host ""
    Write-Host "FEHLER beim Laden des dbatools-Moduls:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    return
}


# =================================================================================================
# HILFSFUNKTION FÜR ÜBERSCHRIFTEN
# =================================================================================================

function Write-Section {

    param (
        [Parameter(Mandatory)]
        [string]$Title
    )

    Write-Host ""
    Write-Host "=================================================================================================" -ForegroundColor Cyan
    Write-Host " $Title" -ForegroundColor Cyan
    Write-Host "=================================================================================================" -ForegroundColor Cyan
    Write-Host ""
}


# =================================================================================================
# 01 - AUSGANGSTABELLEN PRÜFEN
# =================================================================================================

Write-Section "01 - AUSGANGSTABELLEN PRÜFEN"

$CheckTablesQuery = @"
SELECT
    CASE
        WHEN OBJECT_ID(N'$ServerTable', N'U') IS NOT NULL THEN 1
        ELSE 0
    END AS ServerTableExists,

    CASE
        WHEN OBJECT_ID(N'$RulesTable', N'U') IS NOT NULL THEN 1
        ELSE 0
    END AS RulesTableExists,

    CASE
        WHEN OBJECT_ID(N'$CurrentValuesTable', N'U') IS NOT NULL THEN 1
        ELSE 0
    END AS CurrentValuesTableExists;
"@

try {
    $CheckTablesParameters = @{
        SqlInstance     = $RepositoryServer
        Database        = $RepositoryDatabase
        Query           = $CheckTablesQuery
        EnableException = $true
    }

    $TableStatus = Invoke-DbaQuery @CheckTablesParameters |
        Select-Object -First 1
}
catch {
    Write-Host "FEHLER beim Prüfen der Ausgangstabellen:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    return
}


if ($null -eq $TableStatus) {
    Write-Host "FEHLER: Der Tabellenstatus konnte nicht ermittelt werden." -ForegroundColor Red
    return
}

if ($TableStatus.ServerTableExists -ne 1) {
    Write-Host "FEHLER: Die Tabelle $ServerTable existiert nicht." -ForegroundColor Red
    return
}

if ($TableStatus.RulesTableExists -ne 1) {
    Write-Host "FEHLER: Die Tabelle $RulesTable existiert nicht." -ForegroundColor Red
    return
}

if ($TableStatus.CurrentValuesTableExists -ne 1) {
    Write-Host "FEHLER: Die Tabelle $CurrentValuesTable existiert nicht." -ForegroundColor Red
    return
}

Write-Host "Alle benötigten Ausgangstabellen sind vorhanden." -ForegroundColor Green


# =================================================================================================
# 02 - ERGEBNISTABELLE PRÜFEN ODER ERSTELLEN
# =================================================================================================

Write-Section "02 - ERGEBNISTABELLE PRÜFEN"

$PrepareResultsTableQuery = @"
IF OBJECT_ID(N'$ResultsTable', N'U') IS NULL
BEGIN
    CREATE TABLE $ResultsTable
    (
        SqlServer       NVARCHAR(255) NOT NULL,
        SqlInstance     NVARCHAR(255) NULL,
        Environment     NVARCHAR(50)  NULL,
        ServerType      NVARCHAR(50)  NULL,
        DatabaseName    NVARCHAR(255) NULL,
        ObjectName      NVARCHAR(255) NULL,
        CheckName       NVARCHAR(255) NOT NULL,
        ExpectedValue   NVARCHAR(255) NULL,
        ActualValue     NVARCHAR(255) NULL,
        ConfiguredValue NVARCHAR(255) NULL,
        RunningValue    NVARCHAR(255) NULL,
        IsCompliant     BIT           NOT NULL,
        ComplianceState NVARCHAR(50)  NOT NULL,
        [Description]   NVARCHAR(255) NULL,
        EvaluatedAt     DATETIME2(0)  NOT NULL
    );
END;
"@

try {
    $PrepareResultsParameters = @{
        SqlInstance     = $RepositoryServer
        Database        = $RepositoryDatabase
        Query           = $PrepareResultsTableQuery
        EnableException = $true
    }

    Invoke-DbaQuery @PrepareResultsParameters

    Write-Host "Ergebnistabelle wurde geprüft beziehungsweise erstellt." -ForegroundColor Green
}
catch {
    Write-Host "FEHLER beim Prüfen oder Erstellen der Ergebnistabelle:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    return
}


# =================================================================================================
# 03 - SOLL-IST-VERGLEICH DURCHFÜHREN
# =================================================================================================

Write-Section "03 - SOLL-IST-VERGLEICH DURCHFÜHREN"

$ComplianceQuery = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @EvaluatedAt DATETIME2(0) = SYSDATETIME();

BEGIN TRY

    BEGIN TRANSACTION;

    -- Bei jedem Lauf alle bisherigen Konflikte entfernen
    TRUNCATE TABLE $ResultsTable;


    ;WITH ComplianceData AS
    (
        SELECT
            S.SqlServer,
            S.SqlInstance,
            S.Environment,
            S.ServerType,

            C.DatabaseName,
            C.ObjectName,

            R.CheckName,
            R.ExpectedValue,
            R.[Description],

            C.ConfiguredValue,
            C.RunningValue,

            CASE
                WHEN NULLIF(LTRIM(RTRIM(C.RunningValue)), N'') IS NOT NULL
                    THEN LTRIM(RTRIM(C.RunningValue))
                ELSE
                    NULLIF(LTRIM(RTRIM(C.ConfiguredValue)), N'')
            END AS ActualValue

        FROM $ServerTable AS S

        INNER JOIN $RulesTable AS R
            ON
            (
                R.Environment = S.Environment
                OR R.Environment IS NULL
                OR LTRIM(RTRIM(R.Environment)) = N''
                OR UPPER(LTRIM(RTRIM(R.Environment))) = N'ALL'
            )
            AND
            (
                R.ServerType = S.ServerType
                OR R.ServerType IS NULL
                OR LTRIM(RTRIM(R.ServerType)) = N''
                OR UPPER(LTRIM(RTRIM(R.ServerType))) = N'ALL'
            )

        LEFT JOIN $CurrentValuesTable AS C
            ON C.SqlServer = S.SqlServer
            AND ISNULL(C.SqlInstance, N'') = ISNULL(S.SqlInstance, N'')
            AND C.CheckName = R.CheckName
    ),


    EvaluatedData AS
    (
        SELECT
            SqlServer,
            SqlInstance,
            Environment,
            ServerType,

            DatabaseName,
            ObjectName,

            CheckName,
            ExpectedValue,
            ActualValue,

            ConfiguredValue,
            RunningValue,

            [Description],

            CASE
                -- Kein Ist-Wert vorhanden
                WHEN ActualValue IS NULL
                    THEN CAST(0 AS BIT)

                -- Numerischer Vergleich
                WHEN
                    TRY_CONVERT(DECIMAL(38,10), ActualValue) IS NOT NULL
                    AND
                    TRY_CONVERT(DECIMAL(38,10), ExpectedValue) IS NOT NULL
                    AND
                    TRY_CONVERT(DECIMAL(38,10), ActualValue)
                        =
                    TRY_CONVERT(DECIMAL(38,10), ExpectedValue)
                    THEN CAST(1 AS BIT)

                -- Positive boolesche Werte
                WHEN
                    LOWER(LTRIM(RTRIM(ActualValue))) IN
                    (
                        N'true',
                        N'1',
                        N'yes',
                        N'on',
                        N'enabled'
                    )
                    AND
                    LOWER(LTRIM(RTRIM(ExpectedValue))) IN
                    (
                        N'true',
                        N'1',
                        N'yes',
                        N'on',
                        N'enabled'
                    )
                    THEN CAST(1 AS BIT)

                -- Negative boolesche Werte
                WHEN
                    LOWER(LTRIM(RTRIM(ActualValue))) IN
                    (
                        N'false',
                        N'0',
                        N'no',
                        N'off',
                        N'disabled'
                    )
                    AND
                    LOWER(LTRIM(RTRIM(ExpectedValue))) IN
                    (
                        N'false',
                        N'0',
                        N'no',
                        N'off',
                        N'disabled'
                    )
                    THEN CAST(1 AS BIT)

                -- Textvergleich ohne Beachtung der Großschreibung
                WHEN
                    LOWER(LTRIM(RTRIM(ActualValue)))
                        =
                    LOWER(LTRIM(RTRIM(ExpectedValue)))
                    THEN CAST(1 AS BIT)

                ELSE CAST(0 AS BIT)
            END AS IsCompliant,

            CASE
                WHEN ActualValue IS NULL
                    THEN N'MISSING'

                WHEN
                    TRY_CONVERT(DECIMAL(38,10), ActualValue) IS NOT NULL
                    AND
                    TRY_CONVERT(DECIMAL(38,10), ExpectedValue) IS NOT NULL
                    AND
                    TRY_CONVERT(DECIMAL(38,10), ActualValue)
                        =
                    TRY_CONVERT(DECIMAL(38,10), ExpectedValue)
                    THEN N'COMPLIANT'

                WHEN
                    LOWER(LTRIM(RTRIM(ActualValue))) IN
                    (
                        N'true',
                        N'1',
                        N'yes',
                        N'on',
                        N'enabled'
                    )
                    AND
                    LOWER(LTRIM(RTRIM(ExpectedValue))) IN
                    (
                        N'true',
                        N'1',
                        N'yes',
                        N'on',
                        N'enabled'
                    )
                    THEN N'COMPLIANT'

                WHEN
                    LOWER(LTRIM(RTRIM(ActualValue))) IN
                    (
                        N'false',
                        N'0',
                        N'no',
                        N'off',
                        N'disabled'
                    )
                    AND
                    LOWER(LTRIM(RTRIM(ExpectedValue))) IN
                    (
                        N'false',
                        N'0',
                        N'no',
                        N'off',
                        N'disabled'
                    )
                    THEN N'COMPLIANT'

                WHEN
                    LOWER(LTRIM(RTRIM(ActualValue)))
                        =
                    LOWER(LTRIM(RTRIM(ExpectedValue)))
                    THEN N'COMPLIANT'

                ELSE N'NON_COMPLIANT'
            END AS ComplianceState

        FROM ComplianceData
    )


    -- Nur aktuelle Konflikte und fehlende Werte speichern
    INSERT INTO $ResultsTable
    (
        SqlServer,
        SqlInstance,
        Environment,
        ServerType,
        DatabaseName,
        ObjectName,
        CheckName,
        ExpectedValue,
        ActualValue,
        ConfiguredValue,
        RunningValue,
        IsCompliant,
        ComplianceState,
        [Description],
        EvaluatedAt
    )
    SELECT
        SqlServer,
        SqlInstance,
        Environment,
        ServerType,
        DatabaseName,
        ObjectName,
        CheckName,
        ExpectedValue,
        ActualValue,
        ConfiguredValue,
        RunningValue,
        IsCompliant,
        ComplianceState,
        [Description],
        @EvaluatedAt
    FROM EvaluatedData
    WHERE IsCompliant = 0;


    COMMIT TRANSACTION;

END TRY
BEGIN CATCH

    IF @@TRANCOUNT > 0
    BEGIN
        ROLLBACK TRANSACTION;
    END;

    THROW;

END CATCH;
"@

try {
    $ComplianceParameters = @{
        SqlInstance     = $RepositoryServer
        Database        = $RepositoryDatabase
        Query           = $ComplianceQuery
        EnableException = $true
    }

    Invoke-DbaQuery @ComplianceParameters

    Write-Host "Soll-Ist-Vergleich wurde durchgeführt." -ForegroundColor Green
    Write-Host "In der Ergebnistabelle wurden nur aktuelle Konflikte und fehlende Werte gespeichert." -ForegroundColor Green
}
catch {
    Write-Host "FEHLER beim Soll-Ist-Vergleich:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    return
}


# =================================================================================================
# 04 - ERGEBNISZUSAMMENFASSUNG LADEN
# =================================================================================================

Write-Section "04 - ERGEBNISZUSAMMENFASSUNG"

$SummaryQuery = @"
SELECT
    ComplianceState,
    COUNT(*) AS ResultCount
FROM $ResultsTable
GROUP BY
    ComplianceState
ORDER BY
    ComplianceState;
"@

try {
    $SummaryParameters = @{
        SqlInstance     = $RepositoryServer
        Database        = $RepositoryDatabase
        Query           = $SummaryQuery
        EnableException = $true
    }

    $Summary = Invoke-DbaQuery @SummaryParameters

    if ($null -eq $Summary -or @($Summary).Count -eq 0) {
        Write-Host "Keine aktuellen Konflikte oder fehlenden Werte vorhanden." -ForegroundColor Green
    }
    else {
        $Summary |
            Format-Table ComplianceState, ResultCount -AutoSize
    }
}
catch {
    Write-Host "FEHLER beim Laden der Ergebniszusammenfassung:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    return
}


# =================================================================================================
# 05 - AKTUELLE KONFLIKTE UND FEHLENDE WERTE ANZEIGEN
# =================================================================================================

Write-Section "05 - ABWEICHUNGEN"

$DeviationQuery = @"
SELECT
    SqlServer,
    SqlInstance,
    Environment,
    ServerType,
    DatabaseName,
    ObjectName,
    CheckName,
    ExpectedValue,
    ActualValue,
    ConfiguredValue,
    RunningValue,
    ComplianceState,
    [Description],
    EvaluatedAt
FROM $ResultsTable
ORDER BY
    SqlServer,
    SqlInstance,
    DatabaseName,
    ObjectName,
    CheckName;
"@

try {
    $DeviationParameters = @{
        SqlInstance     = $RepositoryServer
        Database        = $RepositoryDatabase
        Query           = $DeviationQuery
        EnableException = $true
    }

    $Deviations = Invoke-DbaQuery @DeviationParameters

    if ($null -eq $Deviations -or @($Deviations).Count -eq 0) {
        Write-Host "Alle geprüften Werte entsprechen den Sollvorgaben." -ForegroundColor Green
    }
    else {
        Write-Host "$(@($Deviations).Count) aktuelle Abweichungen oder fehlende Werte gefunden." -ForegroundColor Yellow
        Write-Host ""

        $Deviations |
            Format-Table `
                SqlServer,
                SqlInstance,
                DatabaseName,
                ObjectName,
                CheckName,
                ExpectedValue,
                ActualValue,
                ComplianceState `
                -AutoSize
    }
}
catch {
    Write-Host "FEHLER beim Laden der Abweichungen:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    return
}


# =================================================================================================
# ENDE
# =================================================================================================

Write-Section "SQL SERVER COMPLIANCE AUSWERTUNG ABGESCHLOSSEN"