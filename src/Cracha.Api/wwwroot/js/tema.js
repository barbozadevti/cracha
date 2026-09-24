// Aplica o tema salvo antes da página desenhar, para não piscar o tema errado.
try {
  const tema = localStorage.getItem("cracha.tema");
  if (tema === "claro" || tema === "escuro") document.documentElement.dataset.tema = tema;
} catch { /* sem armazenamento: segue o sistema */ }
