// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Live;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Checks the bearer token the plugin sends with every batch.
/// </summary>
internal static class IngestAuth
{
	/// <summary>
	/// Checks an <c>Authorization</c> header against the configured token.
	/// </summary>
	/// <param name="authorizationHeader">The header value, if the request had one.</param>
	/// <param name="token">The configured token. Empty rejects every request.</param>
	/// <returns><see langword="true"/> when the header is exactly <c>Bearer</c> followed by the token.</returns>
	public static bool IsAuthorized(string? authorizationHeader, string token)
	{
		if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(authorizationHeader))
		{
			return false;
		}

		byte[] expected = Encoding.UTF8.GetBytes("Bearer " + token);
		byte[] actual = Encoding.UTF8.GetBytes(authorizationHeader);
		return CryptographicOperations.FixedTimeEquals(actual, expected);
	}
}
