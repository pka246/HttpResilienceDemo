public class PartnerClient(HttpClient http)
{
    public async Task<string> GetRatesAsync(CancellationToken ct)
    {
        var response = await http.GetAsync("/partner/rates", ct);
        response.EnsureSuccessStatusCode(); // throws HttpRequestException on 4xx/5xx
        return await response.Content.ReadAsStringAsync(ct);
    }
}