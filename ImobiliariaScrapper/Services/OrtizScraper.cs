using ImobiliariaScrapper.Models;
using ImobiliariaScrapper.Utils;
using Microsoft.Playwright;
using System.Text;
using System.Text.RegularExpressions;

namespace ImobiliariaScrapper.Services;

public class OrtizScraper : AbstractScraper, IScraper
{
    public OrtizScraper(FiltroConsulta filtro) : base(filtro) { }
    public string NomeImobiliaria => "Ortiz";
    int paginas = 1;

    public static string CriarUrl(int preco, int? pagina)
    {
        var url = new StringBuilder("https://ortizimoveis.com.br/imoveis/aluguel/casa/");
        url.Append("?quartos=2");
        url.Append($"&valor_max={preco}");
        url.Append("&ordem=preco-cresce");
        if (pagina != null && pagina > 1)
            url.Append($"&pagina={pagina}"); //pagina
        return url.ToString();
    }

    public async Task<IEnumerable<Imovel>> ExtrairImovelAsync()
    {
        var imoveisEncontrados = new List<Imovel>();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = BROWSER_HEADLESS // Mantenha false para ver onde o robô está clicando
        });

        var page = await ConfigurarExtracao(CriarUrl(2000, paginas), browser);

        bool temProximaPagina = true;

        while (temProximaPagina)
        {
            LogPagina(paginas);

            //VERIFICAR SE EXISTE MENSAGEM DE "NÃO ENCONTRAMOS IMÓVEIS"=========================
            await Task.Delay(DELAY_VALUE);
            await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight);");

            await Task.Delay(DELAY_VALUE);
            var mensagemVazia = await page.Locator("div.card.card-body.empty").IsVisibleAsync();

            if (mensagemVazia)
            {
                LogFinal("mensagem vazia");
                temProximaPagina = false;
                break;
            }

            //OBTEM DADOS DOS IMÓVEIS DA PÁGINA=========================================
            // 1. Busca todos os cards da página
            await Task.Delay(DELAY_VALUE);
            var cards = await page.QuerySelectorAllAsync("div.card-imovel");

            foreach (var card in cards)
            {
                var BairroEl = await card.QuerySelectorAsync("div.local");
                var Bairro = ExtrairApenasBairro(BairroEl != null ? await BairroEl.TextContentAsync() : "");

                var LinkEl = await card.QuerySelectorAsync("a.descricao");
                var Link = LinkEl != null ? await LinkEl.EvaluateAsync<string>("el => el.href") : "";

                var PrecoEl = await card.QuerySelectorAsync("div.valor");
                var Preco = ExtrairPreco(PrecoEl != null ? await PrecoEl.TextContentAsync() : "");

                var ValoresExtras = 0m;

                var DescricaoValoresExtras = "";

                var DescricaoEl = await card.QuerySelectorAsync("div.tipo");
                var Descricao = ConcatenaDescricao(
                    DescricaoEl != null ? await DescricaoEl.TextContentAsync() : "",
                    BairroEl != null ? await BairroEl.TextContentAsync() : "");


                // Filtra pelo bairro de interesse antes de prosseguir
                if (BairroEhDeInteresse(Bairro))
                {
                    imoveisEncontrados.Add(new Imovel
                    {
                        Bairro = Bairro,
                        Link = Link!,
                        PrecoTotal = Preco,
                        Preco = Preco,
                        ValoresExtras = ValoresExtras,
                        DescricaoValoresExtras = DescricaoValoresExtras,
                        Imobiliaria = NomeImobiliaria,
                        Descricao = Descricao,
                        DataConsulta = DateTime.Now
                    });
                }
                ImoveisEncontrados++;
            }

            //TERMINA SE FOR A ULTIMA PAGINA====================================
            var botaoProximo = page.Locator("a.page-link[rel='next']").First;
            if (await botaoProximo.CountAsync() == 0)
            {
                LogFinal("FINAL (ultima pagina)");
                temProximaPagina = false;
                break;
            }

            //NAVEGAR PARA PROXIMA PAGINA====================================
            LogProximaPagina();
            await Task.Delay(DELAY_VALUE);
            await botaoProximo.ScrollIntoViewIfNeededAsync();
            await botaoProximo.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

            paginas++;
        }

        LogFinal("fim do loop");
        LogImovelEncontrado(imoveisEncontrados);
        LogPulaLinha();
        return imoveisEncontrados;
    }

    private static string ExtrairApenasBairro(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return string.Empty;

        var textoSplit = texto.Split(",");

        if (textoSplit.Length == 1)
            return texto;
        if (textoSplit.Length == 2)
            return textoSplit[0].Trim();

        if (textoSplit.Length == 3)
        {
            var subTextoSplit = textoSplit[1].Split(" - ");
            return subTextoSplit[^1].Trim();
        }

        return string.Empty;
    }

    private static decimal ExtrairPreco(string? texto)
    {
        var valorLocacao = 0m;

        if (string.IsNullOrWhiteSpace(texto))
            return valorLocacao;

        var match = Regex.Match(texto, @"([\d\.,]+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            string valorLimpo = match.Groups[1].Value
                .Replace(".", "")
                .Replace(",", ".");

            decimal.TryParse(valorLimpo, System.Globalization.CultureInfo.InvariantCulture, out valorLocacao);
        }

        return valorLocacao;
    }

    private static string ConcatenaDescricao(string? descricao, string? bairro)
    {
        if (string.IsNullOrWhiteSpace(descricao))
            return bairro ?? string.Empty;
        if (string.IsNullOrWhiteSpace(bairro))
            return descricao ?? string.Empty;
        return $"{descricao} - {bairro}";
    }
}
