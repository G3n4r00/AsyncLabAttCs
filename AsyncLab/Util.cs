using AsyncLab;
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

    private static bool MunicipiosIguais(Municipio m1, Municipio m2)
    {
        return m1.Tom == m2.Tom &&
               m1.Ibge == m2.Ibge &&
               m1.NomeTom == m2.NomeTom &&
               m1.NomeIbge == m2.NomeIbge &&
               m1.Uf == m2.Uf;
    }

    public static void SalvarDiferencas(List<DiferencaMunicipio> diferencas)
    {
        if (diferencas.Count == 0)
        {
            Console.WriteLine("Nenhuma diferença encontrada!");
            return;
        }

        string caminhoArquivo = Path.Combine(Directory.GetCurrentDirectory(), "municipios_alt.csv");

        using (var sw = new StreamWriter(caminhoArquivo, false, Encoding.UTF8))
        {
            sw.WriteLine("TipoMudanca;IBGE;TOM;NomeTOM;NomeIBGE;UF;IBGE_Antigo;NomeTOM_Antigo;NomeIBGE_Antigo");

            foreach (var diff in diferencas)
            {
                var m = diff.Municipio;
                var mAntigo = diff.MunicipioAntigo;

                if (diff.TipoMudanca == "ALTERADO")
                {
                    sw.WriteLine($"{diff.TipoMudanca};{m.Ibge};{m.Tom};{m.NomeTom};{m.NomeIbge};{m.Uf};{mAntigo.Ibge};{mAntigo.NomeTom};{mAntigo.NomeIbge}");
                }
                else
                {
                    sw.WriteLine($"{diff.TipoMudanca};{m.Ibge};{m.Tom};{m.NomeTom};{m.NomeIbge};{m.Uf};;;");
                }
            }
        }

        Console.WriteLine($"{diferencas.Count} diferenças salvas em: {caminhoArquivo}");
    }


    public static void CompararEGerarDiferencas(List<Municipio> municipiosOld, List<Municipio> municipiosNovo)
    {
        var dictOld = municipiosOld.ToDictionary(m => m.Ibge, m => m);
        var dictNovo = municipiosNovo.ToDictionary(m => m.Ibge, m => m);

        
        var diferencas = new List<DiferencaMunicipio>();
        var processados = new HashSet<string>(); // Para evitar processar o mesmo IBGE duas vezes, melhora perf

        foreach (var kvp in dictNovo)
        {
            processados.Add(kvp.Key);

            if (dictOld.TryGetValue(kvp.Key, out var municipioOld))
            {
                // Existe nos dois
                if (!MunicipiosIguais(municipioOld, kvp.Value))
                {
                    diferencas.Add(new DiferencaMunicipio
                    {
                        MunicipioAntigo = municipioOld,
                        Municipio = kvp.Value,
                        TipoMudanca = "ALTERADO"
                    });
                }
                // Se são iguais, não faz nada
            }
            else
            {
                // Só existe no novo
                diferencas.Add(new DiferencaMunicipio
                {
                    Municipio = kvp.Value,
                    TipoMudanca = "ADICIONADO"
                });
            }
        }

        
        foreach (var kvp in dictOld)
        {
            if (!processados.Contains(kvp.Key))
            {
                // Só existe no antigo
                diferencas.Add(new DiferencaMunicipio
                {
                    Municipio = kvp.Value,
                    TipoMudanca = "REMOVIDO"
                });
            }
        }
        SalvarDiferencas(diferencas);
    }

    public static void BaixarArquivo(string url, string path)
    {
        using var client = new HttpClient();
        var data = client.GetByteArrayAsync(url).Result;
        File.WriteAllBytes(path, data);
    }

    public static List<Municipio> LerArquivoBinarioComMetadados(string path)
    {
        var municipios = new List<Municipio>();

        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
        using (var br = new BinaryReader(fs, Encoding.UTF8))
        {
            // Lê cabeçalho
            string assinatura = br.ReadString();
            if (assinatura != "MUNHASH")
                throw new InvalidDataException("Arquivo não é um arquivo de municípios válido");

            int versao = br.ReadInt32();
            string uf = br.ReadString();
            DateTime dataGeracao = DateTime.FromBinary(br.ReadInt64());
            int count = br.ReadInt32();

            Console.WriteLine($"Arquivo: UF {uf}, {count} municípios, gerado em {dataGeracao:dd/MM/yyyy HH:mm:ss}");

            for (int i = 0; i < count; i++)
            {
                var municipio = new Municipio
                {
                    Tom = br.ReadString(),
                    Ibge = br.ReadString(),
                    NomeTom = br.ReadString(),
                    NomeIbge = br.ReadString(),
                    Uf = br.ReadString(),
                    Hash = br.ReadString()
                };
                municipios.Add(municipio);
            }
        }

        return municipios;
    }

    public static string ToHex(byte[] data)
    {
        var sb = new StringBuilder(data.Length * 2);
        foreach (var b in data) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
