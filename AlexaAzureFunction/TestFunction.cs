
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
//using System.Linq;
using Microsoft.WindowsAzure.Storage.Table;
using System.Threading.Tasks;

namespace AlexaVstsSkillAzureFunction
{
    public class MyPoco : TableEntity
    {
        public string Text { get; set; }
    }


    public static class TestFunction
    {
        [FunctionName("TestFunction")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)]HttpRequest req,
            [Table("RandomName")]CloudTable outputTable,
            //ICollector<MyPoco> tableBinding,
            TraceWriter log)
        {
            log.Info("C# HTTP trigger function processed a request.");

            //foreach (var poco in pocos)
            //{
                //log.Info($"PK={poco.PartitionKey}, RK={poco.RowKey}, Text={poco.Text}");
            //}
            string name = req.Query["name"];

            var operation2 = TableOperation.InsertOrReplace(new MyPoco() { PartitionKey = "partition1", RowKey = name,  Text = name });

            //outputTable.ExecuteAsync(operation2);

            var operation = TableOperation.Retrieve<MyPoco>("partition1", name);

            var myResult = await outputTable.ExecuteAsync(operation);
            
            //if (myResult.)

            //log.Info($"Result {myResult.Text}");

            string requestBody = new StreamReader(req.Body).ReadToEnd();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            name = name ?? data?.name;
            if (name != null)
            {
                //tableBinding.Add(new MyPoco() { Text = name });
            }
            return name != null
                ? (ActionResult)new OkObjectResult($"Hello, {name}")
                : new BadRequestObjectResult("Please pass a name on the query string or in the request body");
        }
    }
}
