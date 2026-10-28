# =================================================================================================
# SQL Server Compliance - IST-Werte auslesen
# Kein Soll-Ist-Vergleich
# =================================================================================================


# =================================================================================================
# KONFIGURATION
# =================================================================================================

$RepositoryServer   = "DEZANT101V\KATHI"
$RepositoryDatabase = "Backupparameter"

$ServerTable        = "dbo.SqlInstances"
$CurrentValuesTable = "dbo.ComplianceCurrentValues"

$RunTimestamp = Get-Date


# =================================================================================================
# DBATOOLS LADEN
# =================================================================================================

try {
    Import-Module dbatools -ErrorAction Stop
}
catch {
    Write-Host "FEHLER beim Laden von dbatools:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    return
}


# =================================================================================================
# HILFSFUNKTION
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
# 01 - SERVERLISTE LADEN
# =================================================================================================

Write-Section "01 - SERVERLISTE LADEN"

$ServerQuery = @"
SELECT
    SqlServer,
    SqlInstance,
    Environment,
    ServerType
FROM $ServerTable;
"@

try {
    $Servers = @(
        Invoke-DbaQuery `
            -SqlInstance $RepositoryServer `
            -Database $RepositoryDatabase `
            -Query $ServerQuery `
            -As PSObject `
            -EnableException
    )
}
catch {
    Write-Host "FEHLER beim Laden der Serverliste:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    return
}

if ($Servers.Count -eq 0) {
    Write-Host "Keine Server oder Instanzen gefunden." -ForegroundColor Yellow
    return
}

Write-Host "$($Servers.Count) Server/Instanzen geladen." -ForegroundColor Green

$Servers |
    Format-Table SqlServer, SqlInstance, Environment, ServerType -AutoSize


# =================================================================================================
# 02 - IST-WERT-TABELLE VORBEREITEN
# Vorhandene IST-Werte werden geloescht.
# =================================================================================================

Write-Section "02 - IST-WERT-TABELLE VORBEREITEN"

$PrepareResultTableQuery = @"
IF OBJECT_ID(N'$CurrentValuesTable', N'U') IS NULL
BEGIN
    CREATE TABLE $CurrentValuesTable
    (
        SqlServer       NVARCHAR(255) NOT NULL,
        SqlInstance     NVARCHAR(255) NULL,
        DatabaseName    NVARCHAR(255) NULL,
        ObjectName      NVARCHAR(255) NULL,
        CheckName       NVARCHAR(255) NOT NULL,
        ConfiguredValue NVARCHAR(255) NULL,
        RunningValue    NVARCHAR(255) NULL,
        CollectedAt     DATETIME2(0) NOT NULL
    );
END;

IF COL_LENGTH(N'$CurrentValuesTable', N'DatabaseName') IS NULL
BEGIN
    ALTER TABLE $CurrentValuesTable
        ADD DatabaseName NVARCHAR(255) NULL;
END;

IF COL_LENGTH(N'$CurrentValuesTable', N'ObjectName') IS NULL
BEGIN
    ALTER TABLE $CurrentValuesTable
        ADD ObjectName NVARCHAR(255) NULL;
END;

TRUNCATE TABLE $CurrentValuesTable;

ALTER TABLE $CurrentValuesTable
    ALTER COLUMN CheckName NVARCHAR(255) NOT NULL;
"@

try {
    Invoke-DbaQuery `
        -SqlInstance $RepositoryServer `
        -Database $RepositoryDatabase `
        -Query $PrepareResultTableQuery `
        -EnableException

    Write-Host "IST-Wert-Tabelle wurde geprueft und geleert." -ForegroundColor Green
}
catch {
    Write-Host "FEHLER beim Vorbereiten der IST-Wert-Tabelle:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    return
}


# =================================================================================================
# SAMMLUNG INITIALISIEREN
# =================================================================================================

$CollectedValues = @()


# =================================================================================================
# 03 - SQL-INSTANZEN DURCHLAUFEN
# =================================================================================================

Write-Section "03 - SQL-INSTANZEN PRUEFEN"

foreach ($Server in $Servers) {

    $SqlServerName = ([string]$Server.SqlServer).Trim()
    $InstanceName  = ([string]$Server.SqlInstance).Trim()

if (-not $SqlServerName) {
    Write-Host "FEHLER: SQL-Servername ist leer." -ForegroundColor Red
    continue
}

    if ($InstanceName) {
        $TargetInstance     = "$SqlServerName\$InstanceName"
        $StoredInstanceName = $InstanceName
    }
    else {
        $TargetInstance     = $SqlServerName
        $StoredInstanceName = $null
    }

    Write-Section "Server: $TargetInstance"

    Write-Host "Environment: $($Server.Environment)"
    Write-Host "ServerType:  $($Server.ServerType)"


    # =============================================================================================
    # VERBINDUNG TESTEN
    # =============================================================================================

    try {
        $Connection = Test-DbaConnection -SqlInstance $TargetInstance

        $SuccessfulConnection = $Connection |
            Where-Object { $_.ConnectSuccess -eq $true } |
            Select-Object -First 1
    }
    catch {
        Write-Host "FEHLER beim Verbindungstest zu $TargetInstance" -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Red
        continue
    }

    if (-not $SuccessfulConnection) {
        Write-Host "FEHLER: Verbindung zu $TargetInstance nicht moeglich." -ForegroundColor Red

        $SqlError = $Connection |
            Where-Object { $_.SqlError } |
            Select-Object -ExpandProperty SqlError -First 1

        if ($SqlError) {
            Write-Host $SqlError -ForegroundColor Red
        }

        continue
    }

    Write-Host "Verbindung erfolgreich." -ForegroundColor Green


    # =============================================================================================
    # BLOCK 01 - SP_CONFIGURE AUSLESEN
    # =============================================================================================

    Write-Section "SP_CONFIGURE"

    try {
        $SpConfigureChecks = [ordered]@{
            MaxServerMemory             = "max server memory (MB)"
            MaxDegreeOfParallelism      = "max degree of parallelism"
            CostThresholdForParallelism = "cost threshold for parallelism"
            BackupCompressionDefault    = "backup compression default"
            OptimizeForAdHocWorkloads   = "optimize for ad hoc workloads"
        }

        $SpConfigure = Get-DbaSpConfigure `
            -SqlInstance $TargetInstance `
            -Name @($SpConfigureChecks.Values) `
            -EnableException

        foreach ($ConfigurationValue in $SpConfigure) {

            $DisplayName  = ([string]$ConfigurationValue.DisplayName).Trim()
            $InternalName = ([string]$ConfigurationValue.Name).Trim()

            $SelectedCheck = $SpConfigureChecks.GetEnumerator() |
                Where-Object {
                    $_.Value -eq $DisplayName -or
                    $_.Value -eq $InternalName
                } |
                Select-Object -First 1

            if ($null -eq $SelectedCheck) {
                continue
            }

            $CollectedValues += [PSCustomObject]@{
                SqlServer       = $SqlServerName
                SqlInstance     = $StoredInstanceName
                DatabaseName    = $null
                ObjectName      = $null
                CheckName       = [string]$SelectedCheck.Key
                ConfiguredValue = [string]$ConfigurationValue.ConfiguredValue
                RunningValue    = [string]$ConfigurationValue.RunningValue
                CollectedAt     = $RunTimestamp
            }
        }
    }
    catch {
        Write-Host "FEHLER beim Auslesen von SP_CONFIGURE auf $TargetInstance" -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Red
    }


    # =============================================================================================
    # BLOCK 02 - DATENBANKDATEIEN UND AUTOGROWTH
    # =============================================================================================

    Write-Section "DATENBANKDATEIEN UND AUTOGROWTH"

    $DatabaseFileQuery = @"
SELECT
    d.name AS DatabaseName,
    mf.name AS ObjectName,
    mf.type_desc AS FileType,

    CASE
        WHEN mf.growth > 0 THEN N'True'
        ELSE N'False'
    END AS AutogrowthEnabled,

    CASE
        WHEN mf.growth = 0 THEN N'None'
        WHEN mf.is_percent_growth = 1 THEN N'Percent'
        ELSE N'MB'
    END AS GrowthType,

    CASE
        WHEN mf.growth = 0
            THEN CONVERT(DECIMAL(19,2), 0)
        WHEN mf.is_percent_growth = 1
            THEN CONVERT(DECIMAL(19,2), mf.growth)
        ELSE
            CONVERT(DECIMAL(19,2), mf.growth * 8.0 / 1024.0)
    END AS GrowthValue,

    CASE
        WHEN mf.max_size = -1 THEN N'Unlimited'
        WHEN mf.max_size = 0 THEN N'NoGrowth'
        ELSE
            CONVERT
            (
                NVARCHAR(255),
                CONVERT(DECIMAL(19,2), mf.max_size * 8.0 / 1024.0)
            )
    END AS MaxSizeMB

FROM sys.master_files AS mf
INNER JOIN sys.databases AS d
    ON d.database_id = mf.database_id

ORDER BY
    d.name,
    mf.type_desc,
    mf.name;
"@

    try {
        $DatabaseFiles = Invoke-DbaQuery `
            -SqlInstance $TargetInstance `
            -Database "master" `
            -Query $DatabaseFileQuery `
            -As PSObject `
            -EnableException

        foreach ($DatabaseFile in $DatabaseFiles) {

            $DatabaseName = ([string]$DatabaseFile.DatabaseName).Trim()
            $ObjectName   = ([string]$DatabaseFile.ObjectName).Trim()

            $FileChecks = [ordered]@{
                FileType          = $DatabaseFile.FileType
                AutogrowthEnabled = $DatabaseFile.AutogrowthEnabled
                GrowthType        = $DatabaseFile.GrowthType
                GrowthValue       = $DatabaseFile.GrowthValue
                MaxSizeMB         = $DatabaseFile.MaxSizeMB
            }

            foreach ($FileCheck in $FileChecks.GetEnumerator()) {

                $CollectedValues += [PSCustomObject]@{
                    SqlServer       = $SqlServerName
                    SqlInstance     = $StoredInstanceName
                    DatabaseName    = $DatabaseName
                    ObjectName      = $ObjectName
                    CheckName       = [string]$FileCheck.Key
                    ConfiguredValue = [string]$FileCheck.Value
                    RunningValue    = $null
                    CollectedAt     = $RunTimestamp
                }
            }
        }
    }
    catch {
        Write-Host "FEHLER beim Auslesen der Datenbankdateien auf $TargetInstance" -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Red
    }


    # =============================================================================================
    # BLOCK 03 - SQL-AGENT-JOBS AUSLESEN
    # LastRunDate und NextRunDate bleiben wie in deiner bisherigen Version enthalten.
    # =============================================================================================

    Write-Section "SQL-AGENT-JOBS"

    try {
        $AgentJobs = @(
            Get-DbaAgentJob `
                -SqlInstance $TargetInstance `
                -EnableException
        )

        foreach ($AgentJob in $AgentJobs) {

            $JobName = ([string]$AgentJob.Name).Trim()

            if (-not $JobName) {
                continue
            }

            $JobChecks = [ordered]@{
                JobEnabled  = $AgentJob.IsEnabled
                LastRunDate = $AgentJob.LastRunDate
                NextRunDate = $AgentJob.NextRunDate
            }

            foreach ($JobCheck in $JobChecks.GetEnumerator()) {

                $CollectedValues += [PSCustomObject]@{
                    SqlServer       = $SqlServerName
                    SqlInstance     = $StoredInstanceName
                    DatabaseName    = $null
                    ObjectName      = $JobName
                    CheckName       = [string]$JobCheck.Key
                    ConfiguredValue = [string]$JobCheck.Value
                    RunningValue    = $null
                    CollectedAt     = $RunTimestamp
                }
            }
        }

        Write-Host "$($AgentJobs.Count) SQL-Agent-Jobs gefunden." -ForegroundColor Green
    }
    catch {
        Write-Host "FEHLER beim Auslesen der SQL-Agent-Jobs auf $TargetInstance" -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Red
    }


    # =============================================================================================
    # BLOCK 04 - DATABASE MAIL KONFIGURATION
    # =============================================================================================

    Write-Section "DATABASE MAIL KONFIGURATION"

    $DatabaseMailCheckNames = @(
        "DatabaseMailXPsEnabled"
        "DatabaseMailStatus"
        "ServiceBrokerEnabled"
        "MailProfileConfigured"
        "SQLAgentMailProfileEnabled"
        "SQLAgentMailProfile"
        "SQLAgentDatabaseMailEnabled"
        "UseDatabaseMail"
    )

    $DatabaseMailQuery = @"
SET NOCOUNT ON;

DECLARE @DatabaseMailStatus NVARCHAR(20) = NULL;
DECLARE @SQLAgentMailProfile NVARCHAR(255) = NULL;
DECLARE @UseDatabaseMail INT = NULL;

BEGIN TRY
    DECLARE @MailStatus TABLE
    (
        [Status] NVARCHAR(20)
    );

    INSERT INTO @MailStatus
    EXECUTE msdb.dbo.sysmail_help_status_sp;

    SELECT TOP (1)
        @DatabaseMailStatus = [Status]
    FROM @MailStatus;
END TRY
BEGIN CATCH
    SET @DatabaseMailStatus = NULL;
END CATCH;

BEGIN TRY
    EXEC master.dbo.xp_instance_regread
        N'HKEY_LOCAL_MACHINE',
        N'SOFTWARE\Microsoft\MSSQLServer\SQLServerAgent',
        N'DatabaseMailProfile',
        @SQLAgentMailProfile OUTPUT;
END TRY
BEGIN CATCH
    SET @SQLAgentMailProfile = NULL;
END CATCH;

BEGIN TRY
    EXEC master.dbo.xp_instance_regread
        N'HKEY_LOCAL_MACHINE',
        N'SOFTWARE\Microsoft\MSSQLServer\SQLServerAgent',
        N'UseDatabaseMail',
        @UseDatabaseMail OUTPUT;
END TRY
BEGIN CATCH
    SET @UseDatabaseMail = NULL;
END CATCH;

SELECT
    CAST
    (
        CASE
            WHEN EXISTS
            (
                SELECT 1
                FROM sys.configurations
                WHERE name = N'Database Mail XPs'
                  AND value_in_use = 1
            )
            THEN 1
            ELSE 0
        END AS BIT
    ) AS DatabaseMailXPsEnabled,

    @DatabaseMailStatus AS DatabaseMailStatus,

    CAST
    (
        ISNULL
        (
            (
                SELECT is_broker_enabled
                FROM sys.databases
                WHERE name = N'msdb'
            ),
            0
        ) AS BIT
    ) AS ServiceBrokerEnabled,

    CAST
    (
        CASE
            WHEN EXISTS (SELECT 1 FROM msdb.dbo.sysmail_profile)
             AND EXISTS (SELECT 1 FROM msdb.dbo.sysmail_account)
             AND EXISTS (SELECT 1 FROM msdb.dbo.sysmail_profileaccount)
            THEN 1
            ELSE 0
        END AS BIT
    ) AS MailProfileConfigured,

    CAST
    (
        CASE
            WHEN ISNULL(@SQLAgentMailProfile, N'') <> N''
            THEN 1
            ELSE 0
        END AS BIT
    ) AS SQLAgentMailProfileEnabled,

    ISNULL(@SQLAgentMailProfile, N'NOT CONFIGURED')
        AS SQLAgentMailProfile,

    CAST
    (
        CASE
            WHEN ISNULL(@UseDatabaseMail, 0) = 1
            THEN 1
            ELSE 0
        END AS BIT
    ) AS SQLAgentDatabaseMailEnabled,

    ISNULL(@UseDatabaseMail, 0) AS UseDatabaseMail;
"@

    try {
        $DatabaseMailRow = Invoke-DbaQuery `
            -SqlInstance $TargetInstance `
            -Database "msdb" `
            -Query $DatabaseMailQuery `
            -QueryTimeout 60 `
            -As PSObject `
            -EnableException |
            Select-Object -First 1

        if ($null -ne $DatabaseMailRow) {

            foreach ($CheckName in $DatabaseMailCheckNames) {

                $ConfiguredValue = $DatabaseMailRow.$CheckName

                if (
                    $null -eq $ConfiguredValue -or
                    $ConfiguredValue -is [System.DBNull]
                ) {
                    $ConfiguredValue = $null
                }
                else {
                    $ConfiguredValue = [string]$ConfiguredValue
                }

                $CollectedValues += [PSCustomObject]@{
                    SqlServer       = $SqlServerName
                    SqlInstance     = $StoredInstanceName
                    DatabaseName    = $null
                    ObjectName      = $null
                    CheckName       = $CheckName
                    ConfiguredValue = $ConfiguredValue
                    RunningValue    = $null
                    CollectedAt     = $RunTimestamp
                }
            }

            Write-Host "Database-Mail-Werte ausgelesen." -ForegroundColor Green
        }
        else {
            Write-Host "WARNUNG: Keine Database-Mail-Werte erhalten." -ForegroundColor Yellow
        }
    }
    catch {
        Write-Host "FEHLER beim Auslesen der Database-Mail-Konfiguration auf $TargetInstance" -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Red
    }


    # =============================================================================================
    # BLOCK 05 - OLA-BACKUPJOBS VORHANDEN UND AKTIVIERT
    # =============================================================================================

    Write-Section "OLA-BACKUPJOBS"

    $RequiredOlaJobs = [ordered]@{
        OlaSystemDatabasesFull = "DatabaseBackup - SYSTEM_DATABASES - FULL"
        OlaUserDatabasesFull   = "DatabaseBackup - USER_DATABASES - FULL"
        OlaUserDatabasesDiff   = "DatabaseBackup - USER_DATABASES - DIFF"
        OlaUserDatabasesLog    = "DatabaseBackup - USER_DATABASES - LOG"
    }

    try {
        $ExistingAgentJobs = @(
            Get-DbaAgentJob `
                -SqlInstance $TargetInstance `
                -EnableException
        )

        foreach ($RequiredOlaJob in $RequiredOlaJobs.GetEnumerator()) {

            $CheckPrefix = [string]$RequiredOlaJob.Key
            $JobName     = [string]$RequiredOlaJob.Value

            $ExistingJob = $ExistingAgentJobs |
                Where-Object {
                    ([string]$_.Name).Trim() -eq $JobName
                } |
                Select-Object -First 1

            $JobExists  = $null -ne $ExistingJob
            $JobEnabled = $false

            if ($JobExists) {
                $JobEnabled = [bool]$ExistingJob.IsEnabled
            }

            $CollectedValues += [PSCustomObject]@{
                SqlServer       = $SqlServerName
                SqlInstance     = $StoredInstanceName
                DatabaseName    = $null
                ObjectName      = $JobName
                CheckName       = "${CheckPrefix}Exists"
                ConfiguredValue = [string]$JobExists
                RunningValue    = $null
                CollectedAt     = $RunTimestamp
            }

            $CollectedValues += [PSCustomObject]@{
                SqlServer       = $SqlServerName
                SqlInstance     = $StoredInstanceName
                DatabaseName    = $null
                ObjectName      = $JobName
                CheckName       = "${CheckPrefix}Enabled"
                ConfiguredValue = [string]$JobEnabled
                RunningValue    = $null
                CollectedAt     = $RunTimestamp
            }

            if (-not $JobExists) {
                Write-Host "FEHLT:       $JobName" -ForegroundColor Red
            }
            elseif (-not $JobEnabled) {
                Write-Host "DEAKTIVIERT: $JobName" -ForegroundColor Red
            }
            else {
                Write-Host "OK:          $JobName" -ForegroundColor Green
            }
        }
    }
    catch {
        Write-Host "FEHLER beim Pruefen der OLA-Backupjobs auf $TargetInstance" -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Red
    }


    # =============================================================================================
    # BLOCK 06 - OLA-BACKUPPARAMETER AUS DEN SQL-AGENT-JOBSTEPS AUSLESEN
    #
    # Die CheckNames setzen sich aus dem OLA-Job-Praefix und dem Parameternamen zusammen.
    # Beispiel: OlaUserDatabasesFullCompress
    # =============================================================================================

    Write-Section "OLA-BACKUPPARAMETER"

    $OlaBackupParameterNames = @(
        "Databases"
        "Directory"
        "BackupType"
        "Verify"
        "Compress"
        "CleanupTime"
        "CheckSum"
        "LogToTable"
    )

    $OlaBackupJobStepQuery = @"
SELECT
    J.name          AS JobName,
    S.step_id       AS StepId,
    S.step_name     AS StepName,
    S.database_name AS ExecutionDatabase,
    S.command       AS Command
FROM msdb.dbo.sysjobs AS J
INNER JOIN msdb.dbo.sysjobsteps AS S
    ON S.job_id = J.job_id
WHERE S.subsystem = N'TSQL'
  AND S.command LIKE N'%DatabaseBackup%'
ORDER BY
    J.name,
    S.step_id;
"@

    try {
        $OlaBackupJobSteps = @(
            Invoke-DbaQuery `
                -SqlInstance $TargetInstance `
                -Database "msdb" `
                -Query $OlaBackupJobStepQuery `
                -QueryTimeout 60 `
                -As PSObject `
                -EnableException
        )

        foreach ($OlaBackupJob in $RequiredOlaJobs.GetEnumerator()) {

            $CheckPrefix = [string]$OlaBackupJob.Key
            $JobName     = [string]$OlaBackupJob.Value

            $OlaBackupJobStep = $OlaBackupJobSteps |
                Where-Object {
                    ([string]$_.JobName).Trim() -eq $JobName -and
                    ([string]$_.Command) -match '(?i)(?:EXEC(?:UTE)?\s+)?(?:\[?dbo\]?\.)?\[?DatabaseBackup\]?'
                } |
                Sort-Object StepId |
                Select-Object -First 1

            if ($null -eq $OlaBackupJobStep) {
                Write-Host "WARNUNG: Kein DatabaseBackup-Jobstep gefunden: $JobName" -ForegroundColor Yellow
                continue
            }

            $JobStepCommand = [string]$OlaBackupJobStep.Command
            $StepName       = ([string]$OlaBackupJobStep.StepName).Trim()
            $ObjectName     = "$JobName / $StepName"

            if ($ObjectName.Length -gt 255) {
                $ObjectName = $ObjectName.Substring(0, 255)
            }

            $ExtractedParameterCount = 0

            foreach ($ParameterName in $OlaBackupParameterNames) {

                $EscapedParameterName = [Regex]::Escape($ParameterName)
                $ParameterPattern = "(?is)(?<![A-Za-z0-9_])@$EscapedParameterName\s*=\s*(?<Value>N?'(?:''|[^'])*'|[^,\r\n;]+)"
                $ParameterMatch = [Regex]::Match($JobStepCommand, $ParameterPattern)

                if (-not $ParameterMatch.Success) {
                    continue
                }

                $RawValue = $ParameterMatch.Groups["Value"].Value.Trim()
                $StringLiteralMatch = [Regex]::Match(
                    $RawValue,
                    "^(?:N)?'(?<Text>(?:''|[^'])*)'$",
                    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
                )

                if ($StringLiteralMatch.Success) {
                    $ConfiguredValue = $StringLiteralMatch.Groups["Text"].Value.Replace("''", "'")
                }
                elseif ($RawValue -match '(?i)^NULL$') {
                    $ConfiguredValue = $null
                }
                else {
                    $ConfiguredValue = $RawValue
                }

                $CollectedValues += [PSCustomObject]@{
                    SqlServer       = $SqlServerName
                    SqlInstance     = $StoredInstanceName
                    DatabaseName    = $null
                    ObjectName      = $ObjectName
                    CheckName       = "${CheckPrefix}${ParameterName}"
                    ConfiguredValue = $ConfiguredValue
                    RunningValue    = $null
                    CollectedAt     = $RunTimestamp
                }

                $ExtractedParameterCount++
                Write-Host "  $ParameterName = $ConfiguredValue"
            }

            if ($ExtractedParameterCount -eq 0) {
                Write-Host "WARNUNG: Keine OLA-Parameter aus $JobName ausgelesen." -ForegroundColor Yellow
            }
            else {
                Write-Host "$ExtractedParameterCount OLA-Parameter aus $JobName ausgelesen." -ForegroundColor Green
            }
        }
    }
    catch {
        Write-Host "FEHLER beim Auslesen der OLA-Backupparameter auf $TargetInstance" -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Red
    }


    # =============================================================================================
    # Weitere instanzbezogene Auslesebloecke hier einfuegen
    # =============================================================================================

} # Ende foreach ($Server in $Servers)


# =================================================================================================
# 04 - IST-WERTE SPEICHERN
# =================================================================================================

Write-Section "04 - IST-WERTE SPEICHERN"

if ($CollectedValues.Count -eq 0) {
    Write-Host "Keine IST-Werte gesammelt. Die Tabelle bleibt leer." -ForegroundColor Yellow
}
else {
    try {
        $WriteParameters = @{
            SqlInstance     = $RepositoryServer
            Database        = $RepositoryDatabase
            Schema          = "dbo"
            Table           = "ComplianceCurrentValues"
            InputObject     = $CollectedValues
            EnableException = $true
        }

        Write-DbaDbTableData @WriteParameters

        Write-Host "$($CollectedValues.Count) IST-Werte gespeichert." -ForegroundColor Green
        Write-Host "Zeitstempel: $RunTimestamp" -ForegroundColor Green
    }
        catch {
        Write-Host "FEHLER beim Speichern der IST-Werte:" -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Red
        return
    }
}

# =================================================================================================
# ENDE
# =================================================================================================

Write-Section "SQL SERVER IST-WERTE AUSLESEN ABGESCHLOSSEN"
