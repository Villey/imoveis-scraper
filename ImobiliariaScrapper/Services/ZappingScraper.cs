using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ImobiliariaScrapper.Models;
using Microsoft.Playwright;

namespace ImobiliariaScrapper.Services;

public class ZappingScraper : AbstractScraper, IScraper
{
    private static readonly string BaseDomain = "zap" + "imoveis.com.br";
    private int paginas = 1;
    public string NomeImobiliaria => "Zapping";

    public ZappingScraper(FiltroConsulta filtro) : base(filtro) { }

    public string CriarUrl(int pagina)
    {
        string negocio = _filtro.Negocio == TipoNegocio.Venda ? "venda" : "aluguel";
        var url = new StringBuilder($"https://www.{BaseDomain}/{negocio}/casas/sp+bauru/2-quartos/");
        url.Append("?onde=%2CS%C3%A3o+Paulo%2CBauru%2C%2C%2C%2C%2Ccity%2CBR%3ESao+Paulo%3ENULL%3EBauru%2C-22.31553%2C-49.070822%2C");
        url.Append("&tipos=casa_residencial");
        url.Append("&quartos=2%2C3%2C4");

        if (pagina > 1)
        {
            url.Append($"&pagina={pagina}");
        }

        if (_filtro.ValorMaximo > 0)
        {
            url.Append($"&precoMaximo={_filtro.ValorMaximo.ToString(CultureInfo.InvariantCulture)}");
        }

        url.Append("&ordem=LOWEST_PRICE");
        return url.ToString();
    }

    public async Task<IEnumerable<Imovel>> ExtrairImovelAsync()
    {
        var imoveisEncontrados = new List<Imovel>();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = false//BROWSER_HEADLESS // Mantenha false para ver onde o robô está clicando
        });

        var page = await ConfigurarExtracao(CriarUrl(paginas), browser);

        bool temProximaPagina = true;

        bool cookiesAceito = false;

        while (temProximaPagina)
        {
            LogPagina(paginas);

            //VERIFICAR SE EXISTE MENSAGEM DE "NÃO ENCONTRAMOS IMÓVEIS"=========================
            await Task.Delay(DELAY_VALUE);
            var popupChato = page.Locator("button.olx-modal-close-button");

            await Task.Delay(DELAY_VALUE);
            if (await popupChato.CountAsync() > 0)
            {
                await popupChato.ClickAsync();
            }

            int desiste = 0;
            while (cookiesAceito == false && desiste < 20)
            {
                var popupCookies = await page.QuerySelectorAllAsync("button#adopt-accept-all-button");
                await Task.Delay(DELAY_VALUE);
                if (popupCookies.Count > 0)
                {
                    await popupCookies[0].ClickAsync();
                    cookiesAceito = true;
                    Console.WriteLine($"Tentativas do COOKIE: {desiste}");
                    break;
                }
                desiste++;
            }

            await Task.Delay(DELAY_VALUE);
            await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight);");

            await Task.Delay(DELAY_VALUE);
            if (await page.Locator("h2:has-text('Não encontramos imóveis')").IsVisibleAsync())
            {
                LogFinal("mensagem vazia");
                temProximaPagina = false;
                break;
            }

            //OBTEM DADOS DOS IMÓVEIS DA PÁGINA=========================================
            // 1. Busca todos os cards da página
            await Task.Delay(DELAY_VALUE);
            var cards = await page.QuerySelectorAllAsync("li a[data-ds-component='DS-Card']");

            foreach (var card in cards)
            {
                // Link do imóvel
                string link = await card.GetAttributeAsync("href") ?? "";

                link = await PegarLinkDeDuplicado(page, card, link);
                //Retry
                if (string.IsNullOrEmpty(link))
                {
                    await Task.Delay(10000);
                    link = await PegarLinkDeDuplicado(page, card, link);
                }

                // Descrição
                var descricaoEl = await card.QuerySelectorAsync("[data-cy='rp-cardProperty-location-txt']");
                string descricao = descricaoEl != null ? await descricaoEl.InnerTextAsync() : "";
                descricao = string.IsNullOrWhiteSpace(descricao) && descricaoEl != null ? await descricaoEl.InnerHTMLAsync() : "";

                // Bairro / Localização
                var bairroEl = await card.QuerySelectorAsync("[data-cy='rp-cardProperty-location-txt']");
                string bairro = bairroEl != null ? await bairroEl.InnerTextAsync() : "";
                bairro = string.IsNullOrWhiteSpace(bairro) && bairroEl != null ? await bairroEl.InnerHTMLAsync() : "";

                // Imobiliária
                var imobiliariaEl = await card.QuerySelectorAsync("[data-testid='verified-advertiser-icon'] + span");
                string imobiliaria = imobiliariaEl != null ? await imobiliariaEl.InnerTextAsync() : "";
                imobiliaria = string.IsNullOrWhiteSpace(imobiliaria) && imobiliariaEl != null ? await imobiliariaEl.InnerHTMLAsync() : "";

                // Preço
                var precoEl = await card.QuerySelectorAsync("[data-cy='rp-cardProperty-price-txt'] span");
                string precoTextoStr = precoEl != null ? await precoEl.InnerTextAsync() : "0";
                precoTextoStr = string.IsNullOrWhiteSpace(precoTextoStr) && precoEl != null ? await precoEl.InnerHTMLAsync() : "0";
                decimal precoTexto = LimparEConverterPreco(precoTextoStr);

                // Busca o parágrafo com as informações de condomínio e IPTU
                var detalhesPrecoEl = await card.QuerySelectorAsync("[data-cy='rp-cardProperty-price-txt'] p.typo-caption");
                string detalhesTexto = detalhesPrecoEl != null ? await detalhesPrecoEl.InnerTextAsync() : "";
                detalhesTexto = string.IsNullOrWhiteSpace(detalhesTexto) && detalhesPrecoEl != null ? await detalhesPrecoEl.InnerHTMLAsync() : "";

                // Extrai o valor do Condomínio
                var matchCondo = Regex.Match(detalhesTexto, @"Cond\.\s*(?:R\$\s*)?([\d\.]+|isento)", RegexOptions.IgnoreCase);
                string condominio = matchCondo.Success ? matchCondo.Groups[1].Value : "Não informado";

                // Extrai o valor do IPTU
                var matchIptu = Regex.Match(detalhesTexto, @"IPTU\s*(?:R\$\s*)?([\d\.]+|não informado)", RegexOptions.IgnoreCase);
                string iptu = matchIptu.Success ? matchIptu.Groups[1].Value : "Não informado";

                // Filtra pelo bairro de interesse antes de prosseguir
                if (BairroEhDeInteresse(ExtrairApenasBairro(bairro), _filtro.ApenasBauru) && !string.IsNullOrEmpty(link))
                {
                    imoveisEncontrados.Add(new Imovel
                    {
                        Bairro = ExtrairApenasBairro(bairro),
                        Link = link,
                        PrecoTotal = precoTexto + CalculaValorExtras(condominio, iptu),
                        Preco = precoTexto, // Considerando que o preço total é o mesmo que o preço principal
                        ValoresExtras = CalculaValorExtras(condominio, iptu),
                        DescricaoValoresExtras = DescricaoValoresExtras(condominio, iptu),
                        Imobiliaria = $"{imobiliaria} ({NomeImobiliaria})",
                        Descricao = descricao.Replace("\n", " ").Replace("\r", " ").Replace("\t", " ").Trim(),
                        DataConsulta = DateTime.Now
                    });
                }
                ImoveisEncontrados++;
            }

            //NAVEGAR PARA PROXIMA PAGINA====================================
            var botaoProximo = page.Locator("a[aria-label='próxima página']");

            if (await botaoProximo.CountAsync() > 0)
            {
                string ariaDisabled = await botaoProximo.GetAttributeAsync("aria-disabled") ?? "false";

                if (ariaDisabled == "true")
                {
                    LogFinal("botão desativado");
                    temProximaPagina = false;
                    break;
                }

                await Task.Delay(DELAY_VALUE);
                await botaoProximo.ScrollIntoViewIfNeededAsync();
                await botaoProximo.ClickAsync();
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

                paginas++;
            }
            else
            {
                LogFinal("botão não encontrado");
                temProximaPagina = false;
                break;
            }
        }

        LogFinal("fim do loop");
        LogImovelEncontrado(imoveisEncontrados);
        LogPulaLinha();
        return imoveisEncontrados;
    }

    private static async Task<string> PegarLinkDeDuplicado(IPage page, IElementHandle card, string link)
    {
        if (string.IsNullOrEmpty(link))
        {
            await card.ScrollIntoViewIfNeededAsync();
            await Task.Delay(DELAY_VALUE);
            await card.EvaluateAsync("el => el.click()");
            await Task.Delay(1000);

            var modalSelector = "[data-cy='deduplication-modal-list-step']";
            try
            {
                // Aguarda o modal aparecer no DOM
                await page.WaitForSelectorAsync(modalSelector, new PageWaitForSelectorOptions
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = 3000
                });

                // Captura o link do primeiro item do modal
                var subcards = await page.QuerySelectorAllAsync($"{modalSelector} a.olx-core-card");
                if (subcards.Count > 0)
                {
                    link = await subcards[0].GetAttributeAsync("href") ?? "";
                }
            }
            catch (TimeoutException)
            {
                Console.WriteLine("Modal de duplicados não abriu.");
            }

            // Fecha o modal para liberar a tela
            var botaoFechar = page.Locator("button[aria-label='Fechar modal']").First;
            if (await botaoFechar.IsVisibleAsync())
            {
                await botaoFechar.ClickAsync();
                await Task.Delay(300);
            }
            /*
            var subcard = await page.QuerySelectorAllAsync("[data-cy='deduplication-modal-list-step'] a.olx-core-card");
            await Task.Delay(DELAY_VALUE);

            link = subcard.Count > 0 ? await subcard[0].GetAttributeAsync("href") ?? "" : "";

            await page.Locator("button[aria-label='Fechar modal']").ClickAsync();
            await Task.Delay(DELAY_VALUE);
            */
        }

        return link;
    }

    private static string ExtrairApenasBairro(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return string.Empty;

        if (!texto.Contains(','))
            return texto.Trim();

        var partes = texto.Split(',');

        if (partes[^2].Contains(" em\n"))
            return partes[^2].Split(" em\n")[1].Trim();

        return partes[^2].Trim();
    }

    private static decimal LimparEConverterPreco(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return 0m;
        var apenasNumeros = new string(texto.Where(c => char.IsDigit(c) || c == ',').ToArray());
        return decimal.TryParse(apenasNumeros, out decimal valor) ? valor : 0m;
    }

    private static string DescricaoValoresExtras(string condominio, string iptu)
    {
        var descricao = new List<string>();
        if (!string.IsNullOrWhiteSpace(condominio) && condominio.ToLower() != "isento")
        {
            descricao.Add($"Condomínio: {condominio}");
        }
        if (!string.IsNullOrWhiteSpace(iptu) && iptu.ToLower() != "não informado")
        {
            descricao.Add($"IPTU: {iptu}");
        }
        return string.Join("; ", descricao);
    }

    private static decimal CalculaValorExtras(string condominio, string iptu)
    {
        decimal valorCondominio = 0m;
        decimal valorIptu = 0m;
        // Extrai apenas os números do condomínio
        if (!string.IsNullOrWhiteSpace(condominio) && condominio.ToLower() != "isento")
        {
            string apenasNumerosCondominio = ExtrairApenasNumeros(condominio);
            decimal.TryParse(apenasNumerosCondominio, out valorCondominio);
        }
        // Extrai apenas os números do IPTU
        if (!string.IsNullOrWhiteSpace(iptu) && iptu.ToLower() != "não informado")
        {
            string apenasNumerosIptu = ExtrairApenasNumeros(iptu);
            decimal.TryParse(apenasNumerosIptu, out valorIptu);
        }
        return valorCondominio + valorIptu;
    }

    private static string ExtrairApenasNumeros(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "0";

        // Mantém apenas os dígitos de 0 a 9
        string apenasNumeros = Regex.Replace(texto, @"[^\d]", "");

        return string.IsNullOrEmpty(apenasNumeros) ? "0" : apenasNumeros;
    }
}
