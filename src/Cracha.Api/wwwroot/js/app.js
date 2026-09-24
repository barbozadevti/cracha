"use strict";

const app = document.getElementById("app");
const modal = document.getElementById("modal");
const form = document.getElementById("formFuncionario");

const ACOES = {
  Inclusao: "Admissão",
  Atualizacao: "Atualização",
  Desligamento: "Desligamento",
  Reativacao: "Reativação",
  Remocao: "Remoção",
};
const CORES_ACAO = { Inclusao: "#059669", Atualizacao: "#2563eb", Desligamento: "#d97706", Reativacao: "#0d9488", Remocao: "#dc2626" };
const TIPOS = { Ferias: "Férias", Folga: "Folga", Licenca: "Licença", Atestado: "Atestado" };
const CORES_TIPO = { Ferias: "#0d9488", Folga: "#2563eb", Licenca: "#7c3aed", Atestado: "#db2777" };
const PERFIS = { Administrador: "Administrador", RH: "RH", Gestor: "Gestor", Colaborador: "Colaborador" };

let sessao = null;
let departamentos = [];

// ---------- Utilidades ----------

const esc = (v) => String(v ?? "").replace(/[&<>"']/g, (c) =>
  ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

async function api(caminho, opcoes = {}) {
  const ehArquivo = opcoes.body instanceof FormData;
  const resposta = await fetch(caminho, {
    method: opcoes.method || "GET",
    headers: opcoes.body !== undefined && !ehArquivo ? { "Content-Type": "application/json" } : {},
    body: opcoes.body === undefined ? undefined : ehArquivo ? opcoes.body : JSON.stringify(opcoes.body),
    credentials: "same-origin",
  });
  if (resposta.status === 401 && !opcoes.login) {
    // Sessão expirou ou o acesso mudou: volta para o login.
    if (sessao) avisar("Sua sessão terminou. Entre novamente.", "erro-aviso");
    sessao = null;
    mostrarLogin();
    throw Object.assign(new Error("Sessão encerrada."), { status: 401, silencioso: true });
  }
  if (resposta.status === 204) return null;
  const dados = await resposta.json().catch(() => null);
  if (!resposta.ok) {
    const erro = new Error(dados?.title || (resposta.status === 403 ? "Você não tem permissão para isso." : `Erro ${resposta.status}`));
    erro.status = resposta.status;
    erro.campos = dados?.errors || {};
    throw erro;
  }
  return dados;
}

let temporizador;
function avisar(mensagem, tipo = "") {
  const aviso = document.getElementById("aviso");
  aviso.textContent = mensagem;
  aviso.className = `aviso visivel ${tipo}`;
  clearTimeout(temporizador);
  temporizador = setTimeout(() => aviso.classList.remove("visivel"), 3200);
}
const falhar = (erro) => { if (!erro.silencioso) avisar(Object.values(erro.campos || {})[0]?.[0] || erro.message, "erro-aviso"); };

const guardar = (chave, valor) => { try { localStorage.setItem(chave, valor); } catch { /* sem armazenamento */ } };
const lembrar = (chave, padrao) => { try { return localStorage.getItem(chave) ?? padrao; } catch { return padrao; } };

const moeda = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });
const dinheiro = (v) => moeda.format(v);
const dinheiroCurto = (v) => v >= 1000 ? `R$ ${(v / 1000).toLocaleString("pt-BR", { maximumFractionDigits: 1 })} mil` : dinheiro(v);
const restrito = `<span class="oculto-lgpd" title="Visível só para o RH, o gestor da pessoa e a própria pessoa">🔒 Restrito</span>`;

// Datas sem hora ("2026-09-03") viram data local, sem deslocar o dia pelo fuso.
const dataLocal = (texto) => { const [a, m, d] = texto.split("-").map(Number); return new Date(a, m - 1, d); };
const dataBr = (texto) => texto ? dataLocal(texto).toLocaleDateString("pt-BR") : "—";
const dataCurta = (texto) => dataLocal(texto).toLocaleDateString("pt-BR", { day: "2-digit", month: "short" }).replace(".", "");
const iso = (d) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
const hojeIso = () => iso(new Date());
const diasEntre = (inicio, fim) => Math.round((dataLocal(fim) - dataLocal(inicio)) / 86400000) + 1;

function momento(texto) {
  const d = new Date(texto);
  const dias = Math.round((new Date().setHours(0, 0, 0, 0) - new Date(d).setHours(0, 0, 0, 0)) / 86400000);
  const hora = d.toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });
  if (dias === 0) return `Hoje, ${hora}`;
  if (dias === 1) return `Ontem, ${hora}`;
  return d.toLocaleDateString("pt-BR", { day: "2-digit", month: "short", year: "numeric" }).replace(".", "");
}

function tempoDeCasa(meses) {
  const anos = Math.floor(meses / 12), resto = meses % 12;
  if (anos === 0) return resto <= 1 ? (resto === 0 ? "menos de 1 mês" : "1 mês") : `${resto} meses`;
  const a = anos === 1 ? "1 ano" : `${anos} anos`;
  return resto === 0 ? a : `${a} e ${resto} ${resto === 1 ? "mês" : "meses"}`;
}

function iniciais(nome) {
  const partes = nome.trim().split(/\s+/);
  return ((partes[0]?.[0] || "") + (partes.length > 1 ? partes[partes.length - 1][0] : "")).toUpperCase();
}

const avatar = (nome, cor, classe = "", foto = null) =>
  `<span class="avatar ${classe}" style="--cor:${esc(cor)}" aria-hidden="true">${foto ? `<img src="${esc(foto)}" alt="" loading="lazy">` : esc(iniciais(nome))}</span>`;

const seloDepartamento = (nome, cor) => `<span class="selo" style="--cor:${esc(cor)}">${esc(nome)}</span>`;
const seloAcao = (tipo) => `<span class="selo simples acao ${tipo}">${ACOES[tipo] || tipo}</span>`;
const seloTipo = (tipo) => `<span class="selo tipo-ausencia ${tipo}">${TIPOS[tipo] || tipo}</span>`;
const seloStatus = (status) => `<span class="selo simples status ${status}">${esc(status)}</span>`;
const seloAusente = (a) => a ? `<span class="selo ausente" title="Volta depois de ${dataBr(a.fim)}">${TIPOS[a.tipo]} até ${dataCurta(a.fim)}</span>` : "";
const corDoDepartamento = (nome) => departamentos.find((d) => d.nome === nome)?.cor || "#64748b";

const pode = (permissao) => !!sessao?.permissoes?.[permissao];

async function carregarDepartamentos() {
  departamentos = await api("/api/departamentos");
}

// ---------- Tema ----------

document.getElementById("alternarTema").addEventListener("click", () => {
  const raiz = document.documentElement;
  const escuroAgora = raiz.dataset.tema ? raiz.dataset.tema === "escuro" : matchMedia("(prefers-color-scheme: dark)").matches;
  raiz.dataset.tema = escuroAgora ? "claro" : "escuro";
  guardar("cracha.tema", raiz.dataset.tema);
});

// ---------- Sessão ----------

const telaLogin = document.getElementById("login");
const formLogin = document.getElementById("formLogin");

function mostrarLogin() {
  document.body.className = "sem-sessao";
  telaLogin.hidden = false;
  document.getElementById("lateral").hidden = true;
  app.hidden = true;
  document.title = "Entrar · Crachá";
  document.querySelectorAll("dialog[open]").forEach((d) => d.close());
  api("/api/conta/demonstracao", { login: true }).then((contas) => {
    const caixa = document.getElementById("contasDemo");
    caixa.hidden = !contas.length;
    document.getElementById("listaContasDemo").innerHTML = contas.map((c) => `
      <button type="button" class="conta-demo" data-email="${esc(c.email)}">
        ${avatar(c.nome, { Administrador: "#475569", RH: "#db2777", Gestor: "#0d9488", Colaborador: "#2563eb" }[c.perfil])}
        <span><strong>${esc(c.nome)} · ${esc(PERFIS[c.perfil])}</strong><small>${esc(c.descricao)}</small></span>
      </button>`).join("");
    document.querySelectorAll(".conta-demo").forEach((b) => b.addEventListener("click", () => {
      formLogin.email.value = b.dataset.email;
      formLogin.senha.value = document.getElementById("senhaDemo").textContent;
      formLogin.requestSubmit();
    }));
  }).catch(() => {});
  setTimeout(() => formLogin.email.focus(), 0);
}

formLogin.addEventListener("submit", async (e) => {
  e.preventDefault();
  const erro = document.getElementById("erroLogin");
  erro.textContent = "";
  try {
    const s = await api("/api/conta/entrar", { method: "POST", login: true, body: { email: formLogin.email.value.trim(), senha: formLogin.senha.value } });
    formLogin.reset();
    iniciarSessao(s);
  } catch (falha) {
    erro.textContent = falha.status === 429 ? "Muitas tentativas. Aguarde um minuto e tente de novo." : falha.message;
  }
});

document.getElementById("sair").addEventListener("click", async () => {
  await api("/api/conta/sair", { method: "POST", login: true });
  sessao = null;
  location.hash = "#/";
  mostrarLogin();
});

function iniciarSessao(s) {
  sessao = s;
  // Filtros e abas da pessoa anterior não valem para quem entrou agora.
  Object.assign(estadoAusencias, { aba: null, mes: null });
  Object.assign(filtros, { busca: "", departamentoId: "", situacao: "Ativo", ordem: "Nome", desc: false });
  paletaCache = null;
  document.body.className = "";
  telaLogin.hidden = true;
  document.getElementById("lateral").hidden = false;
  app.hidden = false;
  document.getElementById("novoFuncionario").hidden = !pode("gerirCadastro");
  document.getElementById("eu").innerHTML = `
    ${avatar(s.nome, "#0f766e", "pequeno", s.fotoUrl)}
    <div><strong>${esc(s.nome)}</strong><small>${esc(PERFIS[s.perfil])}</small></div>`;
  montarMenu();
  rotear();
}

function montarMenu() {
  const itens = [
    { rota: "painel", href: "#/", icone: "📊", nome: pode("acompanhar") ? "Painel" : "Início" },
    { rota: "funcionarios", href: "#/funcionarios", icone: "🪪", nome: "Funcionários" },
    { rota: "organograma", href: "#/organograma", icone: "🌳", nome: "Organograma" },
    { rota: "ausencias", href: "#/ausencias", icone: "🏖️", nome: "Ausências", contador: true },
    { rota: "departamentos", href: "#/departamentos", icone: "🏢", nome: "Departamentos" },
    pode("acompanhar") && { rota: "historico", href: "#/historico", icone: "🕘", nome: "Histórico" },
    pode("administrar") && { rota: "acessos", href: "#/acessos", icone: "🔐", nome: "Acessos" },
  ].filter(Boolean);
  document.getElementById("menu").innerHTML = itens.map((i) =>
    `<a href="${i.href}" data-rota="${i.rota}"><span aria-hidden="true">${i.icone}</span> ${i.nome}${i.contador ? `<b class="contador" id="contadorPendentes" hidden></b>` : ""}</a>`).join("");
  atualizarPendentes();
}

async function atualizarPendentes() {
  if (!pode("aprovarAusencias")) return;
  const pendentes = await api("/api/ausencias?paraDecidir=true").catch(() => []);
  const contador = document.getElementById("contadorPendentes");
  if (!contador) return;
  contador.hidden = !pendentes.length;
  contador.textContent = pendentes.length;
  contador.title = `${pendentes.length} pedido(s) aguardando sua decisão`;
}

// ---------- Rotas ----------

async function rotear() {
  if (!sessao) return;
  const [secao = "", id, extra] = location.hash.replace(/^#\/?/, "").split("/");
  const telas = {
    "": pode("acompanhar") ? telaPainel : telaInicio,
    funcionarios: id ? (extra === "cracha" ? () => telaCracha(Number(id)) : () => telaFicha(Number(id))) : telaFuncionarios,
    organograma: telaOrganograma,
    ausencias: telaAusencias,
    departamentos: telaDepartamentos,
    historico: telaHistorico,
    acessos: telaAcessos,
  };
  const tela = telas[secao] || telas[""];
  document.querySelectorAll(".menu a").forEach((a) =>
    a.classList.toggle("ativo", a.dataset.rota === (secao || "painel")));
  try {
    await tela();
  } catch (erro) {
    if (!erro.silencioso)
      app.innerHTML = `<div class="vazio">${erro.status === 403 ? "🔒 " : ""}${esc(erro.message)}</div>`;
  }
  app.focus({ preventScroll: true });
}

window.addEventListener("hashchange", () => { window.scrollTo(0, 0); rotear(); });

// ---------- Painel (RH e gestores) ----------

async function telaPainel() {
  const [p] = await Promise.all([api("/api/painel"), carregarDepartamentos()]);
  document.title = "Painel · Crachá";
  const maxAtivos = Math.max(1, ...p.porDepartamento.map((d) => d.ativos));
  const maxMes = Math.max(1, ...p.admissoesPorMes.map((m) => Math.max(m.admissoes, m.desligamentos)));
  const hoje = new Date();
  const mes = hoje.toLocaleDateString("pt-BR", { month: "long" });
  const daEmpresa = p.escopo === "Empresa";

  app.innerHTML = `
    <div class="topo">
      <div>
        <h1>${daEmpresa ? "Painel de pessoas" : "Painel da equipe"}</h1>
        <p>${daEmpresa ? "" : `${esc(p.escopo)} (direta e indireta) · `}${hoje.toLocaleDateString("pt-BR", { weekday: "long", day: "numeric", month: "long", year: "numeric" })}</p>
      </div>
      <a class="botao" href="/api/exportar/funcionarios.csv" download>⬇ Exportar quadro (CSV)</a>
    </div>

    <section class="indicadores">
      <div class="cartao indicador"><span>${daEmpresa ? "Funcionários ativos" : "Pessoas na equipe"}</span><strong>${p.ativos}</strong><small>${p.desligados} desligado(s) no cadastro</small></div>
      <div class="cartao indicador"><span>Folha mensal</span><strong>${dinheiroCurto(p.folhaMensal)}</strong><small>${dinheiro(p.folhaMensal)}</small></div>
      <div class="cartao indicador"><span>Salário médio</span><strong>${dinheiroCurto(p.salarioMedio)}</strong><small>entre os ativos</small></div>
      <div class="cartao indicador"><span>Tempo médio de casa</span><strong>${(p.mesesMedioDeCasa / 12).toLocaleString("pt-BR", { maximumFractionDigits: 1 })} anos</strong><small>${p.admissoesNoAno} admissões em ${hoje.getFullYear()}</small></div>
      <div class="cartao indicador"><span>Rotatividade (12 meses)</span><strong>${p.rotatividade12Meses.toLocaleString("pt-BR")}%</strong><small>desligamentos ÷ quadro médio</small></div>
    </section>

    <div class="grade-painel">
      <section class="cartao">
        <h2>Aguardando você <a href="#/ausencias">Ver ausências</a></h2>
        ${p.pedidosPendentes
          ? `<p class="carregando"><strong style="font-size:1.6rem;color:var(--alerta)">${p.pedidosPendentes}</strong> pedido(s) de férias ou folga para aprovar.</p>
             <a class="botao primario" href="#/ausencias">Decidir agora</a>`
          : `<p class="carregando">Nenhum pedido pendente. ✅</p>`}
      </section>

      <section class="cartao">
        <h2>Ausentes hoje <small>${p.ausentesHoje.length} pessoa(s)</small></h2>
        ${p.ausentesHoje.length ? `<ul class="lista-simples">
          ${p.ausentesHoje.map((a) => `
            <li>
              ${avatar(a.funcionario, a.cor, "pequeno", a.fotoUrl)}
              <div class="info"><a href="#/funcionarios/${a.funcionarioId}">${esc(a.funcionario)}</a><small>${esc(a.departamento)} · volta em ${dataBr(iso(new Date(dataLocal(a.fim).getTime() + 86400000)))}</small></div>
              ${seloTipo(a.tipo)}
            </li>`).join("")}
        </ul>` : `<p class="carregando">Todo mundo por aqui hoje.</p>`}
      </section>

      <section class="cartao">
        <h2>Pessoas por departamento ${daEmpresa ? `<a href="#/departamentos">Ver todos</a>` : ""}</h2>
        <div class="barras">
          ${p.porDepartamento.map((d) => `
            <div class="barra-linha" style="--cor:${esc(d.cor)}">
              <span class="rotulo" title="${esc(d.nome)}">${esc(d.nome)}</span>
              <span class="barra" role="img" aria-label="${d.ativos} pessoas"><i style="width:${(d.ativos / maxAtivos) * 100}%"></i></span>
              <span class="valor">${d.ativos} · ${dinheiroCurto(d.folha)}</span>
            </div>`).join("")}
        </div>
      </section>

      <section class="cartao">
        <h2>Admissões e desligamentos <small>últimos 12 meses</small></h2>
        <div class="colunas">
          ${p.admissoesPorMes.map((m) => `
            <div class="coluna" title="${esc(m.mes)}: ${m.admissoes} admissão(ões), ${m.desligamentos} desligamento(s)">
              <div class="pilha">
                <i class="${m.admissoes ? "adm" : "zero"}" style="height:${(m.admissoes / maxMes) * 100}%"></i>
                <i class="${m.desligamentos ? "desl" : "zero"}" style="height:${(m.desligamentos / maxMes) * 100}%"></i>
              </div>
              <span>${esc(m.mes.split("/")[0])}</span>
            </div>`).join("")}
        </div>
        <div class="legenda"><span>Admissões</span><span class="desl">Desligamentos</span></div>
      </section>

      <section class="cartao">
        <h2>Aniversários de empresa <small>em ${mes}</small></h2>
        ${p.aniversariosDoMes.length ? `<ul class="lista-simples">
          ${p.aniversariosDoMes.map((a) => `
            <li>
              ${avatar(a.nome, a.cor, "pequeno")}
              <div class="info"><a href="#/funcionarios/${a.id}">${esc(a.nome)}</a><small>${esc(a.departamento)} · ${dataLocal(a.data).toLocaleDateString("pt-BR", { day: "2-digit", month: "long" })}</small></div>
              <span class="selo simples">🎉 ${a.anos} ${a.anos === 1 ? "ano" : "anos"}</span>
            </li>`).join("")}
        </ul>` : `<p class="carregando">Ninguém completa ano de casa este mês.</p>`}
      </section>

      <section class="cartao">
        <h2>Últimas alterações <a href="#/historico">Histórico completo</a></h2>
        ${linhaDoTempo(p.ultimasAlteracoes, { comNome: true, compacta: true })}
      </section>
    </div>`;
}

// ---------- Início (colaborador) ----------

async function telaInicio() {
  document.title = "Início · Crachá";
  if (!sessao.funcionarioId) {
    app.innerHTML = `<div class="topo"><div><h1>Olá, ${esc(sessao.nome)}</h1><p>Seu acesso não está ligado a um funcionário.</p></div></div>
      <div class="vazio">Use o menu para consultar o diretório e o organograma.</div>`;
    return;
  }
  const [eu, ausencias] = await Promise.all([
    api(`/api/funcionarios/${sessao.funcionarioId}`),
    api(`/api/ausencias?funcionarioId=${sessao.funcionarioId}`),
    carregarDepartamentos(),
  ]);
  const primeiroNome = eu.nome.split(" ")[0];
  const proximas = ausencias.filter((a) => a.status === "Aprovada" && a.fim >= hojeIso());

  app.innerHTML = `
    <div class="topo">
      <div><h1>Olá, ${esc(primeiroNome)} 👋</h1><p>${esc(eu.cargo)} · ${esc(eu.departamento)} · ${tempoDeCasa(eu.mesesDeCasa)} de casa</p></div>
      <button class="botao primario" type="button" data-pedir>+ Pedir férias ou folga</button>
    </div>
    <div class="grade-painel">
      <section class="cartao">
        <h2>Meu crachá <a href="#/funcionarios/${eu.id}">Minha ficha</a></h2>
        <div class="pessoa" style="gap:1rem">
          ${avatar(eu.nome, eu.corDepartamento, "grande", eu.fotoUrl)}
          <span><strong>${esc(eu.nome)}</strong><small>${esc(eu.emailProfissional)} · ramal ${esc(eu.ramal)}</small><br>
          ${eu.gestor ? `<small>Gestor(a): <a href="#/funcionarios/${eu.gestorId}">${esc(eu.gestor)}</a></small>` : ""}</span>
        </div>
        <div class="acoes"><a class="botao" href="#/funcionarios/${eu.id}/cracha">🪪 Imprimir crachá</a></div>
      </section>
      <section class="cartao">
        <h2>Próximas ausências</h2>
        ${proximas.length ? `<ul class="lista-simples">${proximas.map((a) => `
          <li>${seloTipo(a.tipo)}<div class="info"><strong>${dataBr(a.inicio)} a ${dataBr(a.fim)}</strong><small>${a.dias} dia(s)</small></div></li>`).join("")}</ul>`
          : `<p class="carregando">Nada marcado. Que tal planejar as férias?</p>`}
      </section>
      <section class="cartao largo">
        <h2>Meus pedidos <a href="#/ausencias">Ver todos</a></h2>
        ${listaPedidos(ausencias.slice(0, 5), { semNome: true })}
      </section>
    </div>`;
  app.querySelector("[data-pedir]").addEventListener("click", () => abrirAusencia());
  ligarPedidos(telaInicio);
}

// ---------- Funcionários ----------

const filtros = {
  busca: "",
  departamentoId: "",
  situacao: "Ativo",
  ordem: "Nome",
  desc: false,
  visao: lembrar("cracha.visao", "crachas"),
};

async function telaFuncionarios() {
  await carregarDepartamentos();
  document.title = "Funcionários · Crachá";
  if (filtros.ordem === "Salario" && !pode("verSalarios")) filtros.ordem = "Nome";
  app.innerHTML = `
    <div class="topo">
      <div><h1>Funcionários</h1><p>Clique num crachá para ver a ficha${pode("acompanhar") ? " e o histórico" : ""}.</p></div>
      <div class="acoes" style="margin:0">
        <a class="botao" href="/api/exportar/funcionarios.csv" download id="exportarFuncionarios">⬇ CSV</a>
        ${pode("gerirCadastro") ? `<button class="botao primario" type="button" data-novo>+ Novo funcionário</button>` : ""}
      </div>
    </div>
    <div class="filtros">
      <input type="search" id="busca" placeholder="Buscar por nome, cargo, e-mail ou ramal" value="${esc(filtros.busca)}" aria-label="Buscar">
      <select id="filtroDepartamento" aria-label="Departamento">
        <option value="">Todos os departamentos</option>
        ${departamentos.map((d) => `<option value="${d.id}" ${String(d.id) === filtros.departamentoId ? "selected" : ""}>${esc(d.nome)}</option>`).join("")}
      </select>
      <select id="filtroSituacao" aria-label="Situação">
        <option value="Ativo">Ativos</option>
        <option value="Desligado">Desligados</option>
        <option value="">Todos</option>
      </select>
      <select id="filtroOrdem" aria-label="Ordenar por">
        <option value="Nome">Nome</option>
        <option value="Admissao">Admissão</option>
        ${pode("verSalarios") ? `<option value="Salario">Salário</option>` : ""}
        <option value="Departamento">Departamento</option>
      </select>
      <div class="alternar" role="group" aria-label="Visualização">
        <button type="button" data-visao="crachas" aria-label="Crachás" title="Crachás">🪪</button>
        <button type="button" data-visao="tabela" aria-label="Tabela" title="Tabela">☰</button>
      </div>
    </div>
    <p class="contagem" id="contagem"></p>
    <div id="lista"><p class="carregando">Carregando…</p></div>`;

  document.getElementById("filtroSituacao").value = filtros.situacao;
  document.getElementById("filtroOrdem").value = filtros.ordem;
  app.querySelector("[data-novo]")?.addEventListener("click", () => abrirFormulario());

  let espera;
  document.getElementById("busca").addEventListener("input", (e) => {
    clearTimeout(espera);
    espera = setTimeout(() => { filtros.busca = e.target.value; listarFuncionarios(); }, 250);
  });
  document.getElementById("filtroDepartamento").addEventListener("change", (e) => { filtros.departamentoId = e.target.value; listarFuncionarios(); });
  document.getElementById("filtroSituacao").addEventListener("change", (e) => { filtros.situacao = e.target.value; listarFuncionarios(); });
  document.getElementById("filtroOrdem").addEventListener("change", (e) => {
    filtros.ordem = e.target.value;
    filtros.desc = e.target.value === "Salario" || e.target.value === "Admissao";
    listarFuncionarios();
  });
  app.querySelectorAll("[data-visao]").forEach((b) => b.addEventListener("click", () => {
    filtros.visao = b.dataset.visao;
    guardar("cracha.visao", filtros.visao);
    listarFuncionarios();
  }));

  await listarFuncionarios();
}

async function listarFuncionarios() {
  const q = new URLSearchParams({ ordem: filtros.ordem, desc: filtros.desc });
  if (filtros.busca.trim()) q.set("busca", filtros.busca.trim());
  if (filtros.departamentoId) q.set("departamentoId", filtros.departamentoId);
  if (filtros.situacao) q.set("situacao", filtros.situacao);
  const lista = await api(`/api/funcionarios?${q}`);

  const exportar = new URLSearchParams();
  if (filtros.situacao) exportar.set("situacao", filtros.situacao);
  if (filtros.departamentoId) exportar.set("departamentoId", filtros.departamentoId);
  document.getElementById("exportarFuncionarios").href = `/api/exportar/funcionarios.csv?${exportar}`;

  app.querySelectorAll("[data-visao]").forEach((b) => b.classList.toggle("ativo", b.dataset.visao === filtros.visao));
  const folha = pode("verSalarios") ? lista.filter((f) => f.situacao === "Ativo").reduce((s, f) => s + (f.salario || 0), 0) : 0;
  document.getElementById("contagem").textContent =
    `${lista.length} ${lista.length === 1 ? "pessoa" : "pessoas"}${folha ? ` · folha de ${dinheiro(folha)}` : ""}`;

  const alvo = document.getElementById("lista");
  if (!lista.length) {
    alvo.innerHTML = `<div class="vazio">Ninguém encontrado com esses filtros.</div>`;
    return;
  }
  alvo.innerHTML = filtros.visao === "tabela" ? tabelaFuncionarios(lista) : crachas(lista);

  alvo.querySelectorAll("th button").forEach((b) => b.addEventListener("click", () => {
    filtros.desc = filtros.ordem === b.dataset.ordem ? !filtros.desc : b.dataset.ordem !== "Nome" && b.dataset.ordem !== "Departamento";
    filtros.ordem = b.dataset.ordem;
    document.getElementById("filtroOrdem").value = filtros.ordem;
    listarFuncionarios();
  }));
}

const crachas = (lista) => `
  <div class="crachas">
    ${lista.map((f) => `
      <a class="cracha ${f.situacao === "Desligado" ? "desligado" : ""}" href="#/funcionarios/${f.id}" style="--cor:${esc(f.corDepartamento)}">
        ${avatar(f.nome, f.corDepartamento, "", f.fotoUrl)}
        <strong>${esc(f.nome)}</strong>
        <span class="cargo">${esc(f.cargo)}</span>
        ${f.situacao === "Desligado" ? `<span class="selo desligado">Desligado em ${dataBr(f.dataDesligamento)}</span>` : seloDepartamento(f.departamento, f.corDepartamento)}
        ${seloAusente(f.ausenteAgora)}
        <span class="rodape"><span>Ramal ${esc(f.ramal)}</span><span>${tempoDeCasa(f.mesesDeCasa)}</span></span>
      </a>`).join("")}
  </div>`;

function tabelaFuncionarios(lista) {
  const cab = (ordem, rotulo, classe = "") => {
    const seta = filtros.ordem === ordem ? (filtros.desc ? " ↓" : " ↑") : "";
    return `<th class="${classe}"><button type="button" data-ordem="${ordem}" class="${filtros.ordem === ordem ? "ativo" : ""}">${rotulo}${seta}</button></th>`;
  };
  return `
    <div class="cartao tabela-rolagem">
      <table>
        <thead><tr>
          ${cab("Nome", "Nome")}
          ${cab("Departamento", "Departamento", "col-opcional")}
          <th class="col-opcional">Gestor</th>
          <th class="col-opcional">Ramal</th>
          ${cab("Admissao", "Admissão", "col-opcional")}
          ${pode("verSalarios") ? cab("Salario", "Salário", "direita") : `<th class="direita">Salário</th>`}
        </tr></thead>
        <tbody>
          ${lista.map((f) => `
            <tr class="${f.situacao === "Desligado" ? "desligado" : ""}">
              <td><a class="pessoa" href="#/funcionarios/${f.id}">${avatar(f.nome, f.corDepartamento, "pequeno", f.fotoUrl)}<span><strong>${esc(f.nome)}</strong><small>${esc(f.cargo)}${f.situacao === "Desligado" ? " · desligado" : ""}</small></span></a></td>
              <td class="col-opcional">${seloDepartamento(f.departamento, f.corDepartamento)}</td>
              <td class="col-opcional">${f.gestor ? `<a href="#/funcionarios/${f.gestorId}">${esc(f.gestor)}</a>` : "—"}</td>
              <td class="col-opcional num">${esc(f.ramal)}</td>
              <td class="col-opcional num">${dataBr(f.dataAdmissao)}</td>
              <td class="direita num">${f.salario == null ? "🔒" : dinheiro(f.salario)}</td>
            </tr>`).join("")}
        </tbody>
      </table>
    </div>`;
}

// ---------- Ficha ----------

async function telaFicha(id) {
  const [funcionario, historico, equipe, ausencias] = await Promise.all([
    api(`/api/funcionarios/${id}`).catch((e) => { if (e.status === 404) return null; throw e; }),
    api(`/api/funcionarios/${id}/historico`).catch((e) => { if (e.status === 403) return null; throw e; }),
    api(`/api/funcionarios?gestorId=${id}&situacao=Ativo`),
    api(`/api/ausencias?funcionarioId=${id}`),
    carregarDepartamentos(),
  ]);

  // Removido do cadastro: a ficha é reconstruída a partir da última foto do histórico.
  if (!funcionario && !historico?.length) {
    app.innerHTML = `<a class="voltar" href="#/funcionarios">← Funcionários</a><div class="vazio">Funcionário não encontrado.</div>`;
    return;
  }
  const f = funcionario || { ...historico[0].foto, detalhes: true, corDepartamento: corDoDepartamento(historico[0].departamento), removido: true };
  document.title = `${f.nome} · Crachá`;
  const desligado = f.situacao === "Desligado";
  const souEu = sessao.funcionarioId === id;
  const gerir = pode("gerirCadastro") && !f.removido;
  const trocaFoto = !f.removido && (gerir || souEu);

  app.innerHTML = `
    <a class="voltar" href="#/funcionarios">← Funcionários</a>
    <div class="ficha">
      <section class="cartao ${desligado ? "desligado" : ""}">
        <div class="ficha-cabeca">
          ${avatar(f.nome, f.corDepartamento, "grande", f.fotoUrl)}
          ${trocaFoto ? `<div class="foto-acoes"><button class="link" type="button" data-foto>📷 ${f.fotoUrl ? "Trocar foto" : "Enviar foto"}</button>${f.fotoUrl ? `<button class="link" type="button" data-sem-foto>Remover</button>` : ""}</div>` : ""}
          <h1>${esc(f.nome)}</h1>
          <p>${esc(f.cargo)}</p>
          ${f.removido ? `<span class="selo simples acao Remocao">Removido do cadastro</span>`
            : desligado ? `<span class="selo desligado">Desligado em ${dataBr(f.dataDesligamento)}</span>`
            : seloDepartamento(f.departamento, f.corDepartamento)}
          ${seloAusente(f.ausenteAgora)}
        </div>
        <dl class="dados">
          <div><dt>Departamento</dt><dd>${esc(f.departamento)}</dd></div>
          <div><dt>Gestor(a)</dt><dd>${f.gestorId ? `<a href="#/funcionarios/${f.gestorId}">${esc(f.gestor)}</a>` : esc(f.gestor) || "—"}</dd></div>
          <div><dt>E-mail</dt><dd>${f.removido ? esc(f.emailProfissional) : `<a href="mailto:${esc(f.emailProfissional)}">${esc(f.emailProfissional)}</a>`}</dd></div>
          <div><dt>Ramal</dt><dd class="num">${esc(f.ramal)}</dd></div>
          <div><dt>Endereço</dt><dd>${f.detalhes ? esc(f.endereco) || "—" : restrito}</dd></div>
          <div><dt>Salário</dt><dd class="num">${f.detalhes ? dinheiro(f.salario) : restrito}</dd></div>
          <div><dt>Admissão</dt><dd>${dataBr(f.dataAdmissao)}</dd></div>
          ${f.mesesDeCasa !== undefined ? `<div><dt>Tempo de casa</dt><dd>${tempoDeCasa(f.mesesDeCasa)}</dd></div>` : ""}
          <div><dt>Matrícula</dt><dd class="num">#${String(f.id).padStart(5, "0")}</dd></div>
        </dl>
        ${f.removido ? "" : `
        <div class="acoes">
          ${gerir ? `<button class="botao primario" type="button" data-acao="editar">Editar</button>
            ${desligado ? `<button class="botao" type="button" data-acao="reativar">Reativar</button>`
              : `<button class="botao" type="button" data-acao="desligar">Desligar</button>`}
            <button class="botao" type="button" data-acao="remover">Remover</button>` : ""}
          ${!desligado && (gerir || souEu) ? `<a class="botao" href="#/funcionarios/${id}/cracha">🪪 Crachá</a>` : ""}
          ${!desligado && gerir ? `<button class="botao" type="button" data-acao="ausencia">🏖️ Lançar ausência</button>` : ""}
        </div>`}
        ${equipe.length ? `
        <div class="bloco">
          <h3>Equipe direta (${equipe.length})</h3>
          <div class="equipe">${equipe.map((p) => `<a href="#/funcionarios/${p.id}">${avatar(p.nome, p.corDepartamento, "pequeno", p.fotoUrl)}${esc(p.nome)}</a>`).join("")}</div>
        </div>` : ""}
      </section>

      <div style="display:grid;gap:1rem;align-content:start">
        ${ausencias.length ? `
        <section class="cartao">
          <h2>Ausências <small>${ausencias.length} registro(s)</small></h2>
          ${listaPedidos(ausencias.slice(0, 6), { semNome: true })}
        </section>` : ""}
        <section class="cartao">
          <h2>Linha do tempo <small>${historico ? `${historico.length} registro(s)` : ""}</small></h2>
          ${historico ? linhaDoTempo(historico) : `<p class="carregando">${restrito} O histórico é visível para o RH, para o gestor e para a própria pessoa.</p>`}
        </section>
      </div>
    </div>`;

  ligarPedidos(() => telaFicha(id));
  if (trocaFoto) {
    app.querySelector("[data-foto]").addEventListener("click", () => escolherFoto(id, () => telaFicha(id)));
    app.querySelector("[data-sem-foto]")?.addEventListener("click", async () => {
      await api(`/api/funcionarios/${id}/foto`, { method: "DELETE" }).catch(falhar);
      avisar("Foto removida.");
      telaFicha(id);
    });
  }
  if (!gerir) return;
  app.querySelector('[data-acao="editar"]').addEventListener("click", () => abrirFormulario(funcionario));
  app.querySelector('[data-acao="ausencia"]')?.addEventListener("click", () => abrirAusencia({ funcionarioId: id }));
  app.querySelector('[data-acao="remover"]').addEventListener("click", async () => {
    const r = await confirmar({
      titulo: "Remover do cadastro?",
      texto: `${f.nome} sai do cadastro. O histórico de alterações é mantido. Se a pessoa saiu da empresa, prefira "Desligar".`,
      botao: "Remover",
    });
    if (!r) return;
    try {
      await api(`/api/funcionarios/${id}`, { method: "DELETE" });
      avisar(`${f.nome} foi removido do cadastro.`);
      location.hash = "#/funcionarios";
    } catch (erro) { falhar(erro); }
  });
  app.querySelector('[data-acao="desligar"]')?.addEventListener("click", async () => {
    const r = await confirmar({ titulo: `Desligar ${f.nome}?`, texto: "A pessoa continua no cadastro, como desligada. Pedidos de ausência pendentes são cancelados.", botao: "Desligar", comData: true });
    if (!r) return;
    try {
      await api(`/api/funcionarios/${id}/desligar`, { method: "POST", body: { data: r.data || null } });
      avisar("Desligamento registrado.");
      telaFicha(id);
    } catch (erro) { falhar(erro); }
  });
  app.querySelector('[data-acao="reativar"]')?.addEventListener("click", async () => {
    await api(`/api/funcionarios/${id}/reativar`, { method: "POST" }).catch(falhar);
    avisar(`${f.nome} está ativo novamente.`);
    telaFicha(id);
  });
}

function linhaDoTempo(registros, { comNome = false, compacta = false } = {}) {
  if (!registros.length) return `<p class="carregando">Nenhuma alteração registrada.</p>`;
  return `
    <ol class="linha-tempo">
      ${registros.map((r) => {
        const mudancas = r.tipoAcao === "Atualizacao" && r.alteracoes.length
          ? (compacta
            ? `<p class="nota">${esc(r.alteracoes.map((a) => a.campo).join(", "))}</p>`
            : `<ul class="mudancas">${r.alteracoes.map((a) => `
                <li><span class="campo-nome">${esc(a.campo)}</span>
                  <span>${a.antes ? `<del>${esc(a.antes)}</del><span class="seta">→</span>` : ""}<ins>${esc(a.depois ?? "—")}</ins></span></li>`).join("")}
              </ul>`)
          : r.tipoAcao === "Inclusao" ? `<p class="nota">${esc(r.foto.cargo)} · ${esc(r.departamento)}${r.foto.salario != null && pode("verSalarios") ? ` · ${dinheiro(r.foto.salario)}` : ""}</p>`
          : r.tipoAcao === "Desligamento" ? `<p class="nota">Saída em ${dataBr(r.foto.dataDesligamento)}</p>`
          : r.tipoAcao === "Remocao" ? `<p class="nota">Removido do cadastro (${esc(r.foto.cargo)}, ${esc(r.departamento)})</p>`
          : "";
        return `
          <li class="evento" style="--cor:${CORES_ACAO[r.tipoAcao]}">
            <header>
              ${comNome ? `<a href="#/funcionarios/${r.funcionarioId}">${esc(r.nomeFuncionario)}</a>` : ""}
              ${seloAcao(r.tipoAcao)}
              <time datetime="${esc(r.quando)}" title="${new Date(r.quando).toLocaleString("pt-BR")}">${momento(r.quando)}</time>
            </header>
            ${mudancas}
            ${compacta ? "" : `<p class="nota">por ${esc(r.autor)}</p>`}
          </li>`;
      }).join("")}
    </ol>`;
}

// ---------- Foto ----------

const arquivoFoto = document.getElementById("arquivoFoto");
let aoEnviarFoto = null;

function escolherFoto(id, depois) {
  aoEnviarFoto = { id, depois };
  arquivoFoto.value = "";
  arquivoFoto.click();
}

arquivoFoto.addEventListener("change", async () => {
  const arquivo = arquivoFoto.files[0];
  if (!arquivo || !aoEnviarFoto) return;
  if (arquivo.size > 2 * 1024 * 1024) { avisar("A foto deve ter até 2 MB.", "erro-aviso"); return; }
  const dados = new FormData();
  dados.append("arquivo", arquivo);
  try {
    const r = await api(`/api/funcionarios/${aoEnviarFoto.id}/foto`, { method: "PUT", body: dados });
    avisar("Foto atualizada.");
    if (aoEnviarFoto.id === sessao.funcionarioId) {
      sessao.fotoUrl = r.fotoUrl;
      document.querySelector("#eu .avatar").innerHTML = `<img src="${esc(r.fotoUrl)}" alt="">`;
    }
    aoEnviarFoto.depois();
  } catch (erro) { falhar(erro); }
});

// ---------- Crachá para impressão ----------

async function telaCracha(id) {
  const f = await api(`/api/funcionarios/${id}`);
  document.title = `Crachá de ${f.nome} · Crachá`;
  app.innerHTML = `
    <a class="voltar" href="#/funcionarios/${id}">← ${esc(f.nome)}</a>
    <div class="topo">
      <div><h1>Crachá para impressão</h1><p>Tamanho CR80 (54 × 86 mm). O QR Code do verso abre a ficha da pessoa no Crachá.</p></div>
      <button class="botao primario" type="button" data-imprimir>🖨️ Imprimir</button>
    </div>
    <div class="impressao" style="--cor:${esc(f.corDepartamento)}">
      <div class="cartao-cracha">
        <span class="furo"></span>
        <div class="faixa">CRACHÁ</div>
        <div class="foto">${f.fotoUrl ? `<img src="${esc(f.fotoUrl)}" alt="">` : esc(iniciais(f.nome))}</div>
        <div class="nome">${esc(f.nome)}</div>
        <div class="cargo">${esc(f.cargo)}</div>
        <div class="depto">${esc(f.departamento)}</div>
        <div class="rodape-cracha"><span>Matrícula #${String(f.id).padStart(5, "0")}</span><span>Ramal ${esc(f.ramal)}</span></div>
      </div>
      <div class="cartao-cracha verso">
        <span class="marca-verso">Crachá</span>
        <img class="qr" src="/api/funcionarios/${id}/qrcode" alt="QR Code da ficha de ${esc(f.nome)}">
        <p>Aponte a câmera para abrir a ficha.<br>Em caso de perda, devolva ao RH.</p>
        <p>Admissão: ${dataBr(f.dataAdmissao)}</p>
      </div>
    </div>`;
  app.querySelector("[data-imprimir]").addEventListener("click", () => window.print());
}

// ---------- Organograma ----------

async function telaOrganograma() {
  const [nos] = await Promise.all([api("/api/organograma"), carregarDepartamentos()]);
  document.title = "Organograma · Crachá";
  const porId = new Map(nos.map((n) => [n.id, { ...n, filhos: [] }]));
  const raizes = [];
  for (const n of porId.values()) {
    const pai = n.gestorId != null ? porId.get(n.gestorId) : null;
    (pai ? pai.filhos : raizes).push(n);
  }
  const contar = (n) => n.filhos.reduce((t, f) => t + 1 + contar(f), 0);
  const desenhar = (n) => `
    <li>
      <a class="no ${n.id === sessao.funcionarioId ? "eu-mesmo" : ""}" href="#/funcionarios/${n.id}" style="--cor:${esc(n.cor)}">
        ${avatar(n.nome, n.cor, "pequeno", n.fotoUrl)}
        <span><strong>${esc(n.nome)}</strong><small>${esc(n.cargo)} · ${esc(n.departamento)}</small></span>
      </a>${n.filhos.length ? `<button class="recolher" type="button" title="Recolher ou expandir (${contar(n)} pessoa(s) abaixo)" aria-expanded="true">−</button>` : ""}
      ${n.filhos.length ? `<ul>${n.filhos.map(desenhar).join("")}</ul>` : ""}
    </li>`;

  app.innerHTML = `
    <div class="topo">
      <div><h1>Organograma</h1><p>${nos.length} pessoas ativas · clique em alguém para abrir a ficha.</p></div>
      <div class="acoes" style="margin:0">
        <button class="botao" type="button" data-todos="abrir">Expandir tudo</button>
        <button class="botao" type="button" data-todos="fechar">Recolher tudo</button>
      </div>
    </div>
    <section class="cartao organograma"><ul class="arvore">${raizes.map(desenhar).join("")}</ul></section>`;

  const alternar = (li, fechar) => {
    li.classList.toggle("fechado", fechar);
    const b = li.querySelector(":scope > .recolher");
    b.textContent = fechar ? "+" : "−";
    b.setAttribute("aria-expanded", String(!fechar));
  };
  app.querySelectorAll(".recolher").forEach((b) => b.addEventListener("click", () => alternar(b.parentElement, !b.parentElement.classList.contains("fechado"))));
  app.querySelectorAll("[data-todos]").forEach((b) => b.addEventListener("click", () =>
    app.querySelectorAll(".arvore li:has(> .recolher)").forEach((li) => alternar(li, b.dataset.todos === "fechar"))));
}

// ---------- Ausências ----------

const estadoAusencias = { aba: null, mes: null };

function listaPedidos(lista, { semNome = false } = {}) {
  if (!lista.length) return `<p class="carregando">Nenhum pedido por aqui.</p>`;
  return `<div class="pedidos">${lista.map((a) => `
    <div class="pedido">
      ${semNome ? seloTipo(a.tipo) : avatar(a.funcionario, a.cor, "", a.fotoUrl)}
      <div class="info">
        <strong>${semNome ? "" : `<a href="#/funcionarios/${a.funcionarioId}">${esc(a.funcionario)}</a> · `}${dataBr(a.inicio)} a ${dataBr(a.fim)} <small>(${a.dias} dia${a.dias > 1 ? "s" : ""})</small></strong>
        <small>${semNome ? "" : `${TIPOS[a.tipo]} · `}${seloStatus(a.status)} pedido por ${esc(a.solicitadaPor)}${a.decididaPor ? ` · decidido por ${esc(a.decididaPor)}` : ""}</small>
      </div>
      <div class="acoes">
        ${a.podeDecidir ? `<button class="botao pequeno primario" type="button" data-aprovar="${a.id}">Aprovar</button>
          <button class="botao pequeno" type="button" data-recusar="${a.id}">Recusar</button>` : ""}
        ${a.podeCancelar ? `<button class="botao pequeno" type="button" data-cancelar="${a.id}">Cancelar</button>` : ""}
      </div>
      ${a.motivoRecusa ? `<p class="motivo">Motivo: ${esc(a.motivoRecusa)}</p>` : a.observacao ? `<p class="motivo">“${esc(a.observacao)}”</p>` : ""}
    </div>`).join("")}</div>`;
}

function ligarPedidos(recarregar) {
  app.querySelectorAll("[data-aprovar]").forEach((b) => b.addEventListener("click", async () => {
    try {
      await api(`/api/ausencias/${b.dataset.aprovar}/decisao`, { method: "POST", body: { aprovar: true } });
      avisar("Pedido aprovado.");
      atualizarPendentes();
      recarregar();
    } catch (erro) { falhar(erro); }
  }));
  app.querySelectorAll("[data-recusar]").forEach((b) => b.addEventListener("click", async () => {
    const r = await confirmar({ titulo: "Recusar o pedido?", texto: "A pessoa verá o motivo.", botao: "Recusar", comMotivo: true });
    if (!r) return;
    try {
      await api(`/api/ausencias/${b.dataset.recusar}/decisao`, { method: "POST", body: { aprovar: false, motivo: r.motivo } });
      avisar("Pedido recusado.");
      atualizarPendentes();
      recarregar();
    } catch (erro) { falhar(erro); }
  }));
  app.querySelectorAll("[data-cancelar]").forEach((b) => b.addEventListener("click", async () => {
    if (!await confirmar({ titulo: "Cancelar esta ausência?", texto: "O período fica livre de novo.", botao: "Cancelar ausência" })) return;
    try {
      await api(`/api/ausencias/${b.dataset.cancelar}/cancelar`, { method: "POST" });
      avisar("Ausência cancelada.");
      atualizarPendentes();
      recarregar();
    } catch (erro) { falhar(erro); }
  }));
}

async function telaAusencias() {
  document.title = "Ausências · Crachá";
  const abas = [
    pode("aprovarAusencias") && { id: "decidir", nome: "Para decidir" },
    sessao.funcionarioId && { id: "minhas", nome: "Minhas" },
    { id: "calendario", nome: "Calendário" },
    (pode("acompanhar")) && { id: "todas", nome: "Todas" },
  ].filter(Boolean);
  if (!abas.some((a) => a.id === estadoAusencias.aba)) estadoAusencias.aba = abas[0].id;
  const pendentes = pode("aprovarAusencias") ? await api("/api/ausencias?paraDecidir=true") : [];

  app.innerHTML = `
    <div class="topo">
      <div><h1>Férias e ausências</h1><p>Pedidos, aprovações e o calendário de quem está fora.</p></div>
      <div class="acoes" style="margin:0">
        <a class="botao" href="/api/exportar/ausencias.csv" download>⬇ CSV</a>
        ${pode("gerirCadastro") ? `<button class="botao" type="button" data-lancar>Lançar ausência</button>` : ""}
        ${sessao.funcionarioId ? `<button class="botao primario" type="button" data-pedir>+ Pedir férias ou folga</button>` : ""}
      </div>
    </div>
    <div class="abas" role="tablist">
      ${abas.map((a) => `<button type="button" role="tab" data-aba="${a.id}" class="${a.id === estadoAusencias.aba ? "ativo" : ""}">${a.nome}${a.id === "decidir" && pendentes.length ? `<span class="contador">${pendentes.length}</span>` : ""}</button>`).join("")}
    </div>
    <div id="conteudoAusencias"></div>`;

  app.querySelector("[data-pedir]")?.addEventListener("click", () => abrirAusencia());
  app.querySelector("[data-lancar]")?.addEventListener("click", () => abrirAusencia({ lancamento: true }));
  app.querySelectorAll("[data-aba]").forEach((b) => b.addEventListener("click", () => { estadoAusencias.aba = b.dataset.aba; telaAusencias(); }));

  const alvo = document.getElementById("conteudoAusencias");
  if (estadoAusencias.aba === "calendario") return desenharCalendario(alvo);

  const lista = estadoAusencias.aba === "decidir" ? pendentes
    : estadoAusencias.aba === "minhas" ? await api(`/api/ausencias?funcionarioId=${sessao.funcionarioId}`)
    : await api("/api/ausencias");
  alvo.innerHTML = estadoAusencias.aba === "decidir" && !lista.length
    ? `<div class="vazio">Nenhum pedido aguardando você. ✅</div>`
    : `<section class="cartao">${listaPedidos(lista, { semNome: estadoAusencias.aba === "minhas" })}</section>`;
  ligarPedidos(telaAusencias);
}

async function desenharCalendario(alvo) {
  const base = estadoAusencias.mes || new Date(new Date().getFullYear(), new Date().getMonth(), 1);
  estadoAusencias.mes = base;
  const ultimo = new Date(base.getFullYear(), base.getMonth() + 1, 0);
  const dias = Array.from({ length: ultimo.getDate() }, (_, i) => new Date(base.getFullYear(), base.getMonth(), i + 1));
  const lista = (await api(`/api/ausencias?de=${iso(base)}&ate=${iso(ultimo)}`))
    .filter((a) => a.status === "Aprovada" || a.status === "Pendente");
  const pessoas = [...new Map(lista.map((a) => [a.funcionarioId, a])).values()].sort((a, b) => a.funcionario.localeCompare(b.funcionario, "pt-BR"));
  const hoje = hojeIso();

  alvo.innerHTML = `
    <section class="cartao">
      <h2><span class="navegar-mes">
        <button class="botao pequeno" type="button" data-mes="-1" aria-label="Mês anterior">‹</button>
        <strong>${base.toLocaleDateString("pt-BR", { month: "long", year: "numeric" })}</strong>
        <button class="botao pequeno" type="button" data-mes="1" aria-label="Próximo mês">›</button>
      </span><small>${pessoas.length} pessoa(s) com ausência no mês</small></h2>
      ${pessoas.length ? `<div class="calendario"><table>
        <thead><tr><th></th>${dias.map((d) => `<th class="${[0, 6].includes(d.getDay()) ? "fds" : ""} ${iso(d) === hoje ? "hoje" : ""}">${d.getDate()}</th>`).join("")}</tr></thead>
        <tbody>${pessoas.map((p) => `<tr><th><a href="#/funcionarios/${p.funcionarioId}">${esc(p.funcionario)}</a></th>${dias.map((d) => {
          const dia = iso(d);
          const a = lista.find((x) => x.funcionarioId === p.funcionarioId && x.inicio <= dia && x.fim >= dia);
          const classes = `${[0, 6].includes(d.getDay()) ? "fds" : ""} ${dia === hoje ? "hoje" : ""}`;
          if (!a) return `<td class="${classes}"></td>`;
          return `<td class="${classes}" title="${esc(p.funcionario)}: ${TIPOS[a.tipo]} de ${dataBr(a.inicio)} a ${dataBr(a.fim)} (${a.status.toLowerCase()})"><i style="--cor:${CORES_TIPO[a.tipo]}" class="${a.inicio === dia ? "inicio" : ""} ${a.fim === dia ? "fim" : ""} ${a.status === "Pendente" ? "pendente" : ""}"></i></td>`;
        }).join("")}</tr>`).join("")}</tbody>
      </table></div>` : `<p class="carregando">Ninguém ausente neste mês.</p>`}
      <div class="legenda-ausencias">${Object.keys(TIPOS).map(seloTipo).join("")}<span class="selo simples status Pendente">▨ listrado = pendente</span></div>
    </section>`;
  alvo.querySelectorAll("[data-mes]").forEach((b) => b.addEventListener("click", () => {
    estadoAusencias.mes = new Date(base.getFullYear(), base.getMonth() + Number(b.dataset.mes), 1);
    desenharCalendario(alvo);
  }));
}

// Formulário de ausência: pedido próprio ou lançamento do RH para qualquer pessoa.
const modalAusencia = document.getElementById("modalAusencia");
const formAusencia = document.getElementById("formAusencia");

async function abrirAusencia({ funcionarioId = null, lancamento = false } = {}) {
  formAusencia.reset();
  limparErros(formAusencia);
  const rh = pode("gerirCadastro");
  const escolherPessoa = rh && (lancamento || funcionarioId);
  document.getElementById("ausenciaTitulo").textContent = escolherPessoa ? "Lançar ausência" : "Pedir férias ou folga";
  formAusencia.querySelector('[type="submit"]').textContent = escolherPessoa ? "Lançar (já aprovada)" : "Enviar pedido";
  formAusencia.tipo.querySelectorAll("[data-rh]").forEach((o) => { o.hidden = !rh; o.disabled = !rh; });
  document.getElementById("campoPessoa").hidden = !escolherPessoa;
  if (escolherPessoa) {
    const ativos = await api("/api/funcionarios?situacao=Ativo");
    formAusencia.funcionarioId.innerHTML = ativos.map((f) => `<option value="${f.id}">${esc(f.nome)}</option>`).join("");
    formAusencia.funcionarioId.value = funcionarioId || sessao.funcionarioId || ativos[0]?.id;
  }
  const inicio = new Date(); inicio.setDate(inicio.getDate() + 14);
  formAusencia.inicio.value = iso(inicio);
  inicio.setDate(inicio.getDate() + 9);
  formAusencia.fim.value = iso(inicio);
  atualizarDias();
  modalAusencia.showModal();
}

function atualizarDias() {
  const { inicio, fim } = formAusencia;
  document.getElementById("diasAusencia").textContent = inicio.value && fim.value && fim.value >= inicio.value
    ? `${diasEntre(inicio.value, fim.value)} dia(s) corrido(s). Férias: de 5 a 30 dias por período.` : "";
}
formAusencia.inicio.addEventListener("change", atualizarDias);
formAusencia.fim.addEventListener("change", atualizarDias);

formAusencia.addEventListener("submit", async (e) => {
  e.preventDefault();
  limparErros(formAusencia);
  const corpo = {
    funcionarioId: document.getElementById("campoPessoa").hidden ? null : Number(formAusencia.funcionarioId.value),
    tipo: formAusencia.tipo.value,
    inicio: formAusencia.inicio.value || null,
    fim: formAusencia.fim.value || null,
    observacao: formAusencia.observacao.value.trim() || null,
  };
  try {
    const a = await api("/api/ausencias", { method: "POST", body: corpo });
    modalAusencia.close();
    avisar(a.status === "Aprovada" ? "Ausência lançada." : "Pedido enviado. Seu gestor foi avisado pela tela de aprovações.");
    atualizarPendentes();
    rotear();
  } catch (erro) { mostrarErros(formAusencia, erro); }
});

// ---------- Departamentos ----------

async function telaDepartamentos() {
  const [, funcionarios] = await Promise.all([carregarDepartamentos(), api("/api/funcionarios?situacao=Ativo")]);
  document.title = "Departamentos · Crachá";
  const gerir = pode("gerirCadastro");
  let editando = null;

  const desenhar = () => {
    app.innerHTML = `
      <div class="topo"><div><h1>Departamentos</h1><p>${departamentos.length} departamentos · ${funcionarios.length} pessoas ativas</p></div></div>
      ${gerir ? `<form class="cartao form-departamento" id="formDepartamento" novalidate>
        <label>${editando ? "Renomear departamento" : "Novo departamento"}
          <input name="nome" maxlength="40" required placeholder="Ex.: Jurídico" value="${esc(editando?.nome || "")}">
        </label>
        <label>Cor <input name="cor" type="color" value="${esc(editando?.cor || "#0d9488")}"></label>
        <span class="acoes" style="margin:0">
          ${editando ? `<button class="botao" type="button" data-cancelar-edicao>Cancelar</button>` : ""}
          <button class="botao primario" type="submit">${editando ? "Salvar" : "Adicionar"}</button>
        </span>
      </form>` : ""}
      <div class="departamentos">
        ${departamentos.map((d) => {
          const pessoas = funcionarios.filter((f) => f.departamentoId === d.id);
          return `
            <section class="cartao departamento" style="--cor:${esc(d.cor)}">
              <h2>${esc(d.nome)}</h2>
              <div class="numeros">
                <div><span>Pessoas</span><strong>${d.ativos}</strong></div>
                <div><span>Folha mensal</span><strong>${d.folha == null ? "🔒" : dinheiroCurto(d.folha)}</strong></div>
              </div>
              <div class="rostos">${pessoas.slice(0, 6).map((f) => avatar(f.nome, d.cor, "pequeno", f.fotoUrl)).join("")}${pessoas.length > 6 ? `<span class="avatar pequeno" style="--cor:#64748b">+${pessoas.length - 6}</span>` : ""}</div>
              <div class="acoes">
                <a class="botao pequeno" href="#/funcionarios" data-ver="${d.id}">Ver pessoas</a>
                ${gerir ? `<button class="botao pequeno" type="button" data-editar="${d.id}">Editar</button>
                <button class="botao pequeno" type="button" data-remover="${d.id}">Remover</button>` : ""}
              </div>
            </section>`;
        }).join("")}
      </div>`;

    app.querySelectorAll("[data-ver]").forEach((a) => a.addEventListener("click", () => {
      filtros.departamentoId = a.dataset.ver;
      filtros.situacao = "Ativo";
    }));
    if (!gerir) return;
    const formDepto = document.getElementById("formDepartamento");
    formDepto.addEventListener("submit", async (e) => {
      e.preventDefault();
      const corpo = { nome: formDepto.nome.value.trim(), cor: formDepto.cor.value };
      if (!corpo.nome) { formDepto.nome.focus(); return; }
      try {
        await api(editando ? `/api/departamentos/${editando.id}` : "/api/departamentos", { method: editando ? "PUT" : "POST", body: corpo });
        avisar(editando ? "Departamento atualizado." : "Departamento criado.");
        editando = null;
        await carregarDepartamentos();
        desenhar();
      } catch (erro) { falhar(erro); }
    });
    formDepto.querySelector("[data-cancelar-edicao]")?.addEventListener("click", () => { editando = null; desenhar(); });
    app.querySelectorAll("[data-editar]").forEach((b) => b.addEventListener("click", () => {
      editando = departamentos.find((d) => d.id === Number(b.dataset.editar));
      desenhar();
      document.querySelector("#formDepartamento input").focus();
    }));
    app.querySelectorAll("[data-remover]").forEach((b) => b.addEventListener("click", async () => {
      const d = departamentos.find((x) => x.id === Number(b.dataset.remover));
      if (!await confirmar({ titulo: `Remover ${d.nome}?`, texto: "Só é possível remover departamentos sem funcionários.", botao: "Remover" })) return;
      try {
        await api(`/api/departamentos/${d.id}`, { method: "DELETE" });
        avisar("Departamento removido.");
        await carregarDepartamentos();
        desenhar();
      } catch (erro) { falhar(erro); }
    }));
  };
  desenhar();
}

// ---------- Histórico ----------

const filtroHistorico = { tipo: "", departamento: "" };

async function telaHistorico() {
  await carregarDepartamentos();
  document.title = "Histórico · Crachá";
  app.innerHTML = `
    <div class="topo">
      <div><h1>Histórico de alterações</h1><p>Todo cadastro, mudança, desligamento e remoção fica registrado, com o antes, o depois e quem fez.</p></div>
      <a class="botao" id="exportarHistorico" href="/api/exportar/historico.csv" download>⬇ CSV</a>
    </div>
    <div class="filtros">
      <select id="filtroTipo" aria-label="Tipo de alteração">
        <option value="">Todas as alterações</option>
        ${Object.entries(ACOES).map(([v, r]) => `<option value="${v}">${r}</option>`).join("")}
      </select>
      <select id="filtroDeptoHistorico" aria-label="Departamento">
        <option value="">Todos os departamentos</option>
        ${departamentos.map((d) => `<option>${esc(d.nome)}</option>`).join("")}
      </select>
    </div>
    <p class="contagem" id="contagemHistorico"></p>
    <section class="cartao" id="listaHistorico"><p class="carregando">Carregando…</p></section>`;

  const tipo = document.getElementById("filtroTipo");
  const depto = document.getElementById("filtroDeptoHistorico");
  tipo.value = filtroHistorico.tipo;
  depto.value = filtroHistorico.departamento;

  const carregar = async () => {
    const q = new URLSearchParams();
    if (filtroHistorico.tipo) q.set("tipo", filtroHistorico.tipo);
    if (filtroHistorico.departamento) q.set("departamento", filtroHistorico.departamento);
    document.getElementById("exportarHistorico").href = `/api/exportar/historico.csv?${q}`;
    q.set("limite", 200);
    const registros = await api(`/api/historico?${q}`);
    document.getElementById("contagemHistorico").textContent = `${registros.length} registro(s)`;
    document.getElementById("listaHistorico").innerHTML = linhaDoTempo(registros, { comNome: true });
  };
  tipo.addEventListener("change", () => { filtroHistorico.tipo = tipo.value; carregar(); });
  depto.addEventListener("change", () => { filtroHistorico.departamento = depto.value; carregar(); });
  await carregar();
}

// ---------- Acessos (administrador) ----------

const modalUsuario = document.getElementById("modalUsuario");
const formUsuario = document.getElementById("formUsuario");
let usuarioEmEdicao = null;

async function telaAcessos() {
  document.title = "Acessos · Crachá";
  const usuarios = await api("/api/usuarios");
  app.innerHTML = `
    <div class="topo">
      <div><h1>Acessos</h1><p>Quem entra no sistema e com qual perfil. Mudar perfil, senha ou situação encerra as sessões abertas da pessoa.</p></div>
      <button class="botao primario" type="button" data-novo-acesso>+ Novo acesso</button>
    </div>
    <div class="cartao tabela-rolagem">
      <table>
        <thead><tr><th>Pessoa</th><th>Perfil</th><th class="col-opcional">Funcionário</th><th class="col-opcional">Último acesso</th><th>Situação</th><th></th></tr></thead>
        <tbody>${usuarios.map((u) => `
          <tr class="${u.ativo ? "" : "desligado"}">
            <td><span class="pessoa">${avatar(u.nome, "#0f766e", "pequeno")}<span><strong>${esc(u.nome)}</strong><small>${esc(u.email)}</small></span></span></td>
            <td><span class="selo simples">${esc(PERFIS[u.perfil])}</span></td>
            <td class="col-opcional">${u.funcionarioId ? `<a href="#/funcionarios/${u.funcionarioId}">${esc(u.funcionario)}</a>` : "—"}</td>
            <td class="col-opcional">${u.ultimoAcesso ? momento(u.ultimoAcesso) : "nunca"}</td>
            <td>${u.ativo ? "Ativo" : "Desativado"}</td>
            <td class="direita"><button class="botao pequeno" type="button" data-editar-acesso="${u.id}">Editar</button></td>
          </tr>`).join("")}</tbody>
      </table>
    </div>`;
  app.querySelector("[data-novo-acesso]").addEventListener("click", () => abrirUsuario());
  app.querySelectorAll("[data-editar-acesso]").forEach((b) => b.addEventListener("click", () =>
    abrirUsuario(usuarios.find((u) => u.id === Number(b.dataset.editarAcesso)))));
}

async function abrirUsuario(usuario = null) {
  usuarioEmEdicao = usuario;
  formUsuario.reset();
  limparErros(formUsuario);
  document.getElementById("usuarioTitulo").textContent = usuario ? `Editar acesso de ${usuario.nome}` : "Novo acesso";
  document.getElementById("dicaSenha").textContent = usuario ? "(vazio mantém a atual)" : "(8+ caracteres, letras e números)";
  const funcionarios = await api("/api/funcionarios?situacao=Ativo");
  formUsuario.funcionarioId.innerHTML = `<option value="">— Sem vínculo (só administrador/RH) —</option>` +
    funcionarios.map((f) => `<option value="${f.id}" data-nome="${esc(f.nome)}" data-email="${esc(f.emailProfissional)}">${esc(f.nome)} · ${esc(f.cargo)}</option>`).join("");
  if (usuario) {
    formUsuario.funcionarioId.value = usuario.funcionarioId ?? "";
    formUsuario.nome.value = usuario.nome;
    formUsuario.email.value = usuario.email;
    formUsuario.perfil.value = usuario.perfil;
    formUsuario.ativo.checked = usuario.ativo;
  }
  modalUsuario.showModal();
}

formUsuario.funcionarioId.addEventListener("change", () => {
  const opcao = formUsuario.funcionarioId.selectedOptions[0];
  if (!opcao?.dataset.nome || usuarioEmEdicao) return;
  formUsuario.nome.value = opcao.dataset.nome;
  formUsuario.email.value = opcao.dataset.email;
});

formUsuario.addEventListener("submit", async (e) => {
  e.preventDefault();
  limparErros(formUsuario);
  const corpo = {
    nome: formUsuario.nome.value.trim(),
    email: formUsuario.email.value.trim(),
    perfil: formUsuario.perfil.value,
    funcionarioId: Number(formUsuario.funcionarioId.value) || null,
    ativo: formUsuario.ativo.checked,
    senha: formUsuario.senha.value || null,
  };
  try {
    await api(usuarioEmEdicao ? `/api/usuarios/${usuarioEmEdicao.id}` : "/api/usuarios", { method: usuarioEmEdicao ? "PUT" : "POST", body: corpo });
    modalUsuario.close();
    avisar(usuarioEmEdicao ? "Acesso atualizado." : "Acesso criado.");
    telaAcessos();
  } catch (erro) { mostrarErros(formUsuario, erro, { "e-mail": "email", funcion: "funcionarioId" }); }
});

// ---------- Troca de senha ----------

const modalSenha = document.getElementById("modalSenha");
const formSenha = document.getElementById("formSenha");
document.getElementById("trocarSenha").addEventListener("click", () => { formSenha.reset(); limparErros(formSenha); modalSenha.showModal(); });
formSenha.addEventListener("submit", async (e) => {
  e.preventDefault();
  limparErros(formSenha);
  try {
    await api("/api/conta/senha", { method: "POST", body: { atual: formSenha.atual.value, nova: formSenha.nova.value } });
    modalSenha.close();
    avisar("Senha trocada. As outras sessões foram encerradas.");
  } catch (erro) { mostrarErros(formSenha, erro); }
});

// ---------- Formulário de funcionário ----------

let emEdicao = null;

async function abrirFormulario(funcionario = null) {
  emEdicao = funcionario;
  if (!departamentos.length) await carregarDepartamentos();
  form.reset();
  limparErros(form);
  document.getElementById("modalTitulo").textContent = funcionario ? `Editar ${funcionario.nome}` : "Novo funcionário";

  form.departamentoId.innerHTML = `<option value="">Escolha…</option>` +
    departamentos.map((d) => `<option value="${d.id}">${esc(d.nome)}</option>`).join("");
  const ativos = await api("/api/funcionarios?situacao=Ativo");
  form.gestorId.innerHTML = `<option value="">— Sem gestor (topo do organograma) —</option>` +
    ativos.filter((f) => f.id !== funcionario?.id).map((f) => `<option value="${f.id}">${esc(f.nome)} · ${esc(f.cargo)}</option>`).join("");
  const cargos = [...new Set(ativos.map((f) => f.cargo))].sort((a, b) => a.localeCompare(b, "pt-BR"));
  document.getElementById("cargos").innerHTML = cargos.map((c) => `<option value="${esc(c)}">`).join("");

  if (funcionario) {
    for (const campo of ["nome", "cargo", "emailProfissional", "ramal", "endereco", "salario", "dataAdmissao", "departamentoId", "gestorId"])
      form[campo].value = funcionario[campo] ?? "";
  } else {
    form.dataAdmissao.value = hojeIso();
    if (filtros.departamentoId) form.departamentoId.value = filtros.departamentoId;
  }
  modal.showModal();
  form.nome.focus();
}

function limparErros(alvo) {
  alvo.querySelectorAll("[data-erro]").forEach((e) => (e.textContent = ""));
  alvo.querySelectorAll("[aria-invalid]").forEach((e) => e.removeAttribute("aria-invalid"));
}

function mostrarErro(alvo, campo, mensagem) {
  const lugar = alvo.querySelector(`[data-erro="${campo}"]`) || alvo.querySelector('[data-erro="geral"]');
  lugar.textContent = mensagem;
  alvo.elements[campo]?.setAttribute?.("aria-invalid", "true");
}

// Leva cada mensagem de validação da API para baixo do campo certo.
function mostrarErros(alvo, erro, conflitos = { "e-mail": "emailProfissional" }) {
  if (erro.silencioso) return;
  if (erro.status === 409) {
    const campo = Object.entries(conflitos).find(([trecho]) => erro.message.toLowerCase().includes(trecho))?.[1] || "geral";
    mostrarErro(alvo, campo, erro.message);
    return;
  }
  const campos = Object.entries(erro.campos || {});
  if (!campos.length) { mostrarErro(alvo, "geral", erro.message); return; }
  for (const [chave, mensagens] of campos) {
    const nome = chave.replace(/^\$\./, "");
    mostrarErro(alvo, nome.charAt(0).toLowerCase() + nome.slice(1), mensagens[0]);
  }
  alvo.querySelector('[aria-invalid="true"]')?.focus();
}

form.addEventListener("submit", async (e) => {
  e.preventDefault();
  limparErros(form);
  const corpo = {
    nome: form.nome.value.trim(),
    cargo: form.cargo.value.trim(),
    emailProfissional: form.emailProfissional.value.trim(),
    ramal: form.ramal.value.trim(),
    endereco: form.endereco.value.trim() || null,
    departamentoId: Number(form.departamentoId.value) || 0,
    gestorId: Number(form.gestorId.value) || null,
    salario: Number(form.salario.value) || 0,
    dataAdmissao: form.dataAdmissao.value || null,
  };
  try {
    const salvo = await api(emEdicao ? `/api/funcionarios/${emEdicao.id}` : "/api/funcionarios",
      { method: emEdicao ? "PUT" : "POST", body: corpo });
    modal.close();
    avisar(emEdicao ? "Alterações salvas e registradas no histórico." : `${salvo.nome} foi cadastrado.`);
    if (location.hash === `#/funcionarios/${salvo.id}`) rotear();
    else location.hash = `#/funcionarios/${salvo.id}`;
  } catch (erro) { mostrarErros(form, erro); }
});

document.querySelectorAll("dialog [data-fechar]").forEach((b) => b.addEventListener("click", () => b.closest("dialog").close()));
document.getElementById("novoFuncionario").addEventListener("click", () => abrirFormulario());

// ---------- Confirmação ----------

const dialogo = document.getElementById("confirmacao");

function confirmar({ titulo, texto, botao = "Confirmar", comData = false, comMotivo = false }) {
  document.getElementById("confirmacaoTitulo").textContent = titulo;
  document.getElementById("confirmacaoTexto").textContent = texto;
  document.getElementById("confirmacaoOk").textContent = botao;
  const campoData = document.getElementById("confirmacaoData");
  const campoMotivo = document.getElementById("confirmacaoMotivo");
  campoData.hidden = !comData;
  campoMotivo.hidden = !comMotivo;
  campoData.querySelector("input").value = hojeIso();
  campoMotivo.querySelector("input").value = "";
  dialogo.returnValue = "";
  dialogo.showModal();
  if (comMotivo) campoMotivo.querySelector("input").focus();
  return new Promise((resolver) => {
    dialogo.addEventListener("close", () => {
      resolver(dialogo.returnValue === "sim"
        ? { data: comData ? campoData.querySelector("input").value : null, motivo: campoMotivo.querySelector("input").value.trim() }
        : null);
    }, { once: true });
  });
}

// ---------- Busca rápida (Ctrl+K) ----------

const paleta = document.getElementById("paleta");
const paletaBusca = document.getElementById("paletaBusca");
const paletaResultados = document.getElementById("paletaResultados");
let paletaLista = [], paletaCache = null, paletaSelecionado = 0;

async function abrirPaleta() {
  if (!sessao) return;
  paletaBusca.value = "";
  paleta.showModal();
  if (!paletaCache || Date.now() - paletaCache.em > 60000)
    paletaCache = { em: Date.now(), lista: await api("/api/funcionarios").catch(() => []) };
  filtrarPaleta();
}

function filtrarPaleta() {
  const normalizar = (t) => t.normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase();
  const termo = normalizar(paletaBusca.value.trim());
  paletaLista = paletaCache.lista
    .filter((f) => !termo || [f.nome, f.cargo, f.ramal, f.emailProfissional, f.departamento].some((v) => normalizar(v).includes(termo)))
    .slice(0, 8);
  paletaSelecionado = 0;
  paletaResultados.innerHTML = paletaLista.length ? paletaLista.map((f, i) => `
    <li role="option" aria-selected="${i === 0}">
      <a href="#/funcionarios/${f.id}">${avatar(f.nome, f.corDepartamento, "pequeno", f.fotoUrl)}
        <span><strong>${esc(f.nome)}</strong><small>${esc(f.cargo)} · ${esc(f.departamento)} · ramal ${esc(f.ramal)}${f.situacao === "Desligado" ? " · desligado" : ""}</small></span></a>
    </li>`).join("") : `<li class="carregando" style="padding:.8rem">Ninguém encontrado.</li>`;
  paletaResultados.querySelectorAll("a").forEach((a) => a.addEventListener("click", () => paleta.close()));
}

paletaBusca.addEventListener("input", filtrarPaleta);
paletaBusca.addEventListener("keydown", (e) => {
  if (!paletaLista.length) return;
  if (e.key === "ArrowDown" || e.key === "ArrowUp") {
    e.preventDefault();
    paletaSelecionado = (paletaSelecionado + (e.key === "ArrowDown" ? 1 : -1) + paletaLista.length) % paletaLista.length;
    paletaResultados.querySelectorAll("li").forEach((li, i) => li.setAttribute("aria-selected", String(i === paletaSelecionado)));
  } else if (e.key === "Enter") {
    e.preventDefault();
    location.hash = `#/funcionarios/${paletaLista[paletaSelecionado].id}`;
    paleta.close();
  }
});
paleta.addEventListener("click", (e) => { if (e.target === paleta) paleta.close(); });
document.getElementById("abrirPaleta").addEventListener("click", abrirPaleta);
document.addEventListener("keydown", (e) => {
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
    e.preventDefault();
    if (paleta.open) paleta.close(); else abrirPaleta();
  }
});

// ---------- Início ----------

api("/api/sistema", { login: true }).then((s) => {
  document.getElementById("sistema").innerHTML =
    `Dados: <b>${esc(s.banco)}</b><br>Histórico: <b>${esc(s.historico)}</b><br>Fotos: <b>${esc(s.fotos)}</b>`;
}).catch(() => {});

api("/api/conta/eu", { login: true })
  .then(iniciarSessao)
  .catch(() => mostrarLogin());
