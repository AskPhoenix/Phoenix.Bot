using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AdaptiveCards;
using Microsoft.AspNetCore.Identity;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.AdaptiveCards;
using Phoenix.Bot.Utilities.Channels.Facebook;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.State;
using Phoenix.Bot.Utilities.State.Options.Actions;
using Phoenix.DataHandle.Identity;
using Phoenix.DataHandle.Main;
using Phoenix.DataHandle.Main.Models;
using Phoenix.DataHandle.Repositories;
using Phoenix.DataHandle.Sms;

namespace Phoenix.Bot.Dialogs.Actions
{
    public class AssignmentsManagementDialog : ComponentDialog
    {
        private readonly IConfiguration configuration;
        private readonly AspNetUserRepository userRepository;
        private readonly IStatePropertyAccessor<UserData> userDataAccesor;
        private readonly UserManager<ApplicationUser> userManager;
        private readonly ApplicationStore appStore;

        public AssignmentsManagementDialog(IConfiguration configuration, PhoenixContext phoenixContext, ApplicationDbContext appContext,
            UserState userState, UserManager<ApplicationUser> userManager)
            : base(nameof(AssignmentsManagementDialog))
        {
            this.configuration = configuration;
            this.userRepository = new AspNetUserRepository(phoenixContext);
            this.userDataAccesor = userState.CreateProperty<UserData>(nameof(UserData));
            this.userManager = userManager;
            this.appStore = new ApplicationStore(appContext);

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));
            
            AddDialog(new WaterfallDialog(WaterfallNames.Actions.AssignmentsManagement.Extension,
                new WaterfallStep[]
                {
                    PasswordCheckStepAsync,
                    ForwardStepAsync,
                    ReminderStepAsync,
                    PostReminderStepAsync
                }));

            InitialDialogId = WaterfallNames.Actions.AssignmentsManagement.Extension;
        }

        #region Assignments Management Menu Waterfall Dialog

        private async Task<DialogTurnResult> PasswordCheckStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var userData = await userDataAccesor.GetAsync(stepContext.Context, null, cancellationToken);
            if (userData.RevealExtensionPassword && !string.IsNullOrEmpty(userData.TempExtensionPassword))
            {
                var options = stepContext.Options as AssignmentsManagementOptions;
                var user = await userRepository.Find(options.UserId);

                if (user.PhoneNumber.StartsWith("690000000"))
                {
                    await stepContext.Context.SendActivityAsync("Ο παρακάτω κωδικός είναι προσωπικός και δεν πρέπει να γνωστοποιείται σε άλλους:");
                    var card = new AdaptiveCard(new AdaptiveSchemaVersion(1, 2))
                    {
                        BackgroundImage = new AdaptiveBackgroundImage(AdaptiveCardsHelper.DarkBackgroundImageUrl)
                    };
                    card.Body.Add(new AdaptiveTextBlockHeaderLight("Κωδικός εργαλειοθήκης καθηγητών"));
                    card.Body.Add(new AdaptiveTextBlockHeaderLight(userData.TempExtensionPassword) { Separator = true });

                    Attachment attachment = new(contentType: AdaptiveCard.ContentType, content: JObject.FromObject(card));
                    await stepContext.Context.SendActivityAsync(MessageFactory.Attachment(attachment));
                }
                else
                {
                    await stepContext.Context.SendActivityAsync("Σύντομα θα λάβεις με SMS τον προσωπικό κωδικό σου για την εργαλειοθήκη καθηγητών.");
                    await stepContext.Context.SendActivityAsync("Σημειώνεται πως δεν πρέπει να γνωστοποιείται σε άλλους, " +
                        "ενώ συνιστάται η δημιουργία ενός νέου μετά την πρώτη σύνδεση.");
                    var sms = new SmsService(configuration["NexmoSMS:ApiKey"], configuration["NexmoSMS:ApiSecret"]);
                    await sms.SendAsync(user.PhoneNumber, $"Ο κωδικός σου για την εργαλειοθήκη καθηγητών είναι ο {userData.TempExtensionPassword}.");
                }

                userData.RevealExtensionPassword = false;
                await userDataAccesor.SetAsync(stepContext.Context, userData, cancellationToken);
            }

            return await stepContext.NextAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> ForwardStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var options = stepContext.Options as AssignmentsManagementOptions;
            var cardAction = new CardAction(type: ActionTypes.OpenUrl, title: options.ExtensionAction.ToFriendlyString(addEmoji: true) + "...");

            string signature = WebUtility.UrlEncode((await userRepository.Find(options.UserId)).GetHashSignature());
            if (options.ExtensionAction == BotAction.Exercises)
                cardAction.Value = AssignmentsManagementOptions.ExerciseButtonUrl;
            else
                cardAction.Value = AssignmentsManagementOptions.ExamButtonUrl;
            cardAction.Value += $"?signature={signature}&t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

            HeroCard heroCard = new()
            {
                Images = new CardImage[1] { new CardImage(tap: cardAction) },
                Buttons = new CardAction[1] { cardAction },
                Tap = cardAction
            };

            if (options.ExtensionAction == BotAction.Exercises)
            {
                heroCard.Title = "Μετάβαση στις εργασίες";
                heroCard.Subtitle = "Διαχειριστείτε τις εργασίες για το σπίτι και προσθέστε νέες.";
                heroCard.Images[0].Url = AssignmentsManagementOptions.ExerciseImageUrl;
            }
            else
            {
                heroCard.Title = "Μετάβαση στα διαγωνίσματα";
                heroCard.Subtitle = "Διαχειριστείτε τα επερχόμενα διαγωνίσματα και προσθέστε νέα.";
                heroCard.Images[0].Url = AssignmentsManagementOptions.ExamImageUrl;
            }

            Activity reply;
            if (stepContext.Context.Activity.ChannelId.ToLower() == LoginProvider.Facebook.GetProviderName().ToLower())
            {
                reply = MessageFactory.Text(null);
                reply.ChannelData = ChannelDataFactory.Attachment(heroCard, 
                    imageAspectRatio: "square", webviewHeightRatio: "tall", messengerExtensions: true);
            }
            else
                reply = (Activity)MessageFactory.Attachment(heroCard.ToAttachment());

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = reply,
                    RetryPrompt = reply,
                    Choices = new Choice[] { new Choice("🔙 Επιστροφή") }
                }, cancellationToken);
        }

        private async Task<DialogTurnResult> ReminderStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var options = stepContext.Options as AssignmentsManagementOptions;
            var userData = await userDataAccesor.GetAsync(stepContext.Context, null, cancellationToken);

            if (!string.IsNullOrEmpty(userData.TempExtensionPassword))
            {
                var appUser = await appStore.FindByIdAsync(options.UserId.ToString());
                if (await userManager.CheckPasswordAsync(appUser, userData.TempExtensionPassword))
                {
                    string msg = "Υπενθυμίζεται πως καλό θα ήταν να αλλάξετε τον κωδικό πρόσβασής σας στην εργαλειοθήκη καθηγητών.";
                    return await stepContext.PromptAsync(nameof(UnaccentedChoicePrompt), new ConfirmPromptOptions(msg), cancellationToken);
                }
                else
                {
                    userData.TempExtensionPassword = null;
                    await userDataAccesor.SetAsync(stepContext.Context, userData, cancellationToken);
                }
            }

            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

        private Task<DialogTurnResult> PostReminderStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return stepContext.EndDialogAsync(null, cancellationToken);
        }

        #endregion
    }
}
