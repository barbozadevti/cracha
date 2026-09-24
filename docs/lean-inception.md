# Lean Inception: Crachá

> Decisões de produto do Crachá no formato Lean Inception (Paulo Caroli):
> visão → escopo → personas → jornadas → funcionalidades → sequenciamento → MVP.

---

## 1. Visão do produto

**Para** pequenas e médias empresas que ainda controlam o quadro de pessoal em planilhas
**cujo** problema é não saber quem mudou o quê, quando e por quê (salário, cargo, departamento),
**o Crachá** é um sistema de RH na web
**que** cadastra funcionários e departamentos e guarda o histórico de cada alteração, com o antes e o depois.
**Diferente de** planilhas compartilhadas, em que qualquer edição apaga o valor anterior,
**o nosso produto** registra toda mudança num log imutável (Azure Table) e mostra indicadores do quadro na hora.

## 2. O produto É / NÃO É / FAZ / NÃO FAZ

| É | NÃO É |
|---|---|
| Um cadastro de pessoas com trilha de auditoria | Uma folha de pagamento (cálculo de INSS, IRRF, holerite) |
| Uma API REST documentada (Swagger) com interface web | Um sistema de ponto eletrônico |
| Pronto para o Azure (App Service, SQL Database, Azure Table) | Um ERP completo |

| FAZ | NÃO FAZ |
|---|---|
| Cadastra, edita, desliga, reativa e remove funcionários | Envia dados ao eSocial |
| Registra cada alteração com os campos que mudaram | Gerencia férias e benefícios (ainda) |
| Organiza por departamento, com cor e folha mensal | Controla acesso por perfil de usuário (ainda) |
| Mostra quadro, folha, rotatividade e aniversários de empresa | Gera organograma (ainda) |
| Busca por nome, cargo, e-mail ou ramal | Importa planilhas antigas (ainda) |

## 3. Objetivos do produto

1. **Rastreabilidade**: toda mudança de um funcionário fica registrada, inclusive depois de ele ser removido.
2. **Cadastro confiável**: e-mail único, ramal válido, departamento existente e datas coerentes.
3. **Visão do quadro**: quantas pessoas, quanto custa a folha e como está a rotatividade, sem montar planilha.
4. **Pronto para nuvem**: o mesmo código roda local (SQLite) e no Azure (SQL Database + Azure Table).

## 4. Personas

### Gabriela, gerente de RH (41 anos)
- **Perfil:** cuida de 20 a 200 pessoas; responde à diretoria e à auditoria.
- **Comportamento:** precisa explicar reajustes e transferências meses depois de acontecerem.
- **Necessidades:** histórico por pessoa com o valor antigo e o novo; indicadores de folha e rotatividade.

### Henrique, analista de departamento pessoal (30 anos)
- **Perfil:** faz admissões, desligamentos e correções cadastrais todo dia.
- **Comportamento:** trabalha rápido, com muitas pessoas parecidas no cadastro.
- **Necessidades:** busca instantânea, formulário que avisa o erro no campo certo, desligar sem apagar.

### Natália, gestora comercial (38 anos)
- **Perfil:** lidera um time e quer saber quem é quem.
- **Comportamento:** consulta ramal e e-mail, acompanha o tempo de casa do time.
- **Necessidades:** diretório visual (crachás) filtrado pelo seu departamento e aniversários de empresa.

## 5. Jornadas

**Henrique admite uma pessoa**
1. Abre o Crachá e clica em **+ Novo funcionário**.
2. Preenche nome, cargo (com sugestões dos cargos existentes), departamento, e-mail, ramal, salário e admissão.
3. Erra o ramal (3 dígitos): o formulário mostra a mensagem embaixo do campo.
4. Salva e cai na ficha da pessoa, já com o registro de **Admissão** na linha do tempo.

**Gabriela responde à auditoria**
1. Abre a ficha de Rafael pela busca.
2. Na linha do tempo, vê a transferência de *Comercial → Tecnologia* com a troca de cargo e, depois, o reajuste de *R$ 7.200,00 → R$ 8.100,00*.
3. No **Histórico**, filtra por *Desligamento* para listar as saídas do ano.

**Natália consulta o time**
1. Em **Departamentos**, clica em *Ver pessoas* no Comercial.
2. Vê os crachás do time com ramal e tempo de casa.
3. No **Painel**, confere quem completa ano de empresa este mês.

## 6. Brainstorming de funcionalidades

| # | Funcionalidade | Persona principal |
|---|---|---|
| F1 | CRUD de funcionários (desafio original) | Henrique |
| F2 | Log de toda alteração em Azure Table (desafio original) | Gabriela |
| F3 | Departamentos como entidade, com cor | Gabriela |
| F4 | Comparação antes/depois em cada alteração | Gabriela |
| F5 | Desligar e reativar sem apagar o cadastro | Henrique |
| F6 | Linha do tempo por funcionário e histórico geral com filtros | Gabriela |
| F7 | Diretório em crachás e tabela, com busca, filtros e ordenação | Natália, Henrique |
| F8 | Painel: quadro, folha, salário médio, tempo de casa, rotatividade, admissões × desligamentos, aniversários | Gabriela, Natália |
| F9 | Validações (e-mail único, ramal, datas) com mensagens por campo | Henrique |
| F10 | SQLite local ou SQL Server/Azure SQL, com migrations | Dev |
| F11 | Histórico no banco ou na Azure Table (Azurite local) | Dev |
| F12 | Infraestrutura como código (Bicep) e script de publicação | Dev |
| F13 | Atalho na Área de Trabalho | Todas |
| F14 | Login com Microsoft Entra ID e perfis (RH × gestor) | Gabriela |
| F15 | Férias e afastamentos | Henrique |
| F16 | Organograma (gestor de cada pessoa) | Natália |
| F17 | Exportar relatórios (CSV/PDF) | Gabriela |

## 7. Revisão técnica, de negócio e de UX

Escala: esforço E (1 baixo – 3 alto), valor de negócio $ (1–3), valor de UX ♥ (1–3).

| # | E | $ | ♥ | Observação |
|---|---|---|---|---|
| F1 | 1 | 3 | 2 | Base do desafio: controllers + EF Core |
| F2 | 2 | 3 | 1 | `Azure.Data.Tables`; PartitionKey = departamento, RowKey = ticks invertidos (mais novo primeiro) |
| F3 | 1 | 2 | 2 | FK com `Restrict`: não se remove departamento com gente |
| F4 | 1 | 3 | 3 | Foto do funcionário em JSON + lista de campos alterados já formatada em pt-BR |
| F5 | 1 | 3 | 2 | Situação + data de desligamento; validações de estado (409) |
| F6 | 1 | 3 | 3 | Mesmo contrato (`IHistorico`) para banco e Azure Table |
| F7 | 2 | 2 | 3 | Ordenação em memória: o SQLite não ordena `decimal` |
| F8 | 2 | 3 | 3 | `TimeProvider` injetado para testar datas |
| F9 | 1 | 2 | 3 | DataAnnotations + `IValidatableObject`; testes rodam em pt-BR |
| F10 | 2 | 3 | 1 | Um `DbContext` por provedor, cada um com suas migrations |
| F11 | 2 | 2 | 1 | Azurite no Windows (npm) e no CI (Docker) |
| F12 | 2 | 3 | 1 | App Service F1 + SQL Basic + Storage; `az bicep build` no CI |
| F13 | 1 | 1 | 3 | Sobe o Azurite sozinho se estiver instalado |
| F14 | 3 | 3 | 2 | |
| F15 | 3 | 2 | 2 | |
| F16 | 2 | 2 | 3 | |
| F17 | 2 | 2 | 2 | |

## 8. Sequenciador

| Onda | Funcionalidades | Resultado |
|---|---|---|
| **1: Desafio** ✅ | F1, F2, F10 | CRUD com log em Azure Table |
| **2: Auditoria** ✅ | F3, F4, F5, F6, F9 | Saber quem mudou o quê, com cadastro confiável |
| **3: Visão do quadro** ✅ | F7, F8, F13 | Diretório, painel e acesso com um clique |
| **4: Nuvem** ✅ | F11, F12 | Azurite, Bicep e CI com SQL Server + Azure Table |
| 5: Acesso | F14 | Login e perfis |
| 6: Rotina de DP | F15, F16, F17 | Férias, organograma e relatórios |

✅ = implementado nesta versão.

## 9. Canvas do MVP

| Bloco | Conteúdo |
|---|---|
| **Proposta do MVP** | Cadastrar pessoas e nunca perder o histórico do que mudou. |
| **Personas segmentadas** | Gabriela (auditoria) e Henrique (cadastro do dia a dia) |
| **Jornadas atendidas** | "Henrique admite uma pessoa", "Gabriela responde à auditoria" |
| **Funcionalidades** | CRUD, departamentos, desligar/reativar, histórico com antes e depois, validações |
| **Resultado esperado** | Qualquer reajuste ou transferência explicado em menos de 1 minuto, direto na ficha. |
| **Métricas para validar** | Testes de integração cobrindo cada tipo de alteração, rodando com SQLite e com SQL Server + Azure Table. |
| **Custo e cronograma** | Uma pessoa desenvolvedora; local gratuito; no Azure, plano F1 + SQL Basic (poucos dólares por mês). |
