using AdaptiveCards;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Newtonsoft.Json.Linq;
using Phoenix.Bot.Utilities.AdaptiveCards;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.State.Options.Actions;
using Phoenix.DataHandle.Main.Models;
using Phoenix.DataHandle.Repositories;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs.Actions
{
    public class AccessDialog : ComponentDialog
    {
        private readonly AspNetUserRepository userRepository;

        public AccessDialog(PhoenixContext phoenixContext)
            : base(nameof(AccessDialog))
        {
            this.userRepository = new AspNetUserRepository(phoenixContext);

            AddDialog(new WaterfallDialog(WaterfallNames.Actions.Access.Top,
                new WaterfallStep[]
                {
                    AffiliatedUsersStepAsync
                }));

            InitialDialogId = WaterfallNames.Actions.Access.Top;
        }

        #region Top Waterfall Dialog

        private async Task<DialogTurnResult> AffiliatedUsersStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var options = stepContext.Options as ActionOptions;
            var affiliatedUsers = userRepository.FindChildren(options.UserId).OrderBy(u => u.User.FullName);

            var card = new AdaptiveCard(new AdaptiveSchemaVersion(1, 2))
            {
                BackgroundImage = new AdaptiveBackgroundImage(AdaptiveCardsHelper.DarkBackgroundImageUrl)
            };
            card.Body.Add(new AdaptiveTextBlockHeaderLight("Κωδικοί επαλήθευσης"));
            card.Body.Add(new AdaptiveTextBlockHeaderLight("Ισχύουν για τα επόμενα 5 λεπτά και είναι μοναδικοί ανά χρήστη")
            {
                Size = AdaptiveTextSize.Medium 
            });
            card.Body.Add(new AdaptiveRichFactSetLight("Όνομα:", "Κωδικός:"));

            foreach (var affUser in affiliatedUsers)
            {
                string initials = affUser.User.FirstName.Substring(0, 1) + affUser.User.LastName.Substring(0, 1);
                string code = DialogsHelper.GenerateVerificationCode(initials, digitsNum: 4);
                card.Body.Add(new AdaptiveRichFactSetLight("- " + affUser.User.FullName, code, separator: true));

                affUser.User.IdentifierCode = code;
                affUser.User.IdentifierCodeCreatedAt = DateTimeOffset.UtcNow;
                userRepository.Update(affUser);
            }

            Attachment attachment = new(contentType: AdaptiveCard.ContentType, content: JObject.FromObject(card));

            string codeForm = affiliatedUsers.Count() == 1 ? "τον κωδικό" : "τους κωδικούς";
            await stepContext.Context.SendActivityAsync(MessageFactory.Attachment(attachment, 
                text: $"Παρακάτω θα βρεις {codeForm} επαλήθευσης που ισχύουν μόνο για τον αντίστοιχο χρήστη τους:"));

            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

        #endregion
    }
}
