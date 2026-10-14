namespace Aion2DpsMeter.Core.Analysis;

/// <summary>
/// Entpackt einen rohen LZ4-Block (nicht das LZ4-Rahmenformat) nach der öffentlichen Spezifikation
/// „LZ4 Block Format Description“ (github.com/lz4/lz4, doc/lz4_Block_format.md). Selbst geschrieben,
/// damit Core ohne Fremdpakete bleibt. Nur Entpacken, kein Packen.
/// </summary>
public static class Lz4BlockDecoder
{
    private const int MinMatch = 4;

    /// <summary>
    /// Entpackt <paramref name="input"/> nach <paramref name="output"/>. Gibt die Anzahl geschriebener Bytes
    /// zurück oder -1, wenn der Block fehlerhaft ist oder nicht in <paramref name="output"/> passt.
    /// </summary>
    public static int Decode(ReadOnlySpan<byte> input, Span<byte> output)
    {
        var ip = 0;
        var op = 0;
        while (true)
        {
            if (ip >= input.Length) return -1;
            var token = input[ip++];

            // Literale
            var literalLength = token >> 4;
            if (literalLength == 15 && !ReadExtraLength(input, ref ip, ref literalLength)) return -1;
            if (literalLength > input.Length - ip || literalLength > output.Length - op) return -1;
            input.Slice(ip, literalLength).CopyTo(output[op..]);
            ip += literalLength;
            op += literalLength;

            // Die letzte Sequenz besteht nur aus Literalen.
            if (ip == input.Length) return op;

            // Rückverweis
            if (input.Length - ip < 2) return -1;
            var offset = input[ip] | (input[ip + 1] << 8);
            ip += 2;
            if (offset == 0 || offset > op) return -1;

            var matchLength = token & 0x0F;
            if (matchLength == 15 && !ReadExtraLength(input, ref ip, ref matchLength)) return -1;
            matchLength += MinMatch;
            if (matchLength > output.Length - op) return -1;

            // Byteweise kopieren: Quelle und Ziel dürfen sich überlappen (Wiederholungen).
            var from = op - offset;
            for (var i = 0; i < matchLength; i++) output[op++] = output[from + i];
        }
    }

    private static bool ReadExtraLength(ReadOnlySpan<byte> input, ref int ip, ref int length)
    {
        byte b;
        do
        {
            if (ip >= input.Length) return false;
            b = input[ip++];
            length += b;
            if (length > 1 << 30) return false;
        }
        while (b == 255);

        return true;
    }
}
