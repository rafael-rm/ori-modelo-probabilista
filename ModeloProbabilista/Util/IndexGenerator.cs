using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using StopWord;

namespace ModeloProbabilista.Util;

public class IndexGeneretor
{
    private static Dictionary<string, double>? _idf = new(); // IDF por Termo
    private static Dictionary<string, List<string>>? _termsInDocs= new ();
    private static Dictionary<string, string>? _documents = new Dictionary<string, string>();

    public static Task GenerateIndex(Dictionary<string, string>? documents)
    {
        _documents = documents;
        var totalDocumentos = _documents.Count;
        var termoOcorrencias = new Dictionary<string, int>();

        // Contar o número de documentos em que cada termo aparece
        foreach (var doc in _documents)
        {
            var documento = doc.Value.Split(' ').Select(term => term.ToLower()).ToList();
            var termosUnicos = new HashSet<string>(documento);
            foreach (string termo in termosUnicos)
            {
                termoOcorrencias[termo] = termoOcorrencias.GetValueOrDefault(termo, 0) + 1;

                if (!_termsInDocs.ContainsKey(termo))
                {
                    _termsInDocs.Add(termo, new List<string> { doc.Key });
                }
                else
                {
                    _termsInDocs[termo].Add(doc.Key);
                }
            }
        }

        // Calcular o IDF para cada termo
        _idf = new Dictionary<string, double>();
        foreach (var kvp in termoOcorrencias)
        {
            var termo = kvp.Key;
            var ocorrencias = kvp.Value;
            _idf[termo] = Math.Log10((double)totalDocumentos / ocorrencias);
        }

        return Task.CompletedTask;
    }

    public static async Task StartIndex()
    {
        if (_idf.Count == 0 || _termsInDocs.Count == 0)
        {
            var projectDirectory = Directory.GetParent(Environment.CurrentDirectory)?.FullName;

            var pathtermsInDocs = projectDirectory + "\\ModeloProbabilista\\Data\\termsInDocs.json";
            var pathIdf = projectDirectory + "\\ModeloProbabilista\\Data\\idf.json";
            var documents = projectDirectory + "\\ModeloProbabilista\\Data\\documents.json";

            if (!File.Exists(pathtermsInDocs) || !File.Exists(pathIdf) || !File.Exists(documents) )
                await SaveIndexDisk();
            else
                await ReadIndexFromDisk();
        }
    }

    public static async Task SaveIndexDisk()
    {
        var projectDirectory = Directory.GetParent(Environment.CurrentDirectory)?.FullName;

        var pathFolderHtmls = projectDirectory + "\\ModeloProbabilista\\Data\\htmlFiles";
        var pathFolderIndex = projectDirectory + "\\ModeloProbabilista\\Data\\";

        var textTreated = await GetTreatedTextFiles(pathFolderHtmls);
        await GenerateIndex(textTreated);

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

        var tfidfPath = Path.Combine(pathFolderIndex, "termsInDocs.json");
        var json = JsonSerializer.Serialize(_termsInDocs, jsonOptions);
        await File.WriteAllTextAsync(tfidfPath, json);
        
        var documentsPath = Path.Combine(pathFolderIndex, "documents.json");
        json = JsonSerializer.Serialize(_documents, jsonOptions);
        await File.WriteAllTextAsync(tfidfPath, json);

        var idfPath = Path.Combine(pathFolderIndex, "idf.json");
        json = JsonSerializer.Serialize(_idf, jsonOptions);
        await File.WriteAllTextAsync(idfPath, json);
    }

    public static async Task ReadIndexFromDisk()
    {
        var projectDirectory = Directory.GetParent(Environment.CurrentDirectory)?.FullName;
        var pathFoldertermsInDocs = projectDirectory + "\\ModeloProbabilista\\Data\\termsInDocs.json";
        var pathFolderidf = projectDirectory + "\\ModeloProbabilista\\Data\\idf.json";
        var pathFolderDocuments = projectDirectory + "\\ModeloProbabilista\\Data\\documents.json";

        if (!File.Exists(pathFoldertermsInDocs) || !File.Exists(pathFolderidf)) await SaveIndexDisk();

        var termsInDocs = await File.ReadAllTextAsync(pathFoldertermsInDocs);
        var idfJson = await File.ReadAllTextAsync(pathFolderidf);
        var documentsJson = await File.ReadAllTextAsync(pathFolderDocuments);

        _termsInDocs = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(termsInDocs);
        _idf = JsonSerializer.Deserialize<Dictionary<string, double>>(idfJson);
        _documents = JsonSerializer.Deserialize<Dictionary<string, string>>(documentsJson);
    }
    
    public static async Task<Dictionary<string, double>> Search(string consulta)
    {
        await StartIndex();
        
        var scores = new Dictionary<string, double>();
        var consultaTerms = consulta.Split(' ').Select(term => term.ToLower()).ToList();

        foreach (var documento in _documents)
        {
            var docId = documento.Key;
            var docTerms = documento.Value.Split(' ').Select(term => term.ToLower()).ToList();

            var similaridade = 0.0;

            foreach (var term in consultaTerms)
            {
                var peso = _idf.GetValueOrDefault(term, 0);
                similaridade += peso * consultaTerms.Count(s => s == term) * docTerms.Count(s => s == term);
            }

            scores.Add(docId, similaridade);
        }

        return scores
            .Where(s => s.Value > 0)
            .OrderByDescending(s => s.Value)
            .ToDictionary(s => s.Key, s => s.Value);
    }
    
    public static async Task<Dictionary<string, double>> RelevanceFeedbackSearch(string consulta, List<string> documentosRelevantes)
    {
        var pesos = new Dictionary<string, double>();
        var consultaInicial = await Search(consulta);
        
        var scores = new Dictionary<string, double>();
        var consultaTerms = consulta.Split(' ').Select(term => term.ToLower()).ToList();
        
        // Calcula os pesos dos documentos Relevantes
        foreach (var term in consultaTerms)
        {
            var temp = _termsInDocs[term].Count(s => documentosRelevantes.Contains(s));
            
            var r = temp > 0 ? temp - 0.1 : temp; // quantidade de documentos relevantes em que o termo está presente
            var N = temp > 0 ? consultaInicial.Count + 1 : consultaInicial.Count; // Quantidade total de elementos
            var R = documentosRelevantes.Count; // Quantidade de documentos retornados como relevantes
            var n = _termsInDocs[term].Count; // Ocorrência do termo em todos os documentos
                
            var w1 =  r * (N - R - n + r);
            var w2 = (n - r) * (R - r);
            var w = Math.Log(w1 / w2, 10);
                
            pesos.Add(term, w);
        }
        
        // Calcula a similaridade para cada documento
        foreach (var documento in _documents)
        {
            var docId = documento.Key;
            var docTerms = documento.Value.Split(' ').Select(term => term.ToLower()).ToList();

            var similaridade = 0.0;

            foreach (var term in consultaTerms)
            {
                var peso = pesos[term];
                similaridade += peso * consultaTerms.Count(s => s == term) * docTerms.Count(s => s == term);
            }

            scores.Add(docId, similaridade);
        }
        

        return scores
            .Where(s => s.Value > 0)
            .OrderByDescending(s => s.Value)
            .ToDictionary(s => s.Key, s => s.Value);
    }

    public static async Task<Dictionary<string, string>?> GetPlainTextFiles(string pathFolder)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var filesHtml = Directory.GetFiles(pathFolder, "*.htm*");

        var filesText = new Dictionary<string, string>();
        foreach (var file in filesHtml)
        {
            var fileName = Path.GetFileName(file);
            var fileText = await File.ReadAllTextAsync(file, Encoding.GetEncoding(1252));
            filesText.TryAdd(fileName, fileText);

            Console.WriteLine($"[INFO] Arquivo {fileName} carregado com sucesso.");
        }

        Console.WriteLine($"[INFO] Total de arquivos carregados: {filesText.Count}");

        return filesText;
    }

    public static async Task<Dictionary<string, string>?> GetTreatedTextFiles(string filePath)
    {
        var dictionaryFiles = await GetPlainTextFiles(filePath);

        foreach (var file in dictionaryFiles)
        {
            var text = file.Value;
            text = Regex.Replace(text, "<.*?>", string.Empty);
            text = WebUtility.HtmlDecode(text);
            text = text.Replace("-", " ");
            text = Regex.Replace(text, @"\s+", " ");
            text = text.ToLower();
            text = Regex.Replace(text, @"[:;()\[\].,?!<>]", string.Empty);
            text = Regex.Replace(text, @"[^\p{L}\d\sÀ-ÖØ-öø-ÿ]", string.Empty);
            text = text.Normalize(NormalizationForm.FormD);
            text = new string(text.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                .ToArray());

            Console.WriteLine($"[INFO] Texto do arquivo {file.Key} limpo e normalizado com sucesso.");

            text = text.RemoveStopWords("pt");
            text = text.RemoveStopWords("en");

            Console.WriteLine($"[INFO] Removido stop-words do texto do arquivo {file.Key} com sucesso.");

            dictionaryFiles[file.Key] = text;
        }

        Console.WriteLine(
            $"[INFO] Todos os textos dos {dictionaryFiles.Count} foram limpos e normalizados com sucesso.");

        return dictionaryFiles;
    }

    public static Task<Dictionary<string, int>> GetRepetitionCount(string text)
    {
        var wordCount = new Dictionary<string, int>();

        Console.WriteLine("[INFO] Iniciando contagem das repetições de palavras.");

        foreach (var word in text.Split(' '))
        {
            if (string.IsNullOrEmpty(word))
                continue;

            if (wordCount.ContainsKey(word))
            {
                wordCount[word]++;
                Console.WriteLine($"[INFO] Palavra {word} somada no dicionario, contagem atual: {wordCount[word]}");
            }
            else
            {
                wordCount[word] = 1;
                Console.WriteLine($"[INFO] Palavra {word} adicionada ao dicionario, contagem atual: {wordCount[word]}");
            }
        }

        Console.WriteLine("[INFO] Contagem de repetições de palavras finalizada com sucesso.");

        return Task.FromResult(wordCount);
    }

    public static Task<Dictionary<string, int>> OrderRepetition(Dictionary<string, int> dictionary,
        bool descending = true)
    {
        Dictionary<string, int> orderedDictionary;

        orderedDictionary = descending
            ? dictionary.OrderByDescending(x => x.Value).ToDictionary(x => x.Key, x => x.Value)
            : dictionary.OrderBy(x => x.Value).ToDictionary(x => x.Key, x => x.Value);

        Console.WriteLine("[INFO] Dicionario ordenado com sucesso.");

        return Task.FromResult(orderedDictionary);
    }

    public static Task<string> GetAllTextDictonary(Dictionary<string, string> dictionary)
    {
        var allText = string.Empty;

        foreach (var file in dictionary) allText += file.Value;

        Console.WriteLine($"$[INFO] O texto de {dictionary.Count} arquivos foi mesclado com sucesso.");
        Console.WriteLine($"$[INFO] Caracteres totais: {allText.Length}");

        return Task.FromResult(allText);
    }
}