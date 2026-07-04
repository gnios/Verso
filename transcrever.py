#!/usr/bin/env python3
"""Script interativo simples para testar transcrição com faster-whisper."""

import os
import sys
import threading
import time
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path


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

    nvidia_base = _site_packages()
    if nvidia_base is None:
        return adicionados

    nvidia_base = nvidia_base / "nvidia"
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
from faster_whisper import WhisperModel
from faster_whisper.audio import decode_audio

from audio_silencio import SAMPLE_RATE, preparar_partes_paralelo


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
        return ctranslate2.get_cuda_device_count() > 0 and cuda_dlls_disponiveis()
    except Exception:
        return False


def listar_dispositivos() -> list[tuple[str, str, bool]]:
    gpu_detectada = False
    try:
        gpu_detectada = ctranslate2.get_cuda_device_count() > 0
    except Exception:
        pass

    cuda_ok = gpu_detectada and cuda_dlls_disponiveis()
    return [
        ("cpu", "CPU", True),
        ("cuda", "CUDA (NVIDIA)", cuda_ok),
        ("vulkan", "Vulkan", False),
    ]


def compute_type_padrao(device: str) -> str:
    if device == "cuda":
        return "float16"
    return "int8"


def pedir_dispositivo() -> tuple[str, str]:
    dispositivos = listar_dispositivos()
    disponiveis = [item for item in dispositivos if item[2]]
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
                    if device == "vulkan":
                        print(
                            "Vulkan não é suportado pelo faster-whisper/CTranslate2. "
                            "Use whisper.cpp para aceleração Vulkan."
                        )
                    else:
                        print(f"{nome} não está disponível nesta máquina.")
                        if device == "cuda" and not cuda_dlls_disponiveis():
                            print(
                                "Bibliotecas CUDA 12 ausentes (cublas64_12.dll). "
                                "Instale com:\n"
                                "  pip install nvidia-cublas-cu12 nvidia-cudnn-cu12==9.*"
                            )
                    continue
                return device, compute_type_padrao(device)
            print("Número inválido.")
            continue

        nomes_validos = {codigo: (codigo, disp) for codigo, _, disp in dispositivos}
        if escolha in nomes_validos:
            device, disponivel = nomes_validos[escolha]
            if not disponivel:
                if device == "vulkan":
                    print(
                        "Vulkan não é suportado pelo faster-whisper/CTranslate2. "
                        "Use whisper.cpp para aceleração Vulkan."
                    )
                else:
                    print(f"{escolha.upper()} não está disponível nesta máquina.")
                    if device == "cuda" and not cuda_dlls_disponiveis():
                        print(
                            "Bibliotecas CUDA 12 ausentes (cublas64_12.dll). "
                            "Instale com:\n"
                            "  pip install nvidia-cublas-cu12 nvidia-cudnn-cu12==9.*"
                        )
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


def formatar_tempo(segundos: float) -> str:
    if segundos < 60:
        return f"{segundos:.2f}s"
    minutos = int(segundos // 60)
    resto = segundos % 60
    return f"{minutos}m {resto:.2f}s"


def caminho_saida_txt(arquivo: Path) -> Path:
    return arquivo.with_name(f"{arquivo.stem}_transcricao.txt")


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


_modelo_config: dict[str, str] = {}
_thread_local = threading.local()
_print_lock = threading.Lock()


def _inicializar_worker_whisper() -> None:
    _thread_local.whisper = WhisperModel(
        _modelo_config["modelo"],
        device=_modelo_config["device"],
        compute_type=_modelo_config["compute_type"],
    )


def _transcrever_parte(args: tuple[int, int, float, object]) -> tuple[int, object | None, list[tuple[float, float, str]]]:
    indice, total, offset, chunk = args
    whisper = _thread_local.whisper

    with _print_lock:
        print(f"  Parte {indice + 1}/{total} iniciada ({offset:.1f}s)...")

    segs, chunk_info = whisper.transcribe(chunk, beam_size=5)
    resultados = [
        (seg.start + offset, seg.end + offset, seg.text)
        for seg in segs
    ]

    with _print_lock:
        for inicio, fim, texto in resultados:
            print(f"[{inicio:6.2f}s -> {fim:6.2f}s] {texto.strip()}")
        print(f"  Parte {indice + 1}/{total} concluída.")

    return indice, chunk_info, resultados


def transcrever_partes_paralelo(
    partes: list[tuple[float, object]],
    modelo: str,
    device: str,
    compute_type: str,
    paralelismo: int,
) -> tuple[list[tuple[float, float, str]], object | None]:
    global _modelo_config
    _modelo_config = {
        "modelo": modelo,
        "device": device,
        "compute_type": compute_type,
    }

    tarefas = [
        (indice, len(partes), offset, chunk)
        for indice, (offset, chunk) in enumerate(partes)
    ]

    resultados_por_parte: dict[int, tuple[object | None, list[tuple[float, float, str]]]] = {}

    with ThreadPoolExecutor(
        max_workers=paralelismo,
        initializer=_inicializar_worker_whisper,
    ) as executor:
        futures = {
            executor.submit(_transcrever_parte, tarefa): tarefa[0]
            for tarefa in tarefas
        }
        for future in as_completed(futures):
            indice, chunk_info, resultados = future.result()
            resultados_por_parte[indice] = (chunk_info, resultados)

    info = None
    segmentos: list[tuple[float, float, str]] = []
    for indice in sorted(resultados_por_parte):
        chunk_info, resultados = resultados_por_parte[indice]
        if info is None and chunk_info is not None:
            info = chunk_info
        segmentos.extend(resultados)

    segmentos.sort(key=lambda item: item[0])
    return segmentos, info


def main() -> int:
    print("=" * 50)
    print("  Teste de transcrição — faster-whisper")
    print("=" * 50)

    arquivo = pedir_caminho_audio()
    modelo = pedir_modelo()
    device, compute_type = pedir_dispositivo()

    print(f"\nArquivo: {arquivo}")
    print(f"Modelo:  {modelo}")
    print(f"Device:  {device} ({compute_type})")

    print("\nCarregando áudio...")
    inicio_audio = time.perf_counter()
    audio_completo = decode_audio(str(arquivo), sampling_rate=SAMPLE_RATE)
    trechos_silencio, partes, _, paralelismo = preparar_partes_paralelo(
        audio_completo, device
    )
    tempo_audio = time.perf_counter() - inicio_audio
    threads_cpu = os.cpu_count() or 1
    print(
        f"Áudio carregado em {formatar_tempo(tempo_audio)}.\n"
        f"  Trechos por silêncio: {len(trechos_silencio)}\n"
        f"  Partes para transcrição: {len(partes)}\n"
        f"  Paralelismo: {paralelismo} | Threads CPU: {threads_cpu}"
    )

    print("\nTranscrevendo (paralelo)...")
    inicio_transcricao = time.perf_counter()
    segmentos_raw, info = transcrever_partes_paralelo(
        partes, modelo, device, compute_type, paralelismo
    )
    segmentos = [
        type("_Seg", (), {"start": s, "end": e, "text": t})()
        for s, e, t in segmentos_raw
    ]
    tempo_transcricao = time.perf_counter() - inicio_transcricao

    print("\n" + "-" * 50)
    if info:
        print(
            f"Idioma detectado: {info.language} "
            f"(probabilidade: {info.language_probability:.2%})"
        )
    else:
        print("Idioma detectado: ?")
    print(f"Duração do áudio: {len(audio_completo) / SAMPLE_RATE:.2f}s")
    print("-" * 50)

    linhas_txt = [
        f"[{segmento.start:6.2f}s -> {segmento.end:6.2f}s] {segmento.text.strip()}"
        for segmento in segmentos
    ]
    saida_txt = caminho_saida_txt(arquivo)
    salvar_transcricao_txt(
        saida_txt,
        linhas_txt,
        cabecalho=[
            f"Arquivo: {arquivo.name}",
            f"Modelo: {modelo}",
            f"Device: {device} ({compute_type})",
            f"Idioma: {info.language if info else '?'} ({info.language_probability:.2%})" if info else "Idioma: ?",
            f"Duração: {len(audio_completo) / SAMPLE_RATE:.2f}s",
            f"Partes (silêncio→agrupadas): {len(trechos_silencio)}→{len(partes)}",
            f"Paralelismo: {paralelismo}",
            f"Tempo de transcrição: {formatar_tempo(tempo_transcricao)}",
        ],
    )

    print("\n" + "=" * 50)
    print(f"Transcrição salva em: {saida_txt}")
    print(f"Tempo de transcrição: {formatar_tempo(tempo_transcricao)}")
    print(f"Tempo total (áudio + transcrição): {formatar_tempo(tempo_audio + tempo_transcricao)}")
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
