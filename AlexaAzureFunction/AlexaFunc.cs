using System;
using System.Collections.Generic;
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
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using static AlexaVstsSkillAzureFunction.Security;

namespace AlexaVstsSkillAzureFunction
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
            [Table("AlexaUser")]CloudTable outputTable,
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

            //1. look up user id in cloud table
            //2. if you can't find an account and memberid then look up account info and get member id



            string identityName = "";
            AccessToken accessToken = null;
            try
            {
                var patToken = AlexaRequestSecurity.GetEncodedPatToken(req.Headers);
                if (IsDebug && patToken != null)
                {
                    identityName = "Debug Mode";
                    accessToken = new AccessToken("basic", patToken);

                }
                else
                {
                    // Would this need to be done more than once (if at all) we persisted the identityName?
                    // We could let VSTS take care of this validation
                    // Get the VSTS Principle from Oauth token
                    ClaimsPrincipal principal = await AlexaRequestSecurity.GetVSTSPrinciple(skillRequest, log, cancellationToken);
                    identityName = principal.Identity.Name;
                    accessToken = new AccessToken("bearer", skillRequest.Session.User.AccessToken);
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

            if (intentRequest.Intent.Name.Equals("queueBuilds"))
            {
                return await VstsApiRequest(log, accessToken, "queueBuilds");
            }

            return AlexaResponse($"I don't know how to do that yet");
        }

        private static HttpClient GetHttpClient(string accountUri, AccessToken accessToken)
        {
            var httpClient = new HttpClient()
            {
                BaseAddress = new Uri(accountUri),
            };

            httpClient.DefaultRequestHeaders.Accept.Clear();
            httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(accessToken.Scheme, accessToken.TokenValue);

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
                // get the header that has member id which has a format of <GUID>:<account email>
                string vssUserData = httpresponse.Headers.FirstOrDefault(kvp => kvp.Key == "X-VSS-UserData").Value.FirstOrDefault() ?? "";

                memberId = vssUserData.Split(':')[0];
            }

            return memberId;
        }


        private static async Task<HttpResponseMessage> VstsApiRequest(TraceWriter log, AccessToken accessToken, string intent)
        {
            using (var httpClient = GetHttpClient("https://mseng.visualstudio.com", accessToken))
            {
                var memberId = GetMemberId(httpClient);
                if (memberId == null)
                {
                    return AlexaResponse($"There was a problem communicating with the v. s. t. s. API");
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

                var policyEvaluationsList = new List<PolicyEvaluations.Evaluation>();

                var pullRequestsWithExpiredBuilds = pullRequestRootObject.value.Where(pr =>
                {
                    string artifactid = "vstfs:///CodeReview/CodeReviewId/" + pr.repository.project.id + "/" + pr.pullRequestId;
                    var policyEvaluations =  GetPolicyEvaluations(httpClient, pr.repository.project.id, artifactid).Result;
                    if (!policyEvaluations.Any(pe => pe.configuration.isBlocking && pe.configuration.isEnabled && pe.configuration.type.displayName == "Build" && pe.status == "rejected"))
                    {
                        var requeueList = policyEvaluations
                            .Where(pe => pe.configuration.isBlocking && pe.configuration.isEnabled && pe.configuration.type.displayName == "Build" && pe.context.isExpired)
                            .Select(pe => new PolicyEvaluations.Evaluation(){ EvaluationId = pe.evaluationId, ProjectId = pr.repository.project.id }).ToList();
                        if (requeueList.Count > 0)
                        {
                            policyEvaluationsList.AddRange(requeueList);
                            return true;
                        }
                        else
                        {
                            return false;
                        }
                        
                    }
                    else
                    {
                        return false;
                    }
                                  
                }).ToList();

                if (pullRequestsWithExpiredBuilds.Count == 1)
                {
                    text += $" It looks like {pullRequestsWithExpiredBuilds.First().title} has at last one expired build.";
                }
                else if (pullRequestsWithExpiredBuilds.Count > 1)
                {
                    text += $" You have {pullRequestsWithExpiredBuilds.Count} pull requests with expired builds.";
                }

                if (intent == "queueBuilds")
                {
                    text = "";

                    if (pullRequestsWithExpiredBuilds.Count == 0)
                    {
                        text = $"I couldn't find any pull requests with expired builds.";
                    }
                    else
                    {
                        foreach (var pe in policyEvaluationsList)
                        {
                            RequeueBuild(httpClient, pe);
                        }

                        text = $"I requeued {policyEvaluationsList.Count} build{(policyEvaluationsList.Count > 1 ? "s" : "")}";
                        if (pullRequestsWithExpiredBuilds.Count == 1)
                        {
                            text += $" for {pullRequestsWithExpiredBuilds.First().title}.";
                        }
                        else if (pullRequestsWithExpiredBuilds.Count > 1)
                        {
                            text += $" for {pullRequestsWithExpiredBuilds.Count} pull request{(pullRequestsWithExpiredBuilds.Count > 1 ? "s" : "")}.";
                        }
                    }
                }
                return AlexaResponse(text);
            }
        }

        private static void RequeueBuild(HttpClient httpClient, PolicyEvaluations.Evaluation pe)
        {
            var method = new HttpMethod("PATCH");

            var request = new HttpRequestMessage(method, $"{pe.ProjectId}/_apis/policy/evaluations/{pe.EvaluationId}?api-version=4.1-preview.1");
            var httpResponseMessage = httpClient.SendAsync(request);    
        }

        private static async Task<PolicyEvaluations.Value[]> GetPolicyEvaluations(HttpClient httpClient, string projectId, string artifactId)
        {
            var httpResponseMessage = httpClient.GetAsync($"{projectId}/_apis/policy/evaluations?artifactId={artifactId}&api-version=4.1-preview.1").Result;
            var responseBody = await httpResponseMessage.Content.ReadAsStringAsync();
            httpResponseMessage.Dispose();
            PolicyEvaluations.Rootobject policyEvaluations = JsonConvert.DeserializeObject<PolicyEvaluations.Rootobject>(responseBody);
            return policyEvaluations.value;
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
