# Lean Inception: Crachá

> Decisões de produto do Crachá no formato Lean Inception (Paulo Caroli):
> visão → escopo → personas → jornadas → funcionalidades → sequenciamento → MVP.

---

## 1. Visão do produto

**Para** pequenas e médias empresas que ainda controlam o quadro de pessoal em planilhas e e-mails
**cujo** problema é não saber quem mudou o quê, quando e por quê, nem quem pode ver salários, e aprovar férias por mensagem,
**o Crachá** é um sistema de RH na web
**que** cadastra pessoas e a hierarquia, controla quem vê cada dado, organiza férias com aprovação e guarda o histórico de cada alteração com o autor.
**Diferente de** planilhas compartilhadas, em que qualquer um vê tudo e cada edição apaga o valor anterior,
**o nosso produto** aplica as regras de acesso da LGPD na API, registra toda mudança num log imutável (Azure Table) e roda igual no notebook e no Azure.

## 2. O produto É / NÃO É / FAZ / NÃO FAZ

| É | NÃO É |
|---|---|
| Um cadastro de pessoas com trilha de auditoria | Uma folha de pagamento (cálculo de INSS, IRRF, holerite) |
| Um portal com acesso por perfil (RH, gestor, colaborador) | Um sistema de ponto eletrônico |
| Pronto para o Azure (App Service, SQL, Table, Blob) | Um ERP completo |

| FAZ | NÃO FAZ |
|---|---|
| Cadastra, edita, desliga, reativa e remove funcionários | Envia dados ao eSocial |
| Registra cada alteração com autor e campos alterados | Calcula saldo de férias por período aquisitivo (ainda) |
| Controla quem vê salário, endereço e histórico | Login único com a conta da empresa, SSO (ainda) |
| Monta o organograma e a "equipe" de cada gestor | Envia notificações por e-mail (ainda) |
| Recebe, aprova e recusa pedidos de férias e folgas | Avaliação de desempenho |
| Imprime crachá com foto e QR Code | Controle de benefícios |
| Exporta relatórios em CSV | |

## 3. Objetivos do produto

1. **Rastreabilidade**: toda mudança de um funcionário fica registrada com o autor, inclusive depois de ele ser removido.
2. **Privacidade (LGPD)**: cada pessoa vê só o que o papel dela exige, com a regra aplicada no servidor.
3. **Autonomia com controle**: o colaborador pede férias sozinho; o gestor decide pela equipe; o RH enxerga tudo.
4. **Visão do quadro**: pessoas, folha, rotatividade e ausências sem montar planilha.
5. **Pronto para nuvem**: o mesmo código roda local (SQLite), em contêiner e no Azure, e é publicado por pipeline.

## 4. Personas

### Gabriela, gerente de RH (41 anos)
- **Perfil:** cuida de 20 a 200 pessoas; responde à diretoria e à auditoria.
- **Comportamento:** precisa explicar reajustes e transferências meses depois de acontecerem.
- **Necessidades:** histórico por pessoa com o antes, o depois e quem fez; indicadores de folha e rotatividade; lançar atestados.

### Bruno, gestor de tecnologia (38 anos)
- **Perfil:** lidera 5 pessoas e responde à diretora.
- **Comportamento:** aprovava férias por chat e esquecia de avisar o RH.
- **Necessidades:** uma fila de pedidos da equipe, o calendário de quem estará fora e o painel só do time, sem ver salários de outras áreas.

### Ana, desenvolvedora (29 anos)
- **Perfil:** colaboradora com 3 anos de casa.
- **Comportamento:** quer resolver sozinha: consultar ramal de colegas, trocar a foto, pedir férias.
- **Necessidades:** portal simples, diretório da empresa e a certeza de que seu salário não aparece para os colegas.

### Helena, diretora-geral (52 anos)
- **Perfil:** topo do organograma.
- **Necessidades:** visão da empresa inteira como a "equipe" dela, sem precisar de perfil de RH.

## 5. Jornadas

**Ana pede férias**
1. Entra no Crachá e cai no portal "Início", com o próprio crachá e os pedidos.
2. Clica em **Pedir férias ou folga**, escolhe 14 a 23/12 e envia; o formulário mostra os dias corridos e as regras.
3. O pedido aparece como *Pendente*.

**Bruno decide**
1. Vê o número de pedidos pendentes no menu **Ausências**.
2. Abre a aba **Para decidir**, confere no **Calendário** que ninguém do time estará fora e aprova.
3. Um pedido que choca com a entrega da sprint é recusado com o motivo, que a pessoa vê.

**Gabriela responde à auditoria**
1. Abre a ficha de Rafael com **Ctrl+K**.
2. Na linha do tempo, vê a transferência *Comercial → Tecnologia* (com troca de gestor) e o reajuste *R$ 7.200,00 → R$ 8.100,00*, cada um com o autor.
3. Exporta o histórico em CSV para anexar ao relatório.

**Henrique admite uma pessoa**
1. Cadastra nome, cargo, departamento, **gestor**, e-mail, ramal, salário e admissão.
2. Imprime o **crachá** com foto e QR Code.
3. O administrador cria o acesso de colaborador ligado ao novo cadastro.

## 6. Brainstorming de funcionalidades

| # | Funcionalidade | Persona principal |
|---|---|---|
| F1 | CRUD de funcionários (desafio original) | Gabriela |
| F2 | Log de toda alteração em Azure Table (desafio original) | Gabriela |
| F3 | Departamentos como entidade, com cor | Gabriela |
| F4 | Comparação antes/depois em cada alteração | Gabriela |
| F5 | Desligar e reativar sem apagar o cadastro | Gabriela |
| F6 | Linha do tempo por funcionário e histórico geral com filtros | Gabriela |
| F7 | Diretório em crachás e tabela, com busca, filtros e ordenação | Ana |
| F8 | Painel: quadro, folha, rotatividade, admissões × desligamentos, aniversários | Gabriela |
| F9 | Validações com mensagens por campo | Gabriela |
| F10 | SQLite local ou SQL Server/Azure SQL, com migrations | Dev |
| F11 | Histórico no banco ou na Azure Table (Azurite local) | Dev |
| F12 | Infraestrutura como código (Bicep) | Dev |
| F13 | Atalho na Área de Trabalho | Todas |
| F14 | Login com perfis e gestão de acessos | Todas |
| F15 | Regras de visibilidade (LGPD) aplicadas na API | Ana |
| F16 | Autor em cada registro do histórico | Gabriela |
| F17 | Organograma com gestor imediato e bloqueio de ciclos | Helena, Bruno |
| F18 | Férias, folgas, licenças e atestados com aprovação | Ana, Bruno |
| F19 | Calendário de ausências e "ausentes hoje" | Bruno |
| F20 | Foto no Blob Storage e crachá impresso com QR Code | Ana |
| F21 | Exportação CSV segura para Excel | Gabriela |
| F22 | Health checks, cabeçalhos de segurança, limite de login | Dev |
| F23 | Docker Compose e deploy por GitHub Actions (OIDC) | Dev |
| F24 | Tema escuro e busca rápida (Ctrl+K) | Todas |
| F25 | SSO com Microsoft Entra ID | Todas |
| F26 | Notificações por e-mail (Azure Communication Services) | Bruno, Ana |
| F27 | Saldo de férias por período aquisitivo (CLT) | Gabriela |
| F28 | Observabilidade com Application Insights | Dev |

## 7. Revisão técnica, de negócio e de UX

Escala: esforço E (1 baixo – 3 alto), valor de negócio $ (1–3), valor de UX ♥ (1–3).

| # | E | $ | ♥ | Observação |
|---|---|---|---|---|
| F1–F13 | – | – | – | Entregues nas ondas 1 a 4 (ver sequenciador) |
| F14 | 2 | 3 | 2 | Cookie HttpOnly + SameSite=Strict, `PasswordHasher`, carimbo que derruba sessões |
| F15 | 2 | 3 | 2 | Serviço `Visibilidade`: equipe direta e indireta calculada pelo organograma |
| F16 | 1 | 3 | 1 | Assinatura "Nome (Perfil)" gravada no registro |
| F17 | 2 | 2 | 3 | Autorrelacionamento com `Restrict`; validação de ciclo subindo a cadeia |
| F18 | 3 | 3 | 3 | Regras CLT simplificadas (5 a 30 dias); sobreposição bloqueada |
| F19 | 2 | 2 | 3 | Calendário em tabela, pendentes listrados |
| F20 | 2 | 2 | 3 | Tipo detectado pelos bytes; QRCoder gera SVG |
| F21 | 1 | 2 | 2 | BOM + `;` + neutralização de fórmula |
| F22 | 1 | 3 | 1 | Degradado (não fora do ar) se só as fotos falharem |
| F23 | 2 | 3 | 1 | CI sobe o Compose e faz teste de fumaça |
| F24 | 1 | 1 | 3 | Tema aplicado antes da pintura (sem piscar) |
| F25 | 2 | 3 | 3 | Trocar o cookie local por OpenID Connect |
| F26 | 2 | 2 | 3 | |
| F27 | 3 | 3 | 2 | |
| F28 | 1 | 2 | 1 | |

## 8. Sequenciador

| Onda | Funcionalidades | Resultado |
|---|---|---|
| **1: Desafio** ✅ | F1, F2, F10 | CRUD com log em Azure Table |
| **2: Auditoria** ✅ | F3, F4, F5, F6, F9 | Saber o que mudou, com cadastro confiável |
| **3: Visão do quadro** ✅ | F7, F8, F13 | Diretório, painel e acesso com um clique |
| **4: Nuvem** ✅ | F11, F12 | Azurite, Bicep e CI com SQL Server + Azure Table |
| **5: Acesso e LGPD** ✅ | F14, F15, F16 | Cada um vê só o que deve, e o histórico diz quem fez |
| **6: Rotina de RH** ✅ | F17, F18, F19, F20, F21 | Organograma, férias com aprovação, crachá e relatórios |
| **7: Operação** ✅ | F22, F23, F24 | Pronto para rodar como produto |
| 8: Integração corporativa | F25, F26, F28 | SSO, notificações e observabilidade |
| 9: Departamento pessoal | F27 | Saldo de férias conforme a CLT |

✅ = implementado nesta versão.

## 9. Canvas do MVP (versão empresa)

| Bloco | Conteúdo |
|---|---|
| **Proposta** | Cada pessoa resolve o que é dela, cada gestor decide pela equipe, e o RH tem a trilha completa. |
| **Personas segmentadas** | Gabriela (RH), Bruno (gestor), Ana (colaboradora) |
| **Jornadas atendidas** | "Ana pede férias", "Bruno decide", "Gabriela responde à auditoria" |
| **Funcionalidades** | Login por perfil, LGPD, organograma, férias com aprovação, histórico com autor, crachá, CSV |
| **Resultado esperado** | Pedidos de férias decididos em menos de 1 dia, sem e-mail; zero salário exposto fora do perfil certo. |
| **Métricas para validar** | 68 testes de integração cobrindo cada regra de acesso e de negócio, rodando com SQLite e com SQL Server + Azurite; teste de fumaça do Docker no CI. |
| **Custo e cronograma** | Uma pessoa desenvolvedora; local gratuito; no Azure, plano F1 + SQL Basic (poucos dólares por mês). |
