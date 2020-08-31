﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Microsoft.Bot.Connector.SkillAuthentication
{
    /// <summary>
    /// Contains helper methods for authenticating incoming HTTP requests.
    /// </summary>
    public static class JwtTokenValidation
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// Authenticates the request and adds the activity's <see cref="Activity.ServiceUrl"/>
        /// to the set of trusted URLs.
        /// </summary>
        /// <param name="activity">The activity.</param>
        /// <param name="authHeader">The authentication header.</param>
        /// <param name="credentials">The bot's credential provider.</param>
        /// <param name="authConfig">The optional authentication configuration.</param>
        /// <param name="httpClient">The HTTP client.</param>
        /// <returns>A task that represents the work queued to execute.</returns>
        /// <remarks>If the task completes successfully, the result contains the claims-based
        /// identity for the request.</remarks>
        public static async Task<ClaimsIdentity> AuthenticateRequest(IActivity activity, string authHeader, ICredentialProvider credentials, AuthenticationConfiguration authConfig, HttpClient httpClient = null)
        {
            if (authConfig == null)
            {
                throw new ArgumentNullException(nameof(authConfig));
            }

            if (string.IsNullOrWhiteSpace(authHeader))
            {
                var isAuthDisabled = await credentials.IsAuthenticationDisabledAsync().ConfigureAwait(false);
                if (isAuthDisabled)
                {
                    // In the scenario where Auth is disabled, we still want to have the
                    // IsAuthenticated flag set in the ClaimsIdentity. To do this requires
                    // adding in an empty claim.
                    return new ClaimsIdentity(new List<Claim>(), "anonymous");
                }

                // No Auth Header. Auth is required. Request is not authorized.
                throw new UnauthorizedAccessException();
            }

            var claimsIdentity = await ValidateAuthHeader(authHeader, credentials, activity.ChannelId, authConfig, activity.ServiceUrl, httpClient ?? _httpClient).ConfigureAwait(false);

            MicrosoftAppCredentials.TrustServiceUrl(activity.ServiceUrl);

            return claimsIdentity;
        }

        /// <summary>
        /// Validates the authentication header of an incoming request.
        /// </summary>
        /// <param name="authHeader">The authentication header to validate.</param>
        /// <param name="credentials">The bot's credential provider.</param>
        /// <param name="channelId">The ID of the channel that sent the request.</param>
        /// <param name="authConfig">The authentication configuration.</param>
        /// <param name="serviceUrl">The service URL for the activity.</param>
        /// <param name="httpClient">The HTTP client.</param>
        /// <returns>A task that represents the work queued to execute.</returns>
        /// <remarks>If the task completes successfully, the result contains the claims-based
        /// identity for the request.</remarks>
        public static async Task<ClaimsIdentity> ValidateAuthHeader(string authHeader, ICredentialProvider credentials, string channelId, AuthenticationConfiguration authConfig, string serviceUrl = null, HttpClient httpClient = null)
        {
            if (string.IsNullOrEmpty(authHeader))
            {
                throw new ArgumentNullException(nameof(authHeader));
            }

            if (authConfig == null)
            {
                throw new ArgumentNullException(nameof(authConfig));
            }

            httpClient = httpClient ?? _httpClient;

            var identity = await AuthenticateToken(authHeader, credentials, channelId, authConfig, serviceUrl, httpClient);

            await ValidateClaimsAsync(authConfig, identity.Claims).ConfigureAwait(false);

            return identity;
        }

        /// <summary>
        /// Gets the AppId from a claims list.
        /// </summary>
        /// <remarks>
        /// In v1 tokens the AppId is in the the <see cref="AuthenticationConstants.AppIdClaim"/> claim.
        /// In v2 tokens the AppId is in the azp <see cref="AuthenticationConstants.AuthorizedParty"/> claim.
        /// If the <see cref="AuthenticationConstants.VersionClaim"/> is not present, this method will attempt to
        /// obtain the attribute from the <see cref="AuthenticationConstants.AppIdClaim"/> or if present.
        /// </remarks>
        /// <param name="claims">A list of <see cref="Claim"/> instances.</param>
        /// <returns>The value of the appId claim if found (null if it can't find a suitable claim).</returns>
        public static string GetAppIdFromClaims(IEnumerable<Claim> claims)
        {
            if (claims == null)
            {
                throw new ArgumentNullException(nameof(claims));
            }

            var claimsList = claims as IList<Claim> ?? claims.ToList();
            string appId = null;

            // Depending on Version, the is either in the
            // appid claim (Version 1) or the Authorized Party claim (Version 2).
            var tokenVersion = claimsList.FirstOrDefault(claim => claim.Type == AuthenticationConstants.VersionClaim)?.Value;
            if (string.IsNullOrWhiteSpace(tokenVersion) || tokenVersion == "1.0")
            {
                // either no Version or a version of "1.0" means we should look for
                // the claim in the "appid" claim.
                var appIdClaim = claimsList.FirstOrDefault(c => c.Type == AuthenticationConstants.AppIdClaim);
                appId = appIdClaim?.Value;
            }
            else if (tokenVersion == "2.0")
            {
                // "2.0" puts the AppId in the "azp" claim.
                var appZClaim = claimsList.FirstOrDefault(c => c.Type == AuthenticationConstants.AuthorizedParty);
                appId = appZClaim?.Value;
            }

            return appId;
        }

        /// <summary>
        /// Validates the identity claims against the <see cref="ClaimsValidator"/> in <see cref="AuthenticationConfiguration"/> if present. 
        /// </summary>
        /// <param name="authConfig">An <see cref="AuthenticationConfiguration"/> instance.</param>
        /// <param name="claims">The list of claims to validate.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        /// <exception cref="UnauthorizedAccessException">If the validation returns false.</exception>
        internal static async Task ValidateClaimsAsync(AuthenticationConfiguration authConfig, IEnumerable<Claim> claims)
        {
            if (authConfig.ClaimsValidator == null)
            {
                throw new UnauthorizedAccessException("JwtTokenValidation.ValidateClaimsAsync.authConfig must have a ClaimsValidator.");
            }

            var claimsList = claims as IList<Claim> ?? claims.ToList();
            if (!claims.Any())
            {
                throw new UnauthorizedAccessException("JwtTokenValidation.ValidateClaimsAsync.claims parameter must contain at least one element.");
            }

            // Call the validation method (it should throw an exception if the validation fails)
            await authConfig.ClaimsValidator.ValidateClaimsAsync(claimsList).ConfigureAwait(false);
        }

        /// <summary>
        /// Internal helper to check if the token has the shape we expect "Bearer [big long string]".
        /// </summary>
        /// <param name="authHeader">A string containing the token header.</param>
        /// <returns>True if the token is valid, false if not.</returns>
        internal static bool IsValidTokenFormat(string authHeader)
        {
            if (string.IsNullOrWhiteSpace(authHeader))
            {
                // No token, not valid.
                return false;
            }

            var parts = authHeader.Split(' ');
            if (parts.Length != 2)
            {
                // Tokens MUST have exactly 2 parts. If we don't have 2 parts, it's not a valid token
                return false;
            }

            // We now have an array that should be:
            // [0] = "Bearer"
            // [1] = "[Big Long String]"
            var authScheme = parts[0];
            if (!authScheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
            {
                // The scheme MUST be "Bearer"
                return false;
            }

            return true;
        }

        /// <summary>
        /// Authenticates the auth header token from the request.
        /// </summary>
        private static async Task<ClaimsIdentity> AuthenticateToken(string authHeader, ICredentialProvider credentials, string channelId, AuthenticationConfiguration authConfig, string serviceUrl, HttpClient httpClient)
        {
            if (SkillValidation.IsSkillToken(authHeader))
            {
                return await SkillValidation.AuthenticateChannelToken(authHeader, credentials, httpClient, channelId, authConfig).ConfigureAwait(false);
            }

            if (EmulatorValidation.IsTokenFromEmulator(authHeader))
            {
                return await EmulatorValidation.AuthenticateEmulatorToken(authHeader, credentials, httpClient, channelId, authConfig).ConfigureAwait(false);
            }

            // No empty or null check. Empty can point to issues. Null checks only.
            if (serviceUrl != null)
            {
                return await ChannelValidation.AuthenticateChannelToken(authHeader, credentials, serviceUrl, httpClient, channelId, authConfig).ConfigureAwait(false);
            }

            return await ChannelValidation.AuthenticateChannelToken(authHeader, credentials, httpClient, channelId, authConfig).ConfigureAwait(false);
        }
    }
}
