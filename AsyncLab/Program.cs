using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;

// =================== Configuração ===================
// Iterações elevadas deixam o trabalho realmente pesado (CPU-bound).
const int PBKDF2_ITERATIONS = 50_000;
const int HASH_BYTES = 32; // 32 = 256 bits
const string CSV_URL = "https://www.gov.br/receitafederal/dados/municipios.csv";
const string OUT_DIR_NAME = "mun_hash_por_uf";

string FormatTempo(long ms)
{
    var ts = TimeSpan.FromMilliseconds(ms);
    return $"{ts.Minutes}m {ts.Seconds}s {ts.Milliseconds}ms";
}

var sw = Stopwatch.StartNew();

string baseDir = Directory.GetCurrentDirectory();
string tempCsvPath = Path.Combine(baseDir, "municipios.csv");
string outRoot = Path.Combine(baseDir, OUT_DIR_NAME);

Console.WriteLine("Baixando CSV de municípios (Receita Federal) ...");

if (File.Exists(tempCsvPath))
{
    Console.WriteLine("municipio.csv ja existe");
}
else
{
    using (var wc = new WebClient())
    {
        wc.Encoding = Encoding.UTF8; // ajuste para ISO-8859-1 se necessário
        wc.DownloadFile(CSV_URL, tempCsvPath);
    }
}

   

Console.WriteLine("Lendo e parseando o CSV ...");
var linhas = File.ReadAllLines(tempCsvPath, Encoding.UTF8);
if (linhas.Length == 0)
{
    Console.WriteLine("Arquivo CSV vazio.");
    return;
}

int startIndex = 0;
if (linhas[0].IndexOf("IBGE", StringComparison.OrdinalIgnoreCase) >= 0 ||
    linhas[0].IndexOf("UF", StringComparison.OrdinalIgnoreCase) >= 0)
{
    startIndex = 1; // pula cabeçalho
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

Console.WriteLine($"Registros lidos: {municipios.Count}");

// Grupo por UF
var porUf = new Dictionary<string, List<Municipio>>(StringComparer.OrdinalIgnoreCase);
foreach (var m in municipios)
{
    if (!porUf.ContainsKey(m.Uf))
        porUf[m.Uf] = new List<Municipio>();
    porUf[m.Uf].Add(m);
}

// Ordena as UFs alfabeticamente e ignora a UF "EX"
var ufsOrdenadas = porUf.Keys
    .Where(uf => !string.Equals(uf, "EX", StringComparison.OrdinalIgnoreCase))
    .OrderBy(uf => uf, StringComparer.OrdinalIgnoreCase)
    .ToList();

// Gera saída
Directory.CreateDirectory(outRoot);
Console.WriteLine("Calculando hash por município e gerando arquivos por UF ...");

foreach (var uf in ufsOrdenadas)
{
    var listaUf = porUf[uf];

    // Ordena por Nome preferido para saída consistente
    listaUf.Sort((a, b) => string.Compare(a.NomePreferido, b.NomePreferido, StringComparison.OrdinalIgnoreCase));

    Console.WriteLine($"Processando UF: {uf} ({listaUf.Count} municípios)");
    var swUf = Stopwatch.StartNew();
    string outPath = Path.Combine(outRoot, $"municipios_hash_{uf}.csv");
    using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None))
    using (var swOut = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
    {
        swOut.WriteLine("TOM;IBGE;NomeTOM;NomeIBGE;UF;Hash");

        var listaJson = new List<object>();
        int count = 0;

        Parallel.ForEach(listaUf, m =>
        {
            // Password: todos os campos concatenados; Salt: IBGE + “pepper” fixo (opcional)
            string password = m.ToConcatenatedString();
            byte[] salt = Util.BuildSalt(m.Ibge);

            // Trabalho pesado real (PBKDF2/SHA-256)
            string hashHex = Util.DeriveHashHex(password, salt, PBKDF2_ITERATIONS, HASH_BYTES);

            //Caso ocorra Overflow Exception rodar o programa de novo => Acontece por estarmos calculando o hash aquele tanto de vezes
            swOut.WriteLine($"{m.Tom};{m.Ibge};{m.NomeTom};{m.NomeIbge};{m.Uf};{hashHex}");

            listaJson.Add(new
            {
                m.Tom,
                m.Ibge,
                m.NomeTom,
                m.NomeIbge,
                m.Uf,
                Hash = hashHex
            });

            count++;
            if (count % 50 == 0 || count == listaUf.Count)
            {
                Console.WriteLine($"  Parcial: {count}/{listaUf.Count} municípios processados para UF {uf} | Tempo parcial: {FormatTempo(swUf.ElapsedMilliseconds)}");
            }
        });

        // Salva JSON
        string jsonPath = Path.Combine(outRoot, $"municipios_hash_{uf}.json");
        var json = await Task.Run(()=> JsonSerializer.Serialize(listaJson, new JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllTextAsync(jsonPath, json, Encoding.UTF8);
        swUf.Stop();
        Console.WriteLine($"UF {uf} concluída. Arquivos gerados: CSV e JSON. Tempo total UF: {FormatTempo(swUf.ElapsedMilliseconds)}");
    }
}

sw.Stop();
Console.WriteLine();
Console.WriteLine("===== RESUMO =====");
Console.WriteLine($"UFs geradas: {ufsOrdenadas.Count}");
Console.WriteLine($"Pasta de saída: {outRoot}");
Console.WriteLine($"Tempo total: {FormatTempo(sw.ElapsedMilliseconds)} ({sw.Elapsed})");