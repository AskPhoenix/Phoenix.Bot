using AdaptiveCards;
using Microsoft.Bot.Schema;
using Newtonsoft.Json.Linq;
using Phoenix.Bot.Utilities.AdaptiveCards;

namespace Phoenix.Bot.Dialogs.Actions
{
    public class AccessDialog : StateDialog
    {
        private readonly OneTimeCodeRepository _otcRepository;

        public AccessDialog(
            UserState userState,
            ConversationState convState,
            ApplicationUserManager userManager,
            PhoenixContext phoenixContext)
            : base(userState, convState, userManager, phoenixContext, nameof(AccessDialog))
        {
            _otcRepository = new(phoenixContext);

            AddDialog(new WaterfallDialog(WaterfallNames.Actions.Access.Top,
                new WaterfallStep[]
                {
                    AffiliatedUsersStepAsync
                }));

            InitialDialogId = WaterfallNames.Actions.Access.Top;
        }

        #region Top Waterfall Dialog

        private async Task<DialogTurnResult> AffiliatedUsersStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var card = new AdaptivePhoenixCard(new AdaptiveTextBlockHeaderLight[]
            {
                new("Κωδικοί επαλήθευσης"),
                new("Ισχύουν για τα επόμενα 5 λεπτά και είναι μοναδικοί ανά χρήστη")
                {
                    Size = AdaptiveTextSize.Medium
                }
            });

            var affiliatedUsers = UData.PhoenixUser!.Children;
            var codes = new HashSet<int>(affiliatedUsers.Count);

            foreach (var affUser in affiliatedUsers)
            {
                while (!codes.Add(CodeGenHelper.GenerateCode(5)));
                
                card.Body.Add(new AdaptiveRichFactSetLight(affUser.FullName, $"{codes.Last(), 10}"));

                await _otcRepository.CreateAsync(new()
                {
                    Purpose = OneTimeCodePurpose.Identification,
                    Token = codes.Last().ToString(),
                    UserId = affUser.AspNetUserId,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(5)
                }, canTkn);
            }

            var attachment = new Attachment(contentType: AdaptiveCard.ContentType,
                content: JObject.FromObject(card));

            await stepCtx.Context.SendActivityAsync(MessageFactory.Attachment(attachment, 
                text: $"Χρησιμοποιείστε τους παρακάτω κωδικούς για την επαλήθευση του μαθητή στον οποίο αντιστοιχούν:"),
                cancellationToken: canTkn);

            return await stepCtx.EndDialogAsync(null, canTkn);
        }

        #endregion
    }
}
