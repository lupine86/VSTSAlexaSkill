# VSTSAlexaSkill
Visual Studio Team Services Alexa Skill

This skill currently allows a user to get information about pull requests

You should be able to pull the source and run this locally if you have Visual Studio 2017 with Azure Cloud development enabled

To test first make an Http Get Request to the test endpoint

TestFunction: http://localhost:7071/api/TestFunction

After that try hitting the Alexa endpoint using a VSTS PAT token and  your favorite REST client (e.g. Postman) with the following configuration:

POST

Headers:

  Content-Type:application/json
  
  Authorization:Basic `<your PAT token>`
  
  Body: 
{
  "version": "1.0",
  "session": {
    "new": true,
    "sessionId": "amzn1.echo-api.session.0000000",
    "application": {
      "applicationId": "amzn1.ask.skill.00000000"
    }
  },
  "context": {
    "System": {
      "application": {
        "applicationId": "amzn1.ask.skill.00000000"
      },
      "device": {
        "deviceId": "amzn1.ask.device.00000",
        "supportedInterfaces": {}
      },
      "apiEndpoint": "https://api.amazonalexa.com"
    }
  },
  "request": {
    "type": "IntentRequest",
    "requestId": "amzn1.echo-api.request.000000",
    "locale": "en-US",
    "intent": {
      "name": "pullRequestStatus",
      "confirmationStatus": "NONE"
    }
  }
}

You should get a json response that has information about your pull requests:
{"version":"1.0","response":{"outputSpeech":{"type":"PlainText","text":"You have 3 active pull requests. PR test1, PR test 2, PR test 3."},"shouldEndSession":true}}
