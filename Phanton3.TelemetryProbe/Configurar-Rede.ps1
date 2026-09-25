param(
    [ValidateSet('Aplicar', 'Restaurar')]
    [string]$Acao = 'Aplicar'
)

$ErrorActionPreference = 'Stop'
$admin = [Security.Principal.WindowsPrincipal]::new(
    [Security.Principal.WindowsIdentity]::GetCurrent()
).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $admin) {
    throw 'Abra o PowerShell como administrador para configurar as prioridades das placas.'
}

$backupPath = Join-Path $PSScriptRoot '.network-metrics-backup.json'

if ($Acao -eq 'Aplicar') {
    $wifi = Get-NetIPInterface -InterfaceAlias 'Wi-Fi' -AddressFamily IPv4
    $ethernet = Get-NetIPInterface -InterfaceAlias 'Ethernet' -AddressFamily IPv4

    if (-not (Test-Path -LiteralPath $backupPath)) {
        @($wifi, $ethernet) |
            ForEach-Object {
                [pscustomobject]@{
                    InterfaceAlias = $_.InterfaceAlias
                    InterfaceMetric = $_.InterfaceMetric
                    AutomaticMetric = $_.AutomaticMetric.ToString()
                }
            } |
            ConvertTo-Json |
            Set-Content -LiteralPath $backupPath -Encoding UTF8
    }

    Set-NetIPInterface -InterfaceAlias 'Ethernet' -AddressFamily IPv4 -InterfaceMetric 10
    Set-NetIPInterface -InterfaceAlias 'Wi-Fi' -AddressFamily IPv4 -InterfaceMetric 100
    Write-Host 'Ethernet priorizada para a internet; Wi-Fi disponível para a telemetria DJI.'
}
else {
    if (-not (Test-Path -LiteralPath $backupPath)) {
        throw 'Não encontrei o arquivo de backup das prioridades anteriores.'
    }

    $previous = Get-Content -LiteralPath $backupPath -Raw | ConvertFrom-Json
    foreach ($item in $previous) {
        if ($item.AutomaticMetric -eq 'Enabled') {
            Set-NetIPInterface -InterfaceAlias $item.InterfaceAlias -AddressFamily IPv4 -AutomaticMetric Enabled
        }
        else {
            Set-NetIPInterface -InterfaceAlias $item.InterfaceAlias -AddressFamily IPv4 -InterfaceMetric $item.InterfaceMetric
        }
    }

    Write-Host 'Prioridades anteriores restauradas.'
}
