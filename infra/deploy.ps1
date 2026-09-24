# Cria os recursos no Azure (infra/main.bicep) e publica a API no App Service.
# Pré-requisitos: Azure CLI (az) e .NET 9 SDK. Rode antes: az login
#
# Exemplo:
#   ./infra/deploy.ps1 -GrupoRecursos rg-cracha -Local brazilsouth

param(
    [string]$GrupoRecursos = "rg-cracha",
    [string]$Local = "brazilsouth",
    [string]$Prefixo = "cracha"
)

$ErrorActionPreference = "Stop"
$raiz = Split-Path $PSScriptRoot -Parent

$senha = Read-Host "Senha do administrador do SQL (mín. 8 caracteres, com maiúscula, minúscula e número)" -AsSecureString
$senhaTexto = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($senha))

Write-Host "1/3 Grupo de recursos $GrupoRecursos em $Local"
az group create --name $GrupoRecursos --location $Local --output none

Write-Host "2/3 App Service, SQL Database e Storage Account (Azure Table)"
$saida = az deployment group create `
    --resource-group $GrupoRecursos `
    --template-file (Join-Path $PSScriptRoot "main.bicep") `
    --parameters prefixo=$Prefixo sqlSenha=$senhaTexto `
    --query properties.outputs --output json | ConvertFrom-Json

Write-Host "3/3 Publicando a API"
$pasta = Join-Path $env:TEMP "cracha-publish"
$zip = Join-Path $env:TEMP "cracha-publish.zip"
Remove-Item $pasta, $zip -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish (Join-Path $raiz "src/Cracha.Api") --configuration Release --output $pasta
Compress-Archive -Path (Join-Path $pasta "*") -DestinationPath $zip
az webapp deploy --resource-group $GrupoRecursos --name $saida.nomeApi.value --src-path $zip --type zip --output none

Write-Host ""
Write-Host "Pronto: $($saida.urlApi.value)"
Write-Host "Para apagar tudo depois: az group delete --name $GrupoRecursos"
