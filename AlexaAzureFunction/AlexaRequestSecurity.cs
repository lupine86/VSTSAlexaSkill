using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alexa.NET.Request;
using Microsoft.Azure.WebJobs.Host;

namespace AlexaAzureFunction
{
    /// <summary>
    /// This class holds verification methods needed to authorize requests from an Alexa backend
    /// </summary>
    public static class AlexaRequestSecurity
    {

        public static async Task<ClaimsPrincipal> GetVSTSPrinciple(SkillRequest skillRequest, TraceWriter log, CancellationToken cancellationToken)
        {

            if (skillRequest?.Session?.User?.AccessToken == null)
            {
                throw new HttpRequestException("There was a problem with authentication.  No AccessToken available.");
            }
            ClaimsPrincipal principal = await Security.ValidateTokenAsync(skillRequest.Session.User.AccessToken, log, cancellationToken);

            if (principal == null)
            {
                throw new HttpRequestException("There was a problem with validating your authentication information");
            }

            if (String.IsNullOrWhiteSpace(principal?.Identity?.Name))
            {
                throw new HttpRequestException("There was a problem identifying the account user");
            }

            return principal;
        }

        public static async Task<bool> ValidateRequest(HttpRequestHeaders requestHeaders, string requestBody, TraceWriter log)
        {
            requestHeaders.TryGetValues("SignatureCertChainUrl", out var signatureChainUrls);
            var signatureChainUrl = signatureChainUrls == null ? null : signatureChainUrls.FirstOrDefault();
            if (String.IsNullOrWhiteSpace(signatureChainUrl))
            {
                return false;
            }

            Uri certUrl;
            try
            {
                certUrl = new Uri(signatureChainUrl);
            }
            catch
            {
                return false;
            }

            requestHeaders.TryGetValues("Signature", out var signatures);

            var signature = signatures == null ? null : signatures.FirstOrDefault();
            if (String.IsNullOrWhiteSpace(signature))
            {
                return false;
            }

            if (String.IsNullOrWhiteSpace(requestBody))
            {
                return false;
            }
            var valid = await AlexaRequestSecurity.Verify(signature, certUrl, requestBody, log);
            if (!valid)
            {

                return false;
            }

            return true;
        }

        internal static string GetEncodedPatToken(HttpRequestHeaders headers)
        {
            string patToken = headers?.Authorization?.Parameter?.ToString();

            if (String.IsNullOrWhiteSpace(patToken))
            {
                throw new HttpRequestException("In debug mode you must include a PAT token in the basic authorization header");
            }

            return patToken;
        }

        /// <summary>
        /// Verfiy runs through all verification steps
        /// </summary>
        /// <param name="encodedSignature"></param>
        /// <param name="certificatePath"></param>
        /// <param name="body"></param>
        /// <returns></returns>
        public static async Task<bool> Verify(string encodedSignature, Uri certificatePath, string body, TraceWriter log)
        {
            log.Info($"Verify Alexa Request...");
            if (!VerifyCertificateUrl(certificatePath))
            {
                return false;
            }
            log.Info($"Valid Cert Path");
            var certificate = await GetCertificate(certificatePath);
            if (!ValidSigningCertificate(certificate) || !VerifyChain(certificate))
            {
                return false;
            }
            log.Info($"Valid Signed Cert");
            if (!AssertHashMatch(certificate, encodedSignature, body))
            {
                return false;
            }
            log.Info($"Body is signature match");
            return true;
        }

        /// <summary>
        /// Checks if the body has been modified making the signature invalid
        /// </summary>
        /// <param name="certificate"></param>
        /// <param name="encodedSignature"></param>
        /// <param name="body"></param>
        /// <returns></returns>
        public static bool AssertHashMatch(X509Certificate2 certificate, string encodedSignature, string body)
        {
            byte[] signature;
            try
            {
                signature = Convert.FromBase64String(encodedSignature);
            }
            catch
            {
                return false;
            }
            var rsa = certificate.GetRSAPublicKey();

            return rsa.VerifyData(Encoding.UTF8.GetBytes(body), signature, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        }

        /// <summary>
        /// Download the certificate from the server
        /// </summary>
        /// <param name="certificatePath"></param>
        /// <returns></returns>
        public static async Task<X509Certificate2> GetCertificate(Uri certificatePath)
        {
            var response = await new HttpClient().GetAsync(certificatePath);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            return new X509Certificate2(bytes);
        }

        /// <summary>
        /// Verify the certificate chain
        /// </summary>
        /// <param name="certificate"></param>
        /// <returns></returns>
        public static bool VerifyChain(X509Certificate2 certificate)
        {
            X509Chain certificateChain = new X509Chain();
            certificateChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            return certificateChain.Build(certificate);
        }

        /// <summary>
        /// Check if certificate is valid for this point in time
        /// </summary>
        /// <param name="certificate"></param>
        /// <returns></returns>
        private static bool ValidSigningCertificate(X509Certificate2 certificate)
        {
            return DateTime.Now < certificate.NotAfter && DateTime.Now > certificate.NotBefore &&
                   certificate.GetNameInfo(X509NameType.SimpleName, false) == "echo-api.amazon.com";
        }

        /// <summary>
        /// Verify that the certificate is stored in the right place
        /// </summary>
        /// <param name="certificate"></param>
        /// <returns></returns>
        public static bool VerifyCertificateUrl(Uri certificate)
        {
            return certificate.Scheme == "https" &&
                certificate.Host == "s3.amazonaws.com" &&
                certificate.LocalPath.StartsWith("/echo.api") &&
                certificate.IsDefaultPort;
        }
    }
}