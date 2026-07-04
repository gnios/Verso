"""Divide áudio em partes apenas nos silêncios (nunca por tempo fixo)."""

from __future__ import annotations

import os

import numpy as np

SAMPLE_RATE = 16000
MIN_SILENCE_MS = 500
SILENCE_DB = -40.0
FRAME_MS = 30
MIN_SPEECH_MS = 100


def dividir_por_silencio(
    audio: np.ndarray,
    sr: int = SAMPLE_RATE,
    min_silence_ms: int = MIN_SILENCE_MS,
    silence_db: float = SILENCE_DB,
    frame_ms: int = FRAME_MS,
    min_speech_ms: int = MIN_SPEECH_MS,
) -> list[tuple[float, np.ndarray]]:
    """Retorna lista de (offset_segundos, chunk) cortada somente em silêncio."""
    if audio.size == 0:
        return []

    frame_size = max(1, int(sr * frame_ms / 1000))
    hop = max(1, frame_size // 2)
    min_silence_frames = max(1, int(min_silence_ms / frame_ms))
    min_speech_samples = int(sr * min_speech_ms / 1000)

    peak_db = 20 * np.log10(float(np.max(np.abs(audio))) + 1e-10)
    threshold = peak_db + silence_db

    n_frames = max(1, (len(audio) - frame_size) // hop + 1)
    silent = np.empty(n_frames, dtype=bool)
    for i in range(n_frames):
        start = i * hop
        frame = audio[start : start + frame_size]
        rms = float(np.sqrt(np.mean(frame**2) + 1e-10))
        db = 20 * np.log10(rms + 1e-10)
        silent[i] = db < threshold

    partes: list[tuple[float, np.ndarray]] = []
    speech_start = 0
    silence_run = 0

    for i in range(n_frames):
        if silent[i]:
            silence_run += 1
            continue

        if silence_run >= min_silence_frames and i * hop > speech_start:
            end_sample = max(speech_start, (i - silence_run) * hop)
            chunk = audio[speech_start:end_sample]
            if len(chunk) >= min_speech_samples:
                partes.append((speech_start / sr, chunk))
            speech_start = i * hop

        silence_run = 0

    if len(audio) - speech_start >= min_speech_samples:
        partes.append((speech_start / sr, audio[speech_start:]))

    if not partes:
        partes.append((0.0, audio))

    return partes


def calcular_limites_paralelos(dispositivo: str) -> tuple[int, int]:
    """Retorna (max_partes, paralelismo) conforme hardware e dispositivo."""
    threads = os.cpu_count() or 1

    if dispositivo in ("cuda", "vulkan"):
        paralelismo = max(1, min(2, threads // 4))
        max_partes = max(paralelismo, min(4, (threads + 1) // 2))
        return max_partes, paralelismo

    cpu_paralelismo = max(1, threads)
    return cpu_paralelismo, cpu_paralelismo


def agrupar_partes(
    trechos: list[tuple[float, np.ndarray]],
    max_partes: int,
) -> list[tuple[float, np.ndarray]]:
    """Agrupa trechos adjacentes (silêncio) em até max_partes balanceadas por duração."""
    if len(trechos) <= max_partes or max_partes <= 1:
        return trechos

    total_samples = sum(len(chunk) for _, chunk in trechos)
    target_samples = total_samples / max_partes

    agrupadas: list[tuple[float, np.ndarray]] = []
    buffer_chunks: list[np.ndarray] = []
    offset_inicio = trechos[0][0]

    for offset, samples in trechos:
        if not buffer_chunks:
            offset_inicio = offset

        buffer_chunks.append(samples)
        buffer_len = sum(len(chunk) for chunk in buffer_chunks)
        ultima_parte = len(agrupadas) >= max_partes - 1
        atingiu_alvo = buffer_len >= target_samples

        if not ultima_parte and atingiu_alvo:
            agrupadas.append((offset_inicio, np.concatenate(buffer_chunks)))
            buffer_chunks = []

    if buffer_chunks:
        agrupadas.append((offset_inicio, np.concatenate(buffer_chunks)))

    return agrupadas


def preparar_partes_paralelo(
    audio: np.ndarray,
    dispositivo: str,
    sr: int = SAMPLE_RATE,
) -> tuple[list[tuple[float, np.ndarray]], list[tuple[float, np.ndarray]], int, int]:
    """Detecta silêncios, agrupa partes e calcula paralelismo."""
    trechos = dividir_por_silencio(audio, sr=sr)
    max_partes, paralelismo = calcular_limites_paralelos(dispositivo)
    partes = agrupar_partes(trechos, max_partes)
    return trechos, partes, max_partes, paralelismo
