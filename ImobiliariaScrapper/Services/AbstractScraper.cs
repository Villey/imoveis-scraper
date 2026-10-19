using ImobiliariaScrapper.Models;
using Microsoft.Playwright;

namespace ImobiliariaScrapper.Services;

public abstract class AbstractScraper
{
    protected readonly FiltroConsulta _filtro;

    public AbstractScraper(FiltroConsulta filtro)
    {
        _filtro = filtro;
    }

    public const bool BROWSER_HEADLESS = true;
    public const int DELAY_VALUE = 250;
    public int ImoveisEncontrados { get; set; } = 0;

    public async Task<IPage> ConfigurarExtracao(string url, IBrowser browser)
    {
        Console.WriteLine($"URL: {url}");
        Console.WriteLine("====================================================================================================================");

        var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync(url, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded
        });

        return page;
    }

    public static bool BairroEhDeInteresse(string texto, bool apenasBauru)
    {
        if (apenasBauru) return true;

        if (string.IsNullOrWhiteSpace(texto)) return false;

        return !Utils.Bairros.BairrosExcluir.Any(b =>
            texto.Contains(b, StringComparison.OrdinalIgnoreCase));
    }

    public static void LogPagina(int pagina) => Console.WriteLine($"Processando página {pagina}.");
    public static void LogProximaPagina() => Console.WriteLine($"Próxima página...");
    public static void LogFinal(string? obs) => Console.WriteLine($"FINAL{(string.IsNullOrEmpty(obs) ? string.Empty : $": ({obs})")}.");
    public static void LogPulaLinha() => Console.WriteLine("\n\n");
    public void LogImovelEncontrado(IEnumerable<Imovel> imovel)
    {
        Console.WriteLine($"Imóveis encontrados: {ImoveisEncontrados}");
        Console.WriteLine($"Imóveis filtrados: {imovel.Count()}");
    }
}
