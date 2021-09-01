using Microsoft.AspNetCore.Mvc;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Controllers
{
    //Authentication?
    [Route("api/notify")]
    [ApiController]
    public class NotifyController : Controller
    {
        private readonly IBotFrameworkHttpAdapter _adapter;
        private readonly string _appId;

        public NotifyController(IBotFrameworkHttpAdapter adapter, IConfiguration configuration)
        {
            _adapter = adapter;
            _appId = configuration["MicrosoftAppId"] ?? string.Empty;
        }

        public async Task<IActionResult> Get()
        {
            var conversationReference = new ConversationReference()
            {
                Bot = new ChannelAccount(id: "1998322767104444"),
                ChannelId = "facebook",
                Conversation = new ConversationAccount(id: "660706657386972-1998322767104444"),
                ServiceUrl = "https://facebook.botframework.com/",
                //User = new ChannelAccount(id: "1824061630972169", name: "Θεόφιλος Σπύρου")
                User = new ChannelAccount(id: "660706657386972", name: "Μεταξάς Γαμβρέλης")
            };

            await ((BotAdapter)_adapter).ContinueConversationAsync(_appId, conversationReference, BotCallback, default(CancellationToken));

            // Let the caller know proactive messages have been sent
            return new ContentResult()
            {
                Content = "<html><body><h1>Proactive messages have been sent.</h1></body></html>",
                ContentType = "text/html",
                StatusCode = (int)HttpStatusCode.OK,
            };
        }

        private async Task BotCallback(ITurnContext turnContext, CancellationToken cancellationToken)
        {
            string broadcastMessage = "📢 Ανακοίνωση: Ο διάλογος Broadcast είναι έτοιμος!";
            var activity = MessageFactory.SuggestedActions(new string[1] { "🏠 Αρχική" }, broadcastMessage);

            activity.ChannelData = JObject.FromObject(new
            {
                //REGULAR, SILENT_PUSH, NO_PUSH
                notification_type = "REGULAR"
            });

            await turnContext.SendActivityAsync(activity);
        }
    }
}
