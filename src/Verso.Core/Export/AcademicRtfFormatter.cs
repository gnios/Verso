using System.Text;
using Verso.Core.Data.Entities;

namespace Verso.Core.Export;

/// <summary>
/// Gera um documento RTF no padrão de transcrição acadêmica usado no Brasil:
/// formatação compatível com a ABNT NBR 14724 (margens 3 cm à esquerda/superior,
/// 2 cm à direita/inferior, Times New Roman 12, espaçamento 1,5), bloco de
/// identificação, convenções de transcrição declaradas e falas com locutor e
/// minutagem. A ABNT não tem norma específica de transcrição — o modelo segue o
/// consenso de uso em apêndices de trabalhos acadêmicos.
/// </summary>
public static class AcademicRtfFormatter
{
    // A4 em twips; margens: 1 cm = 567 twips
    private const string PageSetup =
        @"\paperw11907\paperh16840\margl1701\margt1701\margr1134\margb1134\widowctrl";

    private const string Spacing15 = @"\sl360\slmult1";

    public static string Build(Transcription transcription)
    {
        var sb = new StringBuilder();
        sb.Append(@"{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\froman\fprq2 Times New Roman;}}");
        sb.Append(PageSetup);
        sb.Append('\n');

        WriteTitle(sb, transcription.Title);
        WriteIdentification(sb, transcription);
        WriteConventions(sb);
        WriteBody(sb, transcription);
        WriteFooter(sb);

        sb.Append('}');
        return sb.ToString();
    }

    private static void WriteTitle(StringBuilder sb, string title)
    {
        Para(sb, @"\qc", Bold("TRANSCRIÇÃO"));
        if (!string.IsNullOrWhiteSpace(title))
        {
            Para(sb, @"\qc", Esc(title));
        }

        BlankLine(sb);
    }

    private static void WriteIdentification(StringBuilder sb, Transcription transcription)
    {
        Para(sb, "", Bold("IDENTIFICAÇÃO"));
        Info(sb, "Título", transcription.Title);
        Info(sb, "Arquivo de mídia", FileName(transcription.MediaFilePath));
        Info(sb, "Duração", FormatDurationLabel(transcription.DurationSeconds));
        Info(sb, "Data da transcrição", transcription.CreatedAt.ToString("dd/MM/yyyy"));
        Info(sb, "Idioma", LanguageLabel(transcription.Language));
        Info(sb, "Método", "Transcrição automática por reconhecimento de fala, com revisão no aplicativo Verso");
        BlankLine(sb);
    }

    private static void WriteConventions(StringBuilder sb)
    {
        Para(sb, "", Bold("CONVENÇÕES DE TRANSCRIÇÃO"));
        Bullet(sb, "[Locutor] — [hh:mm:ss]: identifica quem fala e o tempo inicial do trecho no áudio.");
        Bullet(sb, "Texto entre parênteses: palavra ou trecho de audição incerta na revisão.");
        Bullet(sb, "Marcações entre colchetes como [inaudível] ou [pausa]: observações do transcritor, quando presentes.");
        Bullet(sb, "Demais sinais de pontuação foram inferidos a partir da fala na transcrição literal.");
        BlankLine(sb);
    }

    private static void WriteBody(StringBuilder sb, Transcription transcription)
    {
        Para(sb, "", Bold("TRANSCRIÇÃO"));

        string? previousSpeaker = null;
        foreach (var segment in transcription.Segments.OrderBy(s => s.SortOrder))
        {
            var speaker = segment.Speaker?.Name;
            if (previousSpeaker is not null && speaker != previousSpeaker)
            {
                BlankLine(sb);
            }

            var timestamp = TranscriptionTextFormatter.FormatTxtTimestamp(segment.StartSeconds);
            var header = string.IsNullOrEmpty(speaker)
                ? $"[LOCUTOR] — [{timestamp}]"
                : $"[{speaker}] — [{timestamp}]";
            Para(sb, "", Bold(header));
            Para(sb, @"\fi708", Esc(segment.Text.Trim()));
            previousSpeaker = speaker;
        }

        BlankLine(sb);
    }

    private static void WriteFooter(StringBuilder sb)
    {
        Para(sb, "", Run(@"\fs20\i",
            "Documento gerado pelo Verso a partir de reconhecimento de fala automático; confira o conteúdo junto ao áudio antes de citar. " +
            "Formatação compatível com a ABNT NBR 14724 (margens, fonte e espaçamento). Para uso acadêmico, inclua como apêndice do trabalho, " +
            "declare as convenções na metodologia e cite os trechos conforme a NBR 10520."));
    }

    private static void Para(StringBuilder sb, string props, string runRtf)
    {
        sb.Append(@"\pard").Append(Spacing15).Append(@"\fi0").Append(props).Append(' ')
            .Append(runRtf).Append(@"\par").Append('\n');
    }

    private static void BlankLine(StringBuilder sb) => Para(sb, "", "");

    private static void Info(StringBuilder sb, string label, string value) =>
        Para(sb, "", $"{Bold(label + ":")} {Esc(value)}");

    private static void Bullet(StringBuilder sb, string text) =>
        Para(sb, @"\fi-360\li720", $"{Esc("•")}  {Esc(text)}");

    private static string Bold(string text) => $"{{\\b {Esc(text)}}}";

    private static string Run(string props, string text) => $"{{{props} {Esc(text)}}}";

    private static string FileName(string? path) =>
        string.IsNullOrWhiteSpace(path) ? "—" : Path.GetFileName(path);

    private static string LanguageLabel(string language) =>
        language.Equals("pt", StringComparison.OrdinalIgnoreCase) ? "Português (Brasil)" : language;

    private static string FormatDurationLabel(double seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours} h {duration.Minutes:00} min";
        }

        return duration.TotalMinutes >= 1
            ? $"{(int)duration.TotalMinutes} min {duration.Seconds:00} s"
            : $"{duration.TotalSeconds:0} s";
    }

    private static string Esc(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case '{': sb.Append(@"\{"); break;
                case '}': sb.Append(@"\}"); break;
                case '\r': break;
                case '\n': sb.Append(@"\line "); break;
                default:
                    if (c > 127)
                    {
                        sb.Append(@"\u").Append((short)c).Append('?');
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.ToString();
    }
}
