namespace ImobiliariaScrapper.Models;

public class Imovel
{
    public decimal PrecoTotal { get; set; }
    public decimal Preco { get; set; }
    public decimal ValoresExtras { get; set; }
    public string DescricaoValoresExtras { get; set; } = string.Empty;
    public string Bairro { get; set; } = string.Empty;
    public string Imobiliaria { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public string Link { get; set; } = string.Empty;
    public DateTime DataConsulta { get; set; } = DateTime.Now;
}
