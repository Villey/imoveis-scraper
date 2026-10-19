using System.Globalization;
using System.Text.RegularExpressions;
using ImobiliariaScrapper.Models;
using Microsoft.Playwright;

namespace ImobiliariaScrapper.Services;

public class KazaScraper : AbstractScraper, IScraper
{
    public string NomeImobiliaria => "Kaza";

    public KazaScraper(FiltroConsulta filtro) : base(filtro) { }

    public string CriarUrl(int pagina)
    {
        string negocio = _filtro.Negocio == TipoNegocio.Venda ? "venda" : "aluguel";
        
        // Se for apenas Bauru: "2-casa/1-bauru"
        // Se for todas as cidades: "imoveis"
        string escopo = _filtro.ApenasBauru ? "1-bauru" : "";

        string maxParam = string.Empty;
        if (_filtro.ValorMaximo > 0)
        {
            // Formata com separador de milhar pt-BR (ex: 1500 -> "1.500")
            string valorFormatado = _filtro.ValorMaximo.ToString("#,##0", new CultureInfo("pt-BR"));
            maxParam = $"&max={valorFormatado}";
        }

        return $"https://imobiliariakaza.com.br/busca/{negocio}/2-casa/{escopo}?quartos=2{maxParam}&ordem=menor&page={pagina}";
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

            var cards = await page.QuerySelectorAllAsync(".card-imovel");

            if (cards.Count == 0 || pagina >= 10)
            {
                LogFinal("sem mais imóveis nesta busca");
                temProximaPagina = false;
                break;
            }

            foreach (var card in cards)
            {
                // 1. Bairro
                var locElement = await card.QuerySelectorAsync(".localizacao");
                var locTexto = locElement != null ? await locElement.InnerTextAsync() : string.Empty;
                var bairroTexto = CleanBairro(locTexto);

                // 2. Link do imóvel
                var linkElement = await card.QuerySelectorAsync("a[href*='/imovel/']");
                var href = linkElement != null ? await linkElement.GetAttributeAsync("href") : string.Empty;
                
                string linkCompleto = string.IsNullOrEmpty(href) 
                    ? string.Empty 
                    : (href.StartsWith("http") ? href : $"https://imobiliariakaza.com.br{href}");

                // 3. Preço (busca pelo rótulo dinâmico: VENDA ou LOCAÇÃO)
                string rotuloPreco = _filtro.Negocio == TipoNegocio.Venda ? "VENDA" : "LOCAÇÃO";
                var precoElement = await card.QuerySelectorAsync($".valor:has-text('{rotuloPreco}')");
                
                // Fallback para pegar qualquer div.valor caso o rótulo específico não seja encontrado
                if (precoElement == null)
                {
                    precoElement = await card.QuerySelectorAsync(".valor");
                }

                var precoTexto = precoElement != null ? await precoElement.InnerTextAsync() : string.Empty;
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
                        Descricao = locTexto,
                        DataConsulta = DateTime.Now
                    });
                }

                ImoveisEncontrados++;
            }

            LogProximaPagina();
            pagina++;
        }

        LogFinal("fim da mineração Kaza");
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
