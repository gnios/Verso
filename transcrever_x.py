#!/usr/bin/env python3
"""Script interativo para transcrição com WhisperX + diarização."""

import gc
import os
import sys
import threading
import time
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path


def verificar_python() -> None:
    if sys.version_info < (3, 10) or sys.version_info >= (3, 14):
        print(
            "WhisperX requer Python >= 3.10 e < 3.14.\n"
            f"Versão atual: {sys.version.split()[0]}\n"
            "Crie um ambiente virtual com Python 3.12 ou 3.13, por exemplo:\n"
            "  py -3.12 -m venv .venv\n"
            "  .venv\\Scripts\\activate\n"
            "  pip install -r requirements-whisperx.txt"
        )
        raise SystemExit(1)


verificar_python()


def _site_packages() -> Path | None:
    for entry in sys.path:
        if entry.endswith("site-packages"):
            return Path(entry)
    return None


def configurar_dlls_cuda() -> list[Path]:
    """Adiciona DLLs NVIDIA (pip) ao PATH no Windows."""
    adicionados: list[Path] = []
    if sys.platform != "win32":
        return adicionados

    site_packages = _site_packages()
    if site_packages is None:
        return adicionados

    nvidia_base = site_packages / "nvidia"
    if not nvidia_base.is_dir():
        return adicionados

    for subdir in ("cublas", "cudnn", "cuda_nvrtc", "cufft"):
        bin_path = nvidia_base / subdir / "bin"
        if bin_path.is_dir():
            os.add_dll_directory(str(bin_path))
            os.environ["PATH"] = str(bin_path) + os.pathsep + os.environ.get("PATH", "")
            adicionados.append(bin_path)

    return adicionados


CUDA_DLL_PATHS = configurar_dlls_cuda()

import ctranslate2
import torch
import whisperx
from whisperx.diarize import DiarizationPipeline

from audio_silencio import preparar_partes_paralelo


MODELOS_SUGERIDOS = [
    "tiny",
    "base",
    "small",
    "medium",
    "large-v2",
    "large-v3",
    "distil-large-v3",
    "turbo",
]

HF_TOKEN = "hf_JWVbMOCcTnkHnXPwyglRQXHDgNsIiPalOdI"

SAMPLE_RATE = 16000


def _carregar_audio_pyav(arquivo: str, sr: int = SAMPLE_RATE):
    import av
    import numpy as np

    container = av.open(arquivo)
    resampler = av.audio.resampler.AudioResampler(format="fltp", layout="mono", rate=sr)
    chunks: list = []

    for frame in container.decode(audio=0):
        resampled = resampler.resample(frame)
        if resampled is None:
            continue
        frames = resampled if isinstance(resampled, list) else [resampled]
        for item in frames:
            chunks.append(item.to_ndarray().flatten())

    flush = resampler.resample(None)
    if flush is not None:
        frames = flush if isinstance(flush, list) else [flush]
        for item in frames:
            chunks.append(item.to_ndarray().flatten())

    container.close()
    if not chunks:
        raise RuntimeError(f"Nenhum áudio encontrado em: {arquivo}")

    return np.concatenate(chunks).astype(np.float32)


def carregar_audio(arquivo: str):
    try:
        return whisperx.load_audio(arquivo)
    except FileNotFoundError:
        print("ffmpeg não encontrado, usando PyAV...")
        return _carregar_audio_pyav(arquivo)
    except RuntimeError as exc:
        if "Failed to load audio" in str(exc):
            print("ffmpeg falhou, usando PyAV...")
            return _carregar_audio_pyav(arquivo)
        raise


def cuda_dlls_disponiveis() -> bool:
    if sys.platform != "win32":
        return True

    for bin_path in CUDA_DLL_PATHS:
        if (bin_path / "cublas64_12.dll").is_file():
            return True

    site_packages = _site_packages()
    if site_packages is not None:
        dll = site_packages / "nvidia" / "cublas" / "bin" / "cublas64_12.dll"
        if dll.is_file():
            return True

    return False


def cuda_disponivel() -> bool:
    try:
        torch_ok = torch.cuda.is_available()
        ctranslate2_ok = ctranslate2.get_cuda_device_count() > 0
        dlls_ok = cuda_dlls_disponiveis()
        return torch_ok and ctranslate2_ok and dlls_ok
    except Exception:
        return False


def listar_dispositivos() -> list[tuple[str, str, bool]]:
    gpu_ctranslate2 = False
    gpu_torch = False
    try:
        gpu_ctranslate2 = ctranslate2.get_cuda_device_count() > 0
    except Exception:
        pass
    try:
        gpu_torch = torch.cuda.is_available()
    except Exception:
        pass

    cuda_ok = gpu_ctranslate2 and gpu_torch and cuda_dlls_disponiveis()
    if gpu_ctranslate2 and not gpu_torch:
        print(
            "\nAviso: GPU detectada pelo CTranslate2, mas PyTorch está sem CUDA.\n"
            "Reinstale com: pip install torch==2.8.0 torchaudio==2.8.0 torchvision==0.23.0 "
            "--index-url https://download.pytorch.org/whl/cu128"
        )
    return [
        ("cpu", "CPU", True),
        ("cuda", "CUDA (NVIDIA)", cuda_ok),
        ("vulkan", "Vulkan", False),
    ]


def compute_type_padrao(device: str) -> str:
    if device == "cuda":
        return "float16"
    return "int8"


def batch_size_padrao(device: str) -> int:
    return 16 if device == "cuda" else 4


def _mensagem_cuda_indisponivel(device: str, nome: str) -> None:
    if device == "vulkan":
        print(
            "Vulkan não é suportado pelo WhisperX/faster-whisper. "
            "Use whisper.cpp para aceleração Vulkan."
        )
        return

    print(f"{nome} não está disponível nesta máquina.")
    if device == "cuda" and not cuda_dlls_disponiveis():
        print(
            "Bibliotecas CUDA 12 ausentes (cublas64_12.dll). "
            "Instale com:\n"
            "  pip install nvidia-cublas-cu12 nvidia-cudnn-cu12==9.*"
        )


def pedir_dispositivo() -> tuple[str, str]:
    dispositivos = listar_dispositivos()
    padrao = "cuda" if cuda_disponivel() else "cpu"

    print("\nDispositivos disponíveis:")
    for i, (codigo, nome, disponivel) in enumerate(dispositivos, start=1):
        status = "disponível" if disponivel else "indisponível"
        print(f"  {i}. {nome} ({status})")

    while True:
        escolha = input(
            "\nEscolha o dispositivo (número ou nome: cpu, cuda, vulkan) "
            f"(padrão: {padrao}): "
        ).strip().lower()

        if not escolha:
            return padrao, compute_type_padrao(padrao)

        if escolha.isdigit():
            indice = int(escolha)
            if 1 <= indice <= len(dispositivos):
                device, nome, disponivel = dispositivos[indice - 1]
                if not disponivel:
                    _mensagem_cuda_indisponivel(device, nome)
                    continue
                return device, compute_type_padrao(device)
            print("Número inválido.")
            continue

        nomes_validos = {codigo: (codigo, disp) for codigo, _, disp in dispositivos}
        if escolha in nomes_validos:
            device, disponivel = nomes_validos[escolha]
            if not disponivel:
                _mensagem_cuda_indisponivel(device, escolha.upper())
                continue
            return device, compute_type_padrao(device)

        print("Opção inválida. Use cpu, cuda, vulkan ou o número da lista.")


def pedir_caminho_audio() -> Path:
    while True:
        caminho = input("\nCaminho do arquivo de áudio: ").strip().strip('"').strip("'")
        if not caminho:
            print("Informe um caminho válido.")
            continue

        arquivo = Path(caminho).expanduser().resolve()
        if not arquivo.is_file():
            print(f"Arquivo não encontrado: {arquivo}")
            continue

        return arquivo


def pedir_modelo() -> str:
    print("\nModelos sugeridos:")
    for i, nome in enumerate(MODELOS_SUGERIDOS, start=1):
        print(f"  {i}. {nome}")

    while True:
        escolha = input(
            "\nDigite o nome do modelo ou o número da lista "
            f"(padrão: {MODELOS_SUGERIDOS[2]}): "
        ).strip()

        if not escolha:
            return MODELOS_SUGERIDOS[2]

        if escolha.isdigit():
            indice = int(escolha)
            if 1 <= indice <= len(MODELOS_SUGERIDOS):
                return MODELOS_SUGERIDOS[indice - 1]
            print("Número inválido.")
            continue

        return escolha


def pedir_opcional_int(pergunta: str) -> int | None:
    valor = input(f"{pergunta} (Enter para ignorar): ").strip()
    if not valor:
        return None
    if valor.isdigit():
        return int(valor)
    print("Valor inválido, ignorando.")
    return None


def formatar_tempo(segundos: float) -> str:
    if segundos < 60:
        return f"{segundos:.2f}s"
    minutos = int(segundos // 60)
    resto = segundos % 60
    return f"{minutos}m {resto:.2f}s"


def caminho_saida_txt(arquivo: Path) -> Path:
    return arquivo.with_name(f"{arquivo.stem}_transcricao_diarizada.txt")


def salvar_transcricao_txt(
    caminho: Path,
    linhas: list[str],
    cabecalho: list[str] | None = None,
) -> None:
    partes: list[str] = []
    if cabecalho:
        partes.extend(cabecalho)
        partes.append("")
    partes.extend(linhas)
    caminho.write_text("\n".join(partes), encoding="utf-8")


def liberar_gpu() -> None:
    gc.collect()
    if torch.cuda.is_available():
        torch.cuda.empty_cache()


_modelo_config: dict[str, str | int] = {}
_thread_local = threading.local()
_print_lock = threading.Lock()


def _inicializar_worker_whisperx() -> None:
    _thread_local.model = whisperx.load_model(
        str(_modelo_config["modelo"]),
        str(_modelo_config["device"]),
        compute_type=str(_modelo_config["compute_type"]),
    )


def _transcrever_parte_whisperx(
    args: tuple[int, int, float, object, int],
) -> tuple[int, str | None, list[dict]]:
    indice, total, offset, chunk, batch_size = args
    model = _thread_local.model

    with _print_lock:
        print(f"  Parte {indice + 1}/{total} iniciada ({offset:.1f}s)...")

    chunk_result = model.transcribe(chunk, batch_size=batch_size)
    idioma = chunk_result.get("language")
    segmentos = [
        {
            "start": seg["start"] + offset,
            "end": seg["end"] + offset,
            "text": seg.get("text", ""),
        }
        for seg in chunk_result.get("segments", [])
    ]

    with _print_lock:
        print(f"  Parte {indice + 1}/{total} concluída ({len(segmentos)} segmento(s)).")

    return indice, idioma, segmentos


def transcrever_partes_paralelo_whisperx(
    partes: list[tuple[float, object]],
    modelo: str,
    device: str,
    compute_type: str,
    batch_size: int,
    paralelismo: int,
) -> tuple[list[dict], str | None]:
    global _modelo_config
    _modelo_config = {
        "modelo": modelo,
        "device": device,
        "compute_type": compute_type,
    }

    tarefas = [
        (indice, len(partes), offset, chunk, batch_size)
        for indice, (offset, chunk) in enumerate(partes)
    ]

    resultados_por_parte: dict[int, tuple[str | None, list[dict]]] = {}

    with ThreadPoolExecutor(
        max_workers=paralelismo,
        initializer=_inicializar_worker_whisperx,
    ) as executor:
        futures = {
            executor.submit(_transcrever_parte_whisperx, tarefa): tarefa[0]
            for tarefa in tarefas
        }
        for future in as_completed(futures):
            indice, idioma, segmentos = future.result()
            resultados_por_parte[indice] = (idioma, segmentos)

    idioma = None
    segmentos_merged: list[dict] = []
    for indice in sorted(resultados_por_parte):
        chunk_idioma, segmentos = resultados_por_parte[indice]
        if idioma is None and chunk_idioma:
            idioma = chunk_idioma
        segmentos_merged.extend(segmentos)

    segmentos_merged.sort(key=lambda s: s["start"])
    return segmentos_merged, idioma


def imprimir_segmentos(segmentos: list[dict]) -> None:
    for segmento in segmentos:
        print(formatar_linha_segmento(segmento))


def formatar_linha_segmento(segmento: dict) -> str:
    speaker = segmento.get("speaker", "SPEAKER_?")
    inicio = segmento.get("start", 0.0)
    fim = segmento.get("end", 0.0)
    texto = segmento.get("text", "").strip()
    return f"[{inicio:6.2f}s -> {fim:6.2f}s] {speaker}: {texto}"


def main() -> int:
    print("=" * 50)
    print("  Transcrição + diarização — WhisperX")
    print("=" * 50)

    if not HF_TOKEN or HF_TOKEN == "hf_COLE_SEU_TOKEN_AQUI":
        print(
            "Defina HF_TOKEN no início de transcrever_x.py.\n"
            "Token em: https://huggingface.co/settings/tokens\n"
            "Aceite os termos em:\n"
            "  https://huggingface.co/pyannote/speaker-diarization-community-1"
        )
        return 1

    arquivo = pedir_caminho_audio()
    modelo = pedir_modelo()
    device, compute_type = pedir_dispositivo()
    min_speakers = pedir_opcional_int("Número mínimo de falantes")
    max_speakers = pedir_opcional_int("Número máximo de falantes")
    batch_size = batch_size_padrao(device)

    print(f"\nArquivo: {arquivo}")
    print(f"Modelo:  {modelo}")
    print(f"Device:  {device} ({compute_type})")
    print(f"Batch:   {batch_size}")

    tempos: dict[str, float] = {}
    inicio_total = time.perf_counter()

    print("\n[1/4] Carregando áudio...")
    inicio = time.perf_counter()
    audio = carregar_audio(str(arquivo))
    trechos_silencio, partes, _, paralelismo = preparar_partes_paralelo(
        audio, device
    )
    tempos["carregar_audio"] = time.perf_counter() - inicio
    threads_cpu = os.cpu_count() or 1
    print(
        f"Áudio carregado em {formatar_tempo(tempos['carregar_audio'])}.\n"
        f"  Trechos por silêncio: {len(trechos_silencio)}\n"
        f"  Partes para transcrição: {len(partes)}\n"
        f"  Paralelismo: {paralelismo} | Threads CPU: {threads_cpu}"
    )

    print("\n[2/4] Transcrevendo (paralelo)...")
    inicio = time.perf_counter()
    segmentos_merged, idioma = transcrever_partes_paralelo_whisperx(
        partes, modelo, device, compute_type, batch_size, paralelismo
    )
    resultado = {"segments": segmentos_merged, "language": idioma}
    tempos["transcrever"] = time.perf_counter() - inicio
    print(f"Transcrição em {formatar_tempo(tempos['transcrever'])}.")

    idioma = resultado.get("language", "?")
    print(f"Idioma detectado: {idioma}")

    liberar_gpu()

    print("\n[3/4] Alinhando palavras...")
    inicio = time.perf_counter()
    model_a, metadata = whisperx.load_align_model(
        language_code=idioma,
        device=device,
    )
    resultado = whisperx.align(
        resultado["segments"],
        model_a,
        metadata,
        audio,
        device,
        return_char_alignments=False,
    )
    tempos["alinhar"] = time.perf_counter() - inicio
    print(f"Alinhamento em {formatar_tempo(tempos['alinhar'])}.")

    liberar_gpu()
    del model_a

    print("\n[4/4] Diarizando falantes...")
    inicio = time.perf_counter()
    diarize_model = DiarizationPipeline(token=HF_TOKEN, device=device)
    kwargs_diarizacao: dict[str, int] = {}
    if min_speakers is not None:
        kwargs_diarizacao["min_speakers"] = min_speakers
    if max_speakers is not None:
        kwargs_diarizacao["max_speakers"] = max_speakers

    diarize_segments = diarize_model(audio, **kwargs_diarizacao)
    resultado = whisperx.assign_word_speakers(diarize_segments, resultado)
    tempos["diarizar"] = time.perf_counter() - inicio
    print(f"Diarização em {formatar_tempo(tempos['diarizar'])}.")

    liberar_gpu()
    del diarize_model

    segmentos = resultado.get("segments", [])
    tempo_total = time.perf_counter() - inicio_total

    print("\n" + "-" * 50)
    print(f"Segmentos: {len(segmentos)}")
    print("-" * 50)
    imprimir_segmentos(segmentos)

    linhas_txt = [formatar_linha_segmento(s) for s in segmentos]
    saida_txt = caminho_saida_txt(arquivo)
    salvar_transcricao_txt(
        saida_txt,
        linhas_txt,
        cabecalho=[
            f"Arquivo: {arquivo.name}",
            f"Modelo: {modelo}",
            f"Device: {device} ({compute_type})",
            f"Idioma: {idioma}",
            f"Segmentos: {len(segmentos)}",
            f"Partes (silêncio→agrupadas): {len(trechos_silencio)}→{len(partes)}",
            f"Paralelismo: {paralelismo}",
            f"Tempo total: {formatar_tempo(tempo_total)}",
        ],
    )

    print("\n" + "=" * 50)
    print(f"Transcrição salva em: {saida_txt}")
    print("Tempos por etapa:")
    for etapa, duracao in tempos.items():
        print(f"  {etapa:16s}: {formatar_tempo(duracao)}")
    print(f"\nTempo total: {formatar_tempo(tempo_total)}")
    print("=" * 50)

    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        print("\n\nInterrompido pelo usuário.")
        raise SystemExit(130)
    except RuntimeError as exc:
        if "cublas64_12.dll" in str(exc):
            print(
                "\nErro: bibliotecas CUDA 12 não encontradas.\n"
                "Instale e tente novamente:\n"
                "  pip install nvidia-cublas-cu12 nvidia-cudnn-cu12==9.*\n"
                "Ou instale o CUDA Toolkit 12.x da NVIDIA."
            )
            raise SystemExit(1) from exc
        raise
    except Exception as exc:
        mensagem = str(exc).lower()
        if "401" in mensagem or "403" in mensagem or "gated" in mensagem:
            print(
                "\nErro de autenticação Hugging Face.\n"
                "Verifique HF_TOKEN no script e aceite os termos do modelo:\n"
                "  https://huggingface.co/pyannote/speaker-diarization-community-1"
            )
            raise SystemExit(1) from exc
        raise
