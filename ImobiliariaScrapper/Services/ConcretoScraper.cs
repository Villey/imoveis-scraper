using ImobiliariaScrapper.Models;
using Microsoft.Playwright;
using System.Text;
using System.Text.RegularExpressions;

namespace ImobiliariaScrapper.Services;

public class ConcretoScraper : AbstractScraper, IScraper
{
    public ConcretoScraper(FiltroConsulta filtro) : base(filtro) { }

    public string NomeImobiliaria => "Concreto";
    int paginas = 1;

    public static string CriarUrl(int preco, int? pagina)
    {
        var url = new StringBuilder("https://www.concretoimoveis.com.br/pesquisa-de-imoveis/");
        url.Append("?busca_free=&locacao_venda=L");
        url.Append($"&valor_loc_min_input=0&valor_loc_max_input={preco}");
        url.Append("&valor_ven_min_input=0&valor_ven_max_input=0");
        url.Append("&id_cidade[]=521");
        url.Append("&id_tipo_imovel[]=61&id_tipo_imovel[]=49&id_tipo_imovel[]=136");
        url.Append("&dormitorio=2&garagem=&finalidade=residencial");
        url.Append("&a_min=&a_max=&area_tipo=area-util&vmi=&vma=");
        url.Append("&ordem=1");
        if (pagina != null && pagina > 1)
            url.Append($"&a_util_min=&a_util_max=&&pag{pagina}"); //pagina
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
            var mensagemVazia = await page.Locator("div:has-text('Nenhum imóvel encontrado')").IsVisibleAsync();

            if (mensagemVazia)
            {
                LogFinal("mensagem vazia");
                temProximaPagina = false;
                break;
            }

            //OBTEM DADOS DOS IMÓVEIS DA PÁGINA=========================================
            // 1. Busca todos os cards da página
            await Task.Delay(DELAY_VALUE);
            var cards = await page.QuerySelectorAllAsync("div.muda_card1");

            foreach (var card in cards)
            {
                var BairroEl = await card.QuerySelectorAsync("div.card-bairro-cidade-texto");
                var Bairro = ExtrairApenasBairro(BairroEl != null ? await BairroEl.TextContentAsync() : "");
                
                var LinkEl = await card.QuerySelectorAsync("a.carousel-cell.is-selected");
                var Link = LinkEl != null ? await LinkEl.EvaluateAsync<string>("el => el.href") : "";
                
                var PrecoEl = await card.QuerySelectorAsync("div.card-valores");
                var Preco = ExtrairPreco(PrecoEl != null ? await PrecoEl.TextContentAsync() : "");

                var ValoresExtras = 0m;

                var DescricaoValoresExtras = "";

                var DescricaoEl = await card.QuerySelectorAsync("div.card-texto p");
                var Descricao = DescricaoEl != null ? await DescricaoEl.TextContentAsync() : "";


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
                        Descricao = ExtrairDescricao(Descricao),
                        DataConsulta = DateTime.Now
                    });
                }
                ImoveisEncontrados++;
            }

            //TERMINA SE FOR A ULTIMA PAGINA====================================
            var ultimaPagina = page.Locator("li.disabled a[href]:has-text('»')");
            if(await ultimaPagina.CountAsync() > 0)
            {
                LogFinal("última página");
                temProximaPagina = false;
                break;
            }

            //NAVEGAR PARA PROXIMA PAGINA====================================
            var botaoProximo = page.Locator("div.pagination a[href]:has-text('»')");
            if(await botaoProximo.CountAsync() > 0)
            {
                LogProximaPagina();
                await Task.Delay(DELAY_VALUE);
                await botaoProximo.ScrollIntoViewIfNeededAsync();
                await botaoProximo.ClickAsync();
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

                paginas++;
            }
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

        return texto.Split(" - ")[0].Trim() ?? string.Empty;
    }

    private static decimal ExtrairPreco(string? texto)
    {
        var valorLocacao = 0m;

        if (string.IsNullOrWhiteSpace(texto))
            return valorLocacao;

        var match = Regex.Match(texto, @"([\d\.,]+)\s*L", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            string valorLimpo = match.Groups[1].Value
                .Replace(".", "")
                .Replace(",", ".");

            decimal.TryParse(valorLimpo, System.Globalization.CultureInfo.InvariantCulture, out valorLocacao);
        }

        return valorLocacao;
    }

    private static string ExtrairDescricao(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return string.Empty;

        texto = texto.Length > 100 ? texto[..100] : texto;
        return texto.Replace("\n", " ").Replace("\r", " ").Replace("\t", " ").Replace(";", ",").Trim();
    }
}
