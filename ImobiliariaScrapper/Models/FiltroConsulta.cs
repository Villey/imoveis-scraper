namespace ImobiliariaScrapper.Models;

public enum TipoNegocio
{
    Aluguel,
    Venda
}

public class FiltroConsulta
{
    public TipoNegocio Negocio { get; set; } = TipoNegocio.Aluguel;
    public decimal ValorMaximo { get; set; }
    public bool ApenasBauru { get; set; } = true;
}
