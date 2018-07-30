using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace AlexaAzureFunction
{
    public static class Security
    {
        private static readonly string ISSUER = "https://sts.windows.net/72f988bf-86f1-41af-91ab-2d7cd011db47/";
        //private static readonly string AUDIENCE = "https://microsoft.onmicrosoft.com/06415cfa-7edb-418b-8a85-87d12e9ef390";
        private static readonly string AUDIENCE = "499b84ac-1321-427f-aa17-267ca6975798";
        private static readonly IConfigurationManager<OpenIdConnectConfiguration> _configurationManager;

        static Security()
        {
            HttpDocumentRetriever documentRetriever = new HttpDocumentRetriever();
            documentRetriever.RequireHttps = ISSUER.StartsWith("https://");

            _configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                $"{ISSUER}/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(),
                documentRetriever);
        }

        public static async Task<ClaimsPrincipal> ValidateTokenAsync(string value, TraceWriter log)
        {
            var config = await _configurationManager.GetConfigurationAsync(CancellationToken.None);
            var issuer = ISSUER;
            var audience = AUDIENCE;

            var validationParameter = new TokenValidationParameters()
            {
                RequireSignedTokens = true,
                ValidAudience = audience,
                ValidateAudience = true,
                ValidIssuer = issuer,
                ValidateIssuer = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                IssuerSigningKeys = config.SigningKeys
            };

            ClaimsPrincipal result = null;
            var tries = 0;

            while (result == null && tries <= 1)
            {
                try
                {
                    var handler = new JwtSecurityTokenHandler();
                    result = handler.ValidateToken(value, validationParameter, out var token);
                }
                catch (SecurityTokenSignatureKeyNotFoundException)
                {
                    // This exception is thrown if the signature key of the JWT could not be found.
                    // This could be the case when the issuer changed its signing keys, so we trigger a 
                    // refresh and retry validation.
                    _configurationManager.RequestRefresh();
                    tries++;
                }
                catch (SecurityTokenException e)
                {
                    log.Info(e.Message);
                    return null;
                }
            }

            return result;
        }
    }
}
