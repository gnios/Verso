# Verso.E2E (Playwright)

Testes end-to-end para validar a abertura de transcrição e o streaming de áudio.

## Suites

| Teste | O que cobre | Requer |
|-------|-------------|--------|
| `MediaStreamingE2ETests` | LocalMediaServer + HTML harness: prepare não baixa; load usa Range; bytes ≪ arquivo | Chromium (Playwright) |
| `OpenTranscriptionE2ETests` | App Photino real via CDP: abrir card sem fetch de mídia; play usa HTTP Range | Windows + WebView2 + `Verso.App.exe` |

## Como rodar

```powershell
# 1) Build do app (necessário para o E2E Photino)
dotnet build src/Verso.App/Verso.App.csproj

# 2) E2E (instala Chromium na primeira vez)
dotnet test tests/Verso.E2E/Verso.E2E.csproj -v n

# Só o harness (sem janela Photino)
dotnet test tests/Verso.E2E/Verso.E2E.csproj --filter FullyQualifiedName~MediaStreamingE2ETests -v n
```

Ou:

```powershell
pwsh scripts/run-e2e.ps1
```

## Isolamento

Os testes definem `VERSO_DATA_ROOT` para um diretório temporário (banco + mídia), sem tocar no `data/` do seu app de desenvolvimento.
