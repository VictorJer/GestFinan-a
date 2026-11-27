param(
    # Recebe o relatório JSON produzido pelo reporter do Vitest no diretório temporário do runner.
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ReportPath
)

# Exige que a execução tenha produzido um relatório legível, para não confundir ausência de evidência com sucesso.
if (-not (Test-Path -LiteralPath $ReportPath -PathType Leaf)) {
    Write-Error "Relatório Vitest ausente: $ReportPath"
    exit 1
}

try {
    $report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json -ErrorAction Stop
}
catch {
    Write-Error "Não foi possível interpretar o relatório JSON do Vitest: $($_.Exception.Message)"
    exit 1
}

# Recusa seleção vazia, falha do runner ou qualquer teste que não tenha terminado aprovado.
$totalTests = [int]$report.numTotalTests
$passedTests = [int]$report.numPassedTests
$failedTests = [int]$report.numFailedTests
$nonPassedAssertions = @(
    $report.testResults |
        ForEach-Object { $_.assertionResults } |
        Where-Object { $_.status -ne 'passed' }
)

if ($report.success -ne $true -or $totalTests -lt 1 -or $passedTests -ne $totalTests -or $failedTests -gt 0 -or $nonPassedAssertions.Count -gt 0) {
    Write-Error "Vitest não aprovou toda a seleção: total=$totalTests, aprovados=$passedTests, falhas=$failedTests, não aprovados=$($nonPassedAssertions.Count)."
    exit 1
}

# Registra a contagem observada para que o check identifique a seleção efetivamente executada.
Write-Host "Vitest aprovado: $($report.numTotalTestSuites) suítes, $totalTests testes, $passedTests aprovados, sem casos ignorados ou pendentes."
