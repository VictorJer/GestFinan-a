param(
    [Parameter(Mandatory = $true)]
    [string] $ResultsDirectory
)

# Valida que o runner produziu ao menos um TRX e lê somente os resultados dos testes selecionados.
if (-not (Test-Path -LiteralPath $ResultsDirectory -PathType Container)) {
    throw "Diretório de resultados TRX não encontrado: $ResultsDirectory"
}

$reports = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -File -Recurse)
if ($reports.Count -eq 0) {
    throw "Nenhum relatório TRX foi produzido em $ResultsDirectory."
}

# TRX registra outcomes por teste; qualquer teste ignorado é obrigatório neste recorte e deixa o job vermelho.
$results = [System.Collections.Generic.List[object]]::new()
foreach ($report in $reports) {
    [xml] $document = Get-Content -LiteralPath $report.FullName -Raw
    foreach ($result in $document.SelectNodes("//*[local-name()='UnitTestResult']")) {
        $results.Add([pscustomobject]@{
            Name = $result.GetAttribute('testName')
            Outcome = $result.GetAttribute('outcome')
            Report = $report.Name
        })
    }
}

# A guarda adicional detecta relatório vazio e estados diferentes de aprovação, inclusive NotExecuted/Skipped.
if ($results.Count -eq 0) {
    throw 'Os relatórios TRX não contêm resultados de teste.'
}

$passed = @($results | Where-Object { $_.Outcome -eq 'Passed' }).Count
$skipped = @($results | Where-Object { $_.Outcome -in @('NotExecuted', 'Skipped') })
$otherFailures = @($results | Where-Object { $_.Outcome -notin @('Passed', 'NotExecuted', 'Skipped') })
Write-Output "TRX: $passed aprovados; $($skipped.Count) ignorados; $($otherFailures.Count) outros resultados não aprovados; $($results.Count) total."

if ($skipped.Count -gt 0) {
    $names = ($skipped | ForEach-Object { $_.Name }) -join ', '
    throw "Testes obrigatórios ignorados: $names"
}

if ($otherFailures.Count -gt 0 -or $passed -ne $results.Count) {
    $names = ($otherFailures | ForEach-Object { "$($_.Outcome): $($_.Name)" }) -join ', '
    throw "Há resultados de teste diferentes de Passed: $names"
}
