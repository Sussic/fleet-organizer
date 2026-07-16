using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FleetOrganizer.Infrastructure.Authentication;

internal static class LoopbackAuthorizationListener
{
    private const int MaximumRequestLineLength = 8_192;
    private const int MaximumHeaderLines = 100;

    public static async Task<string> WaitForCodeAsync(
        Uri redirectUri,
        string expectedState,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedState);

        var listener = new TcpListener(IPAddress.Loopback, redirectUri.Port);
        listener.Start(1);

        try
        {
            using var client = await listener
                .AcceptTcpClientAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(
                stream,
                Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4_096,
                leaveOpen: true);

            var requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(requestLine) ||
                requestLine.Length > MaximumRequestLineLength)
            {
                await SendResponseAsync(stream, success: false, cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException("EVE SSO returned an invalid callback request.");
            }

            for (var headerIndex = 0; headerIndex < MaximumHeaderLines; headerIndex++)
            {
                var header = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (header is null || header.Length == 0)
                {
                    break;
                }

                if (headerIndex == MaximumHeaderLines - 1)
                {
                    await SendResponseAsync(stream, success: false, cancellationToken).ConfigureAwait(false);
                    throw new InvalidOperationException("EVE SSO returned too many callback headers.");
                }
            }

            var requestParts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (requestParts.Length != 3 ||
                !string.Equals(requestParts[0], "GET", StringComparison.Ordinal))
            {
                await SendResponseAsync(stream, success: false, cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException("EVE SSO returned an unsupported callback request.");
            }

            var callbackUri = new Uri(redirectUri, requestParts[1]);
            if (!string.Equals(callbackUri.Scheme, redirectUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(callbackUri.Host, redirectUri.Host, StringComparison.OrdinalIgnoreCase) ||
                callbackUri.Port != redirectUri.Port ||
                !string.Equals(callbackUri.AbsolutePath, redirectUri.AbsolutePath, StringComparison.Ordinal))
            {
                await SendResponseAsync(stream, success: false, cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException("EVE SSO returned to an unexpected callback address.");
            }

            var query = ParseQuery(callbackUri.Query);
            if (query.TryGetValue("error", out _))
            {
                await SendResponseAsync(stream, success: false, cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException("EVE sign-in was cancelled or denied.");
            }

            if (!query.TryGetValue("state", out var returnedState) ||
                !CryptographicEquals(returnedState, expectedState))
            {
                await SendResponseAsync(stream, success: false, cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException("EVE sign-in returned an invalid security state.");
            }

            if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
            {
                await SendResponseAsync(stream, success: false, cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException("EVE sign-in did not return an authorization code.");
            }

            await SendResponseAsync(stream, success: true, cancellationToken).ConfigureAwait(false);
            return code;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static Dictionary<string, string> ParseQuery(string queryText)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var query = queryText.TrimStart('?');

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = pair.IndexOf('=');
            var encodedName = separatorIndex >= 0 ? pair[..separatorIndex] : pair;
            var encodedValue = separatorIndex >= 0 ? pair[(separatorIndex + 1)..] : string.Empty;
            var name = DecodeQueryComponent(encodedName);
            var value = DecodeQueryComponent(encodedValue);

            if (!values.TryAdd(name, value))
            {
                throw new InvalidOperationException("EVE SSO returned a duplicate callback value.");
            }
        }

        return values;
    }

    private static string DecodeQueryComponent(string value) =>
        Uri.UnescapeDataString(value.Replace('+', ' '));

    private static bool CryptographicEquals(string first, string second)
    {
        var firstBytes = Encoding.UTF8.GetBytes(first);
        var secondBytes = Encoding.UTF8.GetBytes(second);

        try
        {
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                firstBytes,
                secondBytes);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(firstBytes);
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(secondBytes);
        }
    }

    private static async Task SendResponseAsync(
        Stream stream,
        bool success,
        CancellationToken cancellationToken)
    {
        var title = success ? "Fleet Organizer sign-in complete" : "Fleet Organizer sign-in failed";
        var detail = success
            ? "You can close this browser tab and return to Fleet Organizer."
            : "Return to Fleet Organizer for details. You can close this browser tab.";
        var body =
            $"<!doctype html><html><head><meta charset=\"utf-8\"><title>{title}</title></head>" +
            $"<body style=\"font-family:Segoe UI,sans-serif;padding:3rem\"><h1>{title}</h1><p>{detail}</p></body></html>";
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var status = success ? "200 OK" : "400 Bad Request";
        var headers =
            $"HTTP/1.1 {status}\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Cache-Control: no-store\r\n" +
            "Connection: close\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(headers);

        await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bodyBytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
