"""Semeia uma transcrição de teste no SQLite de dev (bin/Debug) para o smoke E2E do Editor.

IMPORTANTE: o Microsoft.Data.Sqlite grava Guid como TEXT MAIÚSCULO
(Guid.ToString().ToUpperInvariant()). Comparado via sonda .tmp-probe:
typeof(Id)=text, raw='E2E11111-1111-1111-1111-111111111111'.
Texto minúsculo ou BLOB não casam em WHERE Id = @guid (SQLite é case-sensitive).
"""
import math
import os
import sqlite3
import struct
import sys
import wave

DB = sys.argv[1] if len(sys.argv) > 1 else "src/Verso.App/bin/Debug/net10.0/data/verso.db"


def gid(guid_text: str) -> str:
    return guid_text.upper()


TID = "e2e11111-1111-1111-1111-111111111111"
SP1 = "e2e22222-2222-2222-2222-222222222221"
SP2 = "e2e22222-2222-2222-2222-222222222222"
SG1 = "e2e33333-3333-3333-3333-333333333331"
SG2 = "e2e33333-3333-3333-3333-333333333332"
SG3 = "e2e33333-3333-3333-3333-333333333333"

conn = sqlite3.connect(DB)
cur = conn.cursor()

# Limpa QUALQUER resto de seeds anteriores (texto minúsculo, maiúsculo ou blob).
for table, column in (
    ("Segments", "TranscriptionId"),
    ("Speakers", "TranscriptionId"),
    ("TranscriptionTag", "TranscriptionId"),
    ("Transcriptions", "Id"),
):
    cur.execute(
        f"DELETE FROM {table} WHERE {column} IN (?, ?, ?)",
        (TID, gid(TID), sqlite3.Binary(__import__("uuid").UUID(TID).bytes_le)),
    )

# WAV de teste em data/media/<guid-sem-traços>/ — exigência do LocalMediaServer
# (MediaSchemeHandler.IsUnderMediaDirectory rejeita paths fora de data/media/).
data_root = os.path.dirname(os.path.abspath(DB))
media_dir = os.path.join(data_root, "media", TID.replace("-", ""))
os.makedirs(media_dir, exist_ok=True)
wav_path = os.path.join(media_dir, "e2e-audio.wav")
with wave.open(wav_path, "w") as wav:
    wav.setnchannels(1)
    wav.setsampwidth(2)
    wav.setframerate(16000)
    wav.writeframes(
        b"".join(
            struct.pack("<h", int(12000 * math.sin(2 * math.pi * 440 * t / 16000)))
            for t in range(16000 * 30)
        )
    )

# Status: InProgress=0, Done=1, Error=2 | Engine: Whisper=0, Parakeet=1
# CreatedAt no formato EF: 'yyyy-MM-dd HH:mm:ss.ffff'
cur.execute(
    """
    INSERT INTO Transcriptions (Id, Title, Icon, Status, ErrorMessage, CreatedAt,
        DurationSeconds, MediaFilePath, Language, Quality, Engine, ParakeetModel,
        Device, SpeakerMode, ProcessingDurationSeconds)
    VALUES (?, ?, ?, 1, NULL, '2026-11-01 10:00:00.0000', 30.0, ?, 'pt', 0, 1, 1, 0, 0, 752.0)
    """,
    (gid(TID), "E2E — Entrevista de teste", "🎙️", wav_path),
)

cur.execute(
    "INSERT INTO Speakers (Id, TranscriptionId, Name, ColorHex) VALUES (?, ?, ?, ?)",
    (gid(SP1), gid(TID), "Entrevistador", "#2eaadc"),
)
cur.execute(
    "INSERT INTO Speakers (Id, TranscriptionId, Name, ColorHex) VALUES (?, ?, ?, ?)",
    (gid(SP2), gid(TID), "Dra. Ana", "#4dab6f"),
)

segments = [
    (SG1, 0.0, 6.2, "Boa tarde, doutora. Obrigada por aceitar a entrevista.", 0, SP1),
    (SG2, 6.2, 14.8, "Boa tarde. É um prazer contribuir com a pesquisa.", 1, SP2),
    (SG3, 14.8, 28.0, "Como a senhora começou a se interessar por políticas públicas?", 2, SP1),
]
for segment_id, start, end, text, order, speaker_id in segments:
    cur.execute(
        "INSERT INTO Segments (Id, TranscriptionId, StartSeconds, EndSeconds, Text, SortOrder, SpeakerId)"
        " VALUES (?, ?, ?, ?, ?, ?, ?)",
        (gid(segment_id), gid(TID), start, end, text, order, gid(speaker_id)),
    )

conn.commit()
count = cur.execute("SELECT COUNT(*) FROM Segments WHERE TranscriptionId = ?", (gid(TID),)).fetchone()[0]
conn.close()
print(f"OK: transcrição de teste semeada como TEXT maiúsculo ({count} segmentos) — id {TID}")
