﻿using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using System.Collections.Generic;
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

namespace Phoenix.Bot.Dialogs.Common.Authentication
{
    public class AuthDialog : ComponentDialog
    {
        private readonly IConfiguration configuration;
        private readonly PhoenixContext phoenixContext;

        private readonly IStatePropertyAccessor<UserOptions> userOptionsAccesor;
        private readonly IStatePropertyAccessor<ConversationsOptions> convOptionsAccesor;

        private static class WaterfallNames
        {
            public const string Main        = "AuthMain_WaterfallDialog";
            public const string Phone       = "AuthPhone_WaterfallDialog";
            public const string Code        = "AuthCode_WaterfallDialog";
            public const string SendPin     = "AuthSendPin_WaterfallDialog";
            public const string CheckPin    = "AuthCheckPin_WaterfallDialog";
        }

        private static class PromptNames
        {
            public const string Phone = "PhoneNumber_Prompt";
            public const string Pin = "Pin_Prompt";
        }

        public AuthDialog(
            IConfiguration configuration, 
            ConversationState conversationState,
            UserState userState,
            PhoenixContext phoenixContext)
            : base(nameof(AuthDialog))
        {
            this.configuration = configuration;
            this.phoenixContext = phoenixContext;

            this.userOptionsAccesor = userState.CreateProperty<UserOptions>("Options");
            this.convOptionsAccesor = conversationState.CreateProperty<ConversationsOptions>("Options");

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));
            AddDialog(new TextPrompt(nameof(TextPrompt)));
            AddDialog(new NumberPrompt<long>(PromptNames.Phone, PromptValidators.PhoneNumberPromptValidator));
            AddDialog(new NumberPrompt<int>(PromptNames.Pin, PromptValidators.PinPromptValidator));

            AddDialog(new WaterfallDialog(WaterfallNames.Main,
                new WaterfallStep[]
                {
                    SignInStepAsync,
                    FinalStepAsync
                }));

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

            AddDialog(new WaterfallDialog(WaterfallNames.SendPin,
                new WaterfallStep[]
                {
                    SmsLeftCheckStepAsync,
                    SendPinStepAsync,
                    PinReceivedReplyStepAsync,

                    PinSendProblemStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.CheckPin,
                new WaterfallStep[]
                {
                    AskPinStepAsync,
                    CheckPinStepAsync
                }));

            InitialDialogId = WaterfallNames.Main;
        }

        #region Main Waterfall Dialog

        private async Task<DialogTurnResult> SignInStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            await stepContext.Context.SendActivityAsync("Αρχικά, θα χρειαστώ το κινητό τηλέφωνο επικοινωνίας που έχεις δώσει για την επαλήθευση.");

            return await stepContext.BeginDialogAsync(WaterfallNames.Phone, null, cancellationToken);
        }

        private async Task<DialogTurnResult> FinalStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
            => await stepContext.EndDialogAsync((bool?)stepContext.Result ?? false);

        #endregion

        #region Phone Waterfall Dialog

        private async Task<DialogTurnResult> AskPhoneStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
            => await stepContext.PromptAsync(
                PromptNames.Phone,
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

            var convOptions = await convOptionsAccesor.GetAsync(stepContext.Context, cancellationToken: cancellationToken);
            convOptions.Authentication = new AuthenticationOptions() { PhoneNumber = phone };
            await convOptionsAccesor.SetAsync(stepContext.Context, convOptions, cancellationToken);

            if (countUsersWithThatPhoneAtThisSchool == 1)
                return await stepContext.BeginDialogAsync(WaterfallNames.SendPin, phone, cancellationToken);

            //If a student (most probably a phone duplicate will belong to students and not to teachers) has their parent's phone registered,
            //then they must be differentiated by a unique code given by the school.
            return await stepContext.BeginDialogAsync(WaterfallNames.Code, phone, cancellationToken);
        }

        private async Task<DialogTurnResult> PhoneNotFoundStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (stepContext.Result != null)
                return await stepContext.EndDialogAsync(stepContext.Result, cancellationToken);

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
                await stepContext.Context.SendActivityAsync("Για να χρησιμοποιήσεις το Phoenix, " +
                    "θα πρέπει το φροντιστήριό σου να έχει πρώτα κάνει εγγραφή.");
                await stepContext.Context.SendActivityAsync("Εάν πιστεύεις ότι κάτι είναι λάθος, μπορείς να επικοινωνήσεις με το φροντιστήριο.");
                await stepContext.Context.SendActivityAsync("Φυσικά, μπορείς να μάθεις περισσότερα για το Phoenix " +
                    "πατώντας τον παρακάτω σύνδεσμο: https://www.askphoenix.gr/");
                await stepContext.Context.SendActivityAsync("Ελπίζω να τα ξαναπούμε σύντομα! Εις το επανιδείν! 😊");

                return await stepContext.EndDialogAsync(false, cancellationToken);
            }

            await stepContext.Context.SendActivityAsync("Μην ανησυχείς, κανένα πρόβλημα!");
            await stepContext.Context.SendActivityAsync("Ας προσπαθήσουμε ξανά, πιο προσεκτικά!");

            return await stepContext.ReplaceDialogAsync(stepContext.ActiveDialog.Id, stepContext.Options, cancellationToken);
        }

        #endregion

        #region Code Waterfall Dialog

        private async Task<DialogTurnResult> AskCodeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
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
                
                return await stepContext.EndDialogAsync(false, cancellationToken);
            }

            //TODO: Make code valid only for 5 mins
            //TODO: Check and validate the phone of the user that the OTC was sent to
            //TODO: Use channel name to validate the login provider
            bool codeOk = phoenixContext.AspNetUserLogins.Any(l => l.LoginProvider == LoginProvider.Facebook.GetProviderName() && l.OneTimeCode == result);
            if (!codeOk)
                return await stepContext.NextAsync(null, cancellationToken);



            //await conversationState.CreateProperty<string>("OneTimeCode").SetAsync(stepContext.Context, result);
            
            return await stepContext.BeginDialogAsync(WaterfallNames.SendPin, stepContext.Options, cancellationToken);
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

                return await stepContext.BeginDialogAsync(WaterfallNames.CheckPin, stepContext.Values["pin"], cancellationToken);
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
                return await stepContext.BeginDialogAsync(WaterfallNames.CheckPin, stepContext.Values["pin"], cancellationToken);

            return await stepContext.ReplaceDialogAsync(stepContext.ActiveDialog.Id, stepContext.Options, cancellationToken);
        }

        #endregion

        #region Check Pin Waterfall Dialog

        private async Task<DialogTurnResult> AskPinStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
            => await stepContext.PromptAsync(
                PromptNames.Pin,
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