using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ImobiliariaScrapper.Models;
using Microsoft.Playwright;

namespace ImobiliariaScrapper.Services;

public class ConcretoScraper : AbstractScraper, IScraper
{
    public ConcretoScraper(FiltroConsulta filtro) : base(filtro) { }

    public string NomeImobiliaria => "Concreto";

    public string CriarUrl(int pagina)
    {
        string negocio = _filtro.Negocio == TipoNegocio.Venda ? "V" : "L";
        string cidadeParam = _filtro.ApenasBauru ? "&id_cidade%5B%5D=521" : string.Empty;
        string vmaParam = string.Empty;

        if (_filtro.ValorMaximo > 0)
        {
            string valorFormatado = _filtro.ValorMaximo.ToString("#,##0.00", new CultureInfo("pt-BR"));
            string valorEncoded = Uri.EscapeDataString(valorFormatado);
            vmaParam = $"&vma={valorEncoded}";
        }

        var url = new StringBuilder("https://www.concretoimoveis.com.br/pesquisa-de-imoveis/");
        url.Append($"?locacao_venda={negocio}");
        url.Append(cidadeParam);
        url.Append("&id_tipo_imovel%5B%5D=49");
        url.Append("&finalidade=residencial&dormitorio=2&garagem=&vmi=");
        url.Append(vmaParam);
        url.Append("&ordem=1");

        if (pagina > 1)
        {
            url.Append($"&pag={pagina}");
        }

        return url.ToString();
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

        var page = await ConfigurarExtracao(CriarUrl(pagina), browser);

        while (temProximaPagina)
        {
            LogPagina(pagina);

            await Task.Delay(DELAY_VALUE);
            await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight);");
            await Task.Delay(DELAY_VALUE);

            var mensagemVazia = await page.Locator("div:has-text('Nenhum imóvel encontrado')").IsVisibleAsync();
            if (mensagemVazia)
            {
                LogFinal("mensagem vazia");
                temProximaPagina = false;
                break;
            }

            var cards = await page.QuerySelectorAllAsync("div.muda_card1");
            if (cards.Count == 0 || pagina >= 15)
            {
                LogFinal("sem mais imóveis nesta busca");
                temProximaPagina = false;
                break;
            }

            foreach (var card in cards)
            {
                var bairroEl = await card.QuerySelectorAsync("div.card-bairro-cidade-texto");
                var bairro = ExtrairApenasBairro(bairroEl != null ? await bairroEl.TextContentAsync() : string.Empty);

                var linkEl = await card.QuerySelectorAsync("a.carousel-cell.is-selected");
                var link = linkEl != null ? await linkEl.EvaluateAsync<string>("el => el.href") : string.Empty;

                var precoEl = await card.QuerySelectorAsync("div.card-valores");
                var preco = ExtrairPreco(precoEl != null ? await precoEl.TextContentAsync() : string.Empty, _filtro.Negocio);

                var descricaoEl = await card.QuerySelectorAsync("div.card-texto p");
                var descricao = descricaoEl != null ? await descricaoEl.TextContentAsync() : string.Empty;

                if (BairroEhDeInteresse(bairro))
                {
                    imoveisEncontrados.Add(new Imovel
                    {
                        Bairro = bairro,
                        Link = link,
                        PrecoTotal = preco,
                        Preco = preco,
                        ValoresExtras = 0m,
                        DescricaoValoresExtras = string.Empty,
                        Imobiliaria = NomeImobiliaria,
                        Descricao = ExtrairDescricao(descricao),
                        DataConsulta = DateTime.Now
                    });
                }
                ImoveisEncontrados++;
            }

            var ultimaPagina = page.Locator("li.disabled a[href]:has-text('»')");
            if (await ultimaPagina.CountAsync() > 0)
            {
                LogFinal("última página");
                temProximaPagina = false;
                break;
            }

            var botaoProximo = page.Locator("div.pagination a[href]:has-text('»')");
            if (await botaoProximo.CountAsync() > 0)
            {
                LogProximaPagina();
                await Task.Delay(DELAY_VALUE);
                await botaoProximo.ScrollIntoViewIfNeededAsync();
                await botaoProximo.ClickAsync();
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                pagina++;
            }
            else
            {
                temProximaPagina = false;
            }
        }

        LogFinal("fim do loop Concreto");
        LogImovelEncontrado(imoveisEncontrados);
        LogPulaLinha();

        return imoveisEncontrados;
    }

    private static string ExtrairApenasBairro(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
        return texto.Split(" - ")[0].Trim();
    }

    private static decimal ExtrairPreco(string? texto, TipoNegocio negocio)
    {
        if (string.IsNullOrWhiteSpace(texto)) return 0m;

        string padrao = negocio == TipoNegocio.Venda
            ? @"([\d.,]+)\s*(?:V|Venda)"
            : @"([\d.,]+)\s*(?:L|Locação|Locacao)";

        var match = Regex.Match(texto, padrao, RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            match = Regex.Match(texto, @"([\d.,]+)");
        }

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

    private static string ExtrairDescricao(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
        texto = texto.Length > 100 ? texto[..100] : texto;
        return texto.Replace("\n", " ").Replace("\r", " ").Replace("\t", " ").Replace(";", ",").Trim();
    }
}
