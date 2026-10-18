using ImobiliariaScrapper.Models;
using Microsoft.Extensions.Logging;

namespace ImobiliariaScrapper.Services;

public class ScrapingOrchestrator(IEnumerable<IScraper> scrapers)
{
    private readonly IEnumerable<IScraper> _scrapers = scrapers;

    public async Task<List<Imovel>> ExecutarTodosAsync()
    {
        // Roda a mineração de todos os sites simultaneamente
        var tarefas = _scrapers.Select(s => s.ExtrairImovelAsync());
        var resultados = await Task.WhenAll(tarefas);

        // Achata a lista de listas em uma única lista final
        return [.. resultados.SelectMany(r => r)];
    }

    public async Task<List<Imovel>> ExecutarTodosEmFila()
    {
        var resultados = new List<Imovel>();
        foreach (var scraper in _scrapers)
        {
            try
            {
                var imoveis = await scraper.ExtrairImovelAsync();
                resultados.AddRange(imoveis);

            }
            catch (Exception e)
            {
                Console.WriteLine($"Erro ao executar o scraper {scraper.GetType().Name}: {e.Message}");
                continue;
            }
        }

        return resultados;
    }
}
