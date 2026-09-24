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
let departamentos = [];

// ---------- Utilidades ----------

const esc = (v) => String(v ?? "").replace(/[&<>"']/g, (c) =>
  ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

async function api(caminho, opcoes = {}) {
  const resposta = await fetch(caminho, {
    method: opcoes.method || "GET",
    headers: opcoes.body !== undefined ? { "Content-Type": "application/json" } : {},
    body: opcoes.body !== undefined ? JSON.stringify(opcoes.body) : undefined,
  });
  if (resposta.status === 204) return null;
  const dados = await resposta.json().catch(() => null);
  if (!resposta.ok) {
    const erro = new Error(dados?.title || `Erro ${resposta.status}`);
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
  temporizador = setTimeout(() => aviso.classList.remove("visivel"), 3000);
}

const guardar = (chave, valor) => { try { localStorage.setItem(chave, valor); } catch { /* sem armazenamento */ } };
const lembrar = (chave, padrao) => { try { return localStorage.getItem(chave) ?? padrao; } catch { return padrao; } };

const moeda = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });
const dinheiro = (v) => moeda.format(v);
const dinheiroCurto = (v) => v >= 1000 ? `R$ ${(v / 1000).toLocaleString("pt-BR", { maximumFractionDigits: 1 })} mil` : dinheiro(v);

// Datas sem hora ("2026-09-03") viram data local, sem deslocar o dia pelo fuso.
const dataLocal = (texto) => { const [a, m, d] = texto.split("-").map(Number); return new Date(a, m - 1, d); };
const dataBr = (texto) => texto ? dataLocal(texto).toLocaleDateString("pt-BR") : "—";
const hojeIso = () => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`; };

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

const avatar = (nome, cor, classe = "") =>
  `<span class="avatar ${classe}" style="--cor:${esc(cor)}" aria-hidden="true">${esc(iniciais(nome))}</span>`;

const seloDepartamento = (nome, cor) => `<span class="selo" style="--cor:${esc(cor)}">${esc(nome)}</span>`;
const seloAcao = (tipo) => `<span class="selo simples acao ${tipo}">${ACOES[tipo] || tipo}</span>`;
const corDoDepartamento = (nome) => departamentos.find((d) => d.nome === nome)?.cor || "#64748b";

async function carregarDepartamentos() {
  departamentos = await api("/api/departamentos");
}

// ---------- Rotas ----------

const rotas = {
  "": telaPainel,
  funcionarios: telaFuncionarios,
  departamentos: telaDepartamentos,
  historico: telaHistorico,
};

async function rotear() {
  const [secao = "", id] = location.hash.replace(/^#\/?/, "").split("/");
  const tela = secao === "funcionarios" && id ? () => telaFicha(Number(id)) : rotas[secao] || telaPainel;
  document.querySelectorAll(".menu a").forEach((a) =>
    a.classList.toggle("ativo", a.dataset.rota === (secao || "painel")));
  try {
    await tela();
  } catch (erro) {
    app.innerHTML = `<div class="vazio">Não foi possível carregar: ${esc(erro.message)}</div>`;
  }
  app.focus({ preventScroll: true });
}

window.addEventListener("hashchange", () => { window.scrollTo(0, 0); rotear(); });

// ---------- Painel ----------

async function telaPainel() {
  const [p] = await Promise.all([api("/api/painel"), carregarDepartamentos()]);
  document.title = "Painel · Crachá";
  const maxAtivos = Math.max(1, ...p.porDepartamento.map((d) => d.ativos));
  const maxMes = Math.max(1, ...p.admissoesPorMes.map((m) => Math.max(m.admissoes, m.desligamentos)));
  const hoje = new Date();
  const mes = hoje.toLocaleDateString("pt-BR", { month: "long" });

  app.innerHTML = `
    <div class="topo">
      <div>
        <h1>Painel de pessoas</h1>
        <p>${hoje.toLocaleDateString("pt-BR", { weekday: "long", day: "numeric", month: "long", year: "numeric" })}</p>
      </div>
    </div>

    <section class="indicadores">
      <div class="cartao indicador"><span>Funcionários ativos</span><strong>${p.ativos}</strong><small>${p.desligados} desligado(s) no cadastro</small></div>
      <div class="cartao indicador"><span>Folha mensal</span><strong>${dinheiroCurto(p.folhaMensal)}</strong><small>${dinheiro(p.folhaMensal)}</small></div>
      <div class="cartao indicador"><span>Salário médio</span><strong>${dinheiroCurto(p.salarioMedio)}</strong><small>entre os ativos</small></div>
      <div class="cartao indicador"><span>Tempo médio de casa</span><strong>${(p.mesesMedioDeCasa / 12).toLocaleString("pt-BR", { maximumFractionDigits: 1 })} anos</strong><small>${p.admissoesNoAno} admissões em ${hoje.getFullYear()}</small></div>
      <div class="cartao indicador"><span>Rotatividade (12 meses)</span><strong>${p.rotatividade12Meses.toLocaleString("pt-BR")}%</strong><small>desligamentos ÷ quadro médio</small></div>
    </section>

    <div class="grade-painel">
      <section class="cartao">
        <h2>Pessoas por departamento <a href="#/departamentos">Ver todos</a></h2>
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
  app.innerHTML = `
    <div class="topo">
      <div><h1>Funcionários</h1><p>Clique num crachá para ver a ficha e o histórico.</p></div>
      <button class="botao primario" type="button" data-novo>+ Novo funcionário</button>
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
        <option value="Salario">Salário</option>
        <option value="Departamento">Departamento</option>
      </select>
      <div class="alternar" role="group" aria-label="Visualização">
        <button type="button" data-visao="crachas" aria-label="Crachás">🪪</button>
        <button type="button" data-visao="tabela" aria-label="Tabela">☰</button>
      </div>
    </div>
    <p class="contagem" id="contagem"></p>
    <div id="lista"><p class="carregando">Carregando…</p></div>`;

  document.getElementById("filtroSituacao").value = filtros.situacao;
  document.getElementById("filtroOrdem").value = filtros.ordem;
  app.querySelector("[data-novo]").addEventListener("click", () => abrirFormulario());

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

  app.querySelectorAll("[data-visao]").forEach((b) => b.classList.toggle("ativo", b.dataset.visao === filtros.visao));
  const folha = lista.filter((f) => f.situacao === "Ativo").reduce((s, f) => s + f.salario, 0);
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
        ${avatar(f.nome, f.corDepartamento)}
        <strong>${esc(f.nome)}</strong>
        <span class="cargo">${esc(f.cargo)}</span>
        ${f.situacao === "Desligado" ? `<span class="selo desligado">Desligado em ${dataBr(f.dataDesligamento)}</span>` : seloDepartamento(f.departamento, f.corDepartamento)}
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
          <th class="col-opcional">Ramal</th>
          ${cab("Admissao", "Admissão", "col-opcional")}
          ${cab("Salario", "Salário", "direita")}
        </tr></thead>
        <tbody>
          ${lista.map((f) => `
            <tr class="${f.situacao === "Desligado" ? "desligado" : ""}">
              <td><a class="pessoa" href="#/funcionarios/${f.id}">${avatar(f.nome, f.corDepartamento, "pequeno")}<span><strong>${esc(f.nome)}</strong><small>${esc(f.cargo)}${f.situacao === "Desligado" ? " · desligado" : ""}</small></span></a></td>
              <td class="col-opcional">${seloDepartamento(f.departamento, f.corDepartamento)}</td>
              <td class="col-opcional num">${esc(f.ramal)}</td>
              <td class="col-opcional num">${dataBr(f.dataAdmissao)}</td>
              <td class="direita num">${dinheiro(f.salario)}</td>
            </tr>`).join("")}
        </tbody>
      </table>
    </div>`;
}

// ---------- Ficha ----------

async function telaFicha(id) {
  const [funcionario, historico] = await Promise.all([
    api(`/api/funcionarios/${id}`).catch((e) => { if (e.status === 404) return null; throw e; }),
    api(`/api/funcionarios/${id}/historico`),
    carregarDepartamentos(),
  ]);

  // Removido do cadastro: a ficha é reconstruída a partir da última foto do histórico.
  if (!funcionario && !historico.length) {
    app.innerHTML = `<a class="voltar" href="#/funcionarios">← Funcionários</a><div class="vazio">Funcionário não encontrado.</div>`;
    return;
  }
  const f = funcionario || { ...historico[0].foto, corDepartamento: corDoDepartamento(historico[0].departamento), removido: true };
  document.title = `${f.nome} · Crachá`;
  const desligado = f.situacao === "Desligado";

  app.innerHTML = `
    <a class="voltar" href="#/funcionarios">← Funcionários</a>
    <div class="ficha">
      <section class="cartao ${desligado ? "desligado" : ""}">
        <div class="ficha-cabeca">
          ${avatar(f.nome, f.corDepartamento, "grande")}
          <h1>${esc(f.nome)}</h1>
          <p>${esc(f.cargo)}</p>
          ${f.removido ? `<span class="selo simples acao Remocao">Removido do cadastro</span>`
            : desligado ? `<span class="selo desligado">Desligado em ${dataBr(f.dataDesligamento)}</span>`
            : seloDepartamento(f.departamento, f.corDepartamento)}
        </div>
        <dl class="dados">
          <div><dt>Departamento</dt><dd>${esc(f.departamento)}</dd></div>
          <div><dt>E-mail</dt><dd>${f.removido ? esc(f.emailProfissional) : `<a href="mailto:${esc(f.emailProfissional)}">${esc(f.emailProfissional)}</a>`}</dd></div>
          <div><dt>Ramal</dt><dd class="num">${esc(f.ramal)}</dd></div>
          <div><dt>Endereço</dt><dd>${esc(f.endereco) || "—"}</dd></div>
          <div><dt>Salário</dt><dd class="num">${dinheiro(f.salario)}</dd></div>
          <div><dt>Admissão</dt><dd>${dataBr(f.dataAdmissao)}</dd></div>
          ${f.mesesDeCasa !== undefined ? `<div><dt>Tempo de casa</dt><dd>${tempoDeCasa(f.mesesDeCasa)}</dd></div>` : ""}
          <div><dt>Matrícula</dt><dd class="num">#${String(f.id).padStart(5, "0")}</dd></div>
        </dl>
        ${f.removido ? "" : `
        <div class="acoes">
          <button class="botao primario" type="button" data-acao="editar">Editar</button>
          ${desligado
            ? `<button class="botao" type="button" data-acao="reativar">Reativar</button>`
            : `<button class="botao" type="button" data-acao="desligar">Desligar</button>`}
          <button class="botao" type="button" data-acao="remover">Remover</button>
        </div>`}
      </section>

      <section class="cartao">
        <h2>Linha do tempo <small>${historico.length} registro(s)</small></h2>
        ${linhaDoTempo(historico)}
      </section>
    </div>`;

  if (f.removido) return;
  app.querySelector('[data-acao="editar"]').addEventListener("click", () => abrirFormulario(funcionario));
  app.querySelector('[data-acao="remover"]').addEventListener("click", async () => {
    const r = await confirmar({
      titulo: "Remover do cadastro?",
      texto: `${f.nome} sai do cadastro. O histórico de alterações é mantido. Se a pessoa saiu da empresa, prefira "Desligar".`,
      botao: "Remover",
    });
    if (!r) return;
    await api(`/api/funcionarios/${id}`, { method: "DELETE" });
    avisar(`${f.nome} foi removido do cadastro.`);
    location.hash = "#/funcionarios";
  });
  app.querySelector('[data-acao="desligar"]')?.addEventListener("click", async () => {
    const r = await confirmar({ titulo: `Desligar ${f.nome}?`, texto: "A pessoa continua no cadastro, como desligada.", botao: "Desligar", comData: true });
    if (!r) return;
    try {
      await api(`/api/funcionarios/${id}/desligar`, { method: "POST", body: { data: r.data || null } });
      avisar("Desligamento registrado.");
      telaFicha(id);
    } catch (erro) {
      avisar(Object.values(erro.campos)[0]?.[0] || erro.message, "erro-aviso");
    }
  });
  app.querySelector('[data-acao="reativar"]')?.addEventListener("click", async () => {
    await api(`/api/funcionarios/${id}/reativar`, { method: "POST" });
    avisar(`${f.nome} está ativo novamente.`);
    telaFicha(id);
  });
}

function linhaDoTempo(registros, { comNome = false, compacta = false } = {}) {
  if (!registros.length) return `<p class="carregando">Nenhuma alteração registrada.</p>`;
  return `
    <ol class="linha-tempo">
      ${registros.map((r) => {
        const cor = { Inclusao: "#059669", Atualizacao: "#2563eb", Desligamento: "#d97706", Reativacao: "#0d9488", Remocao: "#dc2626" }[r.tipoAcao];
        const mudancas = r.tipoAcao === "Atualizacao" && r.alteracoes.length
          ? (compacta
            ? `<p class="nota">${esc(r.alteracoes.map((a) => a.campo).join(", "))}</p>`
            : `<ul class="mudancas">${r.alteracoes.map((a) => `
                <li><span class="campo-nome">${esc(a.campo)}</span>
                  <span>${a.antes ? `<del>${esc(a.antes)}</del><span class="seta">→</span>` : ""}<ins>${esc(a.depois ?? "—")}</ins></span></li>`).join("")}
              </ul>`)
          : r.tipoAcao === "Inclusao" ? `<p class="nota">${esc(r.foto.cargo)} · ${esc(r.departamento)} · ${dinheiro(r.foto.salario)}</p>`
          : r.tipoAcao === "Desligamento" ? `<p class="nota">Saída em ${dataBr(r.foto.dataDesligamento)}</p>`
          : r.tipoAcao === "Remocao" ? `<p class="nota">Removido do cadastro (${esc(r.foto.cargo)}, ${esc(r.departamento)})</p>`
          : "";
        return `
          <li class="evento" style="--cor:${cor}">
            <header>
              ${comNome ? `<a href="#/funcionarios/${r.funcionarioId}">${esc(r.nomeFuncionario)}</a>` : ""}
              ${seloAcao(r.tipoAcao)}
              <time datetime="${esc(r.quando)}" title="${new Date(r.quando).toLocaleString("pt-BR")}">${momento(r.quando)}</time>
            </header>
            ${mudancas}
          </li>`;
      }).join("")}
    </ol>`;
}

// ---------- Departamentos ----------

async function telaDepartamentos() {
  const [, funcionarios] = await Promise.all([carregarDepartamentos(), api("/api/funcionarios?situacao=Ativo")]);
  document.title = "Departamentos · Crachá";
  let editando = null;

  const desenhar = () => {
    app.innerHTML = `
      <div class="topo"><div><h1>Departamentos</h1><p>${departamentos.length} departamentos · ${funcionarios.length} pessoas ativas</p></div></div>
      <form class="cartao form-departamento" id="formDepartamento" novalidate>
        <label>${editando ? "Renomear departamento" : "Novo departamento"}
          <input name="nome" maxlength="40" required placeholder="Ex.: Jurídico" value="${esc(editando?.nome || "")}">
        </label>
        <label>Cor <input name="cor" type="color" value="${esc(editando?.cor || "#0d9488")}"></label>
        <span class="acoes" style="margin:0">
          ${editando ? `<button class="botao" type="button" data-cancelar>Cancelar</button>` : ""}
          <button class="botao primario" type="submit">${editando ? "Salvar" : "Adicionar"}</button>
        </span>
      </form>
      <div class="departamentos">
        ${departamentos.map((d) => {
          const pessoas = funcionarios.filter((f) => f.departamentoId === d.id);
          return `
            <section class="cartao departamento" style="--cor:${esc(d.cor)}">
              <h2>${esc(d.nome)}</h2>
              <div class="numeros">
                <div><span>Pessoas</span><strong>${d.ativos}</strong></div>
                <div><span>Folha mensal</span><strong>${dinheiroCurto(d.folha)}</strong></div>
              </div>
              <div class="rostos">${pessoas.slice(0, 6).map((f) => avatar(f.nome, d.cor, "pequeno")).join("")}${pessoas.length > 6 ? `<span class="avatar pequeno" style="--cor:#64748b">+${pessoas.length - 6}</span>` : ""}</div>
              <div class="acoes">
                <a class="botao pequeno" href="#/funcionarios" data-ver="${d.id}">Ver pessoas</a>
                <button class="botao pequeno" type="button" data-editar="${d.id}">Editar</button>
                <button class="botao pequeno" type="button" data-remover="${d.id}">Remover</button>
              </div>
            </section>`;
        }).join("")}
      </div>`;

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
      } catch (erro) {
        avisar(Object.values(erro.campos)[0]?.[0] || erro.message, "erro-aviso");
      }
    });
    formDepto.querySelector("[data-cancelar]")?.addEventListener("click", () => { editando = null; desenhar(); });
    app.querySelectorAll("[data-editar]").forEach((b) => b.addEventListener("click", () => {
      editando = departamentos.find((d) => d.id === Number(b.dataset.editar));
      desenhar();
      document.querySelector("#formDepartamento input").focus();
    }));
    app.querySelectorAll("[data-ver]").forEach((a) => a.addEventListener("click", () => {
      filtros.departamentoId = a.dataset.ver;
      filtros.situacao = "Ativo";
    }));
    app.querySelectorAll("[data-remover]").forEach((b) => b.addEventListener("click", async () => {
      const d = departamentos.find((x) => x.id === Number(b.dataset.remover));
      if (!await confirmar({ titulo: `Remover ${d.nome}?`, texto: "Só é possível remover departamentos sem funcionários.", botao: "Remover" })) return;
      try {
        await api(`/api/departamentos/${d.id}`, { method: "DELETE" });
        avisar("Departamento removido.");
        await carregarDepartamentos();
        desenhar();
      } catch (erro) {
        avisar(erro.message, "erro-aviso");
      }
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
      <div><h1>Histórico de alterações</h1><p>Todo cadastro, mudança, desligamento e remoção fica registrado, com o antes e o depois.</p></div>
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
    const q = new URLSearchParams({ limite: 200 });
    if (filtroHistorico.tipo) q.set("tipo", filtroHistorico.tipo);
    if (filtroHistorico.departamento) q.set("departamento", filtroHistorico.departamento);
    const registros = await api(`/api/historico?${q}`);
    document.getElementById("contagemHistorico").textContent = `${registros.length} registro(s)`;
    document.getElementById("listaHistorico").innerHTML = linhaDoTempo(registros, { comNome: true });
  };
  tipo.addEventListener("change", () => { filtroHistorico.tipo = tipo.value; carregar(); });
  depto.addEventListener("change", () => { filtroHistorico.departamento = depto.value; carregar(); });
  await carregar();
}

// ---------- Formulário de funcionário ----------

let emEdicao = null;

async function abrirFormulario(funcionario = null) {
  emEdicao = funcionario;
  if (!departamentos.length) await carregarDepartamentos();
  form.reset();
  limparErros();
  document.getElementById("modalTitulo").textContent = funcionario ? `Editar ${funcionario.nome}` : "Novo funcionário";

  form.departamentoId.innerHTML = `<option value="">Escolha…</option>` +
    departamentos.map((d) => `<option value="${d.id}">${esc(d.nome)}</option>`).join("");
  api("/api/funcionarios").then((lista) => {
    const cargos = [...new Set(lista.map((f) => f.cargo))].sort((a, b) => a.localeCompare(b, "pt-BR"));
    document.getElementById("cargos").innerHTML = cargos.map((c) => `<option value="${esc(c)}">`).join("");
  }).catch(() => {});

  if (funcionario) {
    for (const campo of ["nome", "cargo", "emailProfissional", "ramal", "endereco", "salario", "dataAdmissao", "departamentoId"])
      form[campo].value = funcionario[campo] ?? "";
  } else {
    form.dataAdmissao.value = hojeIso();
    if (filtros.departamentoId) form.departamentoId.value = filtros.departamentoId;
  }
  modal.showModal();
  form.nome.focus();
}

function limparErros() {
  form.querySelectorAll("[data-erro]").forEach((e) => (e.textContent = ""));
  form.querySelectorAll("[aria-invalid]").forEach((e) => e.removeAttribute("aria-invalid"));
}

function mostrarErro(campo, mensagem) {
  const alvo = form.querySelector(`[data-erro="${campo}"]`) || form.querySelector('[data-erro="geral"]');
  alvo.textContent = mensagem;
  form[campo]?.setAttribute?.("aria-invalid", "true");
}

form.addEventListener("submit", async (e) => {
  e.preventDefault();
  limparErros();
  const corpo = {
    nome: form.nome.value.trim(),
    cargo: form.cargo.value.trim(),
    emailProfissional: form.emailProfissional.value.trim(),
    ramal: form.ramal.value.trim(),
    endereco: form.endereco.value.trim() || null,
    departamentoId: Number(form.departamentoId.value) || 0,
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
  } catch (erro) {
    if (erro.status === 409) {
      mostrarErro(/e-mail/i.test(erro.message) ? "emailProfissional" : "geral", erro.message);
      return;
    }
    const campos = Object.entries(erro.campos);
    if (!campos.length) { mostrarErro("geral", erro.message); return; }
    for (const [chave, mensagens] of campos) {
      const nome = chave.replace(/^\$\./, "");
      mostrarErro(nome.charAt(0).toLowerCase() + nome.slice(1), mensagens[0]);
    }
    form.querySelector('[aria-invalid="true"]')?.focus();
  }
});

modal.querySelectorAll("[data-fechar]").forEach((b) => b.addEventListener("click", () => modal.close()));
document.getElementById("novoFuncionario").addEventListener("click", () => abrirFormulario());

// ---------- Confirmação ----------

const dialogo = document.getElementById("confirmacao");

function confirmar({ titulo, texto, botao = "Confirmar", comData = false }) {
  document.getElementById("confirmacaoTitulo").textContent = titulo;
  document.getElementById("confirmacaoTexto").textContent = texto;
  document.getElementById("confirmacaoOk").textContent = botao;
  const campoData = document.getElementById("confirmacaoData");
  campoData.hidden = !comData;
  campoData.querySelector("input").value = hojeIso();
  dialogo.returnValue = "";
  dialogo.showModal();
  return new Promise((resolver) => {
    dialogo.addEventListener("close", () => {
      resolver(dialogo.returnValue === "sim" ? { data: comData ? campoData.querySelector("input").value : null } : null);
    }, { once: true });
  });
}

// ---------- Início ----------

api("/api/sistema").then((s) => {
  document.getElementById("sistema").innerHTML = `Dados: <b>${esc(s.banco)}</b><br>Histórico: <b>${esc(s.historico)}</b>`;
}).catch(() => {});

rotear();
