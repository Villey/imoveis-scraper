using ImobiliariaScrapper.Models;

namespace ImobiliariaScrapper.Services;

public interface IScraper
{
    string NomeImobiliaria { get; }
    Task<IEnumerable<Imovel>> ExtrairImovelAsync();
}
