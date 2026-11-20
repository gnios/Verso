<p align="center">
  <img src="docs/brand/verso-logo.png" width="112" alt="Logo do Verso, com o perfil de uma capivara usando fones">
</p>

<h1 align="center">Verso</h1>
<p align="center">Transcreva, revise e organize suas gravações no próprio computador.</p>
<p align="center">
  <a href="https://github.com/gnios/Verso/releases/latest"><strong>Baixar o Verso</strong></a> ·
  <a href="#primeira-transcrição">Primeira transcrição</a> ·
  <a href="DOCS.md">Documentação técnica</a>
</p>

O Verso é um aplicativo desktop em português para transformar entrevistas, aulas e outras gravações em texto. Você pode ouvir a gravação enquanto revisa a transcrição, identificar quem está falando e exportar o resultado como texto ou legenda.

## Instalação

Baixe a versão mais recente nas [releases do Verso](https://github.com/gnios/Verso/releases/latest). Os pacotes já incluem o runtime .NET.

- **Windows 10/11 x64 (`win-x64`)** — execute o `*-setup.exe` para instalar sem administrador ou extraia o ZIP e abra `Verso.App.exe`. O aplicativo usa o Microsoft Edge WebView2.
- **Linux x64 (`linux-x64`)** — extraia o ZIP e execute `./Verso.App`. Instale o WebKitGTK da sua distribuição, como `libwebkit2gtk-4.1-0` no Ubuntu ou Debian.
- **macOS Apple Silicon (`osx-arm64`)** — extraia o ZIP e execute `./Verso.App`. O aplicativo usa o WKWebView do sistema.

A variante **CPU** funciona sem aceleração gráfica. A variante **GPU** oferece CUDA ou Vulkan no Windows e no Linux, e Core ML no macOS.

## Primeira transcrição

<img align="right" src="docs/brand/verso-mascote.png" width="160" alt="Capivara pesquisadora com fones, caderno e caneta">

1. Importe uma gravação de áudio ou vídeo ou grave pelo microfone.
2. Aguarde o download do modelo no primeiro uso. Acompanhe a transcrição na fila.
3. Abra o resultado no editor e use o player e a forma de onda para conferir trechos. Use a detecção automática de locutores e ajuste as atribuições por segmento.
4. Organize o material em pesquisas e tags. Exporte em **TXT** para trabalhar com o texto ou em **SRT** e **VTT** para usar como legenda.

<br clear="all">

> [!TIP]
> Para começar, use um arquivo WAV PCM. Para MP3, MP4 e outros formatos, instale o FFmpeg e deixe-o disponível no PATH.

### Modelos e dados

Em **Configurações**, você pode escolher entre dois motores de reconhecimento.

- **Parakeet** usa o modelo **pt-BR TAGARELA** por padrão. Também oferece o **TDT v3** para português, inglês e espanhol.
- **Whisper** oferece os perfis **Rápido**, **Equilibrado** e **Preciso** para ajustar o tempo de processamento e a precisão.

O reconhecimento roda localmente. Nos pacotes ZIP e EXE, o banco SQLite, as mídias, os modelos e os logs ficam em `data/`, ao lado do executável. No MSIX, ficam em `%LOCALAPPDATA%\Gnios\Verso\data`. Os pacotes das GitHub Releases atualizam os binários e preservam a biblioteca local.

## Desenvolvimento

Com o **SDK do .NET 10** e o runtime WebView da sua plataforma instalados, execute na raiz do repositório.

```sh
dotnet restore
dotnet run --project src/Verso.App
dotnet test tests/Verso.Tests
```

- [Verso.App](src/Verso.App) hospeda a interface Photino.Blazor.
- [Verso.Core](src/Verso.Core) reúne os motores de reconhecimento, a persistência SQLite e a exportação.
- [Verso.Worker](src/Verso.Worker) executa a transcrição em um processo separado.
- [Verso.Updater](src/Verso.Updater) aplica atualizações depois que o aplicativo encerra.

Para detalhes de arquitetura e testes de interface, consulte a [documentação técnica](DOCS.md) e o [guia de testes de ponta a ponta](tests/Verso.E2E/README.md).

## Publicação

O [workflow de release](.github/workflows/release.yml) publica uma versão no GitHub após o envio de uma tag `v*.*.*` ou um push para `main`. O push para `main` incrementa automaticamente o patch. A execução manual gera artefatos para validação. O pipeline produz seis ZIPs, um por combinação de plataforma e variante, e dois instaladores Windows.

### Assinatura dos pacotes Windows

O pipeline exige assinatura Authenticode válida e carimbo de tempo para os executáveis e DLLs dos pacotes CPU e GPU, o instalador e o desinstalador. Ele preserva assinaturas incorporadas válidas com carimbo de tempo das dependências. A publicação falha se a configuração estiver incompleta ou se os arquivos não passarem na verificação.

1. Emita um [certificado de assinatura de código OV com eSigner](https://ssl.com/pt/products/software-integrity/code-signing/ov/) em nome da razão social da Gnios. Conclua a validação da empresa com a SSL.com e habilite a assinatura automatizada no eSigner.
2. No GitHub, em **Settings → Secrets and variables → Actions**, cadastre `ESIGNER_USERNAME`, `ESIGNER_PASSWORD`, `ESIGNER_CREDENTIAL_ID` e `ESIGNER_TOTP_SECRET`. Este último é a chave de configuração do TOTP da credencial de assinatura, obtida ao habilitar a automação no eSigner.
3. Execute o workflow **release** manualmente para gerar os artefatos de teste. No Windows, confira o publicador, o status `Valid` e o carimbo de tempo do instalador baixado com `Get-AuthenticodeSignature .\Verso-*-setup.exe | Format-List Status,SignerCertificate,TimeStamperCertificate`. Depois de testar a instalação e o desinstalador, publique pelo fluxo normal de tags.

O SmartScreen também considera a reputação do download. Uma versão assinada ainda pode exibir um aviso enquanto ganha reputação. [Documentação da Microsoft](https://learn.microsoft.com/windows/apps/package-and-deploy/code-signing-options)

### Verso Transcrição na Microsoft Store

No fluxo Win32, crie um produto do tipo **Aplicativo EXE ou MSI** no Partner Center. Na primeira submissão, informe:

- **URL do pacote** — link direto e versionado do `Verso-<versão>-cpu-win-x64-setup.exe` assinado na GitHub Release. Cadastre inicialmente a variante CPU, que usa a mesma arquitetura x64 da variante GPU. Use uma nova URL a cada versão.
- **Arquitetura e idioma** — `x64` e `pt-BR`.
- **Parâmetros do instalador** — `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-`.
- **Documentação dos códigos de saída** — [Setup Exit Codes do Inno Setup](https://jrsoftware.org/inno-setup/documentation/topic_setupexitcodes.htm).

Depois da primeira publicação, use `msstore submission get <ID-Win32>` para obter o JSON de origem, atualize `packageUrl` para o novo instalador e envie com `msstore submission update <ID-Win32> --payload package.json`. Revise a submissão antes de executar `msstore submission publish <ID-Win32>`. [Fluxo EXE/MSI no Partner Center](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/upload-app-packages) · [CLI para EXE/MSI](https://learn.microsoft.com/en-us/windows/apps/publish/msstore-dev-cli/commands-exe)

### Pacote MSIX para a Microsoft Store

Para gerar o MSIX x64 (CPU) para envio ao Partner Center, use o `MakeAppx.exe` x64 do Windows SDK:

```powershell
.\scripts\build-store-msix.ps1 -MakeAppx 'C:\caminho\MakeAppx.exe' -Version '1.0.0.0'
```

Envie o `.msix` de `artifacts/store/` na seção **Pacotes**. A Store assina o pacote após a certificação e distribui as atualizações. Para novas submissões, aumente a versão e mantenha o quarto componente em zero.

Informe no início da descrição da Store que MP3, MP4 e outros formatos além de WAV requerem FFmpeg instalado separadamente. Para a certificação, indique que um WAV PCM permite testar a transcrição sem FFmpeg e que o primeiro uso requer internet para baixar o modelo. O Partner Center também exige a URL pública da política de privacidade.
