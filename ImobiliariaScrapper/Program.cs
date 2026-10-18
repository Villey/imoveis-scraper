using ImobiliariaScrapper.Models;
using ImobiliariaScrapper.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Text;

var filtro = new FiltroConsulta();

Console.WriteLine("========================================");
Console.WriteLine("   CONFIGURAÇÃO DA BUSCA DE IMÓVEIS     ");
Console.WriteLine("========================================\n");

Console.Write("1. Deseja procurar (1) Aluguel ou (2) Venda? [Padrão: 1]: ");
var opcaoNegocio = Console.ReadLine();
filtro.Negocio = opcaoNegocio == "2" ? TipoNegocio.Venda : TipoNegocio.Aluguel;

Console.Write("2. Qual o Valor Máximo em R$? (Ex: 2000) [0 para sem limite]: ");
if (decimal.TryParse(Console.ReadLine(), out decimal valor))
{
    filtro.ValorMaximo = valor;
}

Console.Write("3. Abrangência da busca: (1) Apenas Bauru ou (2) Todas as cidades? [Padrão: 1]: ");
var opcaoCidade = Console.ReadLine();
filtro.ApenasBauru = opcaoCidade != "2";

string escopoCidade = filtro.ApenasBauru ? "Apenas Bauru" : "Todas as cidades";
Console.WriteLine($"\n[INFO] Iniciando busca por '{filtro.Negocio}' em '{escopoCidade}' até R$ {filtro.ValorMaximo:N2}...\n");

var services = new ServiceCollection();

// Registra a instância do filtro como Singleton
services.AddSingleton(filtro);

// Registra os scrapers
services.AddTransient<IScraper, KazaScraper>();
services.AddTransient<IScraper, AkitemScraper>();
//services.AddTransient<IScraper, GilarScraper>();
//services.AddTransient<IScraper, ZappingScraper>();
//services.AddTransient<IScraper, ConcretoScraper>();
//services.AddTransient<IScraper, OrtizScraper>();
services.AddTransient<ScrapingOrchestrator>();

var serviceProvider = services.BuildServiceProvider();

var exitCode = Microsoft.Playwright.Program.Main(["install"]);
if (exitCode != 0)
{
    throw new Exception($"Falha ao instalar navegadores do Playwright. Código: {exitCode}");
}

var orchestrator = serviceProvider.GetRequiredService<ScrapingOrchestrator>();
var imoveis = await orchestrator.ExecutarTodosEmFila();

Console.WriteLine($"Total de imóveis coletados: {imoveis.Count}");

var csv = new StringBuilder();
csv.Append("Link;");
csv.Append("Imobiliaria;");
csv.Append("Bairro;");
csv.Append("PrecoTotal;");
csv.Append("Preco;");
csv.Append("ValoresExtras;");
csv.Append("DescricaoValoresExtras;");
csv.Append("Descricao;");
csv.Append("DataConsulta;");
csv.AppendLine();

foreach (var imovel in imoveis)
{
    csv.Append($"{imovel.Link} ;");
    csv.Append($"{imovel.Imobiliaria};");
    csv.Append($"{imovel.Bairro.Replace(";", ",")};");
    csv.Append($"{imovel.PrecoTotal:F2};");
    csv.Append($"{imovel.Preco:F2};");
    csv.Append($"{imovel.ValoresExtras};");
    csv.Append($"{imovel.DescricaoValoresExtras.Replace(";", ",")};");
    csv.Append($"{imovel.Descricao.Replace(";", ",")};");
    csv.Append($"{imovel.DataConsulta:yyyy-MM-dd HH:mm};");
    csv.AppendLine();
}

string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
string caminhoArquivo = Path.Combine(desktopPath, "imoveis_consolidados.csv");
await File.WriteAllTextAsync(caminhoArquivo, csv.ToString(), Encoding.UTF8);
