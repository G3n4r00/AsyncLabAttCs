using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public static class SistemaPesquisaMunicipios
{
    // Dicionário em memória para cache dos dados por UF
    private static readonly Dictionary<string, List<Municipio>> _cacheMunicipios = new();
    private static string _diretorioArquivos = "";

    // =================== MÉTODO DE INICIALIZAÇÃO ===================
    public static void InicializarSistemaPesquisa(string diretorioArquivos)
    {
        _diretorioArquivos = diretorioArquivos;
        Console.WriteLine("\n=================== SISTEMA DE PESQUISA DE MUNICÍPIOS ===================");
        Console.WriteLine("Sistema inicializado! Você pode pesquisar por:");
        Console.WriteLine("UF completa (ex: SP, RJ, MG)");
        Console.WriteLine("Código IBGE ou TOM (ex: 3550308, 71072)");
        Console.WriteLine("Parte do nome do município (ex: São, Santos, Campinas)");
        Console.WriteLine("\nComandos especiais:");
        Console.WriteLine("'sair' ou 'exit' - Encerra o programa");
        Console.WriteLine("'limpar' ou 'clear' - Limpa a tela");
        Console.WriteLine("'cache' - Mostra status do cache em memória");
        Console.WriteLine("'ajuda' ou 'help' - Mostra esta ajuda");
        Console.WriteLine("=======================================================================\n");
    }

    // =================== MÉTODO PARA LEITURA ===================
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

    // =================== LOOP PRINCIPAL DE INTERAÇÃO ===================
    public static async Task IniciarLoopPesquisa()
    {
        while (true)
        {
            Console.Write("Digite sua pesquisa: ");
            string? input = Console.ReadLine()?.Trim();

            if (string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine("Por favor, digite algo para pesquisar.\n");
                continue;
            }

            // Processamento de comandos especiais
            switch (input.ToLowerInvariant())
            {
                case "sair":
                case "exit":
                    Console.WriteLine("Encerrando o sistema de pesquisa. Até logo!");
                    return;

                case "limpar":
                case "clear":
                    Console.Clear();
                    InicializarSistemaPesquisa(_diretorioArquivos);
                    continue;

                case "cache":
                    MostrarStatusCache();
                    continue;

                case "ajuda":
                case "help":
                    MostrarAjuda();
                    continue;
            }

            var sw = Stopwatch.StartNew();

            try
            {
                await ExecutarPesquisa(input);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro durante a pesquisa: {ex.Message}");
            }

            sw.Stop();
            Console.WriteLine($"Pesquisa executada em {sw.ElapsedMilliseconds}ms\n");
        }
    }

    // =================== LÓGICA PRINCIPAL DE PESQUISA ===================
    private static async Task ExecutarPesquisa(string termo)
    {
        Console.WriteLine($"Pesquisando por: '{termo}'...\n");

        // ESTRATÉGIA 1: Verificar se é uma UF válida (2 caracteres, só letras)
        if (termo.Length == 2 && termo.All(char.IsLetter))
        {
            await PesquisarPorUf(termo.ToUpperInvariant());
            return;
        }

        // ESTRATÉGIA 2: Verificar se é um código numérico (IBGE ou TOM)
        if (termo.All(char.IsDigit) && termo.Length >= 4)
        {
            await PesquisarPorCodigo(termo);
            return;
        }

        // ESTRATÉGIA 3: Pesquisa por nome (mais complexa - busca em todos os arquivos)
        await PesquisarPorNome(termo);
    }

    // =================== PESQUISA POR UF ===================
    private static async Task PesquisarPorUf(string uf)
    {
        Console.WriteLine($"Buscando todos os municípios da UF: {uf}");

        try
        {
            // Carrega os municípios da UF (com cache)
            var municipios = await CarregarMunicipiosUf(uf);

            if (municipios == null || !municipios.Any())
            {
                Console.WriteLine($"Nenhum município encontrado para a UF '{uf}'. Verifique se existe o arquivo correspondente.");
                return;
            }

            Console.WriteLine($"Encontrados {municipios.Count} municípios em {uf}:\n");

            
            ExibirResultadosMunicipios(municipios, limitarExibicao: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao pesquisar UF '{uf}': {ex.Message}");
        }
    }

    // =================== PESQUISA POR CÓDIGO ===================
    private static async Task PesquisarPorCodigo(string codigo)
    {
        Console.WriteLine($"Buscando município com código: {codigo}");

        // Lista para armazenar matches encontrados
        var municipiosEncontrados = new List<Municipio>();

        // Busca em todos os arquivos de UF disponíveis
        var arquivosUf = Directory.GetFiles(_diretorioArquivos, "municipios_hash_*.dat");

        foreach (string arquivoPath in arquivosUf)
        {
            try
            {
                // Extrai a UF do nome do arquivo
                string nomeArquivo = Path.GetFileNameWithoutExtension(arquivoPath);
                string uf = nomeArquivo.Replace("municipios_hash_", "");

                // Carrega municípios desta UF
                var municipiosUf = await CarregarMunicipiosUf(uf);
                if (municipiosUf == null) continue;

                // Busca por código IBGE ou TOM
                var matches = municipiosUf.Where(m =>
                    m.Ibge.Equals(codigo, StringComparison.OrdinalIgnoreCase) ||
                    m.Tom.Equals(codigo, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                municipiosEncontrados.AddRange(matches);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao processar arquivo {arquivoPath}: {ex.Message}");
            }
        }

        if (!municipiosEncontrados.Any())
        {
            Console.WriteLine($"Nenhum município encontrado com o código '{codigo}'.");
            return;
        }

        Console.WriteLine($"Encontrado(s) {municipiosEncontrados.Count} município(s) com código '{codigo}':\n");
        ExibirResultadosMunicipios(municipiosEncontrados, limitarExibicao: false);
    }

    // =================== PESQUISA POR NOME ===================
    private static async Task PesquisarPorNome(string termoBusca)
    {
        Console.WriteLine($"Buscando municípios que contenham: '{termoBusca}'");

        var municipiosEncontrados = new List<Municipio>();
        var arquivosUf = Directory.GetFiles(_diretorioArquivos, "municipios_hash_*.dat");

        // Busca em todos os arquivos de UF
        foreach (string arquivoPath in arquivosUf)
        {
            try
            {
                string nomeArquivo = Path.GetFileNameWithoutExtension(arquivoPath);
                string uf = nomeArquivo.Replace("municipios_hash_", "");

                var municipiosUf = await CarregarMunicipiosUf(uf);
                if (municipiosUf == null) continue;

                // Busca case-insensitive em ambos os nomes (TOM e IBGE)
                var matches = municipiosUf.Where(m =>
                    (m.NomeTom?.Contains(termoBusca, StringComparison.OrdinalIgnoreCase) == true) ||
                    (m.NomeIbge?.Contains(termoBusca, StringComparison.OrdinalIgnoreCase) == true))
                    .ToList();

                municipiosEncontrados.AddRange(matches);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao processar arquivo {arquivoPath}: {ex.Message}");
            }
        }

        municipiosEncontrados = municipiosEncontrados
            .OrderBy(m => m.Uf)
            .ThenBy(m => m.NomeTom)
            .ToList();

        if (!municipiosEncontrados.Any())
        {
            Console.WriteLine($"Nenhum município encontrado contendo '{termoBusca}' no nome.");
            return;
        }

        Console.WriteLine($"Encontrados {municipiosEncontrados.Count} municípios contendo '{termoBusca}':\n");
        ExibirResultadosMunicipios(municipiosEncontrados, limitarExibicao: true);
    }

    // =================== CARREGAMENTO COM CACHE ===================
    private static async Task<List<Municipio>?> CarregarMunicipiosUf(string uf)
    {
        // Verifica se já está em cache
        if (_cacheMunicipios.ContainsKey(uf))
        {
            Console.WriteLine($"Dados da UF {uf} carregados do cache.");
            return _cacheMunicipios[uf];
        }

        // Constrói o caminho do arquivo
        string caminhoArquivo = Path.Combine(_diretorioArquivos, $"municipios_hash_{uf}.dat");

        if (!File.Exists(caminhoArquivo))
        {
            return null;
        }

        try
        {
            Console.WriteLine($"Carregando dados da UF {uf} do disco...");

            var municipios = await Task.Run(() => LerArquivoBinarioComMetadados(caminhoArquivo));

            _cacheMunicipios[uf] = municipios;

            return municipios;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao carregar arquivo da UF {uf}: {ex.Message}");
            return null;
        }
    }

    // =================== EXIBIÇÃO DE RESULTADOS ===================
    private static void ExibirResultadosMunicipios(List<Municipio> municipios, bool limitarExibicao)
    {
        const int LIMITE_EXIBICAO = 20;

        int totalExibir = limitarExibicao && municipios.Count > LIMITE_EXIBICAO
            ? LIMITE_EXIBICAO
            : municipios.Count;

        Console.WriteLine("┌─────────────────────────────────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│ UF │   IBGE   │   TOM   │              NOME MUNICÍPIO              │    HASH (8 chars)    │");
        Console.WriteLine("├─────────────────────────────────────────────────────────────────────────────────────────────┤");

        //cada município
        for (int i = 0; i < totalExibir; i++)
        {
            var m = municipios[i];
            string nome = m.NomeTom ?? m.NomeIbge ?? "N/A";

            // Limita o nome a 38 caracteres para não quebrar a tabela
            if (nome.Length > 38)
                nome = nome.Substring(0, 35) + "...";

            string hashPreview = m.Hash?.Length >= 8 ? m.Hash.Substring(0, 8) : m.Hash ?? "N/A";

            Console.WriteLine($"│ {m.Uf,-2} │ {m.Ibge,-8} │ {m.Tom,-7} │ {nome,-38} │ {hashPreview,-8}...       │");
        }

        Console.WriteLine("└─────────────────────────────────────────────────────────────────────────────────────────────┘");

        // Se limitou a exibição, informa quantos foram omitidos
        if (limitarExibicao && municipios.Count > LIMITE_EXIBICAO)
        {
            int omitidos = municipios.Count - LIMITE_EXIBICAO;
            Console.WriteLine($"... e mais {omitidos} município(s) não exibido(s). Digite um termo mais específico para refinar a busca.");
        }
    }

    // =================== MÉTODOS AUXILIARES ===================

    private static void MostrarStatusCache()
    {
        Console.WriteLine("\nSTATUS DO CACHE EM MEMÓRIA:");
        Console.WriteLine($"UFs carregadas: {_cacheMunicipios.Count}");

        if (_cacheMunicipios.Any())
        {
            int totalMunicipios = _cacheMunicipios.Values.Sum(list => list.Count);
            Console.WriteLine($"Total de municípios em cache: {totalMunicipios:N0}");

            foreach (var kvp in _cacheMunicipios.OrderBy(x => x.Key))
            {
                Console.WriteLine($"  • {kvp.Key}: {kvp.Value.Count:N0} municípios");
            }
        }
        else
        {
            Console.WriteLine("Nenhuma UF carregada ainda.");
        }
        Console.WriteLine();
    }

    private static void MostrarAjuda()
    {
        Console.WriteLine("\nAJUDA DO SISTEMA DE PESQUISA:");
        Console.WriteLine("\nTIPOS DE PESQUISA:");
        Console.WriteLine("Por UF (2 letras): SP, RJ, MG, etc.");
        Console.WriteLine("Por código: 3550308 (IBGE), 71072 (TOM)");
        Console.WriteLine("Por nome: São Paulo, Santos, parte do nome");

        Console.WriteLine("\nCOMANDOS ESPECIAIS:");
        Console.WriteLine("sair/exit - Encerra o programa");
        Console.WriteLine("limpar/clear - Limpa a tela");
        Console.WriteLine("cache - Mostra dados carregados em memória");
        Console.WriteLine("ajuda/help - Esta ajuda");

        Console.WriteLine("\nDICAS:");
        Console.WriteLine("A pesquisa ignora maiúsculas/minúsculas");
        Console.WriteLine("Resultados são limitados a 20 itens para UF/nome");
        Console.WriteLine("Dados são mantidos em cache para consultas mais rápidas");
        Console.WriteLine("Use termos mais específicos se muitos resultados aparecerem");
        Console.WriteLine();
    }
}
