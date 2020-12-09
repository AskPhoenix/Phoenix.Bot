using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Extensions.Configuration;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.Linguistic;
using Phoenix.Bot.Utilities.State;
using Phoenix.DataHandle.Sms;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs.Common.Authentication
{
    public class VerificationDialog : ComponentDialog
    {
        private readonly IConfiguration configuration;
        private readonly IStatePropertyAccessor<UserOptions> userOptionsAccesor;

        private const string wfSendPin = "SendPin";
        private const string wfCheckPin = "CheckPin";
        private const string promptPin = "Pin_Prompt";

        public VerificationDialog(IConfiguration configuration, UserState userState)
            : base(nameof(VerificationDialog))
        {
            this.configuration = configuration;
            this.userOptionsAccesor = userState.CreateProperty<UserOptions>(UserOptionsDefaults.PropertyName);

            AddDialog(new WaterfallDialog(WaterfallNames.BuildWaterfallName(wfSendPin, nameof(VerificationDialog)),
                new WaterfallStep[]
                {
                    SmsLeftCheckStepAsync,
                    SendPinStepAsync,
                    PinReceivedReplyStepAsync,

                    PinSendProblemStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.BuildWaterfallName(wfCheckPin, nameof(VerificationDialog)),
                new WaterfallStep[]
                {
                    AskPinStepAsync,
                    CheckPinStepAsync
                }));

        }

        #region Send Pin Waterfall Dialog

        private async Task<DialogTurnResult> SmsLeftCheckStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var userOptions = await userOptionsAccesor.GetAsync(stepContext.Context, cancellationToken: cancellationToken);
            int sms_left = UserOptionsDefaults.MaxSmsNumber - userOptions.SmsCount;

            if (sms_left > 0)
            {
                string phone = Convert.ToInt64(stepContext.Options).ToString();
                if (phone != "6900000000")
                {
                    userOptions.SmsCount += 1;
                    await userOptionsAccesor.SetAsync(stepContext.Context, userOptions, cancellationToken);
                }

                return await stepContext.NextAsync(null, cancellationToken);
            }

            await stepContext.Context.SendActivityAsync("Δυστυχώς έχεις υπερβεί το όριο μηνυμάτων επαλήθευσης.");
            await stepContext.Context.SendActivityAsync("Παρακαλώ επικοινώνησε με τους καθηγητές σου για να συνεχίσεις.");

            return await stepContext.EndDialogAsync(false, cancellationToken);
        }

        private async Task<DialogTurnResult> SendPinStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            string phone = Convert.ToInt64(stepContext.Options).ToString();
            int pin = new Random().Next(1000, 9999);

            // Avoid sending the sms with the pin when the phone is the fake one
            if (phone == "6900000000")
                pin = Convert.ToInt32(configuration["TestPin"]);
            else
            {
                string name = Greek.NameVocative(stepContext.Context.Activity.From.Name.Split(' ')[0]);
                var sms = new SmsService(configuration["NexmoSMS:ApiKey"], configuration["NexmoSMS:ApiSecret"]);
                await sms.SendAsync(phone, $"Το pin σου για το Phoenix είναι το {pin}.");   //TODO: και έχει διάρκεια 5 λεπτά."
            }
            stepContext.Values.Add("pin", pin);

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Τέλεια! Μόλις σου έστειλα ένα SMS με ένα μοναδικό pin. Το έλαβες;"),
                    RetryPrompt = MessageFactory.Text("Έλαβες το SMS με το pin; Παρακαλώ απάντησε με ένα Ναι ή Όχι:"),
                    Choices = new Choice[] { new Choice("✔️ Ναι"), new Choice("❌ Όχι") }
                });
        }

        private async Task<DialogTurnResult> PinReceivedReplyStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var foundChoice = stepContext.Result as FoundChoice;
            if (foundChoice.Index == 0)
            {
                await stepContext.Context.SendActivityAsync("Ωραία! Για να ολοκληρωθεί η σύνδεση, θα χρειαστεί να γράψεις το pin που μόλις έλαβες.");

                return await stepContext.BeginDialogAsync(wfCheckPin, stepContext.Values["pin"], cancellationToken);
            }

            await stepContext.Context.SendActivityAsync("ΟΚ, μην ανησυχείς! Επειδή καμιά φορά αργεί να έρθει το μήνυμα, περίμενε μερικά λεπτά ακόμα.");

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Αν δεν έχει έρθει ακόμη, μπορώ να προσπαθήσω να σου ξαναστείλω. " +
                    "Αλλιώς, πάτησε \"Το έλαβα\" για να συνεχίσουμε:"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε ή πληκτρολόγησε μία από τις παρακάτω απαντήσεις για να συνεχίσουμε:"),
                    Choices = new Choice[] { new Choice("👌 Το έλαβα"), new Choice("🔁 Στείλε ξανά") }
                });
        }

        private async Task<DialogTurnResult> PinSendProblemStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (stepContext.Result is bool)
                return await stepContext.EndDialogAsync(stepContext.Result, cancellationToken);

            var foundChoice = stepContext.Result as FoundChoice;
            if (foundChoice.Index == 0)
                return await stepContext.BeginDialogAsync(wfCheckPin, stepContext.Values["pin"], cancellationToken);

            return await stepContext.ReplaceDialogAsync(stepContext.ActiveDialog.Id, stepContext.Options, cancellationToken);
        }

        #endregion

        #region Check Pin Waterfall Dialog

        private async Task<DialogTurnResult> AskPinStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
            => await stepContext.PromptAsync(
                promptPin,
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Παρακαλώ πληκτρολόγησε το pin που έλαβες παρακάτω:"),
                    RetryPrompt = MessageFactory.Text("Η μορφή του pin που πληκτρολόγησες δεν είναι έγκυρη. Παρακαλώ πληκτρολόγησέ το ξανά:"),
                    Validations = Math.Ceiling(Math.Log10(Convert.ToInt32(stepContext.Options)))
                });

        private async Task<DialogTurnResult> CheckPinStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            int pinTyped = Convert.ToInt32(stepContext.Result);

            bool pinOk = pinTyped == Convert.ToInt32(stepContext.Options);
            if (pinOk)
            {
                await stepContext.Context.SendActivityAsync("Πολύ ωραία! Η σύνδεση ολοκληρώθηκε επιτυχώς! 😁");

                return await stepContext.EndDialogAsync(true, cancellationToken);
            }

            await stepContext.Context.SendActivityAsync("Το pin που έγραψες δεν είναι ίδιο με αυτό που σου έστειλα. " +
                "Δες ξανά και προσεκτικά το SMS και προσπάθησε πάλι.");

            return await stepContext.ReplaceDialogAsync(stepContext.ActiveDialog.Id, stepContext.Options, cancellationToken);
        }

        #endregion
    }
}
