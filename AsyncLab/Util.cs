using System.Security.Cryptography;
using System.Text;

public static class Util
{
    // Sanitiza campos para CSV simples
    public static string San(string s) => (s ?? "").Replace("\"", "").Trim();

    // Constrói um salt determinístico a partir do IBGE + um “pepper” fixo (opcional)
    public static byte[] BuildSalt(string ibge)
    {
        // Inclui um pepper fixo para fortalecer; mantém determinismo.
        const string pepper = "PBKDF2_DEMOSYNC_V1";
        return Encoding.UTF8.GetBytes($"{ibge}|{pepper}");
    }

    public static string DeriveHashHex(string password, byte[] salt, int iterations, int sizeBytes)
    {
        using var pbk = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
        var bytes = pbk.GetBytes(sizeBytes);
        return ToHex(bytes);
    }

    public static List<Municipio> CarregarMunicipios(string caminhoArquivo)
    {
        Console.WriteLine($"Lendo e parseando o CSV {caminhoArquivo}...");


        var linhas = File.ReadAllLines(caminhoArquivo, Encoding.UTF8);
        if (linhas.Length == 0) return new List<Municipio>();


        int startIndex = 0;
        if (linhas[0].IndexOf("IBGE", StringComparison.OrdinalIgnoreCase) >= 0 ||
            linhas[0].IndexOf("UF", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            startIndex = 1;
        }

        var municipios = new List<Municipio>(linhas.Length - startIndex);

        for (int i = startIndex; i < linhas.Length; i++)
        {
            var linha = (linhas[i] ?? "").Trim();
            if (string.IsNullOrWhiteSpace(linha)) continue;

            var parts = linha.Split(';');
            if (parts.Length < 5) continue;

            municipios.Add(new Municipio
            {
                Tom = Util.San(parts[0]),
                Ibge = Util.San(parts[1]),
                NomeTom = Util.San(parts[2]),
                NomeIbge = Util.San(parts[3]),
                Uf = Util.San(parts[4]).ToUpperInvariant()
            });
        }

        return municipios;
    }

    public static void CompararEGerarDiferencas(List<Municipio> municipiosOld, List<Municipio> municipiosNovo)
    {
        return;
    }

    public static void BaixarArquivo(string url, string path)
    {
        using var client = new HttpClient();
        var data = client.GetByteArrayAsync(url).Result;
        File.WriteAllBytes(path, data);
    }   

    public static string ToHex(byte[] data)
    {
        var sb = new StringBuilder(data.Length * 2);
        foreach (var b in data) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
