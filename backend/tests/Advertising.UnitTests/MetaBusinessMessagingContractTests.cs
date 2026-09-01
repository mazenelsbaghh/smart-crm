using System.Net;
using System.Text;
using System.Text.Json;
using Modules.Advertising.Infrastructure.Facebook;
using Xunit;

namespace Advertising.UnitTests;

public sealed class MetaBusinessMessagingContractTests
{
    [Fact]
    public async Task Waba_and_ctwa_clid_are_inside_user_data_with_business_messaging_source()
    {
        var handler = new CaptureHandler();
        var client = new MetaBusinessMessagingClient(new HttpClient(handler)
            { BaseAddress = new Uri("https://graph.facebook.com/v26.0/") });

        var result = await client.SendAsync("token", new("dataset-1", "waba-1", "click-1", "Purchase",
            "event-1", DateTime.UnixEpoch.AddSeconds(100), 250m, "EGP", "TEST123"));

        Assert.Equal(1, result.EventsReceived);
        Assert.EndsWith("dataset-1/events", handler.Path);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        var form = ParseForm(handler.Form!);
        using var document = JsonDocument.Parse(form["data"]);
        var item = document.RootElement[0];
        Assert.Equal("business_messaging", item.GetProperty("action_source").GetString());
        Assert.Equal("whatsapp", item.GetProperty("messaging_channel").GetString());
        Assert.Equal("waba-1", item.GetProperty("user_data").GetProperty("whatsapp_business_account_id").GetString());
        Assert.Equal("click-1", item.GetProperty("user_data").GetProperty("ctwa_clid").GetString());
        Assert.False(item.TryGetProperty("whatsapp_business_account_id", out _));
        Assert.Equal("TEST123", form["test_event_code"]);
    }

    private static Dictionary<string, string> ParseForm(string form) => form.Split('&').Select(item => item.Split('=', 2))
        .ToDictionary(item => Uri.UnescapeDataString(item[0].Replace('+', ' ')), item => Uri.UnescapeDataString(item[1].Replace('+', ' ')));

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? Form { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.PathAndQuery.TrimStart('/');
            Form = await request.Content!.ReadAsStringAsync(cancellationToken);
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"events_received\":1}", Encoding.UTF8, "application/json") };
        }
    }
}
