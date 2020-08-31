﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.Bot.Connector.SkillAuthentication
{
    /// <summary>
    /// Validates JWT tokens sent to and from a Skill.
    /// </summary>
    public class SkillValidation
    {
        /// <summary>
        /// TO SKILL FROM BOT and TO BOT FROM SKILL: Token validation parameters when connecting a bot to a skill.
        /// </summary>
        private static readonly TokenValidationParameters _tokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuers = new[]
                {
                    "https://sts.windows.net/d6d49420-f39b-4df7-a1dc-d59a935871db/", // Auth v3.1, 1.0 token
                    "https://login.microsoftonline.com/d6d49420-f39b-4df7-a1dc-d59a935871db/v2.0", // Auth v3.1, 2.0 token
                    "https://sts.windows.net/f8cdef31-a31e-4b4a-93e4-5f571e91255a/", // Auth v3.2, 1.0 token
                    "https://login.microsoftonline.com/f8cdef31-a31e-4b4a-93e4-5f571e91255a/v2.0", // Auth v3.2, 2.0 token
                    "https://sts.windows.net/cab8a31a-1906-4287-a0d8-4eef66b95f6e/", // Auth for US Gov, 1.0 token
                    "https://login.microsoftonline.us/cab8a31a-1906-4287-a0d8-4eef66b95f6e/v2.0" // Auth for US Gov, 2.0 token
                },
                ValidateAudience = false, // Audience validation takes place manually in code.
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(5),
                RequireSignedTokens = true
            };

        /// <summary>
        /// Determines if a given Auth header is from from a skill to bot or bot to skill request.
        /// </summary>
        /// <param name="authHeader">Bearer Token, in the "Bearer [Long String]" Format.</param>
        /// <returns>True, if the token was issued for a skill to bot communication. Otherwise, false.</returns>
        public static bool IsSkillToken(string authHeader)
        {
            if (!JwtTokenValidation.IsValidTokenFormat(authHeader))
            {
                return false;
            }

            // We know is a valid token, split it and work with it:
            // [0] = "Bearer"
            // [1] = "[Big Long String]"
            var bearerToken = authHeader.Split(' ')[1];

            // Parse the Big Long String into an actual token.
            var token = new JwtSecurityToken(bearerToken);

            return IsSkillClaim(token.Claims);
        }

        /// <summary>
        /// Checks if the given list of claims represents a skill.
        /// </summary>
        /// <remarks>
        /// A skill claim should contain:
        ///     An <see cref="AuthenticationConstants.VersionClaim"/> claim.
        ///     An <see cref="AuthenticationConstants.AudienceClaim"/> claim.
        ///     An <see cref="AuthenticationConstants.AppIdClaim"/> claim (v1) or an a <see cref="AuthenticationConstants.AuthorizedParty"/> claim (v2).
        /// And the appId claim should be different than the audience claim.
        /// When a channel (webchat, teams, etc.) invokes a bot, the <see cref="AuthenticationConstants.AudienceClaim"/>
        /// is set to <see cref="AuthenticationConstants.ToBotFromChannelTokenIssuer"/> but when a bot calls another bot,
        /// the audience claim is set to the appId of the bot being invoked.
        /// The protocol supports v1 and v2 tokens:
        /// For v1 tokens, the  <see cref="AuthenticationConstants.AppIdClaim"/> is present and set to the app Id of the calling bot.
        /// For v2 tokens, the  <see cref="AuthenticationConstants.AuthorizedParty"/> is present and set to the app Id of the calling bot.
        /// </remarks>
        /// <param name="claims">A list of claims.</param>
        /// <returns>True if the list of claims is a skill claim, false if is not.</returns>
        public static bool IsSkillClaim(IEnumerable<Claim> claims)
        {
            if (claims == null)
            {
                throw new ArgumentNullException(nameof(claims));
            }

            if (!claims.Any())
            {
                throw new UnauthorizedAccessException("SkillValidation.IsSkillClaim.claims parameter must contain at least one element.");
            }

            var claimsList = claims.ToList();

            var version = claimsList.FirstOrDefault(claim => claim.Type == AuthenticationConstants.VersionClaim);
            if (string.IsNullOrWhiteSpace(version?.Value))
            {
                // Must have a version claim.
                return false;
            }

            var audience = claimsList.FirstOrDefault(claim => claim.Type == AuthenticationConstants.AudienceClaim)?.Value;
            if (string.IsNullOrWhiteSpace(audience) || AuthenticationConstants.ToBotFromChannelTokenIssuer.Equals(audience, StringComparison.OrdinalIgnoreCase))
            {
                // The audience is https://api.botframework.com and not an appId.
                return false;
            }

            var appId = JwtTokenValidation.GetAppIdFromClaims(claimsList);
            if (string.IsNullOrWhiteSpace(appId))
            {
                return false;
            }

            // Skill claims must contain and app ID and the AppID must be different than the audience.
            return appId != audience;
        }

        /// <summary>
        /// Validates that the incoming Auth Header is a token sent from a bot to a skill or from a skill to a bot.
        /// </summary>
        /// <param name="authHeader">The raw HTTP header in the format: "Bearer [longString]".</param>
        /// <param name="credentials">The user defined set of valid credentials, such as the AppId.</param>
        /// <param name="httpClient">
        /// Authentication of tokens requires calling out to validate Endorsements and related documents. The
        /// HttpClient is used for making those calls. Those calls generally require TLS connections, which are expensive to
        /// setup and teardown, so a shared HttpClient is recommended.
        /// </param>
        /// <param name="channelId">The ID of the channel to validate.</param>
        /// <param name="authConfig">The authentication configuration.</param>
        /// <returns>A <see cref="ClaimsIdentity"/> instance if the validation is successful.</returns>
        public static async Task<ClaimsIdentity> AuthenticateChannelToken(string authHeader, ICredentialProvider credentials, HttpClient httpClient, string channelId, AuthenticationConfiguration authConfig)
        {
            if (authConfig == null)
            {
                throw new ArgumentNullException(nameof(authConfig));
            }

            var openIdMetadataUrl = AuthenticationConstants.ToBotFromEmulatorOpenIdMetadataUrl;

            var tokenExtractor = new JwtTokenExtractor(
                httpClient,
                _tokenValidationParameters,
                openIdMetadataUrl,
                AuthenticationConstants.AllowedSigningAlgorithms);

            var identity = await tokenExtractor.GetIdentityAsync(authHeader, channelId, authConfig.RequiredEndorsements).ConfigureAwait(false);

            await ValidateIdentityAsync(identity, credentials).ConfigureAwait(false);

            return identity;
        }

        internal static async Task ValidateIdentityAsync(ClaimsIdentity identity, ICredentialProvider credentials)
        {
            if (identity == null)
            {
                // No valid identity. Not Authorized.
                throw new UnauthorizedAccessException("Invalid Identity");
            }

            if (!identity.IsAuthenticated)
            {
                // The token is in some way invalid. Not Authorized.
                throw new UnauthorizedAccessException("Token Not Authenticated");
            }

            var versionClaim = identity.Claims.FirstOrDefault(c => c.Type == AuthenticationConstants.VersionClaim);
            if (versionClaim == null)
            {
                // No version claim
                throw new UnauthorizedAccessException($"'{AuthenticationConstants.VersionClaim}' claim is required on skill Tokens.");
            }

            // Look for the "aud" claim, but only if issued from the Bot Framework
            var audienceClaim = identity.Claims.FirstOrDefault(c => c.Type == AuthenticationConstants.AudienceClaim)?.Value;
            if (string.IsNullOrWhiteSpace(audienceClaim))
            {
                // Claim is not present or doesn't have a value. Not Authorized.
                throw new UnauthorizedAccessException($"'{AuthenticationConstants.AudienceClaim}' claim is required on skill Tokens.");
            }

            if (!await credentials.IsValidAppIdAsync(audienceClaim).ConfigureAwait(false))
            {
                // The AppId is not valid. Not Authorized.
                throw new UnauthorizedAccessException("Invalid audience.");
            }

            var appId = JwtTokenValidation.GetAppIdFromClaims(identity.Claims);
            if (string.IsNullOrWhiteSpace(appId))
            {
                // Invalid appId
                throw new UnauthorizedAccessException("Invalid appId.");
            }
        }
    }
}
