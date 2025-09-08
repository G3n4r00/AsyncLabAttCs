using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;



static async Task Main(string[] args)
{

    // =================== Configuração ===================
    // Iterações elevadas deixam o trabalho realmente pesado (CPU-bound).
    Console.OutputEncoding = System.Text.Encoding.UTF8;
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
    string tempOldCsvPath = Path.Combine(baseDir, "municipios_old.csv");
    string outRoot = Path.Combine(baseDir, OUT_DIR_NAME);

    Console.WriteLine("Baixando CSV de municípios (Receita Federal) ...");

    List<Municipio> municipios = new List<Municipio>();


    if (File.Exists(tempCsvPath))
    {
        Console.WriteLine("municipio.csv ja existe, baixando da receita para comparar...");
        File.Move(tempCsvPath, tempOldCsvPath, true);

        Util.BaixarArquivo(CSV_URL, tempCsvPath);

        var municiosOld = Util.CarregarMunicipios(tempOldCsvPath);
        municipios = Util.CarregarMunicipios(tempCsvPath);

        Util.CompararEGerarDiferencas(municiosOld, municipios);
    }
    else
    {
        Util.BaixarArquivo(CSV_URL, tempCsvPath);
        municipios = Util.CarregarMunicipios(tempCsvPath);
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

        string outPath = Path.Combine(outRoot, $"municipios_hash_{uf}.dat");
        using (var fs = new FileStream(outPath, FileMode.Create))
        using (var bw = new BinaryWriter(fs, Encoding.UTF8))
        {
            var resultados = new ConcurrentBag<(Municipio municipio, string hash)>();


            Parallel.ForEach(listaUf, m =>
            {
                string password = m.ToConcatenatedString();
                byte[] salt = Util.BuildSalt(m.Ibge);
                string hashHex = Util.DeriveHashHex(password, salt, PBKDF2_ITERATIONS, HASH_BYTES);

                // Thread-safe collection
                resultados.Add((m, hashHex));
            });

            var resultadosOrdenados = resultados
                .OrderBy(r => r.municipio.NomePreferido, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Cabeçalho com metadados
            bw.Write("MUNHASH"); // Assinatura do arquivo
            bw.Write(1); // Versão do formato
            bw.Write(uf); // UF
            bw.Write(DateTime.Now.ToBinary()); // Data de geração
            bw.Write(resultadosOrdenados.Count); // Número de registros


            int count = 0;
            // Ordena resultados para saída consistente
            foreach (var (municipio, hash) in resultados)
            {

                bw.Write(municipio.Tom ?? string.Empty);
                bw.Write(municipio.Ibge ?? string.Empty);
                bw.Write(municipio.NomeTom ?? string.Empty);
                bw.Write(municipio.NomeIbge ?? string.Empty);
                bw.Write(municipio.Uf ?? string.Empty);
                bw.Write(hash);

                count++;
                if (count % 50 == 0)
                {
                    Console.WriteLine($"  Parcial: {count}/{resultadosOrdenados.Count} municípios processados para UF {uf}");
                }
            }
        }
        ;

        swUf.Stop();

        //UF em binário 
        var fileInfo = new FileInfo(outPath);
        Console.WriteLine($"UF {uf} concluída. Arquivo binário: {outPath}");
        Console.WriteLine($"Tamanho: {fileInfo.Length:N0} bytes ({fileInfo.Length / 1024.0:F1} KB)");
        Console.WriteLine($"Tempo: {FormatTempo(swUf.ElapsedMilliseconds)}");
    }

    sw.Stop();
    Console.WriteLine();
    Console.WriteLine("===== RESUMO =====");
    Console.WriteLine($"UFs geradas: {ufsOrdenadas.Count}");
    Console.WriteLine($"Pasta de saída: {outRoot}");
    Console.WriteLine($"Tempo total: {FormatTempo(sw.ElapsedMilliseconds)} ({sw.Elapsed})");

    // =================== SISTEMA DE PESQUISA ===================

    Console.WriteLine("\nProcessamento concluído! Iniciando sistema de pesquisa...");

    Console.WriteLine("Pressione ENTER para iniciar o sistema de pesquisa ou ESC para sair...");
    var key = Console.ReadKey(true);

    if (key.Key == ConsoleKey.Escape)
    {
        Console.WriteLine("Programa encerrado pelo usuário.");
        return;
    }

    // Inicia o sistema de pesquisa
    SistemaPesquisaMunicipios.InicializarSistemaPesquisa(outRoot);
    await SistemaPesquisaMunicipios.IniciarLoopPesquisa();
}

Main(args).GetAwaiter().GetResult();