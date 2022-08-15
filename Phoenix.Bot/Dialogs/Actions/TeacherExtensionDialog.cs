using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Bot.Schema;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.AdaptiveCards;
using Phoenix.Bot.Utilities.Channels.Facebook;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.State;
using Phoenix.Bot.Utilities.State.Options.Actions;
using Phoenix.DataHandle.Identity;
using Phoenix.DataHandle.Main.Models;
using Phoenix.DataHandle.Main.Types;
using Phoenix.DataHandle.Senders;
using System.Net;

namespace Phoenix.Bot.Dialogs.Actions
{
    public class TeacherExtensionDialog : StateDialog
    {
        private readonly SmsSender _smsSender;
        private readonly IStatePropertyAccessor<AccessData> _accessDataAcsr;

        public TeacherExtensionDialog(
            UserState userState,
            ConversationState convState,
            ApplicationUserManager userManager,
            PhoenixContext phoenixContext,
            SmsSender smsSender)
            : base(userState, convState, userManager, phoenixContext, nameof(TeacherExtensionDialog))
        {
            _smsSender = smsSender;
            _accessDataAcsr = userState.CreateProperty<AccessData>(nameof(AccessData));

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));
            
            AddDialog(new WaterfallDialog(WaterfallNames.Actions.TeacherExtension.Extension,
                new WaterfallStep[]
                {
                    PasswordCheckStepAsync,
                    ForwardStepAsync,
                    PostReminderStepAsync
                }));

            InitialDialogId = WaterfallNames.Actions.TeacherExtension.Extension;
        }

        #region Teacher Extension Menu Waterfall Dialog

        private async Task<DialogTurnResult> PasswordCheckStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var accessData = await _accessDataAcsr.GetAsync(stepCtx.Context, () => new(), canTkn);

            if (!string.IsNullOrEmpty(accessData.TeacherPassword))
            {
                var options = (TeacherExtensionOptions)stepCtx.Options;

                if (UData.AppUser!.PhoneNumber.StartsWith("69000000"))
                {
                    await stepCtx.Context.SendActivityAsync("Ο παρακάτω κωδικός είναι προσωπικός και δεν πρέπει να γνωστοποιείται σε άλλους:");
                    
                    var card = new AdaptivePhoenixCard(new AdaptiveTextBlockHeaderLight[] 
                    {
                        new("Κωδικός εργαλειοθήκης καθηγητών"),
                        new(accessData.TeacherPassword) { Separator = true }
                    });

                    await stepCtx.Context.SendActivityAsync(card.ToActivity());
                }
                else
                {
                    await stepCtx.Context.SendActivityAsync("Σύντομα θα λάβεις με SMS τον προσωπικό κωδικό σου για την εργαλειοθήκη καθηγητών.");
                    await stepCtx.Context.SendActivityAsync("Σημειώνεται πως δεν πρέπει να γνωστοποιείται σε άλλους, " +
                        "ενώ συνιστάται η δημιουργία ενός νέου μετά την πρώτη σύνδεση.");
                    
                    await _smsSender.SendAsync(UData.AppUser.PhoneNumber, $"Ο κωδικός σου για την εργαλειοθήκη καθηγητών είναι ο {accessData.TeacherPassword}.");
                }

                accessData.TeacherPassword = null;
                await _accessDataAcsr.SetAsync(stepCtx.Context, accessData, canTkn);
            }

            return await stepCtx.NextAsync(null, canTkn);
        }

        private async Task<DialogTurnResult> ForwardStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var options = (TeacherExtensionOptions)stepCtx.Options;

            string signature = WebUtility.UrlEncode(UData.AppUser!.GetHashSignature());

            var tapUrl = options.ExtensionAction == BotAction.Exercises
                ? TeacherExtensionOptions.ExerciseButtonUrl
                : TeacherExtensionOptions.ExamButtonUrl;
            tapUrl += $"?signature={signature}&t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

            var cardAction = new CardAction()
            {
                Type = ActionTypes.OpenUrl,
                Title = options.ExtensionAction.ToFriendlyString(addEmoji: true),
                Value = tapUrl
            };
            
            HeroCard heroCard = new()
            {
                Images = new CardImage[1] { new(tap: cardAction) },
                Buttons = new CardAction[1] { cardAction },
                Tap = cardAction
            };

            if (options.ExtensionAction == BotAction.Exercises)
            {
                heroCard.Title = "Μετάβαση στις εργασίες";
                heroCard.Subtitle = "Διαχειριστείτε τις εργασίες για το σπίτι και προσθέστε νέες.";
                heroCard.Images[0].Url = TeacherExtensionOptions.ExerciseImageUrl;
            }
            else
            {
                heroCard.Title = "Μετάβαση στα διαγωνίσματα";
                heroCard.Subtitle = "Διαχειριστείτε τα επερχόμενα διαγωνίσματα και προσθέστε νέα.";
                heroCard.Images[0].Url = TeacherExtensionOptions.ExamImageUrl;
            }

            Activity reply;
            if (stepCtx.GetProvider() == ChannelProvider.Facebook)
            {
                reply = MessageFactory.Text(null);
                reply.ChannelData = ChannelDataFactory.Attachment(heroCard, 
                    imageAspectRatio: "square", webviewHeightRatio: "tall", messengerExtensions: true);
            }
            else
                reply = (Activity)MessageFactory.Attachment(heroCard.ToAttachment());

            return await stepCtx.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = reply,
                    RetryPrompt = reply,
                    Choices = new Choice[] { new Choice("🔙 Επιστροφή") }
                }, canTkn);
        }

        private Task<DialogTurnResult> PostReminderStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return stepContext.EndDialogAsync(null, cancellationToken);
        }

        #endregion
    }
}
