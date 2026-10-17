var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://localhost:5080");

builder.Services.AddSingleton<ChaosSwitch>();
builder.Services
    .AddHttpClient<PartnerClient>(c => c.BaseAddress = new Uri("http://localhost:5080"))
    // ✨ ONE LINE gives: rate limiter, total timeout, retry, circuit breaker, attempt timeout
    .AddStandardResilienceHandler(o =>
    {
        // Retry: up to 3 more tries after the first failure
        o.Retry.MaxRetryAttempts = 3;
        o.Retry.Delay = TimeSpan.FromMilliseconds(200);

        // Timeouts: cap each try and the whole call (retries included)
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);

        // Circuit breaker: demo-friendly numbers (defaults need ~100 calls to trip)
        o.CircuitBreaker.FailureRatio = 0.7;                           // 70% failures...
        o.CircuitBreaker.MinimumThroughput = 5;                        // ...with at least 5 calls...
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);  // ...within 10 s
        o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(10);     // stay open for 10 s

        // Console messages so students can SEE the pipeline working
        o.Retry.OnRetry = args =>
        {
            Console.WriteLine($"[retry]   attempt {args.AttemptNumber + 1} failed ({(int?)args.Outcome.Result?.StatusCode}), trying again...");
            return default;
        };
        o.CircuitBreaker.OnOpened = args =>
        {
            Console.WriteLine("[breaker] OPEN  - too many failures, failing fast for 10 s");
            return default;
        };
        o.CircuitBreaker.OnHalfOpened = args =>
        {
            Console.WriteLine("[breaker] HALF-OPEN - letting one test call through");
            return default;
        };
        o.CircuitBreaker.OnClosed = args =>
        {
            Console.WriteLine("[breaker] CLOSED - partner recovered, calls flow again");
            return default;
        };
    });


var app = builder.Build();

// ---------- OUR API: depends on the partner ----------
// Try it: GET http://localhost:5080/api/rates
app.MapGet("/api/rates", async (PartnerClient partner, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await partner.GetRatesAsync(ct));
    }
    catch (HttpRequestException)
    {
        return Results.Problem("Partner unavailable.", statusCode: 503);
    }
});

// ---------- Fake third-party API ----------
// Returns 503 some percent of the time (controlled by ChaosSwitch).
app.MapGet("/partner/rates", (ChaosSwitch chaos) =>
    Random.Shared.Next(100) < chaos.PartnerFailurePercent
        ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
        : Results.Ok(new { usdToInr = 83.2 }));

// ---------- Chaos controls for the demo ----------
app.MapGet("/chaos", (ChaosSwitch c) => Results.Ok(c));
app.MapPost("/chaos/partner/{percent:int}", (int percent, ChaosSwitch c) =>
{
    c.PartnerFailurePercent = Math.Clamp(percent, 0, 100);
    return Results.Ok(c);
});

app.Run();

