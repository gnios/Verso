# Verso.Bench — benchmark da Fase 3 (spike CTranslate2)

Ferramenta de linha de comando que executa o protocolo **R3.3** do spike
([`.specs/features/transcricao-cpu-responsiva/phase3-spike.md`](../.specs/features/transcricao-cpu-responsiva/phase3-spike.md)):
compara, no **mesmo hardware Windows**, o motor de transcrição atual da Verso
(**whisper.net**, rodado pelo caminho de produção real `Verso.Worker.exe` via
`WorkerProcessTranscriptionEngine`) contra **`whisper_ctranslate2.exe`** (faster-whisper/CTranslate2).

Coleta, por arquivo e por motor: **RTF** (real-time factor), **wall-clock** e **pico de RAM** (amostrado
por nome de processo), com warmup + N execuções medidas (mediana). Ao final imprime a tabela e o **gate
G1** do spike (ct2 precisa ser ≥ 1,4× mais rápido).

> Este projeto fica **fora da `Verso.sln`** de propósito: não é produto e não roda no CI. Vive na branch
> `bench/fase3-ctranslate2`. Build/execução via `dotnet run --project bench/Verso.Bench`.

## Pré-requisitos

1. **Windows** (o `whisper_ctranslate2.exe` é Windows-only; o problema original — UI travando — é no Windows).
2. **`Verso.Worker.exe`** construído. Gere com:
   ```powershell
   dotnet build src/Verso.App/Verso.App.csproj -c Release
   # Verso.Worker.exe cai no output do App (copy target da Fase 2, T10). Ex.:
   #   src/Verso.App/bin/Release/net10.0/Verso.Worker.exe
   ```
3. **`whisper_ctranslate2.exe`** — via NuGet `Soenneker.Libraries.Whisper.CTranslate` (fica em
   `.../Resources/whisper_ctranslate2.exe`) ou build próprio a partir da fonte MIT. **Fixe (pin) a versão
   e verifique o `hash.txt`** (ver ressalvas de proveniência em `phase3-spike.md`, R3.1).
4. **ffmpeg** no PATH (para medir a duração dos áudios → RTF). O whisper.net já exige ffmpeg de qualquer forma.
5. **`VERSO_WHISPER_N_THREADS` NÃO pode estar setado** — ele sobrepõe o `--threads` no motor A.

## Uso

```powershell
dotnet run --project bench/Verso.Bench -c Release -- `
  --audio-dir .\amostras `
  --worker-exe .\src\Verso.App\bin\Release\net10.0\Verso.Worker.exe `
  --ct2-exe .\tools\whisper_ctranslate2.exe `
  --quality Standard --ct2-model small `
  --language pt `
  --threads 8 `
  --runs 3 --warmup 1 `
  --out .\bench-results.md
```

- Use **3 arquivos representativos**: curto (~2 min), médio (~15 min), longo (~60 min) — de preferência casos reais.
- **`--threads`**: para uma comparação justa, passe o **mesmo N explícito** nos dois motores. O valor da
  Fase 1 é `ProcessorCount/2`; deixe `--threads 0` (default) para que o motor A use esse auto, mas então o
  ctranslate2 usa o default dele — prefira fixar N > 0 igual nos dois.
- `--engines a` roda só whisper.net; `--engines b` só ctranslate2 (útil para isolar/depurar).
- Modelos: pares equivalentes (`--quality Standard` [= small ggml] ↔ `--ct2-model small`; `Base` ↔ `base`; `Medium` ↔ `medium`).

## Interpretando o resultado

O relatório aplica **G1** automaticamente (RTF ct2 ≤ 70% do RTF whisper.net ⇒ ≥ 1,4× mais rápido). Os
demais gates são manuais (ver `phase3-spike.md`):

- **G2** — qualidade comparável (sem degradação perceptível).
- **G3** — estabilidade (sem crash nas N execuções; falha do exe isolada pelo worker — já garantido pela Fase 2).
- **G4** — crescimento aceitável do instalador (o exe PyInstaller + libs é grande).
- **G5** — aceitável perder Vulkan (CTranslate2 só faz CPU/CUDA) ou manter os dois motores.

**Decisão**: adotar só se **G1 E** (G2..G5). Se G1 falhar → não adotar (a Fase 1 já resolveu o travamento).
Se G1 passar mas G4/G5 falharem → considerar adoção **opt-in** (usuário escolhe o motor nas settings).

⚠️ **Não confie no “4× mais rápido” de marketing** — ele é medido contra `openai/whisper` (PyTorch lento),
não contra whisper.cpp/whisper.net que a Verso já usa. Só o número medido aqui vale.
