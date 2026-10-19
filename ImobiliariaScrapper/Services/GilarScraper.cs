using System.Globalization;
using System.Text.RegularExpressions;
using ImobiliariaScrapper.Models;
using Microsoft.Playwright;

namespace ImobiliariaScrapper.Services;

public class GilarScraper : AbstractScraper, IScraper
{
    private static readonly string BaseDomain = "gilar.com.br";

    public GilarScraper(FiltroConsulta filtro) : base(filtro) { }

    public string NomeImobiliaria => "Gilar";

    public string CriarUrl(int pagina)
    {
        string negociacao = _filtro.Negocio == TipoNegocio.Venda ? "Comprar" : "Alugar";
        string cidadeParam = _filtro.ApenasBauru ? "&CidadeId=11" : "";
        string precoParam = $"&IntervaloPreco=0%2C{_filtro.ValorMaximo}";

        string url = $"https://www.{BaseDomain}/Imovel/Lista?Negociacao={negociacao}&IdTipoIdSubtipo=R_2{precoParam}{cidadeParam}&Dormitorios=2&Ordem=2";

        if (pagina > 1)
        {
            url += $"&pagina={pagina}";
        }

        return url;
    }

    public async Task<IEnumerable<Imovel>> ExtrairImovelAsync()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = BROWSER_HEADLESS });

        var imoveisEncontrados = new List<Imovel>();
        int pagina = 1;
        bool temProximaPagina = true;

        while (temProximaPagina)
        {
            LogPagina(pagina);
            var page = await ConfigurarExtracao(CriarUrl(pagina), browser);

            await Task.Delay(DELAY_VALUE);
            await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight);");

            var mensagemVazia = await page.QuerySelectorAsync(".text-subtitle:has-text('Nenhum imóvel localizado')");
            var naoEncontrado = await page.Locator("p:has-text('Nenhum imóvel encontrado')").IsVisibleAsync();

            if (mensagemVazia is not null || naoEncontrado || pagina >= 10)
            {
                LogFinal("mensagem vazia");
                temProximaPagina = false;
                break;
            }

            await page.WaitForSelectorAsync(".homelengo-box");
            var cards = await page.QuerySelectorAllAsync(".homelengo-box");

            foreach (var card in cards)
            {
                // 1. Extrai o Bairro
                var bairroElement = await card.QuerySelectorAsync(".archive-top .bottom");
                var bairroTexto = bairroElement != null ? await bairroElement.InnerTextAsync() : string.Empty;

                // 2. Extrai o Link do imóvel
                var linkElement = await card.QuerySelectorAsync("a.images-group");
                var href = linkElement != null ? await linkElement.GetAttributeAsync("href") : string.Empty;
                var linkCompleto = string.IsNullOrEmpty(href) ? string.Empty : (href.StartsWith("http") ? href : $"https://www.{BaseDomain}{href}");

                // 3. Extrai o Preço
                var precoElement = await card.QuerySelectorAsync("h6.price");
                var precoTexto = precoElement != null ? await precoElement.InnerTextAsync() : string.Empty;
                decimal preco = LimparEConverterPreco(precoTexto);

                // 4. Extrai o Código do imóvel (ex: "Cód 47366")
                var codElement = await card.QuerySelectorAsync(".content-bottom span");
                var codigoTexto = codElement != null ? await codElement.InnerTextAsync() : string.Empty;

                // Filtra pelo bairro de interesse antes de prosseguir
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
                        Descricao = codigoTexto,
                        DataConsulta = DateTime.Now
                    });
                }

                ImoveisEncontrados++;
            }

            LogProximaPagina();
            await Task.Delay(2000);
            pagina++;
        }

        LogFinal("fim da mineração Gilar");
        LogImovelEncontrado(imoveisEncontrados);
        LogPulaLinha();

        return imoveisEncontrados;
    }

    private static decimal LimparEConverterPreco(string? texto)
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
        else
        {
            var apenasNumeros = new string(texto.Where(c => char.IsDigit(c) || c == ',').ToArray());
            if (decimal.TryParse(apenasNumeros, out decimal valor))
            {
                return valor;
            }
        }
        return 0m;
    }
}
