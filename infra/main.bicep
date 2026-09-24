// Crachá no Azure: App Service (API + frontend) + SQL Database (cadastro) + Storage Account/Azure Table (histórico).
// Uso: veja infra/deploy.ps1 ou a seção "Publicando no Azure" do README.

@description('Prefixo dos recursos (letras minúsculas e números).')
@minLength(3)
@maxLength(11)
param prefixo string = 'cracha'

param location string = resourceGroup().location

@description('Usuário administrador do SQL Server.')
param sqlAdmin string = 'rhadmin'

@secure()
@description('Senha do administrador do SQL Server.')
param sqlSenha string

@description('Cria departamentos e funcionários de exemplo no primeiro acesso.')
param criarExemplos bool = true

@description('SKU do App Service Plan. F1 é gratuito; B1 permite Always On.')
param skuPlano string = 'F1'

var sufixo = uniqueString(resourceGroup().id)
var nomeTabela = 'FuncionarioLog'

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: toLower('${prefixo}st${take(sufixo, 8)}')
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource tabelaLogs 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' = {
  parent: tableService
  name: nomeTabela
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: '${prefixo}-sql-${sufixo}'
  location: location
  properties: {
    administratorLogin: sqlAdmin
    administratorLoginPassword: sqlSenha
    minimalTlsVersion: '1.2'
  }
}

// Libera o acesso a partir de serviços do Azure (o App Service).
resource firewallAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource banco 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: 'Cracha'
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
}

resource plano 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${prefixo}-plano'
  location: location
  kind: 'linux'
  sku: { name: skuPlano }
  properties: { reserved: true }
}

resource api 'Microsoft.Web/sites@2023-12-01' = {
  name: '${prefixo}-api-${sufixo}'
  location: location
  kind: 'app,linux'
  properties: {
    serverFarmId: plano.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|9.0'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      // Chaves com "__" viram seções da configuração do .NET (Banco:Provedor etc.).
      appSettings: [
        {
          name: 'Banco__Provedor'
          value: 'SqlServer'
        }
        {
          name: 'Banco__ConnectionString'
          value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${banco.name};User ID=${sqlAdmin};Password=${sqlSenha};Encrypt=True;TrustServerCertificate=False;'
        }
        {
          name: 'Banco__CriarExemplos'
          value: string(criarExemplos)
        }
        {
          name: 'Historico__Provedor'
          value: 'AzureTable'
        }
        {
          name: 'Historico__ConnectionString'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
        }
        {
          name: 'Historico__Tabela'
          value: nomeTabela
        }
      ]
    }
  }
}

output urlApi string = 'https://${api.properties.defaultHostName}'
output nomeApi string = api.name
