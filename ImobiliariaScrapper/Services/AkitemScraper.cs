using System.Globalization;
using System.Text.RegularExpressions;
using ImobiliariaScrapper.Models;
using Microsoft.Playwright;

namespace ImobiliariaScrapper.Services;

public class AkitemScraper : AbstractScraper, IScraper
{
    private static readonly string BaseDomain = "akitem" + "imoveis.com.br";

    public string NomeImobiliaria => "AkiTem";

    public AkitemScraper(FiltroConsulta filtro) : base(filtro) { }

    public string CriarUrl(int pagina)
    {
        string negocio = _filtro.Negocio == TipoNegocio.Venda ? "venda" : "aluguel";
        string cidade = _filtro.ApenasBauru ? "bauru" : "todas";
        string maxParam = _filtro.ValorMaximo > 0 ? $"&preco-max={_filtro.ValorMaximo.ToString(CultureInfo.InvariantCulture)}" : "";

        return $"https://{BaseDomain}/busca/{negocio}/casa/{cidade}?ordem=menor-preco{maxParam}&page={pagina}";
    }

    public async Task<IEnumerable<Imovel>> ExtrairImovelAsync()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = BROWSER_HEADLESS
        });

        var imoveisEncontrados = new List<Imovel>();
        int pagina = 1;
        bool temProximaPagina = true;

        while (temProximaPagina)
        {
            LogPagina(pagina);
            var page = await ConfigurarExtracao(CriarUrl(pagina), browser);

            await Task.Delay(DELAY_VALUE);

            var emptyState = await page.QuerySelectorAsync(".empty");
            if (emptyState != null)
            {
                LogFinal("sem mais imóveis nesta busca (empty state)");
                temProximaPagina = false;
                break;
            }

            var cards = await page.QuerySelectorAllAsync(".card-imovel");

            if (cards.Count == 0 || pagina >= 15)
            {
                LogFinal("sem mais imóveis nesta busca");
                temProximaPagina = false;
                break;
            }

            foreach (var card in cards)
            {
                // 1. Bairro
                var locElement = await card.QuerySelectorAsync("small");
                var locTexto = locElement != null ? await locElement.InnerTextAsync() : string.Empty;
                var bairroTexto = CleanBairro(locTexto);

                // 2. Link
                var linkElement = await card.QuerySelectorAsync("a.descricao, a[href*='/imovel/']");
                var href = linkElement != null ? await linkElement.GetAttributeAsync("href") : string.Empty;

                string linkCompleto = string.IsNullOrEmpty(href)
                    ? string.Empty
                    : (href.StartsWith("http") ? href : $"https://{BaseDomain}{href}");

                // 3. Preço
                string termoValor = _filtro.Negocio == TipoNegocio.Venda ? "Venda" : "Locação";
                var precoElement = await card.QuerySelectorAsync($".valor:has-text('{termoValor}')");
                var precoTexto = precoElement != null ? await precoElement.InnerTextAsync() : string.Empty;
                decimal preco = LimparEConverterPreco(precoTexto);

                // 4. Código
                var refElement = await card.QuerySelectorAsync(".ref");
                var codigoTexto = refElement != null ? (await refElement.InnerTextAsync()).Trim() : string.Empty;

                if (BairroEhDeInteresse(bairroTexto))
                {
                    imoveisEncontrados.Add(new Imovel
                    {
                        Bairro = bairroTexto,
                        Link = linkCompleto,
                        PrecoTotal = preco,
                        Preco = preco,
                        ValoresExtras = 0m,
                        DescricaoValoresExtras = string.Empty,
                        Imobiliaria = NomeImobiliaria,
                        Descricao = string.IsNullOrEmpty(codigoTexto) ? "Sem Código" : $"Cód {codigoTexto}",
                        DataConsulta = DateTime.Now
                    });
                }

                ImoveisEncontrados++;
            }

            LogProximaPagina();
            pagina++;
        }

        LogFinal("fim da mineração Aki Tem");
        LogImovelEncontrado(imoveisEncontrados);
        LogPulaLinha();

        return imoveisEncontrados;
    }

    private static string CleanBairro(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
        var partes = texto.Split(',');
        return partes[0].Trim();
    }

    private static decimal LimparEConverterPreco(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return 0m;

        var match = Regex.Match(texto, @"[\d\.]+\,\d{2}");
        if (match.Success)
        {
            var valorLimpo = match.Value.Replace(".", "").Replace(",", ".");
            if (decimal.TryParse(valorLimpo, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal valor))
            {
                return valor;
            }
        }

        return 0m;
    }
}
