using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Alexa.NET;
using Alexa.NET.Request;
using Alexa.NET.Request.Type;
using Alexa.NET.Response;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;

namespace AlexaAzureFunction
{
    public static class AlexaFunc
    {
        public static bool IsDebug
        {
            get
            {
                bool isDebug = false;
#if DEBUG
                isDebug = true;
#endif
                return isDebug;
            }
        }

        public static TraceWriter Log
        {
            get; set;

        }

        [FunctionName("Alexa")]
        public static async Task<HttpResponseMessage> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequestMessage req,
            TraceWriter log,
            CancellationToken cancellationToken)
        {
            Log = log;
            log.Info("HTTP trigger function processed a request.");
            log.Info($"Debug: {IsDebug}");

            string reqBody = await req?.Content?.ReadAsStringAsync();

            var isValidRequest = await AlexaRequestSecurity.ValidateRequest(req.Headers, reqBody, log);

            if (!isValidRequest && !IsDebug)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            SkillRequest skillRequest =  JsonConvert.DeserializeObject<SkillRequest>(reqBody);

            string identityName = "";
            string accessToken = "";
            try
            {
                if (IsDebug)
                {
                    identityName = "Debug Mode";
                    accessToken = AlexaRequestSecurity.GetEncodedPatToken(req.Headers);
                }
                else
                {
                    ClaimsPrincipal principal = await AlexaRequestSecurity.GetVSTSPrinciple(skillRequest, log, cancellationToken);
                    identityName = principal.Identity.Name;
                    accessToken = skillRequest.Session.User.AccessToken;
                }
            }
            catch (Exception exception)
            {
                return AlexaResponse(exception.Message);
            }
            
            var intentRequest = skillRequest.Request as IntentRequest;
            var intentName = intentRequest?.Intent?.Name;

            if (string.IsNullOrWhiteSpace(intentName))
            {
                return await VstsApiRequest(log, accessToken, "default");
            }
            
            // check the intent name to determine what you should do
            if (intentRequest.Intent.Name.Equals("Account"))
            {
                return AlexaResponse($"You are using {identityName}'s account");                
            }

            if (intentRequest.Intent.Name.Equals("pullRequestStatus"))
            {
                return await VstsApiRequest(log, accessToken, "pullRequestStatus");
            }

            return AlexaResponse($"I don't know how to do that yet");
        }

        private static HttpClient GetHttpClient(string accountUri, string accessToken)
        {
            var httpClient = new HttpClient()
            {
                BaseAddress = new Uri(accountUri),
            };

            httpClient.DefaultRequestHeaders.Accept.Clear();
            httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(IsDebug ? "basic" : "bearer", accessToken);

            return httpClient;
        }

        private static string GetMemberId(HttpClient httpClient)
        {
            string memberId = null;

            // This initial call is just to get a response so we can get the users VSTS ID.
            // We should use a fast API call here or find out if there is a better way to get this ID.
            // Maybe we can get it from a SPS call?

            HttpResponseMessage httpresponse = httpClient.GetAsync("_apis/projects?stateFilter=All&api-version=1.0").Result;

            //check to see if we have a succesful response
            if (httpresponse.IsSuccessStatusCode)
            {
                //set the viewmodel from the content in the response
                //var responseBody = await httpresponse.Content.ReadAsStringAsync();
                //dynamic obj1 = JsonConvert.DeserializeObject(responseBody);
                //countInfo = obj1.count;
                //log.Info($"Response Body: {countInfo}");

                // get the header that has member id which has a format of <GUID>:<account email>
                string vssUserData = httpresponse.Headers.FirstOrDefault(kvp => kvp.Key == "X-VSS-UserData").Value.FirstOrDefault() ?? "";

                memberId = vssUserData.Split(':')[0];
            }

            return memberId;
        }


        private static async Task<HttpResponseMessage> VstsApiRequest(TraceWriter log, string accessToken, string intent)
        {
            using (var httpClient = GetHttpClient("https://mseng.visualstudio.com", accessToken))
            {
                var memberId = GetMemberId(httpClient);
                if (memberId == null)
                {
                    return AlexaResponse($"There was a problem communicating with Azure DevOps");
                }

                PullRequest.Rootobject pullRequestRootObject = await GetPullRequests(httpClient, memberId);
                int pullRequestCount = pullRequestRootObject.count;
                
                if (pullRequestCount == 0)
                {
                    return AlexaResponse($"You don't have any active pull requests");
                }

                PlainTextOutputSpeech outputSpeech = new PlainTextOutputSpeech();
                //string firstName = (req.Request as IntentRequest)?.Intent.Slots.FirstOrDefault(s => s.Key == "FirstName").Value?.Value;
                string text = $"You have {pullRequestCount} active pull request{((pullRequestCount == 1) ? "" : "s")}. ";

                text +=  String.Join(", ", pullRequestRootObject.value.Select(pr => pr.title).ToArray());
                text += ".";
                

                return AlexaResponse(text);
            }
        }

        private static async Task<PullRequest.Rootobject> GetPullRequests(HttpClient httpClient, string memberId)
        {
            var httpResponseMessage = httpClient.GetAsync($"_apis/git/pullrequests?api-version=4.1&searchCriteria.creatorId={memberId}&searchCriteria.status=1").Result;
            var responseBody = await httpResponseMessage.Content.ReadAsStringAsync();
            httpResponseMessage.Dispose();
            PullRequest.Rootobject pullRequestRootObject = JsonConvert.DeserializeObject<PullRequest.Rootobject>(responseBody);
            return pullRequestRootObject;
        }

        private static HttpResponseMessage AlexaResponse(string value)
        {
            PlainTextOutputSpeech outputSpeech = new PlainTextOutputSpeech();
            outputSpeech.Text = value;
            var content = JsonConvert.SerializeObject(ResponseBuilder.Tell(outputSpeech));
            if (IsDebug)
            {
                Log.Info(content);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            };
        }
    }
}
