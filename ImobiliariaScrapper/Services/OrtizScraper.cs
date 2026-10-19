using System.Globalization;
using System.Text.RegularExpressions;
using ImobiliariaScrapper.Models;
using Microsoft.Playwright;

namespace ImobiliariaScrapper.Services;

public class OrtizScraper : AbstractScraper, IScraper
{
    private static readonly string BaseDomain = "ortiz" + "imoveis.com.br";

    public string NomeImobiliaria => "Ortiz";

    public OrtizScraper(FiltroConsulta filtro) : base(filtro) { }

    public string CriarUrl(int pagina)
    {
        string negocio = _filtro.Negocio == TipoNegocio.Venda ? "venda" : "aluguel";
        string escopo = _filtro.ApenasBauru ? "sp/bauru" : "";
        string maxParam = $"&valor_max={_filtro.ValorMaximo}";
        string pageParam = pagina > 1 ? $"&pagina={pagina}" : "";

        return $"https://{BaseDomain}/imoveis/{negocio}/casa/{escopo}?quartos=2{maxParam}&ordem=preco-crescente{pageParam}";
    }

    public async Task<IEnumerable<Imovel>> ExtrairImovelAsync()
    {
        var imoveisEncontrados = new List<Imovel>();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions 
        { 
            Headless = BROWSER_HEADLESS 
        });

        int pagina = 1;
        bool temProximaPagina = true;

        while (temProximaPagina)
        {
            LogPagina(pagina);
            var page = await ConfigurarExtracao(CriarUrl(pagina), browser);
            await Task.Delay(DELAY_VALUE);

            var mensagemVazia = await page.Locator("div.card.card-body.empty").IsVisibleAsync();
            if (mensagemVazia || pagina >= 10)
            {
                LogFinal("sem mais imóveis nesta busca");
                temProximaPagina = false;
                break;
            }

            var cards = await page.QuerySelectorAllAsync("div.card-imovel");
            if (cards.Count == 0)
            {
                LogFinal("sem mais imóveis nesta busca");
                temProximaPagina = false;
                break;
            }

            foreach (var card in cards)
            {
                var bairroEl = await card.QuerySelectorAsync("div.local");
                var bairroTextoRaw = bairroEl != null ? await bairroEl.TextContentAsync() : "";
                var bairroTexto = ExtrairApenasBairro(bairroTextoRaw);

                var linkEl = await card.QuerySelectorAsync("a.descricao, a[href*='/imovel/']");
                var href = linkEl != null ? await linkEl.GetAttributeAsync("href") : "";
                string linkCompleto = string.IsNullOrEmpty(href) ? "" : (href.StartsWith("http") ? href : $"https://{BaseDomain}{href}");

                var precoEl = await card.QuerySelectorAsync("div.valor");
                var precoTexto = precoEl != null ? await precoEl.TextContentAsync() : "";
                decimal preco = ExtrairPreco(precoTexto);

                var descricaoEl = await card.QuerySelectorAsync("div.tipo");
                var descricaoTexto = ConcatenaDescricao(
                    descricaoEl != null ? await descricaoEl.TextContentAsync() : "", 
                    bairroTextoRaw
                );

                if (BairroEhDeInteresse(bairroTexto, _filtro.ApenasBauru))
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
                        Descricao = descricaoTexto,
                        DataConsulta = DateTime.Now
                    });
                }
                ImoveisEncontrados++;
            }

            var botaoProximo = page.Locator("a.page-link[rel='next']").First;
            if (await botaoProximo.CountAsync() == 0)
            {
                LogFinal("última página alcançada");
                temProximaPagina = false;
            }
            else
            {
                LogProximaPagina();
                pagina++;
            }
        }

        LogFinal("fim da mineração Ortiz");
        LogImovelEncontrado(imoveisEncontrados);
        LogPulaLinha();
        return imoveisEncontrados;
    }

    private static string ExtrairApenasBairro(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
        var textoSplit = texto.Split(",");
        if (textoSplit.Length == 1) return texto.Trim();
        if (textoSplit.Length == 2) return textoSplit[0].Trim();
        if (textoSplit.Length == 3)
        {
            var subTextoSplit = textoSplit[1].Split(" - ");
            return subTextoSplit[^1].Trim();
        }
        return string.Empty;
    }

    private static decimal ExtrairPreco(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return 0m;
        var match = Regex.Match(texto, @"([\d.,]+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            string valorLimpo = match.Groups[1].Value.Replace(".", "").Replace(",", ".");
            if (decimal.TryParse(valorLimpo, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal valor))
            {
                return valor;
            }
        }
        return 0m;
    }

    private static string ConcatenaDescricao(string? descricao, string? bairro)
    {
        if (string.IsNullOrWhiteSpace(descricao)) return bairro?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(bairro)) return descricao?.Trim() ?? string.Empty;
        return $"{descricao.Trim()} - {bairro.Trim()}";
    }
}
