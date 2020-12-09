using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Bot.Schema;
using System;
using Phoenix.DataHandle.Main.Models;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Phoenix.DataHandle.Sms;
using Microsoft.EntityFrameworkCore;
using Phoenix.Bot.Utilities.Linguistic;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.DataHandle.Main;
using Phoenix.Bot.Utilities.State;
using Phoenix.Bot.Utilities.State.Dialogs;

namespace Phoenix.Bot.Dialogs.Common.Authentication
{
    public class CredentialsDialog : ComponentDialog
    {
        private readonly IConfiguration configuration;
        private readonly PhoenixContext phoenixContext;

        private readonly IStatePropertyAccessor<UserOptions> userOptionsAccesor;
        private readonly IStatePropertyAccessor<ConversationsOptions> convOptionsAccesor;

        private const string promptPhone = "PhoneNumber_Prompt";
        private const string promptPin = "Pin_Prompt";

        private static class WaterfallNames
        {
            public const string Main        = "AuthMain_WaterfallDialog";
            public const string Phone       = "AuthPhone_WaterfallDialog";
            public const string Code        = "AuthCode_WaterfallDialog";
            public const string SendPin     = "AuthSendPin_WaterfallDialog";
            public const string CheckPin    = "AuthCheckPin_WaterfallDialog";
        }

        public CredentialsDialog(
            IConfiguration configuration, 
            ConversationState conversationState,
            UserState userState,
            PhoenixContext phoenixContext)
            : base(nameof(CredentialsDialog))
        {
            this.configuration = configuration;
            this.phoenixContext = phoenixContext;

            this.userOptionsAccesor = userState.CreateProperty<UserOptions>("Options");
            this.convOptionsAccesor = conversationState.CreateProperty<ConversationsOptions>("Options");

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));
            AddDialog(new TextPrompt(nameof(TextPrompt)));
            
            AddDialog(new NumberPrompt<long>(promptPhone, PromptValidators.PhoneNumberPromptValidator));
            AddDialog(new NumberPrompt<int>(promptPin, PromptValidators.PinPromptValidator));

            AddDialog(new WaterfallDialog(WaterfallNames.Phone,
                new WaterfallStep[]
                {
                    AskPhoneStepAsync,
                    CheckPhoneStepAsync,

                    PhoneNotFoundStepAsync,
                    PhoneRedirectStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Code,
                new WaterfallStep[]
                {
                    AskCodeStepAsync,
                    CheckCodeStepAsync,

                    CodeNotFoundStepAsync,
                    CodeRedirectStepAsync
                }));
            
            InitialDialogId = WaterfallNames.Main;
        }

        protected override async Task<DialogTurnResult> OnBeginDialogAsync(DialogContext innerDc, object options, CancellationToken cancellationToken = default)
        {
            if (!(options is CredentialsOptions))
                options = new CredentialsOptions();
            await innerDc.Context.SendActivityAsync("Αρχικά, θα χρειαστώ το κινητό τηλέφωνο επικοινωνίας που έχεις δώσει για την επαλήθευση.");

            return await base.OnBeginDialogAsync(innerDc, options, cancellationToken);
        }

        #region Phone Waterfall Dialog

        private async Task<DialogTurnResult> AskPhoneStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
            => await stepContext.PromptAsync(
                promptPhone,
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Παρακαλώ πληκτρολόγησε τον αριθμό παρακάτω:"),
                    RetryPrompt = MessageFactory.Text("Ο αριθμός τηλεφώνου πρέπει να είναι στη μορφή 69xxxxxxxx. Παρακαλώ πληκτρολόγησέ τον ξανά:")
                });

        private async Task<DialogTurnResult> CheckPhoneStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            string phone = Convert.ToInt64(stepContext.Result).ToString();
            stepContext.Values.Add("phone", phone);

            string schoolFbId = stepContext.Context.Activity.Recipient.Id;
            int countUsersWithThatPhoneAtThisSchool = phoenixContext.UserSchool.
                Include(us => us.AspNetUser).
                Where(us => us.AspNetUser.PhoneNumber == phone && us.School.FacebookPageId == schoolFbId).
                AsEnumerable().
                GroupBy(us => us.AspNetUser).
                Count();

            if (countUsersWithThatPhoneAtThisSchool == 0)
                return await stepContext.NextAsync(null, cancellationToken);

            (stepContext.Options as CredentialsOptions).Phone = phone;

            var convOptions = await convOptionsAccesor.GetAsync(stepContext.Context, cancellationToken: cancellationToken);
            convOptions.Authentication = new AuthenticationOptions() { PhoneNumber = phone };
            await convOptionsAccesor.SetAsync(stepContext.Context, convOptions, cancellationToken);

            if (countUsersWithThatPhoneAtThisSchool == 1)
                return await stepContext.EndDialogAsync(stepContext.Options, cancellationToken);

            //TODO: No need to enter here
            //If a student (most probably a phone duplicate will belong to students and not to teachers) has their parent's phone registered,
            //then they must be differentiated by a unique code given by the school.
            return await stepContext.BeginDialogAsync(WaterfallNames.Code, stepContext.Options, cancellationToken);
        }

        private async Task<DialogTurnResult> PhoneNotFoundStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Το κινητό τηλέφωνο που έγραψες δε βρέθηκε στο συγκεκριμένο φροντιστήριο. " +
                        $"Είσαι σίγουρος ότι το {stepContext.Values["phone"]} είναι το σωστό;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ απάντησε με ένα Ναι ή Όχι:"),
                    Choices = new Choice[] { new Choice("✔️ Ναι"), new Choice("❌ Όχι") }
                });
        }

        private async Task<DialogTurnResult> PhoneRedirectStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var foundChoice = stepContext.Result as FoundChoice;
            if (foundChoice.Index == 0)
            {
                await stepContext.Context.SendActivityAsync("Εάν πιστεύεις ότι κάτι είναι λάθος, παρακαλώ επικοινώνησε με το φροντιστήριό σου.");
                await stepContext.Context.SendActivityAsync("Ελπίζω να τα ξαναπούμε σύντομα! Εις το επανιδείν! 😊");

                return await stepContext.EndDialogAsync(stepContext.Options, cancellationToken);
            }

            await stepContext.Context.SendActivityAsync("Μην ανησυχείς, κανένα πρόβλημα!");
            await stepContext.Context.SendActivityAsync("Ας προσπαθήσουμε ξανά, πιο προσεκτικά!");

            return await stepContext.ReplaceDialogAsync(stepContext.ActiveDialog.Id, stepContext.Options, cancellationToken);
        }

        #endregion

        #region Code Waterfall Dialog

        private async Task<DialogTurnResult> AskCodeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            (stepContext.Options as CredentialsOptions).HasCode = true;

            //TODO: Generate code
            var reply = MessageFactory.SuggestedActions(
                new CardAction[] { new CardAction(ActionTypes.ImBack, "Δεν έχω κωδικό") },
                text: "Παρακαλώ πληκτρολόγησε τον κωδικό που σου έδωσαν από το φροντιστήριο:");

            return await stepContext.PromptAsync(
                nameof(TextPrompt),
                new PromptOptions { Prompt = (Activity)reply });
        }

        private async Task<DialogTurnResult> CheckCodeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var result = stepContext.Result as string;
            if (result.ToLower().ToUnaccented() == "δεν εχω κωδικο")
            {
                await stepContext.Context.SendActivityAsync("Απ' ό,τι βλέπω το τηλέφωνό σου έχει καταχωρηθεί πολλαπλές φορές.");
                await stepContext.Context.SendActivityAsync("Επικοινώνησε με το φροντιστήριό σου, ώστε να λάβεις έναν μοναδικό κωδικό " +
                    "και προσπάθησε ξανά.");
                
                return await stepContext.EndDialogAsync(stepContext.Options, cancellationToken);
            }

            //TODO: Make code valid only for 5 mins
            //TODO: Check and validate the phone of the user that the OTC was sent to
            //TODO: Use channel name to validate the login provider
            bool codeOk = phoenixContext.AspNetUserLogins.Any(l => l.LoginProvider == LoginProvider.Facebook.GetProviderName() && l.OneTimeCode == result);
            if (!codeOk)
                return await stepContext.NextAsync(null, cancellationToken);



            //await conversationState.CreateProperty<string>("OneTimeCode").SetAsync(stepContext.Context, result);
            
            return await stepContext.EndDialogAsync(stepContext.Options, cancellationToken);
        }

        private async Task<DialogTurnResult> CodeNotFoundStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (stepContext.Result != null)
                return await stepContext.EndDialogAsync(stepContext.Result, cancellationToken);

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Ο κωδικός που πληκτρολόγησες δε βρέθηκε."),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε μία από τις παρακάτω απαντήσεις:"),
                    Choices = new Choice[] { new Choice("🔁 Προσπάθησε ξανά"), new Choice("🔚 Ακύρωση") }
                });
        }

        private async Task<DialogTurnResult> CodeRedirectStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var foundChoice = stepContext.Result as FoundChoice;
            if (foundChoice.Index == 1)
            {
                await stepContext.Context.SendActivityAsync("Επικοινώνησε με το φροντιστήριό σου, ώστε να λάβεις έναν έγκυρο μοναδικό κωδικό " +
                   "και προσπάθησε ξανά.");
                await stepContext.Context.SendActivityAsync("Ελπίζω να τα ξαναπούμε σύντομα! Εις το επανιδείν! 😊");

                return await stepContext.EndDialogAsync(false, cancellationToken);
            }

            await stepContext.Context.SendActivityAsync("Ας προσπαθήσουμε ξανά, πιο προσεκτικά!");

            return await stepContext.ReplaceDialogAsync(stepContext.ActiveDialog.Id, stepContext.Options, cancellationToken);
        }

        #endregion
    }
}
