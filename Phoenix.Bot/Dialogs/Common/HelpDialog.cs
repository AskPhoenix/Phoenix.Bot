using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Schema;
using System.Collections.Generic;
using Phoenix.DataHandle.Main;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.State;
using Phoenix.Bot.Utilities.State.Options;

namespace Phoenix.Bot.Dialogs.Common
{
    public class HelpDialog : ComponentDialog
    {
        private readonly IStatePropertyAccessor<UserData> userDataAccesor;

        public HelpDialog(UserState userState)
            : base(nameof(HelpDialog))
        {
            this.userDataAccesor = userState.CreateProperty<UserData>(nameof(UserData));

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));

            AddDialog(new WaterfallDialog(WaterfallNames.Help.Tutorial,
                new WaterfallStep[]
                {
                    TutorialTopicsStepAsync,
                    TopicRedirectStepAsync,
                    AfterTopicStepAsync,
                    FinalStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Help.Ask,
                new WaterfallStep[]
                {
                    HelpAskStepAsync,
                    HelpReplyStepAsync,
                }));

            InitialDialogId = WaterfallNames.Help.Tutorial;
        }

        protected override Task<DialogTurnResult> OnBeginDialogAsync(DialogContext innerDc, object options, CancellationToken cancellationToken = default)
        {
            if (options is HelpOptions helpOptions && helpOptions != null && helpOptions.AskForTutorial)
                InitialDialogId = WaterfallNames.Help.Ask;

            return base.OnBeginDialogAsync(innerDc, options, cancellationToken);
        }

        #region Tutorial Waterfall Dialog

        private async Task<DialogTurnResult> TutorialTopicsStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var userData = await userDataAccesor.GetAsync(stepContext.Context, cancellationToken: cancellationToken);
            bool isStudent = (Role)userData.Role == Role.Student;

            var cards = new List<Attachment>(2)
            {
                new HeroCard()
                {
                    Title = "🏁 Αρχικό μενού",
                    Subtitle = "Μάθε τις δυνατότητες του αρχικού μενού κατά την έναρξη της συνομιλίας.",
                    Images = new List<CardImage>(1) { new CardImage(url: $"https://www.bot.askphoenix.gr/assets/home_{(isStudent ? "student" : "teacher")}_emoji_sq.png") },
                    Buttons = new List<CardAction>(1) {new CardAction(type: ActionTypes.ImBack, title: "🕊 Περισσότερα", value: "Περισσότερα για το αρχικό μενού") }
                }.ToAttachment(),
                new HeroCard()
                {
                    Title = "🔠 Εκφράσεις - Εντολές",
                    Subtitle = "Δες τι άλλο μπορείς να γράψεις στο Phoenix.",
                    Images = new List<CardImage>(1) { new CardImage(url: "https://www.bot.askphoenix.gr/assets/logo_sq.png") },
                    Buttons = new List<CardAction>(1) {new CardAction(type: ActionTypes.ImBack, title: "🐧 Περισσότερα", value: "Περισσότερα για τις εντολές") }
                }.ToAttachment()
            };

            await stepContext.Context.SendActivityAsync("Οι δυνατότητες του Phoenix είναι διαθέσιμες από τα δύο μενού: το σταθερό και το αρχικό.");
            await stepContext.Context.SendActivityAsync(MessageFactory.Attachment(cards));

            var reply = (Activity)MessageFactory.SuggestedActions(new CardAction[] { new CardAction(ActionTypes.ImBack, "🔚 Παράλειψη") });
            reply.Text = "Πάτησε στα κουμπιά παραπάνω για να μάθεις περισσότερα, ή επίλεξε \"Παράλειψη\" για να ολοκληρώσεις την περιήγηση.";

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = reply,
                    RetryPrompt = reply,
                    Style = ListStyle.None,
                    Choices = new Choice[] { new Choice("Περισσότερα για το αρχικό μενού"), new Choice("Περισσότερα για τις εντολές"), new Choice("🔚 Παράλειψη") }
                }, cancellationToken);
        }

        private async Task<DialogTurnResult> TopicRedirectStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return (stepContext.Result as FoundChoice).Index switch
            {
                0 => await HomeTutorialStepAsync(stepContext, cancellationToken),
                1 => await CommandsTutorialStepAsync(stepContext, cancellationToken),
                2 => await FinalStepAsync(stepContext, cancellationToken),
                _ => await stepContext.EndDialogAsync(null, cancellationToken)
            };
        }

        private async Task<DialogTurnResult> HomeTutorialStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var reply = MessageFactory.Text("Το \"Αρχικό μενού\" είναι διαθέσιμο κατά την έναρξη της συνομιλίας μας.");
            await stepContext.Context.SendActivityAsync(reply);

            reply.Text = "Οι υπηρεσίες που παρέχει εμφανίζονται παρακάτω εν συντομία:";
            await stepContext.Context.SendActivityAsync(reply);

            var userData = await userDataAccesor.GetAsync(stepContext.Context, cancellationToken: cancellationToken);
            Role roleSel = (Role)userData.Role;
            bool isStudent = roleSel == Role.Student;

            var cards = new List<Attachment>(4)
            {
                new HeroCard() 
                {
                    Title = "📚 Εργασίες",
                    Subtitle = isStudent ? "Έλεγξε τις εργασίες σου για το σπίτι." : "Προσθήκη και επεξεργασία των εργασιών των μαθητών."
                }.ToAttachment(),
                new HeroCard() 
                {
                    Title = "📝 Διαγωνίσματα",
                    Subtitle = isStudent ? "Διαχειρίσου τα διαγωνίσματα που έχεις ήδη γράψει ή πρόκειται να γράψεις."
                        : "Δημιουργία νέων διαγωνισμάτων και επεξεργασία της ύλης."
                }.ToAttachment(),
                new HeroCard() 
                {
                    Title = "📅 Πρόγραμμα",
                    Subtitle = isStudent ? "Δες το πρόγραμμα των μαθημάτων σου και τυχόν αλλαγές σε αυτό."
                        : (roleSel > Role.Teacher ? "Εμφάνιση και επεξεργασία των ωρών του προγράμματος διδασκαλίας."
                        : "Εμφάνιση των ωρών του προγράμματος διδασκαλίας.")
                }.ToAttachment()
            };

            if (!isStudent)
            {
                cards.Add(new HeroCard()
                {
                    Title = "💯 Βαθμολογίες",
                    Subtitle = "Εισαγωγή των βαθμολογιών των εργασιών και των διαγωνισμάτων."
                }.ToAttachment());
            }

            await stepContext.Context.SendActivityAsync(MessageFactory.Attachment(cards));
            return await stepContext.NextAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> CommandsTutorialStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var reply = MessageFactory.Text("Εκτός από τα μενού, υπάρχουν και οι παρακάτω εκφράσεις, " +
                "τις οποίες μπορείς να πληκτρολογήσεις ανά πάσα στιγμή:");
            await stepContext.Context.SendActivityAsync(reply);

            var cards = new List<Attachment>(2)
            {
                new HeroCard() 
                {
                    Title = "👋 Χαιρετισμοί",
                    Subtitle = "Χαιρέτισε γράφοντας \"Γεια σου Phoenix!\", \"Καλημέρα\" ή παρόμοιες εκφράσεις."
                }.ToAttachment(),
                new HeroCard() 
                {
                    Title = "💪 Βοήθεια",
                    Subtitle = "Έχεις κάποια αποροία ή έχεις κολλήσει κάπου; Απλά γράψε \"Βοήθεια\"."
                }.ToAttachment()
            };

            await stepContext.Context.SendActivityAsync(MessageFactory.Attachment(cards));
            return await stepContext.NextAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> AfterTopicStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Θα ήθελες να συνεχίσεις την περιήγηση εξερευνώντας κάποιο άλλο θέμα;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε ένα από τα παρακάτω για να συνεχίσουμε:"),
                    Choices = new Choice[] { new Choice("🔙 Άλλο θέμα"), new Choice("🔚 Ολοκλήρωση") }
                }, cancellationToken);
        }

        private async Task<DialogTurnResult> FinalStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var foundChoice = stepContext.Result as FoundChoice;
            if (foundChoice.Index == 0)
                return await stepContext.ReplaceDialogAsync(WaterfallNames.Help.Tutorial, stepContext.Options, cancellationToken);

            await stepContext.Context.SendActivityAsync("Ελπίζω η περιήγηση να σου φάνηκε χρήσιμη! 😊");
            await stepContext.Context.SendActivityAsync("Aν έχεις απορίες σχετικά με κάποια δυνατότητα, μπορείς να πληκτρολογήσεις \"Βοήθεια\".");
            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

        #endregion

        #region Ask Waterfall Dialog

        private async Task<DialogTurnResult> HelpAskStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return await stepContext.PromptAsync(nameof(UnaccentedChoicePrompt),
                new YesNoPromptOptions("Θα ήθελες να σου δείξω τι μπορώ να κάνω με μια σύντομη περιήγηση;"));
        }

        private async Task<DialogTurnResult> HelpReplyStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var foundChoice = stepContext.Result as FoundChoice;
            if (foundChoice.Index == 0)
            {
                await stepContext.Context.SendActivityAsync(MessageFactory.Text("Τέλεια! 😁"));
                return await stepContext.BeginDialogAsync(WaterfallNames.Help.Tutorial, null, cancellationToken);
            }

            var reply = MessageFactory.Text("Έγινε, κανένα πρόβλημα!");
            await stepContext.Context.SendActivityAsync(reply);

            return await stepContext.NextAsync(null, cancellationToken);
        }

        #endregion
    }
}
