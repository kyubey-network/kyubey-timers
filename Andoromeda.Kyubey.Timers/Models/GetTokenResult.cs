namespace Andoromeda.Kyubey.Timers.Models
{
    public class GetTokenResult
    {
        public string Symbol { get; set; }

        public GetTokenResultContract Contract { get; set; }
    }

    public class GetTokenResultContract
    {
        public string Transfer { get; set; }

        public string Depot { get; set; }

        public string Price { get; set; }
    }
}
