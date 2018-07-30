
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using System.Threading.Tasks;
using Alexa.NET.Response;
using Alexa.NET.Request;
using Alexa.NET.Request.Type;
using Alexa.NET;
using System.Linq;
using System.Security.Claims;
using System.Net.Http;
using System.Net.Http.Headers;
using System;
using System.Net;
using System.Collections.Generic;
//using Microsoft.TeamFoundation.SourceControl.WebApi;
//using Microsoft.VisualStudio.Services.WebApi;
//using Microsoft.VisualStudio.Services.Client;

namespace AlexaAzureFunction
{
    public static class AlexaFunc
    {
        [FunctionName("Alexa")]
        public static async Task<HttpResponseMessage> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequestMessage req,
            TraceWriter log)
        {
            log.Info("HTTP trigger function processed a request.");
            string reqBody = await req?.Content?.ReadAsStringAsync();

            //HttpResponseMessage httpRespoonse = req.CreateResponse();

            req.Headers.TryGetValues("SignatureCertChainUrl", out var signatureChainUrls);
            
            var signatureChainUrl = signatureChainUrls == null ? null : signatureChainUrls.FirstOrDefault();
            
            if (String.IsNullOrWhiteSpace(signatureChainUrl))
            { 
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }
            log.Info($"signatureChainUrl: {signatureChainUrl}");

            Uri certUrl;
            try
            {
                certUrl = new Uri(signatureChainUrl);
            }
            catch
            {
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }
            
            // Verify SignatureCertChainUrl is Signature
            req.Headers.TryGetValues("Signature", out var signatures);

            var signature = signatures == null ? null : signatures.FirstOrDefault();
            if (String.IsNullOrWhiteSpace(signature))
            {
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }
            log.Info($"signature: {signature}");

            if (String.IsNullOrWhiteSpace(reqBody))
            {
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }
            var valid = await AlexaRequestSecurity.Verify(signature, certUrl, reqBody, log);
            if (!valid)
            {

                return req.CreateResponse(HttpStatusCode.BadRequest);
            }
            //log.Info($"valid request checked");
            SkillRequest skillRequest =  JsonConvert.DeserializeObject<SkillRequest>(reqBody);

            SkillResponse response = null;
            if (skillRequest?.Session?.User?.AccessToken == null)
            {
                return AlexaResponse("There was a problem with authentication");
            }
            log.Info(reqBody);
            log.Info($"we have a token");
            ClaimsPrincipal principal = await Security.ValidateTokenAsync(skillRequest.Session.User.AccessToken, log);

            if (principal == null)
            {
                return AlexaResponse("There was a problem with validating your authentication information");
                
            }
            log.Info($"we have a principal");
            
            // do some intent-based stuff
            var intentRequest = skillRequest.Request as IntentRequest;

            log.Info($"we have an intent");


            if (intentRequest?.Intent?.Name == null)
            {
                return await defaultSkillResponse(log, skillRequest);
                //return AlexaResponse("There was a problem determining the intent");
            }
            // check the name to determine what you should do
            if (intentRequest.Intent.Name.Equals("Account"))
            {
                return AlexaResponse($"You are using {principal.Identity.Name}'s account");
                
            }

            log.Info($"we have an intent name");

            if (intentRequest.Intent.Name.Equals("pullRequestStatus"))
            {
                return await defaultSkillResponse(log, skillRequest);

            }

            return AlexaResponse($"I don't know how to do that yet");
            //return ResponseBuilder.Tell($"I don't know how to do that yet");
            
        }

        private static async Task<HttpResponseMessage> defaultSkillResponse(TraceWriter log, SkillRequest skillRequest)
        {
            using (var httpClient = new HttpClient())
            {
                httpClient.BaseAddress = new Uri("https://mseng.visualstudio.com");  //url of our account
                httpClient.DefaultRequestHeaders.Accept.Clear();
                httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", skillRequest.Session.User.AccessToken);

                //connect to the REST endpoint  

                // this initial call is just to get a response so we can get the users VSTS ID
                // We should use a fast API call here or find out if there is a datacontribution api that returns this info somehow
                // maybe just use the contribution api and get whatever ruslan is pulling down for _pulls
                HttpResponseMessage httpresponse = httpClient.GetAsync("_apis/projects?stateFilter=All&api-version=1.0").Result;

                //check to see if we have a succesful response
                if (httpresponse.IsSuccessStatusCode)
                {
                    //set the viewmodel from the content in the response
                    //var responseBody = await httpresponse.Content.ReadAsStringAsync();
                    //dynamic obj1 = JsonConvert.DeserializeObject(responseBody);
                    //countInfo = obj1.count;
                    //log.Info($"Response Body: {countInfo}");

                    // get the header that has member id
                    var memberid = httpresponse.Headers.FirstOrDefault(kvp => kvp.Key == "X-VSS-UserData").Value.FirstOrDefault().Split(':')[0];

                    var httpResponse2 = httpClient.GetAsync($"_apis/git/pullrequests?api-version=4.1&searchCriteria.creatorId={memberid}&searchCriteria.status=1").Result;
                    var responseBody = await httpResponse2.Content.ReadAsStringAsync();
                    PullRequest.Rootobject obj1 = JsonConvert.DeserializeObject<PullRequest.Rootobject>(responseBody);
                    int countInfo = obj1.count;
                    log.Info($"Response Body: {countInfo}");

                    if (countInfo == 0)
                    {
                        // return ResponseBuilder.Tell($"You don't have any active pull requests");
                        return AlexaResponse($"You don't have any active pull requests");
                    }

                    PlainTextOutputSpeech outputSpeech = new PlainTextOutputSpeech();
                    //string firstName = (req.Request as IntentRequest)?.Intent.Slots.FirstOrDefault(s => s.Key == "FirstName").Value?.Value;
                    string text = $"You have {countInfo} active pull request{((countInfo == 1) ? "" : "s")}. ";

                    foreach (PullRequest.Value pr in obj1.value)
                    {
                        text += $"{pr.title}. ";
                    }

                    outputSpeech.Text = text;
                    // return ResponseBuilder.Tell(outputSpeech);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonConvert.SerializeObject(ResponseBuilder.Tell(outputSpeech)))
                    };


                }
                else
                {
                    return AlexaResponse($"There was a problem communicating with Azure DevOps");
                    //return ResponseBuilder.Tell("There was a problem communicating with Azure DevOps");
                }

            }
        }

        private static HttpResponseMessage AlexaResponse(string value)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonConvert.SerializeObject(ResponseBuilder.Tell(value)))
            };
        }
    }
}
