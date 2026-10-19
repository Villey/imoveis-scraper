using System.Globalization;
using System.Text.RegularExpressions;
using ImobiliariaScrapper.Models;
using Microsoft.Playwright;

namespace ImobiliariaScrapper.Services;

public class GuerreiroScraper : AbstractScraper, IScraper
{
    public string NomeImobiliaria => "Guerreiro";

    public GuerreiroScraper(FiltroConsulta filtro) : base(filtro)
    {
    }

    public string CriarUrl()
    {
        string dominio = "www.guerreroimoveis.com.br";
        string finalidade = $"&finalidade={(_filtro.Negocio == TipoNegocio.Venda ? "V" : "L")}";
        string valorFinalStr = $"&valorfinal={_filtro.ValorMaximo}";
        string coda = "&min-value=&max-value=&min-value=&max-value=&garagem=&bairro=&min-value=&max-value=&min-value2=&max-value2=";

        return $"https://{dominio}/site/top/pesquisar?tipoimovel=9{finalidade}&valorinicial=R%24+500{valorFinalStr}{coda}";
    }

    public async Task<IEnumerable<Imovel>> ExtrairImovelAsync()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = BROWSER_HEADLESS });

        var imoveisEncontrados = new List<Imovel>();

        LogPagina(1);
        var page = await ConfigurarExtracao(CriarUrl(), browser);
        
        await Task.Delay(DELAY_VALUE);
        await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight);");
        
        await Task.Delay(DELAY_VALUE);
        var cards = await page.QuerySelectorAllAsync("#listLayout .homeya-box");
        
        if (cards.Count == 0)
        {
            cards = await page.QuerySelectorAllAsync(".homeya-box");
        }

        if (cards.Count == 0)
        {
            LogFinal("nenhum imóvel encontrado nesta busca");
            LogImovelEncontrado(imoveisEncontrados);
            LogPulaLinha();
            return imoveisEncontrados;
        }

        foreach (var card in cards)
        {
            var bairroEl = await card.QuerySelectorAsync("a.link, .h7 a");
            var bairroTexto = bairroEl != null ? (await bairroEl.InnerTextAsync()).Trim() : string.Empty;
            var linkEl = await card.QuerySelectorAsync("a[href*='/detalhe/']");
            var href = linkEl != null ? await linkEl.GetAttributeAsync("href") : string.Empty;
            string linkCompleto = string.IsNullOrEmpty(href) ? string.Empty : (href.StartsWith("http") ? href : "https://www.guerreroimoveis.com.br");
            var precoEl = await card.QuerySelectorAsync("h6");
            var precoTexto = precoEl != null ? await precoEl.InnerTextAsync() : string.Empty;
            decimal preco = LimparEConverterPreco(precoTexto);
            
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
                    Descricao = string.Empty,
                    DataConsulta = DateTime.Now
                });
            }
            ImoveisEncontrados++;
        }
        LogFinal("fim da mineração Guerreiro");
        LogImovelEncontrado(imoveisEncontrados);
        LogPulaLinha();
        return imoveisEncontrados;
    }
    private static decimal LimparEConverterPreco(string ? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return 0m;
        var match = Regex.Match(texto, @"[\d.]+,\d{2}");
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