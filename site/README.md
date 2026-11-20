# Landing page do Verso

Página estática independente para apresentar os recursos do aplicativo. O workflow em `.github/workflows/pages.yml` publica o conteúdo desta pasta no GitHub Pages quando há alterações em `site/` na branch `main`, ou quando o workflow é iniciado manualmente.

Na primeira publicação, habilite GitHub Pages nas configurações do repositório e selecione **GitHub Actions** como origem de publicação.

## Prévia local

Na raiz do repositório, sirva a pasta com qualquer servidor HTTP estático. Por exemplo, com Python:

```powershell
py -m http.server 8000 --directory site
```

Depois abra `http://localhost:8000`.

As imagens são locais e usam caminhos relativos, para a página funcionar tanto na raiz do domínio do GitHub Pages quanto em um endereço de projeto como `/Verso/`.
