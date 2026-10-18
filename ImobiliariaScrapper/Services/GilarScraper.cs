using ImobiliariaScrapper.Models;
using Microsoft.Playwright;
using System.Text;

namespace ImobiliariaScrapper.Services;

public class GilarScraper : AbstractScraper, IScraper
{
    public GilarScraper(FiltroConsulta filtro) : base(filtro) { }
    public string NomeImobiliaria => "Gilar";
    int paginas = 1;

    public static string CriarUrl(int preco, int? pagina)
    {
        var url = new StringBuilder("https://www.gilar.com.br/Imovel/Lista");
        url.Append("?Negociacao=Alugar");
        url.Append("&IdTipoIdSubtipo=R_2");
        url.Append("&CidadeId=11");
        url.Append($"&IntervaloPreco={0}%2C{preco}");
        url.Append("&Dormitorios=2");
        url.Append("&Ordem=2");
        if (pagina != null && pagina > 1)
            url.Append($"&pagina={pagina}"); //pagina
        return url.ToString();
    }

    public async Task<IEnumerable<Imovel>> ExtrairImovelAsync()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = BROWSER_HEADLESS // Mantenha false para ver onde o robô está clicando
        });
        var page = await ConfigurarExtracao(CriarUrl(2000, paginas), browser);

        var imoveisEncontrados = new List<Imovel>();
        bool temProximaPagina = true;

        while (temProximaPagina)
        {
            LogPagina(paginas);

            // Verifica se a mensagem de "Nenhum imóvel" apareceu
            await Task.Delay(DELAY_VALUE);
            await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight);");
            var mensagemVazia = await page.QuerySelectorAsync(".text-subtitle:has-text('Nenhum imóvel localizado')");
            var naoEncontrado = await page.Locator("p:has-text('Nenhum imóvel encontrado')").IsVisibleAsync();

            if (mensagemVazia is not null || naoEncontrado || paginas >= 10)
            {
                LogFinal("mensagem vazia");
                temProximaPagina = false;
                break;
            }

            // Espera até que pelo menos um card de imóvel apareça no DOM
            await page.WaitForSelectorAsync(".homelengo-box");

            // Localiza os cards principais (ajustando para divs que representam os cards)
            var cards = await page.QuerySelectorAllAsync(".homelengo-box");

            foreach (var card in cards)
            {
                // 1. Extrai o Bairro (está na div.bottom dentro da tag <a>)
                var bairroElement = await card.QuerySelectorAsync(".archive-top .bottom");
                var bairroTexto = bairroElement != null ? await bairroElement.InnerTextAsync() : string.Empty;

                // 2. Extrai o Link do imóvel
                var linkElement = await card.QuerySelectorAsync("a.images-group");
                var href = linkElement != null ? await linkElement.GetAttributeAsync("href") : string.Empty;
                var linkCompleto = $"https://www.gilar.com.br{href}";

                // 3. Extrai o Preço (está dentro do <h6 class="price">)
                var precoElement = await card.QuerySelectorAsync("h6.price");
                var precoTexto = precoElement != null ? await precoElement.InnerTextAsync() : string.Empty;
                decimal preco = LimparEConverterPreco(precoTexto);

                // 4. Extrai o Código do imóvel (ex: "Cód 47366")
                var codElement = await card.QuerySelectorAsync(".content-bottom span");
                var codigoTexto = codElement != null ? await codElement.InnerTextAsync() : string.Empty;

                // Filtra pelo bairro de interesse antes de prosseguir
                if (BairroEhDeInteresse(bairroTexto))
                {
                    imoveisEncontrados.Add(new Imovel
                    {
                        Bairro = bairroTexto,
                        Link = linkCompleto,
                        PrecoTotal = preco,
                        Preco = preco, // Considerando que o preço total é o mesmo que o preço principal
                        ValoresExtras = 0m, // Não há valores extras visíveis no card principal
                        DescricaoValoresExtras = string.Empty, // Não há descrição de valores extras visível
                        Imobiliaria = NomeImobiliaria,
                        Descricao = codigoTexto, // Usando o código como descrição
                        DataConsulta = DateTime.Now
                    });
                }
                ImoveisEncontrados++;
            }

            //NAVEGAR PARA PROXIMA PAGINA====================================
            LogProximaPagina();
            await Task.Delay(2000);
            paginas++;
            await page.GotoAsync(CriarUrl(2000, paginas));
        }

        LogFinal("fim do loop");
        LogImovelEncontrado(imoveisEncontrados);
        LogPulaLinha();
        return imoveisEncontrados;

        /*
                // --- PREENCHIMENTO DOS FILTROS BASEADOS EM DIV / TEXTO ---

                // 1. Clica no container do dropdown para abrir a lista de opções
                await page.ClickAsync("div.nice-select[data-target-input='#IdTipoIdSubtipo']");
                // Clica no item específico usando o valor 'R_2' ou o texto 'Casas (R)'
                await page.ClickAsync("ul#IdTipoIdSubtipo-list li[data-value='R_2']");

                // 2. Abrir "Busca Detalhada"
                await page.ClickAsync("a.filter-advanced");

                // 3. Preencher valor máximo
                await page.EvaluateAsync(@"() => {
                    // 1. Obtém o elemento do slider
                    const slider = document.querySelector('#price-range');

                    // 2. Se o noUiSlider estiver inicializado, usa o método nativo .set()
                    if (slider && slider.noUiSlider) {
                        slider.noUiSlider.set([0, 2000]);
                    } else {
                        // Fallback: altera diretamente os inputs ocultos e dispara o evento
                        const inputMax = document.querySelector('input[name=""max-value""]');
                        const inputIntervalo = document.querySelector('#IntervaloPreco');

                        if (inputMax) inputMax.value = '2000';
                        if (inputIntervalo) {
                            inputIntervalo.value = '0,2000';
                            inputIntervalo.dispatchEvent(new Event('change', { bubbles: true }));
                        }
                    }
                }");

                // 4. Clica no campo de Quartos para abrir a lista
                await page.ClickAsync("div.nice-select[data-target-input='#Dormitorios']");
                // Clica na opção "2"
                await page.ClickAsync("ul#Dormitorios-list li[data-value='2']");

                // 5. Clicar no botão Buscar
                await page.ClickAsync("button.btn-search");

                // Aguarda a atualização assíncrona dos resultados na tela
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        */
        /*
        while (temProximaPagina)
        {
            Console.WriteLine($"Processando página {paginas}");
            paginas++;

            // Espera até que pelo menos um card de imóvel apareça no DOM
            await page.WaitForSelectorAsync(".homelengo-box");

            // Localiza os cards principais (ajustando para divs que representam os cards)
            var cards = await page.QuerySelectorAllAsync(".homelengo-box");

            foreach (var card in cards)
            {
                // 1. Extrai o Bairro (está na div.bottom dentro da tag <a>)
                var bairroElement = await card.QuerySelectorAsync(".archive-top .bottom");
                var bairroTexto = bairroElement != null ? await bairroElement.InnerTextAsync() : string.Empty;

                // 2. Extrai o Link do imóvel
                var linkElement = await card.QuerySelectorAsync("a.images-group");
                var href = linkElement != null ? await linkElement.GetAttributeAsync("href") : string.Empty;
                var linkCompleto = $"https://www.gilar.com.br{href}";

                // 3. Extrai o Preço (está dentro do <h6 class="price">)
                var precoElement = await card.QuerySelectorAsync("h6.price");
                var precoTexto = precoElement != null ? await precoElement.InnerTextAsync() : string.Empty;
                decimal preco = LimparEConverterPreco(precoTexto);

                // 4. Extrai o Código do imóvel (ex: "Cód 47366")
                var codElement = await card.QuerySelectorAsync(".content-bottom span");
                var codigoTexto = codElement != null ? await codElement.InnerTextAsync() : string.Empty;

                // Filtra pelo bairro de interesse antes de prosseguir
                if (BairroEhDeInteresse(bairroTexto))
                {
                    imoveisEncontrados.Add(new Imovel
                    {
                        Bairro = bairroTexto,
                        Link = linkCompleto,
                        PrecoTotal = preco,
                        Preco = preco, // Considerando que o preço total é o mesmo que o preço principal
                        ValoresExtras = 0m, // Não há valores extras visíveis no card principal
                        DescricaoValoresExtras = string.Empty, // Não há descrição de valores extras visível
                        Imobiliaria = NomeImobiliaria,
                        Descricao = codigoTexto, // Usando o código como descrição
                        DataConsulta = DateTime.Now
                    });
                }
            }

            // Paginação via div ou botão "Próximo" / "Carregar Mais"
            await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight);");
            var botaoProximo = await page.QuerySelectorAsync("a.nav-item[aria-label='Próxima']");
            await Task.Delay(1000); // Pequena pausa para garantir que o botão esteja visível
            var botaoProximoVisivel = await botaoProximo.IsVisibleAsync();
            Console.WriteLine($"botao proximo: {botaoProximo}");
            Console.WriteLine($"botao proximo visivel: {botaoProximoVisivel}");

            if (botaoProximo != null && botaoProximoVisivel)
            {
                await botaoProximo.ClickAsync();
                // Aguarda a nova página carregar
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

                // 2. Verifica se a mensagem de "Nenhum imóvel" apareceu
                await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight);");
                var mensagemVazia = await page.QuerySelectorAsync(".text-subtitle:has-text('Nenhum imóvel localizado')");
                Console.WriteLine($"mensagem Vazia: {mensagemVazia}");
            
                if (mensagemVazia != null)
                {
                    temProximaPagina = false; // Chegou ao fim da listagem
                    Console.WriteLine("FINAL [if mensagemVazia]");
                }
                else
                {
                    // Aguarda os novos cards se a página contiver imóveis
                    await page.WaitForSelectorAsync(".homelengo-box");
                    Console.WriteLine("Próximo loop");
                }
            }
            else
            {
                temProximaPagina = false;
                Console.WriteLine("FINAL [else]");
            }
        }

        return imoveisEncontrados;
        */
    }

    private static decimal LimparEConverterPreco(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return 0m;
        var apenasNumeros = new string(texto.Where(c => char.IsDigit(c) || c == ',').ToArray());
        return decimal.TryParse(apenasNumeros, out decimal valor) ? valor : 0m;
    }
}
